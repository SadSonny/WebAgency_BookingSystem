# Agency Provisioning/Management API — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Esporre un'API protetta (identità di piattaforma "agency-admin") per creare e gestire i tenant senza CLI né accesso DB, riusando la logica di provisioning oggi nella CLI.

**Architecture:** Nuova identità `PlatformAdmin` (separata da `users`, niente TenantId) con login → JWT "platform" (ruolo `PlatformAdmin`, audience dedicata, niente `tenant_id`). Rotte `/api/v1/platform/*` protette da policy `PlatformAdmin`. La logica di provisioning viene estratta in `ITenantProvisioningService` (Infrastructure) condiviso da CLI e API. Setup "break-glass" gated da env token (crea-o-reimposta l'admin per email).

**Tech Stack:** .NET 10, ASP.NET Core Minimal API, EF Core 10 / Npgsql, FluentValidation, BCrypt.Net, Microsoft.IdentityModel JWT, xUnit + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-06-18-agency-provisioning-api-design.md`

## Global Constraints

- Ogni file sorgente inizia con `// [INTENT]: <descrizione>`; membri pubblici con `/// <summary>`; logiche non ovvie con `// WHY:`.
- `Result`/`Result<T>` + `Error` per i flussi attesi (niente eccezioni di controllo). DTO = `record`.
- Build con **warnings-as-errors + analyzer → 0 warning**. `dotnet build` via Bash OK; **`dotnet test` SOLO via PowerShell** (Bash fallisce con FileLoadException in questo ambiente). Docker attivo per i test di integrazione.
- Endpoint con metadati OpenAPI completi (`WithName/WithSummary/WithTags("Platform")/Produces<T>`).
- Errori in italiano. Messaggi di credenziali **neutri**. Query pre-auth/cross-tenant con `IgnoreQueryFilters()`.
- Niente `.sln`: buildare i progetti singolarmente (`dotnet build src/WebAgency_BookingSystem.Api` tira Core+Infrastructure).

---

## Task 1: Estrazione del provisioner in servizio condiviso (Core + Infrastructure), CLI via DI

**Files:**
- Create: `src/WebAgency_BookingSystem.Core/Provisioning/ProvisioningInput.cs` (modelli, **public**)
- Create: `src/WebAgency_BookingSystem.Core/Provisioning/ProvisioningValidator.cs` (**public**)
- Create: `src/WebAgency_BookingSystem.Core/Abstractions/Services/ITenantProvisioningService.cs`
- Create: `src/WebAgency_BookingSystem.Core/Dtos/Provisioning/ProvisioningOutput.cs`
- Create: `src/WebAgency_BookingSystem.Infrastructure/Services/Provisioning/TenantProvisioningService.cs`
- Modify: `src/WebAgency_BookingSystem.Infrastructure/DependencyInjection.cs` (registra il servizio)
- Delete: `tools/WebAgency_BookingSystem.TenantProvisioning/ProvisioningModels.cs`, `tools/.../ProvisioningValidator.cs`
- Modify: `tools/WebAgency_BookingSystem.TenantProvisioning/TenantProvisioner.cs` (rimosso; logica spostata) → eliminare il file
- Modify: `tools/WebAgency_BookingSystem.TenantProvisioning/Program.cs` (risolve `ITenantProvisioningService` da DI; `PrintResult` usa `ProvisioningOutput`)

**Interfaces:**
- Produces: `ProvisioningInput` (record public, stessa forma di `tools/.../ProvisioningModels.cs`: `Slug,Name,SiteUrl,OwnerEmail,Timezone,BookingRules,BusinessHours,SpecialClosures,Services,Staff` + record annidati `BookingRulesInput,BusinessHoursInput,SpecialClosureInput,ServiceInput,StaffInput,StaffBusinessHoursInput,StaffServiceInput`), namespace `WebAgency_BookingSystem.Core.Provisioning`.
- Produces: `ProvisioningValidator.Validate(ProvisioningInput) -> IReadOnlyList<string>` (static, public, namespace `WebAgency_BookingSystem.Core.Provisioning`).
- Produces: `ITenantProvisioningService.CreateAsync(ProvisioningInput input, CancellationToken ct) -> Task<Result<ProvisioningOutput>>` (namespace `WebAgency_BookingSystem.Core.Abstractions.Services`).
- Produces: `ProvisioningOutput(Guid TenantId, string Slug, string ApiKey, string KeyPrefix, string OwnerEmail, int ServiceCount, int StaffCount, int ClosureCount)` (record, namespace `WebAgency_BookingSystem.Core.Dtos.Provisioning`).

- [ ] **Step 1: Spostare i modelli in Core (public)**

Copia il contenuto di `tools/WebAgency_BookingSystem.TenantProvisioning/ProvisioningModels.cs` in `src/WebAgency_BookingSystem.Core/Provisioning/ProvisioningInput.cs`, cambiando: namespace → `WebAgency_BookingSystem.Core.Provisioning`; ogni `internal sealed record` → `public sealed record`; aggiorna il commento `[INTENT]` (modelli condivisi CLI+API). Poi **elimina** `tools/.../ProvisioningModels.cs`.

- [ ] **Step 2: Spostare il validator in Core (public)**

Copia `tools/.../ProvisioningValidator.cs` in `src/WebAgency_BookingSystem.Core/Provisioning/ProvisioningValidator.cs`: namespace → `WebAgency_BookingSystem.Core.Provisioning`; `internal static class` → `public static class`; corpo invariato. Elimina `tools/.../ProvisioningValidator.cs`.

- [ ] **Step 3: Interfaccia + output DTO**

`ITenantProvisioningService.cs`:
```csharp
// [INTENT]: Servizio condiviso di provisioning tenant: crea tenant + configurazioni + Owner (senza password) +
// API key + token/email di attivazione, in un'unica transazione. Usato sia dalla CLI sia dall'API platform, così
// la logica di creazione è una sola fonte di verità.

using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Provisioning;
using WebAgency_BookingSystem.Core.Provisioning;

namespace WebAgency_BookingSystem.Core.Abstractions.Services;

/// <summary>Crea un tenant completo a partire da un input di provisioning.</summary>
public interface ITenantProvisioningService
{
    /// <summary>Crea il tenant in transazione. Fallisce con Conflict se lo slug esiste già.</summary>
    Task<Result<ProvisioningOutput>> CreateAsync(ProvisioningInput input, CancellationToken ct = default);
}
```
`ProvisioningOutput.cs`:
```csharp
// [INTENT]: Esito del provisioning: id/slug del tenant, API key generata (da mostrare UNA volta), prefisso e conteggi.

namespace WebAgency_BookingSystem.Core.Dtos.Provisioning;

/// <summary>Risultato della creazione tenant (segreti da mostrare una sola volta).</summary>
public sealed record ProvisioningOutput(
    Guid TenantId, string Slug, string ApiKey, string KeyPrefix, string OwnerEmail,
    int ServiceCount, int StaffCount, int ClosureCount);
```

- [ ] **Step 4: Implementazione in Infrastructure**

Crea `TenantProvisioningService.cs` copiando la logica di `tools/.../TenantProvisioner.cs` (metodo `CreateAsync` + tutti i privati `AddBusinessHours/AddClosures/AddServices/AddStaff/GenerateApiKey/ToTime/ToDate`), con queste modifiche:
- `internal sealed class TenantProvisioningService : ITenantProvisioningService`, namespace `WebAgency_BookingSystem.Infrastructure.Services.Provisioning`.
- Costruttore identico: `(BookingSystemDbContext db, IEmailOutbox outbox, AccountSettings account)`.
- `CreateAsync` ritorna `Task<Result<ProvisioningOutput>>`. Sostituire il blocco slug:
```csharp
        if (await _db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Slug == input.Slug, ct))
        {
            return Error.Conflict("slug_esistente", $"Esiste già un tenant con slug '{input.Slug}'.");
        }
```
- Alla fine, `return` un `ProvisioningOutput` (stessi valori dell'attuale `ProvisioningResult`).
- Usare `using WebAgency_BookingSystem.Core.Common;` (Result/Error), `...Core.Dtos.Provisioning;`, `...Core.Provisioning;`. Rimuovere il tipo `ProvisioningException` (non più usato).
- Aggiorna `[INTENT]`.

- [ ] **Step 5: Registrare in DI**

In `DependencyInjection.cs` (`AddInfrastructure`), accanto agli altri scoped:
```csharp
        services.AddScoped<ITenantProvisioningService, TenantProvisioningService>();
```
Aggiungi `using WebAgency_BookingSystem.Infrastructure.Services.Provisioning;` se serve.

- [ ] **Step 6: Aggiornare la CLI**

In `tools/.../Program.cs`: sostituire `var provisioner = new TenantProvisioner(db, outbox, accountSettings); ProvisioningResult result = await provisioner.CreateAsync(...)` con:
```csharp
        var provisioning = scope.ServiceProvider.GetRequiredService<WebAgency_BookingSystem.Core.Abstractions.Services.ITenantProvisioningService>();
        var result = await provisioning.CreateAsync(input, CancellationToken.None);
        if (result.IsFailure)
        {
            Console.Error.WriteLine($"Provisioning interrotto: {result.Error.Message}");
            return 1;
        }
        PrintResult(result.Value);
```
Adatta `PrintResult` alla firma `ProvisioningOutput` (campi `TenantId/Slug/ApiKey/KeyPrefix/OwnerEmail/ServiceCount/StaffCount/ClosureCount` — nota `OwnerEmail` invece di `AdminEmail`). Rimuovi il `catch (ProvisioningException ...)` (il tipo non esiste più). Elimina `TenantProvisioner.cs`. La validazione `ProvisioningValidator.Validate` ora usa `WebAgency_BookingSystem.Core.Provisioning`.

- [ ] **Step 7: Build**

Run: `dotnet build tools/WebAgency_BookingSystem.TenantProvisioning` e `dotnet build src/WebAgency_BookingSystem.Api`
Expected: PASS 0/0 entrambi.

- [ ] **Step 8: Test**

Run (PowerShell): `dotnet test tests/WebAgency_BookingSystem.UnitTests`
Expected: PASS (96).

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "refactor(provisioning): estrai ITenantProvisioningService condiviso (Core+Infra), CLI via DI"
```

---

## Task 2: Entità PlatformAdmin + config + migration + repository

**Files:**
- Create: `src/WebAgency_BookingSystem.Core/Entities/PlatformAdmin.cs`
- Create: `src/WebAgency_BookingSystem.Core/Abstractions/Repositories/IPlatformAdminRepository.cs`
- Create: `src/WebAgency_BookingSystem.Infrastructure/Persistence/Configurations/PlatformAdminConfiguration.cs`
- Create: `src/WebAgency_BookingSystem.Infrastructure/Persistence/Repositories/PlatformAdminRepository.cs`
- Modify: `src/WebAgency_BookingSystem.Infrastructure/Persistence/BookingSystemDbContext.cs` (DbSet)
- Modify: `src/WebAgency_BookingSystem.Infrastructure/DependencyInjection.cs` (registra repo)

**Interfaces:**
- Produces: `PlatformAdmin` entity (`Id,Email,PasswordHash?,SecurityStamp,Active,FailedAccessCount,LockoutEnd,ActivatedAt?,LastLoginAt?,CreatedAt,UpdatedAt`).
- Produces: `IPlatformAdminRepository`:
  - `Task<PlatformAdmin?> GetByEmailAsync(string email, CancellationToken ct=default)`
  - `Task<PlatformAdmin?> GetTrackedByIdAsync(Guid id, CancellationToken ct=default)`
  - `Task<Guid?> GetSecurityStampAsync(Guid id, CancellationToken ct=default)`
  - `Task RegisterFailedAttemptAsync(Guid id, int threshold, TimeSpan duration, CancellationToken ct=default)`
  - `Task RegisterSuccessfulLoginAsync(Guid id, CancellationToken ct=default)`
  - `Task<bool> UpsertPasswordByEmailAsync(string email, string passwordHash, CancellationToken ct=default)` (ritorna `true` se creato, `false` se reimpostato)
  - `Task SaveChangesAsync(CancellationToken ct=default)`

- [ ] **Step 1: Entità**

```csharp
// [INTENT]: Identità di piattaforma (agency-admin): amministra i tenant via API/console. SEPARATA da User (NON ha
// TenantId) per non intaccare l'invariante di tenant-isolation. Password come hash bcrypt (null finché non
// impostata dal setup). SecurityStamp invalida i JWT precedenti al cambio password. Lockout come per User (S3).

namespace WebAgency_BookingSystem.Core.Entities;

/// <summary>Amministratore di piattaforma (agenzia), non legato ad alcun tenant.</summary>
public class PlatformAdmin : IAuditableEntity
{
    /// <summary>Identificativo univoco (PK).</summary>
    public Guid Id { get; set; }
    /// <summary>Email di login, univoca globale.</summary>
    public string Email { get; set; } = string.Empty;
    /// <summary>Hash bcrypt della password; null finché non impostata.</summary>
    public string? PasswordHash { get; set; }
    /// <summary>Marca di sicurezza: cambia a ogni mutazione di password e invalida i JWT emessi prima.</summary>
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();
    /// <summary>Se false, non può autenticarsi.</summary>
    public bool Active { get; set; } = true;
    /// <summary>Tentativi di login falliti consecutivi (S3).</summary>
    public int FailedAccessCount { get; set; }
    /// <summary>Se valorizzato e nel futuro, account bloccato fino a questo istante (UTC).</summary>
    public DateTimeOffset? LockoutEnd { get; set; }
    /// <summary>Istante di attivazione/prima impostazione password (UTC).</summary>
    public DateTimeOffset? ActivatedAt { get; set; }
    /// <summary>Ultimo login riuscito (UTC).</summary>
    public DateTimeOffset? LastLoginAt { get; set; }
    /// <summary>Istante di creazione (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Istante dell'ultimo aggiornamento (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
```
(Verifica che `IAuditableEntity` richieda `CreatedAt`/`UpdatedAt`; il `TimestampInterceptor` li valorizza.)

- [ ] **Step 2: Mapping EF**

```csharp
// [INTENT]: Mapping EF Core di PlatformAdmin (tabella platform_admin). Email univoca globale. Nessun global query
// filter tenant (identità di piattaforma, cross-tenant).

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Configurations;

internal sealed class PlatformAdminConfiguration : IEntityTypeConfiguration<PlatformAdmin>
{
    public void Configure(EntityTypeBuilder<PlatformAdmin> builder)
    {
        builder.ToTable("platform_admin");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Email).IsRequired().HasMaxLength(255);
        builder.Property(p => p.PasswordHash).HasMaxLength(255);
        builder.Property(p => p.SecurityStamp).IsRequired();
        builder.Property(p => p.Active).HasDefaultValue(true);
        builder.HasIndex(p => p.Email).IsUnique();
    }
}
```
Confermare che le config si applicano via `ApplyConfigurationsFromAssembly` (auto-discovery) e che `PlatformAdmin` NON è nel metodo `ApplyTenantQueryFilters` del DbContext (quindi è esente dal filtro tenant).

- [ ] **Step 3: DbSet**

In `BookingSystemDbContext.cs`:
```csharp
    public DbSet<PlatformAdmin> PlatformAdmins => Set<PlatformAdmin>();
```

- [ ] **Step 4: Repository**

```csharp
// [INTENT]: Accesso ai PlatformAdmin. Le ricerche sono pre-auth (login/setup) e cross-tenant → IgnoreQueryFilters
// non serve (l'entità non ha filtro), ma manteniamo AsNoTracking sulle letture pure.

using Microsoft.EntityFrameworkCore;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Repositories;

internal sealed class PlatformAdminRepository : IPlatformAdminRepository
{
    private readonly BookingSystemDbContext _db;
    public PlatformAdminRepository(BookingSystemDbContext db) => _db = db;

    public Task<PlatformAdmin?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _db.PlatformAdmins.AsNoTracking().FirstOrDefaultAsync(p => p.Email == email, ct);

    public Task<PlatformAdmin?> GetTrackedByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.PlatformAdmins.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Guid?> GetSecurityStampAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.PlatformAdmins.AsNoTracking().Where(p => p.Id == id)
            .Select(p => new { p.SecurityStamp }).FirstOrDefaultAsync(ct);
        return row?.SecurityStamp;
    }

    public async Task RegisterFailedAttemptAsync(Guid id, int threshold, TimeSpan duration, CancellationToken ct = default)
    {
        PlatformAdmin? a = await _db.PlatformAdmins.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (a is null) return;
        a.FailedAccessCount++;
        if (a.FailedAccessCount >= threshold)
        {
            a.LockoutEnd = DateTimeOffset.UtcNow.Add(duration);
            a.FailedAccessCount = 0;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task RegisterSuccessfulLoginAsync(Guid id, CancellationToken ct = default)
    {
        PlatformAdmin? a = await _db.PlatformAdmins.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (a is null) return;
        a.FailedAccessCount = 0;
        a.LockoutEnd = null;
        a.LastLoginAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> UpsertPasswordByEmailAsync(string email, string passwordHash, CancellationToken ct = default)
    {
        // WHY: setup/break-glass — crea l'admin se non esiste, altrimenti ne reimposta la password. L'unique su
        // Email rende l'operazione idempotente per email anche con chiamate concorrenti.
        PlatformAdmin? a = await _db.PlatformAdmins.FirstOrDefaultAsync(p => p.Email == email, ct);
        bool created = a is null;
        if (a is null)
        {
            a = new PlatformAdmin { Id = Guid.NewGuid(), Email = email, ActivatedAt = DateTimeOffset.UtcNow };
            _db.PlatformAdmins.Add(a);
        }
        a.PasswordHash = passwordHash;
        a.SecurityStamp = Guid.NewGuid();
        a.Active = true;
        a.FailedAccessCount = 0;
        a.LockoutEnd = null;
        await _db.SaveChangesAsync(ct);
        return created;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
```
Aggiungi l'interfaccia `IPlatformAdminRepository` (namespace `...Core.Abstractions.Repositories`) con le firme dell'Interfaces block. Registra in DI: `services.AddScoped<IPlatformAdminRepository, PlatformAdminRepository>();`.

- [ ] **Step 5: Migration**

```
dotnet ef migrations add AddPlatformAdmin --project src/WebAgency_BookingSystem.Infrastructure --startup-project src/WebAgency_BookingSystem.Api
```
Verifica: crea `platform_admin` con unique index su `email`.

- [ ] **Step 6: Build + Commit**

Run: `dotnet build src/WebAgency_BookingSystem.Api` → 0/0.
```bash
git add -A
git commit -m "feat(platform): entita' PlatformAdmin + repository + migration"
```

---

## Task 3: Auth platform (JWT, login, stamp, policy, audience, OnTokenValidated)

**Files:**
- Modify: `src/WebAgency_BookingSystem.Core/Abstractions/Services/IJwtTokenGenerator.cs` (metodo `GeneratePlatform`)
- Modify: `src/WebAgency_BookingSystem.Infrastructure/Auth/JwtTokenGenerator.cs`
- Modify: `src/WebAgency_BookingSystem.Infrastructure/Auth/JwtSettings.cs` (PlatformAudience)
- Modify: `src/WebAgency_BookingSystem.Infrastructure/Auth/AdminClaims.cs` (ruolo + policy const)
- Create: `src/WebAgency_BookingSystem.Core/Abstractions/Services/IPlatformSecurityStampService.cs`
- Create: `src/WebAgency_BookingSystem.Infrastructure/Auth/PlatformSecurityStampService.cs`
- Create: `src/WebAgency_BookingSystem.Core/Dtos/Platform/PlatformAuthDtos.cs`
- Create: `src/WebAgency_BookingSystem.Core/Abstractions/Services/IPlatformAuthService.cs`
- Create: `src/WebAgency_BookingSystem.Infrastructure/Auth/PlatformAuthService.cs`
- Create: `src/WebAgency_BookingSystem.Api/Endpoints/Platform/PlatformAuthEndpoints.cs`
- Create: `src/WebAgency_BookingSystem.Api/Validation/PlatformLoginRequestValidator.cs`
- Create: `src/WebAgency_BookingSystem.Api/Endpoints/Platform/PlatformEndpoints.cs` (aggregatore)
- Modify: `src/WebAgency_BookingSystem.Api/Program.cs` (ValidAudiences, policy PlatformAdmin, OnTokenValidated branch, MapPlatformEndpoints)
- Modify: `src/WebAgency_BookingSystem.Infrastructure/DependencyInjection.cs` (registra stamp + auth service)

**Interfaces:**
- Produces: `IJwtTokenGenerator.GeneratePlatform(Guid platformAdminId, Guid securityStamp) -> (string Token, DateTimeOffset ExpiresAt)`
- Produces: `AdminClaims.PlatformRole = "PlatformAdmin"`, `AdminClaims.PlatformPolicy = "PlatformAdmin"`
- Produces: `IPlatformSecurityStampService.IsCurrentAsync(Guid id, Guid stamp, CancellationToken) -> Task<bool>`, `.Invalidate(Guid id)`
- Produces: `PlatformLoginRequest(string Email, string Password)`; riusa `AdminTokenResponse` (Core.Dtos.Admin)
- Produces: `IPlatformAuthService.LoginAsync(PlatformLoginRequest, CancellationToken) -> Task<Result<AdminTokenResponse>>`
- Consumes: `IPlatformAdminRepository` (Task 2), `JwtSettings`

- [ ] **Step 1: JWT platform**

In `IJwtTokenGenerator.cs` aggiungi:
```csharp
    /// <summary>Genera un JWT di piattaforma (ruolo PlatformAdmin, audience platform, SENZA tenant_id).</summary>
    (string Token, DateTimeOffset ExpiresAt) GeneratePlatform(Guid platformAdminId, Guid securityStamp);
```
In `JwtTokenGenerator.cs` aggiungi il metodo (riusa secret/KeyId/issuer; audience = `_settings.PlatformAudience`):
```csharp
    public (string Token, DateTimeOffset ExpiresAt) GeneratePlatform(Guid platformAdminId, Guid securityStamp)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset expiresAt = now.AddHours(_settings.ExpiryHours);
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret)) { KeyId = JwtSettings.SigningKeyId };
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, platformAdminId.ToString()),
                new Claim(ClaimTypes.Role, AdminClaims.PlatformRole),
                new Claim(AdminClaims.SecurityStamp, securityStamp.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ]),
            Issuer = _settings.Issuer,
            Audience = _settings.PlatformAudience,
            IssuedAt = now.UtcDateTime, NotBefore = now.UtcDateTime, Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256),
        };
        string token = new JsonWebTokenHandler().CreateToken(descriptor);
        return (token, expiresAt);
    }
```

- [ ] **Step 2: JwtSettings.PlatformAudience**

In `JwtSettings.cs` aggiungi al record il campo `string PlatformAudience` e in `FromConfiguration`:
```csharp
        string platformAudience = configuration["Jwt:PlatformAudience"] ?? "WebAgency_BookingSystem.Platform";
```
e includilo nel `return new JwtSettings(..., platformAudience)`. Aggiorna la firma del record `JwtSettings(string Secret, string Issuer, string Audience, int ExpiryHours, string PlatformAudience)` e tutti i call-site (è creato con `FromConfiguration`, quindi nessun altro call-site posizionale).

- [ ] **Step 3: AdminClaims**

In `AdminClaims.cs`:
```csharp
    /// <summary>Valore del claim ruolo per gli admin di piattaforma.</summary>
    public const string PlatformRole = "PlatformAdmin";
    /// <summary>Nome della policy di autorizzazione per le rotte /platform.</summary>
    public const string PlatformPolicy = "PlatformAdmin";
```

- [ ] **Step 4: Stamp service platform**

`IPlatformSecurityStampService.cs` (gemello di `IUserSecurityStampService`, namespace `...Core.Abstractions.Services`):
```csharp
// [INTENT]: Verifica che la SecurityStamp di un JWT platform sia quella corrente del PlatformAdmin (invalidazione
// JWT al cambio password). Cache-first; Invalidate svuota dopo una mutazione.

namespace WebAgency_BookingSystem.Core.Abstractions.Services;

public interface IPlatformSecurityStampService
{
    Task<bool> IsCurrentAsync(Guid platformAdminId, Guid stamp, CancellationToken ct = default);
    void Invalidate(Guid platformAdminId);
}
```
`PlatformSecurityStampService.cs` (copia `UserSecurityStampService` ma su `IPlatformAdminRepository.GetSecurityStampAsync`, chiave cache `platform-stamp:{id}`, TTL 5 min).

- [ ] **Step 5: DTO + auth service**

`PlatformAuthDtos.cs`:
```csharp
// [INTENT]: DTO dell'auth di piattaforma. Riusa AdminTokenResponse per la risposta.

namespace WebAgency_BookingSystem.Core.Dtos.Platform;

/// <summary>Login agency-admin: email + password (identità globale di piattaforma).</summary>
public sealed record PlatformLoginRequest(string Email, string Password);

/// <summary>Setup/break-glass: token operatore + email + password.</summary>
public sealed record PlatformSetupRequest(string SetupToken, string Email, string Password);
```
`IPlatformAuthService.cs`: `Task<Result<AdminTokenResponse>> LoginAsync(PlatformLoginRequest request, CancellationToken ct = default)`.
`PlatformAuthService.cs` (copia la struttura di `AdminAuthService`, ma su `IPlatformAdminRepository`, niente tenant, `GeneratePlatform`, stesso `DummyPasswordHash` per timing, errore neutro):
```csharp
    public async Task<Result<AdminTokenResponse>> LoginAsync(PlatformLoginRequest request, CancellationToken ct = default)
    {
        Error invalid = Error.Unauthorized("unauthorized", "Credenziali non valide.");
        PlatformAdmin? admin = await _admins.GetByEmailAsync(request.Email, ct);
        if (admin is not { Active: true } || admin.PasswordHash is null)
        {
            _ = VerifyPassword(request.Password, DummyPasswordHash);
            return invalid;
        }
        if (admin.LockoutEnd is DateTimeOffset until && until > DateTimeOffset.UtcNow) return invalid;
        if (!VerifyPassword(request.Password, admin.PasswordHash))
        {
            await _admins.RegisterFailedAttemptAsync(admin.Id, MaxFailedAttempts, LockoutDuration, ct);
            return invalid;
        }
        await _admins.RegisterSuccessfulLoginAsync(admin.Id, ct);
        (string token, DateTimeOffset expiresAt) = _jwt.GeneratePlatform(admin.Id, admin.SecurityStamp);
        return Result.Success(new AdminTokenResponse(token, "Bearer", expiresAt.ToString("o")));
    }
```
(Includi `MaxFailedAttempts=5`, `LockoutDuration=15min`, `DummyPasswordHash` e `VerifyPassword` come in `AdminAuthService`.)

- [ ] **Step 6: Login endpoint + validator + aggregatore**

`PlatformLoginRequestValidator.cs`: Email NotEmpty+EmailAddress, Password NotEmpty.
`PlatformAuthEndpoints.cs`:
```csharp
// [INTENT]: Endpoint di login agency-admin (POST /api/v1/platform/auth/token). Anonimo, niente tenant.

using FluentValidation;
using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Core.Dtos.Platform;

namespace WebAgency_BookingSystem.Api.Endpoints.Platform;

internal static class PlatformAuthEndpoints
{
    public static IEndpointRouteBuilder MapPlatformAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/platform/auth/token", async (
            PlatformLoginRequest request, IValidator<PlatformLoginRequest> validator,
            IPlatformAuthService auth, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return validation.ToValidationProblem();
            var result = await auth.LoginAsync(request, ct);
            return result.Match(token => Results.Ok(token));
        })
        .WithName("PlatformLogin").WithSummary("Login agency-admin").WithTags("Platform")
        .Produces<AdminTokenResponse>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)
        .RequireRateLimiting(RateLimitingPolicies.AccountSecurity);
        return app;
    }
}
```
`PlatformEndpoints.cs` aggregatore:
```csharp
// [INTENT]: Registrazione centralizzata degli endpoint /api/v1/platform.
namespace WebAgency_BookingSystem.Api.Endpoints.Platform;

internal static class PlatformEndpoints
{
    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPlatformAuthEndpoints();
        return app;
    }
}
```

- [ ] **Step 7: Program.cs — audience, policy, OnTokenValidated branch, map**

In `Program.cs`:
1. `TokenValidationParameters`: sostituire `ValidateAudience=true; ValidAudience=jwtSettings.Audience;` con:
```csharp
            ValidateAudience = true,
            ValidAudiences = [jwtSettings.Audience, jwtSettings.PlatformAudience],
```
2. `OnTokenValidated`: sostituire la validazione stamp con il branch per ruolo:
```csharp
            OnTokenValidated = async context =>
            {
                string? sub = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                string? stampClaim = context.Principal?.FindFirst(AdminClaims.SecurityStamp)?.Value;
                string? role = context.Principal?.FindFirst(ClaimTypes.Role)?.Value;
                if (!Guid.TryParse(sub, out Guid id) || !Guid.TryParse(stampClaim, out Guid stamp))
                {
                    context.Fail("Token privo dei claim richiesti.");
                    return;
                }
                // WHY: token platform e tenant hanno store di stamp diversi; discriminare sul ruolo è obbligatorio,
                // altrimenti un token valido verrebbe rifiutato cercando l'id nello store sbagliato.
                bool ok = role == AdminClaims.PlatformRole
                    ? await context.HttpContext.RequestServices.GetRequiredService<IPlatformSecurityStampService>().IsCurrentAsync(id, stamp, context.HttpContext.RequestAborted)
                    : await context.HttpContext.RequestServices.GetRequiredService<IUserSecurityStampService>().IsCurrentAsync(id, stamp, context.HttpContext.RequestAborted);
                if (!ok) context.Fail("Sessione non più valida.");
            },
```
(Aggiungi `using System.Security.Claims;` se non presente — c'è già.)
3. Authorization policy: **sostituisci** la riga esistente `builder.Services.AddAuthorization();` con:
```csharp
builder.Services.AddAuthorization(options =>
    options.AddPolicy(AdminClaims.PlatformPolicy, p => p
        .RequireRole(AdminClaims.PlatformRole)
        .RequireClaim("aud", jwtSettings.PlatformAudience)));
```
4. Mappa gli endpoint: dopo `app.MapAdminEndpoints();` aggiungi `app.MapPlatformEndpoints();` e `using WebAgency_BookingSystem.Api.Endpoints.Platform;`.

- [ ] **Step 8: DI**

In `DependencyInjection.cs`:
```csharp
        services.AddScoped<IPlatformSecurityStampService, PlatformSecurityStampService>();
        services.AddScoped<IPlatformAuthService, PlatformAuthService>();
```

- [ ] **Step 9: Build + unit test + commit**

Run: `dotnet build src/WebAgency_BookingSystem.Api` → 0/0; (PowerShell) `dotnet test tests/WebAgency_BookingSystem.UnitTests` → PASS.
```bash
git add -A
git commit -m "feat(platform): JWT platform + login + stamp + policy/audience + OnTokenValidated branch"
```

---

## Task 4: Setup / break-glass endpoint + cambio password platform

**Files:**
- Create: `src/WebAgency_BookingSystem.Core/Abstractions/Services/IPlatformAccountService.cs`
- Create: `src/WebAgency_BookingSystem.Infrastructure/Services/Platform/PlatformAccountService.cs`
- Create: `src/WebAgency_BookingSystem.Api/Endpoints/Platform/PlatformAccountEndpoints.cs`
- Create: `src/WebAgency_BookingSystem.Api/Validation/PlatformSetupRequestValidator.cs` (riusa `ChangePasswordRequestValidator` esistente per il cambio)
- Modify: `PlatformEndpoints.cs` (mappa account)
- Modify: `DependencyInjection.cs` (registra service)

**Interfaces:**
- Produces: `IPlatformAccountService`:
  - `Task<Result<bool>> SetupAsync(PlatformSetupRequest request, CancellationToken ct=default)` (ritorna `created`; errori: 404 se setup disabilitato, 401 token errato)
  - `Task<Result> ChangePasswordAsync(Guid platformAdminId, ChangePasswordRequest request, CancellationToken ct=default)`
- Consumes: `IPlatformAdminRepository`, `IPlatformSecurityStampService`, `AccountSettings` (PasswordMinLength), config `PLATFORM_SETUP_TOKEN`.

- [ ] **Step 1: Service**

```csharp
// [INTENT]: Operazioni sull'account agency-admin: setup/break-glass (crea-o-reimposta per email, gated da env
// token) e cambio password autenticato. Rigenera la SecurityStamp invalidando i JWT precedenti.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Core.Dtos.Platform;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Services.Platform;

internal sealed class PlatformAccountService : IPlatformAccountService
{
    private readonly IPlatformAdminRepository _admins;
    private readonly IPlatformSecurityStampService _stamps;
    private readonly string? _setupToken;
    private readonly ILogger<PlatformAccountService> _logger;

    public PlatformAccountService(IPlatformAdminRepository admins, IPlatformSecurityStampService stamps,
        IConfiguration configuration, ILogger<PlatformAccountService> logger)
    {
        _admins = admins;
        _stamps = stamps;
        _setupToken = configuration["PLATFORM_SETUP_TOKEN"];
        _logger = logger;
    }

    public async Task<Result<bool>> SetupAsync(PlatformSetupRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_setupToken))
        {
            // Endpoint disabilitato se l'env non è configurato.
            return Error.NotFound("not_found", "Risorsa non trovata.");
        }
        // WHY: confronto a tempo costante per non rivelare il token via timing.
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(request.SetupToken ?? string.Empty),
                System.Text.Encoding.UTF8.GetBytes(_setupToken)))
        {
            return Error.Unauthorized("unauthorized", "Token di setup non valido.");
        }
        string hash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        bool created = await _admins.UpsertPasswordByEmailAsync(request.Email, hash, ct);
        _logger.LogInformation("Platform setup: admin {Action} per {Email}", created ? "creato" : "reimpostato", request.Email);
        return Result.Success(created);
    }

    public async Task<Result> ChangePasswordAsync(Guid platformAdminId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        PlatformAdmin? admin = await _admins.GetTrackedByIdAsync(platformAdminId, ct);
        if (admin is null || admin.PasswordHash is null)
            return Error.Unauthorized("unauthorized", "Operazione non consentita.");
        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, admin.PasswordHash))
            return Error.Validation("password_corrente_errata", "La password attuale non è corretta.");
        admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        admin.SecurityStamp = Guid.NewGuid();
        await _admins.SaveChangesAsync(ct);
        _stamps.Invalidate(admin.Id);
        return Result.Success();
    }
}
```
Definisci `IPlatformAccountService` come da Interfaces block.

- [ ] **Step 2: Validators**

`PlatformSetupRequestValidator.cs`: `SetupToken` NotEmpty; `Email` NotEmpty+EmailAddress; `NewPassword`→ qui il campo è `Password`: MinimumLength `settings.PasswordMinLength`. (Inietta `AccountSettings`.)
Per il cambio password riusa l'esistente `ChangePasswordRequestValidator` (Core.Dtos.Admin.ChangePasswordRequest).

- [ ] **Step 3: Endpoints**

`PlatformAccountEndpoints.cs`:
```csharp
// [INTENT]: Endpoint account agency-admin: setup/break-glass (anonimo, gated da setup token) e cambio password (JWT).

using System.Security.Claims;
using FluentValidation;
using Microsoft.IdentityModel.JsonWebTokens;
using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Core.Dtos.Platform;
using WebAgency_BookingSystem.Infrastructure.Auth;

namespace WebAgency_BookingSystem.Api.Endpoints.Platform;

internal static class PlatformAccountEndpoints
{
    public static IEndpointRouteBuilder MapPlatformAccountEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/platform").WithTags("Platform");

        group.MapPost("/setup", async (
            PlatformSetupRequest request, IValidator<PlatformSetupRequest> validator,
            IPlatformAccountService account, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return validation.ToValidationProblem();
            var result = await account.SetupAsync(request, ct);
            return result.IsSuccess
                ? Results.Json(new { created = result.Value }, statusCode: StatusCodes.Status200OK)
                : result.Error.ToErrorResult();
        })
        .WithName("PlatformSetup").WithSummary("Setup/break-glass agency-admin")
        .WithDescription("Crea o reimposta la password dell'agency-admin per email. Gated da PLATFORM_SETUP_TOKEN; 404 se non configurato.")
        .Produces(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
        .RequireRateLimiting(RateLimitingPolicies.AccountSecurity);

        group.MapPost("/account/password", async (
            ChangePasswordRequest request, IValidator<ChangePasswordRequest> validator,
            ClaimsPrincipal principal, IPlatformAccountService account, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return validation.ToValidationProblem();
            string? sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (!Guid.TryParse(sub, out Guid id)) return Results.Forbid();
            var result = await account.ChangePasswordAsync(id, request, ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToErrorResult();
        })
        .WithName("PlatformChangePassword").WithSummary("Cambia password agency-admin")
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .RequireAuthorization(AdminClaims.PlatformPolicy)
        .RequireRateLimiting(RateLimitingPolicies.AccountSecurity);

        return app;
    }
}
```
In `PlatformEndpoints.cs` aggiungi `app.MapPlatformAccountEndpoints();`. In DI: `services.AddScoped<IPlatformAccountService, PlatformAccountService>();`.

- [ ] **Step 4: Build + commit**

Run: `dotnet build src/WebAgency_BookingSystem.Api` → 0/0.
```bash
git add -A
git commit -m "feat(platform): setup break-glass + cambio password agency-admin"
```

---

## Task 5: Gestione tenant (create/list/get) via API platform

**Files:**
- Create: `src/WebAgency_BookingSystem.Core/Dtos/Platform/PlatformTenantDtos.cs`
- Create: `src/WebAgency_BookingSystem.Core/Abstractions/Services/IPlatformTenantService.cs`
- Create: `src/WebAgency_BookingSystem.Infrastructure/Services/Platform/PlatformTenantService.cs`
- Create: `src/WebAgency_BookingSystem.Api/Endpoints/Platform/PlatformTenantEndpoints.cs`
- Modify: `PlatformEndpoints.cs`, `DependencyInjection.cs`

**Interfaces:**
- Produces: `PlatformTenantSummary(Guid Id, string Slug, string Name, string SiteUrl, string OwnerEmail, bool Active, string CreatedAt)`; `PagedResponse<T>` (esistente).
- Produces: `IPlatformTenantService`:
  - `Task<Result<ProvisioningOutput>> CreateAsync(ProvisioningInput input, CancellationToken ct=default)`
  - `Task<PagedResponse<PlatformTenantSummary>> ListAsync(int page, int pageSize, CancellationToken ct=default)`
  - `Task<Result<PlatformTenantSummary>> GetAsync(Guid id, CancellationToken ct=default)`
- Consumes: `ITenantProvisioningService` (Task 1), `BookingSystemDbContext`.

- [ ] **Step 1: DTO**
```csharp
// [INTENT]: DTO di lettura tenant per l'API platform (lista/dettaglio).
namespace WebAgency_BookingSystem.Core.Dtos.Platform;

public sealed record PlatformTenantSummary(
    Guid Id, string Slug, string Name, string SiteUrl, string OwnerEmail, bool Active, string CreatedAt);
```

- [ ] **Step 2: Service**
```csharp
// [INTENT]: Logica platform di gestione tenant: crea (delega a ITenantProvisioningService), elenca e dettaglio
// cross-tenant (IgnoreQueryFilters). La creazione registra l'audit con attore platform.

using Microsoft.EntityFrameworkCore;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Platform;
using WebAgency_BookingSystem.Core.Dtos.Provisioning;
using WebAgency_BookingSystem.Core.Provisioning;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Services.Platform;

internal sealed class PlatformTenantService : IPlatformTenantService
{
    private readonly BookingSystemDbContext _db;
    private readonly ITenantProvisioningService _provisioning;

    public PlatformTenantService(BookingSystemDbContext db, ITenantProvisioningService provisioning)
    {
        _db = db;
        _provisioning = provisioning;
    }

    public Task<Result<ProvisioningOutput>> CreateAsync(ProvisioningInput input, CancellationToken ct = default) =>
        _provisioning.CreateAsync(input, ct);

    public async Task<PagedResponse<PlatformTenantSummary>> ListAsync(int page, int pageSize, CancellationToken ct = default)
    {
        int p = page < 1 ? 1 : page;
        int size = pageSize is < 1 or > 200 ? 50 : pageSize;
        IQueryable<Core.Entities.Tenant> q = _db.Tenants.AsNoTracking().IgnoreQueryFilters().OrderByDescending(t => t.CreatedAt);
        int total = await q.CountAsync(ct);
        List<PlatformTenantSummary> items = await q.Skip((p - 1) * size).Take(size)
            .Select(t => new PlatformTenantSummary(t.Id, t.Slug, t.Name, t.SiteUrl, t.OwnerEmail, t.Active, t.CreatedAt.ToString("o")))
            .ToListAsync(ct);
        return new PagedResponse<PlatformTenantSummary>(items, p, size, total);
    }

    public async Task<Result<PlatformTenantSummary>> GetAsync(Guid id, CancellationToken ct = default)
    {
        PlatformTenantSummary? t = await _db.Tenants.AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.Id == id)
            .Select(x => new PlatformTenantSummary(x.Id, x.Slug, x.Name, x.SiteUrl, x.OwnerEmail, x.Active, x.CreatedAt.ToString("o")))
            .FirstOrDefaultAsync(ct);
        return t is null ? Error.NotFound("not_found", "Tenant non trovato.") : Result.Success(t);
    }
}
```
**Verifica** la firma del costruttore di `PagedResponse<T>` (cerca `record PagedResponse`); adatta i parametri (items/page/pageSize/total) all'ordine reale. Definisci `IPlatformTenantService` come da Interfaces.

- [ ] **Step 3: Endpoints**
```csharp
// [INTENT]: Endpoint platform di gestione tenant (crea/lista/dettaglio). Protetti da policy PlatformAdmin.

using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Platform;
using WebAgency_BookingSystem.Core.Dtos.Provisioning;
using WebAgency_BookingSystem.Core.Provisioning;
using WebAgency_BookingSystem.Infrastructure.Auth;

namespace WebAgency_BookingSystem.Api.Endpoints.Platform;

internal static class PlatformTenantEndpoints
{
    public static IEndpointRouteBuilder MapPlatformTenantEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/platform/tenants").WithTags("Platform")
            .RequireAuthorization(AdminClaims.PlatformPolicy);

        group.MapPost("", async (ProvisioningInput input, IPlatformTenantService svc, CancellationToken ct) =>
        {
            // WHY: la validazione del provisioning è una lista piatta di messaggi → 422 con errors["provisioning"].
            IReadOnlyList<string> errors = ProvisioningValidator.Validate(input);
            if (errors.Count > 0)
            {
                return Results.Json(new ErrorResponse("validation_error", "Input di provisioning non valido.",
                    new Dictionary<string, string[]> { ["provisioning"] = [.. errors] }),
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            var result = await svc.CreateAsync(input, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : result.Error.ToErrorResult();
        })
        .WithName("PlatformCreateTenant").WithSummary("Crea tenant")
        .Produces<ProvisioningOutput>(StatusCodes.Status201Created)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("", async (int? page, int? pageSize, IPlatformTenantService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(page ?? 1, pageSize ?? 50, ct)))
        .WithName("PlatformListTenants").WithSummary("Elenca tenant (paginato)")
        .Produces<PagedResponse<PlatformTenantSummary>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", async (Guid id, IPlatformTenantService svc, CancellationToken ct) =>
        {
            var result = await svc.GetAsync(id, ct);
            return result.Match(t => Results.Ok(t));
        })
        .WithName("PlatformGetTenant").WithSummary("Dettaglio tenant")
        .Produces<PlatformTenantSummary>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        return app;
    }
}
```
(Rimuovi l'`using FluentValidation;` se l'analyzer segnala unused.) In `PlatformEndpoints.cs` aggiungi `app.MapPlatformTenantEndpoints();`. In DI: `services.AddScoped<IPlatformTenantService, PlatformTenantService>();`.

- [ ] **Step 4: Build + commit**

Run: `dotnet build src/WebAgency_BookingSystem.Api` → 0/0.
```bash
git add -A
git commit -m "feat(platform): API gestione tenant (crea/lista/dettaglio)"
```

---

## Task 6: Deactivate/reactivate (+ cache), API key cross-tenant, resend activation

**Files:**
- Modify: `src/WebAgency_BookingSystem.Core/Abstractions/Services/IPlatformTenantService.cs` (nuovi metodi)
- Modify: `src/WebAgency_BookingSystem.Infrastructure/Services/Platform/PlatformTenantService.cs`
- Modify: `src/WebAgency_BookingSystem.Api/Endpoints/Platform/PlatformTenantEndpoints.cs`

**Interfaces:**
- Produces (aggiunti a `IPlatformTenantService`):
  - `Task<Result> SetActiveAsync(Guid tenantId, bool active, CancellationToken ct=default)`
  - `Task<Result<IReadOnlyList<ApiKeyResponse>>> ListApiKeysAsync(Guid tenantId, CancellationToken ct=default)`
  - `Task<Result<CreateApiKeyResponse>> CreateApiKeyAsync(Guid tenantId, string? description, CancellationToken ct=default)`
  - `Task<Result> RevokeApiKeyAsync(Guid tenantId, Guid keyId, CancellationToken ct=default)`
  - `Task<Result> ResendOwnerActivationAsync(Guid tenantId, CancellationToken ct=default)`
- Consumes: `IMemoryCache`, `ApiKeyGenerator`/`ApiKeyHasher`, `IEmailOutbox`, `AccountSettings`, `SecurityTokenGenerator`. Riusa `ApiKeyResponse`/`CreateApiKeyResponse` (Core.Dtos.Admin).

- [ ] **Step 1: Estendere il service**

Inietta anche `IMemoryCache _cache`, `IEmailOutbox _outbox`, `AccountSettings _account` nel costruttore di `PlatformTenantService`. Aggiungi:
```csharp
    public async Task<Result> SetActiveAsync(Guid tenantId, bool active, CancellationToken ct = default)
    {
        Core.Entities.Tenant? tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return Error.NotFound("not_found", "Tenant non trovato.");
        tenant.Active = active;
        await _db.SaveChangesAsync(ct);
        if (!active)
        {
            // WHY: la risoluzione tenant per API key è cachata (apikey:{hash}); senza evacuazione il tenant
            // disattivato resterebbe risolvibile fino alla TTL. Rimuoviamo le voci di tutte le sue chiavi.
            List<string> hashes = await _db.TenantApiKeys.AsNoTracking().IgnoreQueryFilters()
                .Where(k => k.TenantId == tenantId).Select(k => k.KeyHash).ToListAsync(ct);
            foreach (string h in hashes) _cache.Remove($"apikey:{h}");
        }
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<ApiKeyResponse>>> ListApiKeysAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (!await _db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId, ct))
            return Error.NotFound("not_found", "Tenant non trovato.");
        List<ApiKeyResponse> keys = await _db.TenantApiKeys.AsNoTracking().IgnoreQueryFilters()
            .Where(k => k.TenantId == tenantId).OrderByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeyResponse(k.Id, k.KeyPrefix, k.Description, k.Active, k.CreatedAt.ToString("o")))
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<ApiKeyResponse>>(keys);
    }

    public async Task<Result<CreateApiKeyResponse>> CreateApiKeyAsync(Guid tenantId, string? description, CancellationToken ct = default)
    {
        if (!await _db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId, ct))
            return Error.NotFound("not_found", "Tenant non trovato.");
        GeneratedApiKey gen = ApiKeyGenerator.Generate();
        var entity = new Core.Entities.TenantApiKey
        {
            Id = Guid.NewGuid(), TenantId = tenantId, KeyHash = gen.KeyHash, KeyPrefix = gen.KeyPrefix,
            Description = string.IsNullOrWhiteSpace(description) ? "Chiave generata da Platform API" : description,
            Active = true, CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.TenantApiKeys.Add(entity);
        await _db.SaveChangesAsync(ct);
        return Result.Success(new CreateApiKeyResponse(entity.Id, gen.ApiKey, gen.KeyPrefix));
    }

    public async Task<Result> RevokeApiKeyAsync(Guid tenantId, Guid keyId, CancellationToken ct = default)
    {
        Core.Entities.TenantApiKey? key = await _db.TenantApiKeys.IgnoreQueryFilters()
            .FirstOrDefaultAsync(k => k.Id == keyId && k.TenantId == tenantId, ct);
        if (key is null) return Error.NotFound("not_found", "API key non trovata.");
        key.Active = false;
        await _db.SaveChangesAsync(ct);
        _cache.Remove($"apikey:{key.KeyHash}");
        return Result.Success();
    }

    public async Task<Result> ResendOwnerActivationAsync(Guid tenantId, CancellationToken ct = default)
    {
        Core.Entities.Tenant? tenant = await _db.Tenants.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return Error.NotFound("not_found", "Tenant non trovato.");
        Core.Entities.User? owner = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Role == Core.Enums.UserRole.Owner, ct);
        if (owner is null) return Error.NotFound("not_found", "Owner non trovato.");

        GeneratedSecurityToken gen = SecurityTokenGenerator.Generate();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        // Invalida i token attivazione attivi precedenti dell'Owner.
        List<Core.Entities.UserSecurityToken> previous = await _db.UserSecurityTokens.IgnoreQueryFilters()
            .Where(t => t.UserId == owner.Id && t.Purpose == Core.Enums.SecurityTokenPurpose.Activation && t.UsedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);
        foreach (var t in previous) t.UsedAt = now;
        _db.UserSecurityTokens.Add(new Core.Entities.UserSecurityToken
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = owner.Id, TokenHash = gen.TokenHash,
            Purpose = Core.Enums.SecurityTokenPurpose.Activation,
            ExpiresAt = now.AddHours(_account.ActivationTokenHours), CreatedAt = now,
        });
        string url = $"{_account.PublicBaseUrl}/api/v1/admin/account/activate?token={gen.Token}";
        _outbox.EnqueueAccountActivation(tenantId, tenant.Name, owner.Email, url);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
```
Aggiungi i `using`: `Microsoft.Extensions.Caching.Memory;`, `WebAgency_BookingSystem.Core.Security;`, `WebAgency_BookingSystem.Core.Dtos.Admin;`, `WebAgency_BookingSystem.Infrastructure.Auth;`, `WebAgency_BookingSystem.Infrastructure.Email;`.

- [ ] **Step 2: Endpoints**

In `PlatformTenantEndpoints.cs`, dentro `group` (già `RequireAuthorization(PlatformPolicy)`):
```csharp
        group.MapPost("/{id:guid}/deactivate", async (Guid id, IPlatformTenantService svc, CancellationToken ct) =>
        { var r = await svc.SetActiveAsync(id, false, ct); return r.IsSuccess ? Results.NoContent() : r.Error.ToErrorResult(); })
        .WithName("PlatformDeactivateTenant").WithSummary("Disattiva tenant")
        .Produces(StatusCodes.Status204NoContent).Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/reactivate", async (Guid id, IPlatformTenantService svc, CancellationToken ct) =>
        { var r = await svc.SetActiveAsync(id, true, ct); return r.IsSuccess ? Results.NoContent() : r.Error.ToErrorResult(); })
        .WithName("PlatformReactivateTenant").WithSummary("Riattiva tenant")
        .Produces(StatusCodes.Status204NoContent).Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/api-keys", async (Guid id, IPlatformTenantService svc, CancellationToken ct) =>
        { var r = await svc.ListApiKeysAsync(id, ct); return r.Match(list => Results.Ok(list)); })
        .WithName("PlatformListTenantApiKeys").WithSummary("Elenca API key del tenant")
        .Produces<IReadOnlyList<ApiKeyResponse>>(StatusCodes.Status200OK).Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/api-keys", async (Guid id, CreateApiKeyRequest request, IPlatformTenantService svc, CancellationToken ct) =>
        { var r = await svc.CreateApiKeyAsync(id, request.Description, ct);
          return r.IsSuccess ? Results.Json(r.Value, statusCode: StatusCodes.Status201Created) : r.Error.ToErrorResult(); })
        .WithName("PlatformCreateTenantApiKey").WithSummary("Crea API key per il tenant")
        .Produces<CreateApiKeyResponse>(StatusCodes.Status201Created).Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}/api-keys/{keyId:guid}", async (Guid id, Guid keyId, IPlatformTenantService svc, CancellationToken ct) =>
        { var r = await svc.RevokeApiKeyAsync(id, keyId, ct); return r.IsSuccess ? Results.NoContent() : r.Error.ToErrorResult(); })
        .WithName("PlatformRevokeTenantApiKey").WithSummary("Revoca API key del tenant")
        .Produces(StatusCodes.Status204NoContent).Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/owner/resend-activation", async (Guid id, IPlatformTenantService svc, CancellationToken ct) =>
        { var r = await svc.ResendOwnerActivationAsync(id, ct); return r.IsSuccess ? Results.Accepted() : r.Error.ToErrorResult(); })
        .WithName("PlatformResendOwnerActivation").WithSummary("Re-invia attivazione Owner")
        .Produces(StatusCodes.Status202Accepted).Produces<ErrorResponse>(StatusCodes.Status404NotFound);
```
Aggiungi `using WebAgency_BookingSystem.Core.Dtos.Admin;` (ApiKeyResponse, CreateApiKeyResponse, CreateApiKeyRequest).

- [ ] **Step 3: Build + commit**

Run: `dotnet build src/WebAgency_BookingSystem.Api` → 0/0.
```bash
git add -A
git commit -m "feat(platform): deactivate/reactivate (+cache), API key cross-tenant, resend attivazione Owner"
```

---

## Task 7: Seed PlatformAdmin nei test + suite di integrazione

**Files:**
- Modify: `tests/WebAgency_BookingSystem.IntegrationTests/Fixtures/TestData.cs` (seed PlatformAdmin)
- Modify: `tests/WebAgency_BookingSystem.IntegrationTests/Fixtures/BookingSystemFactory.cs` (env `PLATFORM_SETUP_TOKEN`)
- Create: `tests/WebAgency_BookingSystem.IntegrationTests/Platform/PlatformFlowTests.cs`

**Interfaces:**
- Consumes: tutti gli endpoint platform. Pattern come `AccountFlowTests`.

- [ ] **Step 1: Seed admin + env token**

In `TestData.cs` aggiungi costanti e seed (nel ramo fresh + guard in `EnsureLaterSeedAsync`):
```csharp
    public static readonly Guid PlatformAdminId = new("50000000-0000-0000-0000-000000000001");
    public const string PlatformEmail = "agency@test.example.it";
    public const string PlatformPassword = "PlatformPass123!";
```
seed:
```csharp
        db.PlatformAdmins.Add(new PlatformAdmin
        {
            Id = PlatformAdminId, Email = PlatformEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(PlatformPassword),
            SecurityStamp = Guid.NewGuid(), Active = true, ActivatedAt = now, CreatedAt = now, UpdatedAt = now,
        });
```
In `BookingSystemFactory.cs` aggiungi nel costruttore: `Environment.SetEnvironmentVariable("PLATFORM_SETUP_TOKEN", "test-setup-token");` e in config in-memory `["PLATFORM_SETUP_TOKEN"] = "test-setup-token"`.

- [ ] **Step 2: Test del flusso**

```csharp
// [INTENT]: Verifica end-to-end dell'API platform: login agency-admin, isolamento token platform↔tenant,
// crea tenant, lista/dettaglio, deactivate (cache), API key cross-tenant, setup break-glass.

using System.Net;
using System.Net.Http.Json;
using WebAgency_BookingSystem.IntegrationTests.Fixtures;
using Xunit;

namespace WebAgency_BookingSystem.IntegrationTests.Platform;

[Collection("Integration")]
public class PlatformFlowTests : IntegrationTestBase
{
    public PlatformFlowTests(BookingSystemFixture fixture) : base(fixture) { }

    private sealed record TokenDto(string Token, string TokenType, string ExpiresAt);

    private async Task<string> LoginPlatformAsync(HttpClient c)
    {
        var r = await c.PostAsJsonAsync("/api/v1/platform/auth/token",
            new { email = TestData.PlatformEmail, password = TestData.PlatformPassword });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<TokenDto>())!.Token;
    }

    [Fact]
    public async Task PlatformLogin_Works_AndTokenAccessesPlatformRoute()
    {
        var c = Fixture.Factory.CreateClient();
        var token = await LoginPlatformAsync(c);
        c.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var list = await c.GetAsync("/api/v1/platform/tenants?page=1&pageSize=5");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task TenantJwt_IsRejected_OnPlatformRoute()
    {
        var c = Fixture.Factory.CreateClient();
        var login = await c.PostAsJsonAsync("/api/v1/admin/auth/token",
            new { email = TestData.OwnerEmail, password = TestData.OwnerPassword });
        var token = (await login.Content.ReadFromJsonAsync<TokenDto>())!.Token;
        c.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var resp = await c.GetAsync("/api/v1/platform/tenants");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task CreateTenant_ThenListAndGet()
    {
        var c = Fixture.Factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new("Bearer", await LoginPlatformAsync(c));
        string slug = $"plat-{Guid.NewGuid():N}".Substring(0, 18);
        var body = new
        {
            slug, name = "Plat Test", siteUrl = "https://plat.example.it", ownerEmail = $"o-{slug}@test.it",
            services = new[] { new { localId = "s1", name = "Taglio", durationMinutes = 30, basePrice = 15.0 } },
        };
        var create = await c.PostAsJsonAsync("/api/v1/platform/tenants", body);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var list = await c.GetAsync("/api/v1/platform/tenants?pageSize=200");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task Setup_BreakGlass_ResetsPassword()
    {
        var c = Fixture.Factory.CreateClient();
        // env token corretto → reimposta la password dell'admin seedato.
        var ok = await c.PostAsJsonAsync("/api/v1/platform/setup",
            new { setupToken = "test-setup-token", email = TestData.PlatformEmail, password = "NewPlatform999!" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        // login con la nuova password.
        var login = await c.PostAsJsonAsync("/api/v1/platform/auth/token",
            new { email = TestData.PlatformEmail, password = "NewPlatform999!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        // token errato → 401.
        var bad = await c.PostAsJsonAsync("/api/v1/platform/setup",
            new { setupToken = "wrong", email = TestData.PlatformEmail, password = "Whatever123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        // ripristina la password seed per non sporcare altri test.
        await c.PostAsJsonAsync("/api/v1/platform/setup",
            new { setupToken = "test-setup-token", email = TestData.PlatformEmail, password = TestData.PlatformPassword });
    }
}
```

- [ ] **Step 3: Eseguire i test**

Run (PowerShell): `dotnet test tests/WebAgency_BookingSystem.IntegrationTests --filter PlatformFlowTests` → PASS.
Poi l'intera suite: `dotnet test tests/WebAgency_BookingSystem.IntegrationTests` e `dotnet test tests/WebAgency_BookingSystem.UnitTests` → PASS. Sistema eventuali rotture.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "test(platform): seed agency-admin + flusso login/isolamento/crea-tenant/setup"
```

---

## Task 8: Documentazione

**Files:**
- Modify: `CLAUDE.md` (sommario endpoint Platform + env `PLATFORM_SETUP_TOKEN`, `Jwt:PlatformAudience`)
- Modify: `Claude_Instructions/VISIONE_PRODOTTO_E_ROADMAP.md` (spunta filone 4.1 backend; nota osservabilità + frontend ancora aperti)
- Modify: `Claude_Instructions/GUIDA_INTEGRAZIONE_API.md` (sezione "API platform agenzia": login, crea tenant, gestione)
- Modify: `Claude_Instructions/DEVELOPMENT_PLAN.md` (changelog)

- [ ] **Step 1: Aggiorna i doc**

- CLAUDE.md: nuova sottosezione endpoint `### Platform (autenticazione: JWT PlatformAdmin)` con le rotte `/api/v1/platform/*`; aggiungi `PLATFORM_SETUP_TOKEN` e `Jwt:PlatformAudience` alle variabili/config; nota che l'identità platform è separata dai tenant.
- VISIONE_PRODOTTO_E_ROADMAP.md: in §4.1 segna **fatto il backend** (API provisioning/gestione); restano osservabilità (4.2) e frontend console.
- GUIDA_INTEGRAZIONE_API.md: sezione per l'agenzia su come autenticarsi e creare/gestire i tenant via API (con esempi di body `ProvisioningInput`).
- DEVELOPMENT_PLAN.md: voce changelog 2026-06-18 "Agency Provisioning API" con i punti chiave + i follow-up rimandati (§9 dello spec).

- [ ] **Step 2: Commit**

```bash
git add -A
git commit -m "docs(platform): API provisioning/gestione agenzia (endpoint, env, guida, roadmap)"
```

---

## Note finali
- **Audit (scoping documentato)**: la creazione tenant continua a scrivere `audit_log` con attore `"provisioning"` (il servizio condiviso è invariato). L'**attribuzione per-sorgente** (attore `platform-admin:{id}`) e l'**audit delle azioni platform** (deactivate/apikey/resend) sono **rimandate**: per ora quelle azioni sono tracciate dai **log applicativi** (persistiti su DB). Vedi spec §9.5. Quando si implementa, passare l'id dell'admin agente (dal claim `sub`) ai metodi mutativi e scrivere righe `AuditLog`.
- **CORS console**: l'origine della futura console va aggiunta a `Cors:AllowedOrigins` quando il frontend esiste (nessuna azione di codice ora).
- **Migration in dev**: dopo il merge, `dotnet ef database update` applica `AddPlatformAdmin` (in dev l'auto-migrate è già on).
- **Config nuova**: `PLATFORM_SETUP_TOKEN` (abilita/gate setup), `Jwt:PlatformAudience` (default `WebAgency_BookingSystem.Platform`).
- **Follow-up rimandati** (spec §9, documentati): invito multi-admin, reset password platform via email, attivazione primo admin via email, edit tenant (`PATCH /platform/tenants/{id}`).
- **Smoke test** consigliato al deploy: setup admin → login platform → crea tenant → verifica API key e attivazione Owner.
