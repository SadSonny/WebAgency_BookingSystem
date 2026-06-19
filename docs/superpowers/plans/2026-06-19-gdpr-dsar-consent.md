# GDPR — DSAR on-demand + consenso arricchito — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dare al titolare endpoint admin per esportare e cancellare (anonimizzare) on-demand i dati di un cliente identificato per email, e registrare la versione dell'informativa accettata nel consenso.

**Architecture:** Un servizio tenant-scoped `GdprDsarService` (isolamento automatico via global query filter su `tenant_id`) espone `ExportAsync`/`EraseAsync`. L'erase carica le righe del singolo cliente (bookings + outbox), le anonimizza/elimina e scrive un audit PII-free, il tutto in **un solo `SaveChangesAsync`** (atomico). Endpoint admin sotto `/api/v1/admin/gdpr`. Il consenso aggiunge un campo `GdprConsentVersion` (nullable) salvato alla creazione del booking.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API, EF Core 10 + Npgsql, FluentValidation, xUnit + NSubstitute, EF InMemory per i test.

## Global Constraints

- .NET `net10.0`; build con analyzer + **warnings-as-errors** (0 warning).
- Ogni file sorgente inizia con `// [INTENT]: ...`; `// WHY:` sulle logiche non ovvie; `/// <summary>` sui membri pubblici.
- DTO = `record` immutabili; `Result<T>` per esiti attesi (no eccezioni di flusso); `async/await` con `CancellationToken`; `DateTimeOffset` UTC in storage; errori/messaggi in **italiano**.
- Contratti condivisi in `Core` (**public**); implementazioni in `Infrastructure` (**internal**; `InternalsVisibleTo` UnitTests già presente).
- **Erasure = anonimizzazione** (riusa `DataRetentionService.AnonymizedMarker = "[rimosso]"`: nome→marker, telefono/email→`""`, note→`null`) + **eliminazione** delle `OutboxEmails` del cliente. **Identificativo = email** (case-insensitive, `Trim().ToLowerInvariant()`). Isolamento tenant via global query filter (niente `IgnoreQueryFilters`).
- **Audit PII-free**: mai l'email in chiaro nei log. `subjectRef` = **HMAC-SHA256 hex** dell'email normalizzata con chiave `Jwt:Secret`. `Actor = "owner"`. `Action` ∈ {`customer_data_exported`, `customer_data_erased`}.
- **Atomicità via singolo `SaveChangesAsync`** (NO `ExecuteUpdate`/`ExecuteDelete`, NO transazione esplicita: non supportati da EF InMemory e non necessari per la piccola N di un singolo cliente).
- Endpoint admin: `app.MapGroup("/api/v1/admin/gdpr").WithTags("Admin").RequireAuthorization(AdminClaims.AdminPolicy)`; metadati OpenAPI completi (`WithName`/`WithSummary`/`Produces<T>`); handler ritornano `result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToErrorResult()`.
- Comandi: build `dotnet build`; test `dotnet test tests/WebAgency_BookingSystem.UnitTests/WebAgency_BookingSystem.UnitTests.csproj`; singolo test `--filter "FullyQualifiedName~<Classe>"`. Migration: `dotnet ef migrations add <Nome> --project src/WebAgency_BookingSystem.Infrastructure --startup-project src/WebAgency_BookingSystem.Api`.

---

## File Structure

| File | Progetto | Responsabilità |
|---|---|---|
| `Entities/Booking.cs` (mod) | Core | + `string? GdprConsentVersion`. |
| `Dtos/Public/CreateBookingRequest.cs` (mod) | Core | + `string? GdprConsentVersion = null` (param opzionale in coda). |
| `Services/BookingService.cs` (mod) | Infrastructure | salva `GdprConsentVersion` alla creazione. |
| Migration `AddGdprConsentVersion` | Infrastructure | colonna nullable `gdpr_consent_version`. |
| `Dtos/Admin/GdprDsarDtos.cs` | Core | `CustomerDataExport`, `BookingExportItem`, `EraseCustomerRequest`, `ErasureResult`. |
| `Abstractions/Services/IGdprDsarService.cs` | Core | contratto export/erase. |
| `Services/GdprDsarService.cs` | Infrastructure | implementazione tenant-scoped. |
| `Endpoints/Admin/AdminGdprEndpoints.cs` | Api | i due endpoint. |
| `Endpoints/Admin/AdminEndpoints.cs` (mod) | Api | + `MapAdminGdprEndpoints()`. |
| `Validation/EraseCustomerRequestValidator.cs` | Api | email obbligatoria e valida. |
| `DependencyInjection.cs` (mod) | Infrastructure | `AddScoped<IGdprDsarService, GdprDsarService>()`. |
| `Claude_Instructions/GDPR_COMPLIANCE.md` | doc | sub-responsabili, data-flow, retention, ruoli, DSAR. |
| `CLAUDE.md` (mod) | doc | endpoint DSAR + nota consenso + stato. |

---

### Task 1: Consenso arricchito — campo `GdprConsentVersion`

**Files:**
- Modify: `src/WebAgency_BookingSystem.Core/Entities/Booking.cs`
- Modify: `src/WebAgency_BookingSystem.Core/Dtos/Public/CreateBookingRequest.cs`
- Modify: `src/WebAgency_BookingSystem.Infrastructure/Services/BookingService.cs` (~riga 187-188)
- Create: migration `AddGdprConsentVersion`

**Interfaces:**
- Produces: `Booking.GdprConsentVersion` (string?, nullable); `CreateBookingRequest.GdprConsentVersion` (string?, default null). Task 3 (export) e i test vi dipendono.

> Nota: `BookingService.CreateAsync` NON ha unit test (usa advisory lock + SQL raw Postgres — vedi `BookingServiceTests` riga 4-6). Il passaggio del campo è verificato da: build verde + migration + il round-trip nel test di export del Task 3 (che semina un booking con la versione). Niente unit test dedicato qui, coerente con la postura esistente.

- [ ] **Step 1: Aggiungi il campo all'entità**

In `Booking.cs`, dopo la proprietà `GdprConsentAt` (riga ~52), aggiungi:
```csharp
    /// <summary>Versione dell'informativa privacy accettata (opaca, decisa dall'agenzia; prova del consenso).
    /// Null per le prenotazioni precedenti a questa feature o per i client che non la inviano.</summary>
    public string? GdprConsentVersion { get; set; }
```

- [ ] **Step 2: Aggiungi il parametro alla request**

In `CreateBookingRequest.cs`, aggiungi il parametro opzionale **in coda** al record (dopo `AdditionalServiceIds`):
```csharp
public sealed record CreateBookingRequest(
    Guid ServiceId,
    Guid? StaffId,
    string Date,
    string Time,
    CustomerRequest Customer,
    bool GdprConsent,
    IReadOnlyList<Guid>? AdditionalServiceIds = null,
    string? GdprConsentVersion = null);
```
Aggiorna il `/// <param>` aggiungendo: `/// <param name="GdprConsentVersion">Versione dell'informativa mostrata al cliente (opzionale); salvata come prova del consenso.</param>`.

- [ ] **Step 3: Salva il campo alla creazione**

In `BookingService.cs`, nell'inizializzazione di `new Booking { ... }` (riga ~187), subito dopo `GdprConsentAt = nowUtc,` aggiungi:
```csharp
                GdprConsentVersion = request.GdprConsentVersion,
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: verde, 0 warning.

- [ ] **Step 5: Crea la migration**

Run: `dotnet ef migrations add AddGdprConsentVersion --project src/WebAgency_BookingSystem.Infrastructure --startup-project src/WebAgency_BookingSystem.Api`
Expected: crea i file migration. Apri il file `*_AddGdprConsentVersion.cs` e verifica che contenga `migrationBuilder.AddColumn<string>(name: "gdpr_consent_version", table: "bookings", nullable: true)` (o equivalente). Niente altre modifiche di schema inattese.

- [ ] **Step 6: Build di verifica**

Run: `dotnet build`
Expected: verde, 0 warning.

- [ ] **Step 7: Commit**

```bash
git add src/WebAgency_BookingSystem.Core/Entities/Booking.cs src/WebAgency_BookingSystem.Core/Dtos/Public/CreateBookingRequest.cs src/WebAgency_BookingSystem.Infrastructure/Services/BookingService.cs src/WebAgency_BookingSystem.Infrastructure/Persistence/Migrations/
git commit -m "feat(gdpr): versione informativa nel consenso (campo + request + migration)"
```

---

### Task 2: DTO + contratto `IGdprDsarService` (Core)

**Files:**
- Create: `src/WebAgency_BookingSystem.Core/Dtos/Admin/GdprDsarDtos.cs`
- Create: `src/WebAgency_BookingSystem.Core/Abstractions/Services/IGdprDsarService.cs`

**Interfaces:**
- Produces: i record DTO e `IGdprDsarService` (firme sotto). Task 3 (impl) e Task 4 (endpoint) vi dipendono.

- [ ] **Step 1: Crea i DTO**

`src/WebAgency_BookingSystem.Core/Dtos/Admin/GdprDsarDtos.cs`:
```csharp
// [INTENT]: DTO del sottosistema DSAR (GDPR 4.3): export dei dati di un cliente (diritto d'accesso) ed esito
// dell'anonimizzazione/cancellazione (diritto all'oblio). Record immutabili, serializzati come JSON dall'API.

namespace WebAgency_BookingSystem.Core.Dtos.Admin;

/// <summary>Dati personali di un cliente esportati per il diritto d'accesso. Lista eventualmente vuota.</summary>
public sealed record CustomerDataExport(string Email, int Count, IReadOnlyList<BookingExportItem> Bookings);

/// <summary>Una prenotazione del cliente con i suoi dati personali e gli estremi del consenso.</summary>
public sealed record BookingExportItem(
    Guid BookingId,
    string Date,
    string Time,
    int DurationMinutes,
    string CustomerName,
    string CustomerPhone,
    string CustomerEmail,
    string? CustomerNotes,
    bool GdprConsent,
    DateTimeOffset GdprConsentAt,
    string? GdprConsentVersion,
    string Status,
    DateTimeOffset CreatedAt);

/// <summary>Richiesta di cancellazione on-demand dei dati di un cliente, identificato per email.</summary>
public sealed record EraseCustomerRequest(string Email);

/// <summary>Esito della cancellazione: quante prenotazioni anonimizzate e quante email outbox eliminate.</summary>
public sealed record ErasureResult(int AnonymizedBookings, int PurgedOutbox);
```

- [ ] **Step 2: Crea il contratto del servizio**

`src/WebAgency_BookingSystem.Core/Abstractions/Services/IGdprDsarService.cs`:
```csharp
// [INTENT]: Contratto del servizio DSAR (GDPR 4.3) tenant-scoped: esporta e cancella on-demand i dati di un
// cliente identificato per email. L'isolamento per tenant è garantito dal global query filter del DbContext.

using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.Core.Abstractions.Services;

/// <summary>Operazioni DSAR (diritto d'accesso e all'oblio) sui dati di un cliente del tenant corrente.</summary>
public interface IGdprDsarService
{
    /// <summary>Esporta tutte le prenotazioni del cliente con l'email indicata (case-insensitive). Riesce sempre;
    /// la lista è vuota se non ci sono dati. Registra un evento di audit dell'accesso.</summary>
    Task<Result<CustomerDataExport>> ExportAsync(string email, CancellationToken ct = default);

    /// <summary>Anonimizza le prenotazioni e elimina le email outbox del cliente con l'email indicata. Ritorna
    /// NotFound se non c'è nulla da cancellare. Registra un evento di audit della cancellazione.</summary>
    Task<Result<ErasureResult>> EraseAsync(string email, CancellationToken ct = default);
}
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: verde, 0 warning.

- [ ] **Step 4: Commit**

```bash
git add src/WebAgency_BookingSystem.Core/Dtos/Admin/GdprDsarDtos.cs src/WebAgency_BookingSystem.Core/Abstractions/Services/IGdprDsarService.cs
git commit -m "feat(gdpr): DTO e contratto IGdprDsarService (export/erase)"
```

---

### Task 3: `GdprDsarService` (Infrastructure) + test — il cuore

**Files:**
- Create: `src/WebAgency_BookingSystem.Infrastructure/Services/GdprDsarService.cs`
- Test: `tests/WebAgency_BookingSystem.UnitTests/Services/GdprDsarServiceTests.cs`

**Interfaces:**
- Consumes: `IGdprDsarService`, DTO (Task 2); `BookingSystemDbContext`, `ITenantContext`, `DataRetentionService.AnonymizedMarker` (esistente, internal const = `"[rimosso]"`); `IConfiguration` (chiave `Jwt:Secret`).
- Produces: `internal sealed class GdprDsarService : IGdprDsarService`, ctor `(BookingSystemDbContext db, ITenantContext tenantContext, IConfiguration configuration, ILogger<GdprDsarService> logger)`.

- [ ] **Step 1: Scrivi i test (falliscono)**

`tests/WebAgency_BookingSystem.UnitTests/Services/GdprDsarServiceTests.cs`:
```csharp
// [INTENT]: Unit test del servizio DSAR (GDPR 4.3) con EF InMemory + ITenantContext fake. Coprono export (match,
// tutti i campi, isolamento tenant, vuoto=successo, case-insensitive), erase (anonimizza bookings, elimina outbox,
// idempotenza, 404), e l'audit PII-free (subjectRef = HMAC, niente email in chiaro).

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WebAgency_BookingSystem.Core.Abstractions;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Infrastructure.Persistence;
using WebAgency_BookingSystem.Infrastructure.Services;

namespace WebAgency_BookingSystem.UnitTests.Services;

public class GdprDsarServiceTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = "test-secret-key-at-least-32-chars-long!!" })
        .Build();

    private static BookingSystemDbContext NewDb(Guid tenantId, InMemoryDatabaseRoot root, string name)
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);
        DbContextOptions<BookingSystemDbContext> options = new DbContextOptionsBuilder<BookingSystemDbContext>()
            .UseInMemoryDatabase(name, root)
            .Options;
        return new BookingSystemDbContext(options, tenantContext);
    }

    private static Booking Booking(Guid tenantId, string email, string name = "Mario Rossi", BookingStatus status = BookingStatus.Confirmed) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        ServiceId = Guid.NewGuid(),
        BookingDate = new DateOnly(2035, 1, 1),
        BookingTime = new TimeOnly(10, 0),
        DurationMinutes = 30,
        CustomerName = name,
        CustomerPhone = "+39 333 0000000",
        CustomerEmail = email,
        CustomerNotes = "note",
        GdprConsent = true,
        GdprConsentAt = DateTimeOffset.UtcNow,
        GdprConsentVersion = "2026-06-01",
        Status = status,
        CancellationToken = Guid.NewGuid(),
    };

    // Helper: crea un servizio il cui DbContext è bound a TenantA, condividendo il root con i seed.
    private static (GdprDsarService sut, BookingSystemDbContext queryDb) SutForTenantA(InMemoryDatabaseRoot root, string name)
    {
        BookingSystemDbContext db = NewDb(TenantA, root, name);
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(TenantA);
        var sut = new GdprDsarService(db, tenantContext, Config(), NullLogger<GdprDsarService>.Instance);
        return (sut, db);
    }

    [Fact]
    public async Task export_restituisce_le_prenotazioni_del_cliente_con_tutti_i_campi()
    {
        var root = new InMemoryDatabaseRoot();
        string name = $"gdpr-{Guid.NewGuid()}";
        using (BookingSystemDbContext seed = NewDb(TenantA, root, name))
        {
            seed.Bookings.Add(Booking(TenantA, "mario@example.it"));
            seed.Bookings.Add(Booking(TenantA, "altro@example.it"));
            await seed.SaveChangesAsync();
        }
        (GdprDsarService sut, BookingSystemDbContext _) = SutForTenantA(root, name);

        Result<CustomerDataExport> result = await sut.ExportAsync("mario@example.it");

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Count);
        BookingExportItem item = Assert.Single(result.Value.Bookings);
        Assert.Equal("mario@example.it", item.CustomerEmail);
        Assert.Equal("2026-06-01", item.GdprConsentVersion);
    }

    [Fact]
    public async Task export_isolamento_tenant_non_vede_dati_di_altri_tenant()
    {
        var root = new InMemoryDatabaseRoot();
        string name = $"gdpr-{Guid.NewGuid()}";
        using (BookingSystemDbContext seedB = NewDb(TenantB, root, name))
        {
            seedB.Bookings.Add(Booking(TenantB, "mario@example.it"));
            await seedB.SaveChangesAsync();
        }
        (GdprDsarService sut, BookingSystemDbContext _) = SutForTenantA(root, name);

        Result<CustomerDataExport> result = await sut.ExportAsync("mario@example.it");

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Count); // i dati di TenantB sono nascosti dal global query filter
    }

    [Fact]
    public async Task export_vuoto_e_successo_non_404_e_scrive_audit()
    {
        var root = new InMemoryDatabaseRoot();
        (GdprDsarService sut, BookingSystemDbContext db) = SutForTenantA(root, $"gdpr-{Guid.NewGuid()}");

        Result<CustomerDataExport> result = await sut.ExportAsync("nessuno@example.it");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Bookings);
        AuditLog audit = Assert.Single(db.AuditLogs.IgnoreQueryFilters().ToList());
        Assert.Equal("customer_data_exported", audit.Action);
        Assert.DoesNotContain("nessuno@example.it", audit.Metadata); // niente email in chiaro
    }

    [Fact]
    public async Task export_email_case_insensitive()
    {
        var root = new InMemoryDatabaseRoot();
        string name = $"gdpr-{Guid.NewGuid()}";
        using (BookingSystemDbContext seed = NewDb(TenantA, root, name))
        {
            seed.Bookings.Add(Booking(TenantA, "mario@example.it"));
            await seed.SaveChangesAsync();
        }
        (GdprDsarService sut, BookingSystemDbContext _) = SutForTenantA(root, name);

        Result<CustomerDataExport> result = await sut.ExportAsync("  MARIO@Example.IT ");

        Assert.Equal(1, result.Value.Count);
    }

    [Fact]
    public async Task erase_anonimizza_bookings_ed_elimina_outbox()
    {
        var root = new InMemoryDatabaseRoot();
        string name = $"gdpr-{Guid.NewGuid()}";
        Guid bookingId;
        using (BookingSystemDbContext seed = NewDb(TenantA, root, name))
        {
            Booking b = Booking(TenantA, "mario@example.it");
            bookingId = b.Id;
            seed.Bookings.Add(b);
            seed.OutboxEmails.Add(new OutboxEmail
            {
                Id = Guid.NewGuid(), TenantId = TenantA, Kind = EmailKind.BookingConfirmation,
                Status = OutboxEmailStatus.Sent, ToEmail = "mario@example.it", ToName = "Mario",
                Subject = "Conferma", HtmlBody = "<p>Mario</p>", TextBody = "Mario",
            });
            await seed.SaveChangesAsync();
        }
        (GdprDsarService sut, BookingSystemDbContext db) = SutForTenantA(root, name);

        Result<ErasureResult> result = await sut.EraseAsync("mario@example.it");

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.AnonymizedBookings);
        Assert.Equal(1, result.Value.PurgedOutbox);
        Booking after = await db.Bookings.IgnoreQueryFilters().SingleAsync(b => b.Id == bookingId);
        Assert.Equal("[rimosso]", after.CustomerName);
        Assert.Equal(string.Empty, after.CustomerEmail);
        Assert.Null(after.CustomerNotes);
        Assert.Empty(db.OutboxEmails.IgnoreQueryFilters().ToList());
        Assert.Contains(db.AuditLogs.IgnoreQueryFilters().ToList(), a => a.Action == "customer_data_erased");
    }

    [Fact]
    public async Task erase_idempotente_seconda_volta_404()
    {
        var root = new InMemoryDatabaseRoot();
        string name = $"gdpr-{Guid.NewGuid()}";
        using (BookingSystemDbContext seed = NewDb(TenantA, root, name))
        {
            seed.Bookings.Add(Booking(TenantA, "mario@example.it"));
            await seed.SaveChangesAsync();
        }
        (GdprDsarService sut1, BookingSystemDbContext _) = SutForTenantA(root, name);
        await sut1.EraseAsync("mario@example.it");

        (GdprDsarService sut2, BookingSystemDbContext _) = SutForTenantA(root, name);
        Result<ErasureResult> second = await sut2.EraseAsync("mario@example.it");

        Assert.True(second.IsFailure);
        Assert.Equal(ErrorType.NotFound, second.Error.Type);
    }

    [Fact]
    public async Task erase_senza_dati_404_nessun_audit()
    {
        var root = new InMemoryDatabaseRoot();
        (GdprDsarService sut, BookingSystemDbContext db) = SutForTenantA(root, $"gdpr-{Guid.NewGuid()}");

        Result<ErasureResult> result = await sut.EraseAsync("nessuno@example.it");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(db.AuditLogs.IgnoreQueryFilters().ToList());
    }
}
```

> Nota implementativa per i test: l'helper `NewDb` crea contesti distinti che condividono lo **stesso**
> `InMemoryDatabaseRoot`, così i seed (anche di un altro tenant) finiscono nello stesso store mentre il global
> query filter del contesto-A nasconde i dati di B. `db.GetService<ITenantContext>()` non è usato dall'impl —
> il servizio riceve l'`ITenantContext` esplicito nel ctor.

- [ ] **Step 2: Esegui i test (falliscono)**

Run: `dotnet test tests/WebAgency_BookingSystem.UnitTests/WebAgency_BookingSystem.UnitTests.csproj --filter "FullyQualifiedName~GdprDsarServiceTests"`
Expected: errore di compilazione (`GdprDsarService` non esiste).

- [ ] **Step 3: Implementa il servizio**

`src/WebAgency_BookingSystem.Infrastructure/Services/GdprDsarService.cs`:
```csharp
// [INTENT]: Servizio DSAR (GDPR 4.3) tenant-scoped. ExportAsync raccoglie le prenotazioni di un cliente (diritto
// d'accesso); EraseAsync anonimizza quelle prenotazioni ed elimina le email outbox del cliente (diritto all'oblio).
// L'isolamento per tenant è garantito dal global query filter (niente IgnoreQueryFilters qui). Ogni operazione
// scrive un audit PII-FREE (subjectRef = HMAC dell'email, mai l'email in chiaro) e persiste TUTTO in un solo
// SaveChangesAsync: su Postgres è un'unica transazione atomica; su EF InMemory funziona (no ExecuteUpdate).

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Abstractions;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Services;

internal sealed class GdprDsarService : IGdprDsarService
{
    private readonly BookingSystemDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly byte[] _hmacKey;
    private readonly ILogger<GdprDsarService> _logger;

    public GdprDsarService(
        BookingSystemDbContext db,
        ITenantContext tenantContext,
        IConfiguration configuration,
        ILogger<GdprDsarService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
        string secret = configuration["JWT_SECRET"] ?? configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret è richiesto per l'audit DSAR (chiave HMAC).");
        _hmacKey = Encoding.UTF8.GetBytes(secret);
    }

    public async Task<Result<CustomerDataExport>> ExportAsync(string email, CancellationToken ct = default)
    {
        string normalized = Normalize(email);
        List<Booking> bookings = await _db.Bookings
            .Where(b => b.CustomerEmail.ToLower() == normalized)
            .OrderBy(b => b.BookingDate).ThenBy(b => b.BookingTime)
            .ToListAsync(ct);

        var items = bookings.Select(b => new BookingExportItem(
            b.Id,
            b.BookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            b.BookingTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            b.DurationMinutes,
            b.CustomerName,
            b.CustomerPhone,
            b.CustomerEmail,
            b.CustomerNotes,
            b.GdprConsent,
            b.GdprConsentAt,
            b.GdprConsentVersion,
            b.Status.ToString().ToLowerInvariant(),
            b.CreatedAt)).ToList();

        WriteAudit("customer_data_exported", normalized,
            JsonSerializer.Serialize(new { matched = items.Count, subjectRef = SubjectRef(normalized) }));
        await _db.SaveChangesAsync(ct);

        return new CustomerDataExport(normalized, items.Count, items);
    }

    public async Task<Result<ErasureResult>> EraseAsync(string email, CancellationToken ct = default)
    {
        string normalized = Normalize(email);

        // WHY: la cancellazione opera sulle poche righe di UN cliente → caricamento tracked + un solo SaveChanges
        // (atomico su Postgres). Niente ExecuteUpdate/Delete: non supportati da EF InMemory e non necessari qui.
        List<Booking> bookings = await _db.Bookings
            .Where(b => b.CustomerEmail.ToLower() == normalized && b.CustomerName != DataRetentionService.AnonymizedMarker)
            .ToListAsync(ct);

        // WHY: l'HTML congelato delle email outbox contiene PII → vanno eliminate, non solo le prenotazioni.
        List<OutboxEmail> outbox = await _db.OutboxEmails
            .Where(e => e.ToEmail.ToLower() == normalized)
            .ToListAsync(ct);

        if (bookings.Count == 0 && outbox.Count == 0)
        {
            return Error.NotFound("customer_not_found", "Nessun dato trovato per l'email indicata.");
        }

        foreach (Booking b in bookings)
        {
            // WHY: stessi campi di DataRetentionService (marker condiviso) — anonimizzazione coerente.
            b.CustomerName = DataRetentionService.AnonymizedMarker;
            b.CustomerPhone = string.Empty;
            b.CustomerEmail = string.Empty;
            b.CustomerNotes = null;
        }
        _db.OutboxEmails.RemoveRange(outbox);

        WriteAudit("customer_data_erased", normalized,
            JsonSerializer.Serialize(new { anonymizedBookings = bookings.Count, purgedOutbox = outbox.Count, subjectRef = SubjectRef(normalized) }));

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("DSAR erase: {Bookings} prenotazioni anonimizzate, {Outbox} email outbox eliminate", bookings.Count, outbox.Count);
        return new ErasureResult(bookings.Count, outbox.Count);
    }

    private void WriteAudit(string action, string normalizedEmail, string metadata) =>
        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId ?? Guid.Empty,
            Action = action,
            Actor = "owner",
            Metadata = metadata,
            CreatedAt = DateTimeOffset.UtcNow,
        });

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    // WHY: subjectRef = HMAC-SHA256 dell'email (chiave server) → correlazione tra eventi sullo stesso soggetto
    // senza esporre l'email; non reversibile da chi legge il DB (a differenza di un hash non salato).
    private string SubjectRef(string normalizedEmail)
    {
        byte[] hash = HMACSHA256.HashData(_hmacKey, Encoding.UTF8.GetBytes(normalizedEmail));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
```

- [ ] **Step 4: Esegui i test (passano)**

Run: `dotnet test tests/WebAgency_BookingSystem.UnitTests/WebAgency_BookingSystem.UnitTests.csproj --filter "FullyQualifiedName~GdprDsarServiceTests"`
Expected: PASS (7 test). Se `ToLower()` desse problemi su InMemory, è comunque LINQ-to-objects e funziona; su Postgres traduce in `LOWER(...)`.

- [ ] **Step 5: Build completo**

Run: `dotnet build`
Expected: verde, 0 warning.

- [ ] **Step 6: Commit**

```bash
git add src/WebAgency_BookingSystem.Infrastructure/Services/GdprDsarService.cs tests/WebAgency_BookingSystem.UnitTests/Services/GdprDsarServiceTests.cs
git commit -m "feat(gdpr): GdprDsarService export/erase tenant-scoped con audit HMAC (+test)"
```

---

### Task 4: Endpoint admin + validator + DI

**Files:**
- Create: `src/WebAgency_BookingSystem.Api/Endpoints/Admin/AdminGdprEndpoints.cs`
- Create: `src/WebAgency_BookingSystem.Api/Validation/EraseCustomerRequestValidator.cs`
- Modify: `src/WebAgency_BookingSystem.Api/Endpoints/Admin/AdminEndpoints.cs`
- Modify: `src/WebAgency_BookingSystem.Infrastructure/DependencyInjection.cs`
- Test: `tests/WebAgency_BookingSystem.UnitTests/Validation/EraseCustomerRequestValidatorTests.cs`

**Interfaces:**
- Consumes: `IGdprDsarService` (Task 2/3); `EraseCustomerRequest`, `CustomerDataExport`, `ErasureResult` (Task 2); pattern `AdminClaims.AdminPolicy`, `ToErrorResult()` (esistenti).
- Produces: `MapAdminGdprEndpoints()`.

- [ ] **Step 1: Scrivi il test del validator (fallisce)**

`tests/WebAgency_BookingSystem.UnitTests/Validation/EraseCustomerRequestValidatorTests.cs`:
```csharp
// [INTENT]: Unit test del validator della richiesta di erase DSAR: email obbligatoria e formalmente valida.

using WebAgency_BookingSystem.Api.Validation;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.UnitTests.Validation;

public class EraseCustomerRequestValidatorTests
{
    private readonly EraseCustomerRequestValidator _sut = new();

    [Fact]
    public void email_valida_passa()
    {
        var result = _sut.Validate(new EraseCustomerRequest("mario@example.it"));
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("non-una-email")]
    public void email_mancante_o_invalida_fallisce(string email)
    {
        var result = _sut.Validate(new EraseCustomerRequest(email));
        Assert.False(result.IsValid);
    }
}
```

- [ ] **Step 2: Esegui (fallisce)**

Run: `dotnet test tests/WebAgency_BookingSystem.UnitTests/WebAgency_BookingSystem.UnitTests.csproj --filter "FullyQualifiedName~EraseCustomerRequestValidatorTests"`
Expected: errore di compilazione (`EraseCustomerRequestValidator` non esiste).

- [ ] **Step 3: Implementa il validator**

`src/WebAgency_BookingSystem.Api/Validation/EraseCustomerRequestValidator.cs`:
```csharp
// [INTENT]: Validazione della richiesta di cancellazione DSAR: l'email del cliente è obbligatoria e formalmente
// valida (altrimenti 422 con messaggio dedicato invece di un match silenzioso a vuoto).

using FluentValidation;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.Api.Validation;

public sealed class EraseCustomerRequestValidator : AbstractValidator<EraseCustomerRequest>
{
    public EraseCustomerRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("L'email è obbligatoria.")
            .EmailAddress().WithMessage("Formato email non valido.");
    }
}
```

- [ ] **Step 4: Esegui (passa)**

Run: `dotnet test tests/WebAgency_BookingSystem.UnitTests/WebAgency_BookingSystem.UnitTests.csproj --filter "FullyQualifiedName~EraseCustomerRequestValidatorTests"`
Expected: PASS (3 casi).

- [ ] **Step 5: Implementa gli endpoint**

`src/WebAgency_BookingSystem.Api/Endpoints/Admin/AdminGdprEndpoints.cs`:
```csharp
// [INTENT]: Endpoint admin DSAR (GDPR 4.3), protetti da JWT. Export (diritto d'accesso) ed erase (diritto
// all'oblio) dei dati di un cliente identificato per email. La logica è in IGdprDsarService; qui solo routing,
// validazione della richiesta di erase e mappatura del Result.

using FluentValidation;
using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Infrastructure.Auth;

namespace WebAgency_BookingSystem.Api.Endpoints.Admin;

internal static class AdminGdprEndpoints
{
    public static IEndpointRouteBuilder MapAdminGdprEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/admin/gdpr").WithTags("Admin").RequireAuthorization(AdminClaims.AdminPolicy);

        group.MapGet("/customer", async (string email, IGdprDsarService dsar, CancellationToken ct) =>
        {
            var result = await dsar.ExportAsync(email, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToErrorResult();
        })
        .WithName("AdminGdprExportCustomer")
        .WithSummary("Esporta i dati di un cliente (GDPR)")
        .WithDescription("Diritto d'accesso: restituisce tutte le prenotazioni del cliente con l'email indicata. Riesce sempre (lista vuota se non ci sono dati). L'accesso è tracciato in audit_log.")
        .Produces<CustomerDataExport>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        group.MapPost("/customer/erase", async (EraseCustomerRequest request, IValidator<EraseCustomerRequest> validator, IGdprDsarService dsar, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return validation.ToValidationProblem();
            }

            var result = await dsar.EraseAsync(request.Email, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToErrorResult();
        })
        .WithName("AdminGdprEraseCustomer")
        .WithSummary("Cancella i dati di un cliente (GDPR)")
        .WithDescription("Diritto all'oblio: anonimizza le prenotazioni ed elimina le email outbox del cliente con l'email indicata. 404 se non c'è nulla da cancellare. L'azione è tracciata in audit_log.")
        .Produces<ErasureResult>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
```
> `ToValidationProblem()` è l'extension su `ValidationResult` definita in
> `src/WebAgency_BookingSystem.Api/Http/ValidationResults.cs` (namespace `WebAgency_BookingSystem.Api.Http`):
> produce la 422 `{ type: "validation_error", message, errors }`. Assicurati di avere `using WebAgency_BookingSystem.Api.Http;`.

- [ ] **Step 6: Registra gli endpoint e il servizio**

In `src/WebAgency_BookingSystem.Api/Endpoints/Admin/AdminEndpoints.cs`, dentro `MapAdminEndpoints`, aggiungi dopo `app.MapAdminAccountEndpoints();`:
```csharp
        app.MapAdminGdprEndpoints();
```
Aggiorna il `/// <summary>` aggiungendo "DSAR GDPR" all'elenco.

In `src/WebAgency_BookingSystem.Infrastructure/DependencyInjection.cs`, accanto agli altri `AddScoped` dei servizi admin (es. dopo `services.AddScoped<IAdminApiKeyManager, AdminApiKeyManager>();`), aggiungi:
```csharp
        services.AddScoped<IGdprDsarService, GdprDsarService>();
```
Verifica gli `using` necessari (`WebAgency_BookingSystem.Core.Abstractions.Services`, `WebAgency_BookingSystem.Infrastructure.Services` — probabilmente già presenti).

- [ ] **Step 7: Build + intera suite**

Run: `dotnet build`
Expected: verde, 0 warning.

Run: `dotnet test tests/WebAgency_BookingSystem.UnitTests/WebAgency_BookingSystem.UnitTests.csproj`
Expected: tutti verdi.

- [ ] **Step 8: Commit**

```bash
git add src/WebAgency_BookingSystem.Api/Endpoints/Admin/AdminGdprEndpoints.cs src/WebAgency_BookingSystem.Api/Validation/EraseCustomerRequestValidator.cs src/WebAgency_BookingSystem.Api/Endpoints/Admin/AdminEndpoints.cs src/WebAgency_BookingSystem.Infrastructure/DependencyInjection.cs tests/WebAgency_BookingSystem.UnitTests/Validation/EraseCustomerRequestValidatorTests.cs
git commit -m "feat(gdpr): endpoint admin DSAR export/erase + validator + DI (+test)"
```

---

### Task 5: Documentazione GDPR

**Files:**
- Create: `Claude_Instructions/GDPR_COMPLIANCE.md`
- Modify: `CLAUDE.md`

**Interfaces:** nessuna (solo doc).

- [ ] **Step 1: Crea il documento di compliance**

Crea `Claude_Instructions/GDPR_COMPLIANCE.md` con queste sezioni (in italiano, stile coerente con gli altri doc in `Claude_Instructions/`):
1. **Catena dei ruoli**: barber = **titolare** del trattamento; agenzia web = **responsabile** (gestisce i dati per conto del barber); piattaforma/dev = **sub-responsabile** (hosting/codice). Il DPA barber↔agenzia↔piattaforma è documento legale, fuori da questo repo.
2. **Sub-responsabili (processori)**: **Brevo** (invio email transazionali, server EU); **Railway** (hosting applicazione + PostgreSQL, region EU West). Entrambi trattano PII per conto del titolare.
3. **Dati personali trattati e dove vivono**: tabella `bookings` (nome, telefono, email, note, consenso); `outbox_emails` (copia PII nell'HTML, effimera); `audit_log` (**PII-free**: solo IP anonimizzato e `subjectRef` HMAC, mai email/nome in chiaro); `logs` (**PII-free**: niente IP, parametri SQL mascherati).
4. **Retention ed erasure**: anonimizzazione automatica delle prenotazioni oltre `Gdpr:RetentionDays` (365); purga outbox inviate oltre `Gdpr:OutboxRetentionDays` (30); retention `logs` 90 giorni. **DSAR on-demand**: `GET /api/v1/admin/gdpr/customer?email=` (export) e `POST /api/v1/admin/gdpr/customer/erase` (anonimizza prenotazioni + elimina outbox del cliente, immediato).
5. **Consenso**: registrato su ogni prenotazione come `GdprConsent` (bool) + `GdprConsentAt` (timestamp) + `GdprConsentVersion` (versione informativa accettata, inviata dal client).

- [ ] **Step 2: Aggiorna CLAUDE.md**

In `CLAUDE.md`:
- Nel sommario endpoint **Admin**, aggiungi sotto le rotte account:
  ```
  GET    /api/v1/admin/gdpr/customer?email=        → export dati cliente (diritto d'accesso)
  POST   /api/v1/admin/gdpr/customer/erase         body: { email } → anonimizza prenotazioni + elimina outbox
  ```
- Aggiungi una riga nello stato del progetto (filone 4.3): DSAR on-demand + versione consenso + doc compliance.
- Nei "Riferimenti", aggiungi `Claude_Instructions/GDPR_COMPLIANCE.md`.

- [ ] **Step 3: Commit**

```bash
git add Claude_Instructions/GDPR_COMPLIANCE.md CLAUDE.md
git commit -m "docs(gdpr): documento compliance (sub-responsabili, data-flow, retention, DSAR) + CLAUDE.md"
```

---

## Self-Review (compilato in fase di stesura)

- **Spec coverage:** A-DSAR (Task 2 DTO/contratto, Task 3 servizio+test export/erase/outbox/audit, Task 4 endpoint); B-consenso (Task 1 campo+request+mapping+migration); C-doc (Task 5). Audit PII-free HMAC (Task 3). Export 200-vuoto, erase 404, idempotenza, isolamento tenant: tutti coperti da test in Task 3. ✔
- **Type consistency:** `IGdprDsarService.ExportAsync→Result<CustomerDataExport>`, `EraseAsync→Result<ErasureResult>` identici in Task 2/3/4; `ErasureResult(AnonymizedBookings, PurgedOutbox)` coerente; `EraseCustomerRequest(Email)` usato in validator+endpoint+test; `DataRetentionService.AnonymizedMarker` riusato. ✔
- **Placeholder scan:** nessun TODO/TBD; codice completo in ogni step. Due note di verifica puntuale (nome metodo in `ValidationResults`, contenuto AddColumn migration) sono controlli espliciti, non placeholder. ✔
- **Decisione tecnica chiave annotata:** load-then-modify + singolo SaveChanges invece di ExecuteUpdate/transazione (EF InMemory non li supporta; atomicità preservata su Postgres) — coerente con la deroga prevista dallo spec §7. ✔
