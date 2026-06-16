# Onboarding Owner: attivazione, login email, gestione password — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dare all'Owner il controllo sicuro delle proprie credenziali: attivazione account via link email (nessuna password trasmessa), login con sola email, cambio password autenticato, reset "password dimenticata", invalidazione JWT al cambio password.

**Architecture:** Il provisioning crea l'account senza password e accoda un'email di attivazione con token monouso (hash in DB). L'Owner imposta la password su una pagina HTML minimale servita dall'API; login con email globale → JWT con claim `security_stamp`. Il middleware JWT invalida i token con stamp obsoleto (cache `IMemoryCache`). Reset e cambio password riusano la stessa macchina a token. Il sito del cliente ospita login + pannello (Modello A); l'API resta backend + sole pagine set-password.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API, EF Core 10 / Npgsql, FluentValidation, BCrypt.Net, Microsoft.IdentityModel JWT, xUnit + Testcontainers.

**Spec di riferimento:** `docs/superpowers/specs/2026-06-16-onboarding-owner-credenziali-design.md`

---

## File Structure

**Nuovi file**
- `src/.../Core/Entities/UserSecurityToken.cs` — entità token (attivazione/reset).
- `src/.../Core/Enums/SecurityTokenPurpose.cs` — enum `Activation` | `PasswordReset`.
- `src/.../Core/Security/SecurityTokenGenerator.cs` — genera token random + hash (riusa `ApiKeyHasher`).
- `src/.../Core/Abstractions/Services/IAdminAccountService.cs` — contratto account (attiva/cambia/reset).
- `src/.../Core/Abstractions/Services/IUserSecurityStampService.cs` — contratto verifica/invalidazione stamp.
- `src/.../Core/Dtos/Admin/AdminAccountDtos.cs` — DTO request (set/change/reset).
- `src/.../Infrastructure/Auth/AccountSettings.cs` — config (base URL, scadenze token, policy password).
- `src/.../Infrastructure/Auth/UserSecurityStampService.cs` — cache stamp + invalidazione.
- `src/.../Infrastructure/Services/Admin/AdminAccountService.cs` — logica account.
- `src/.../Infrastructure/Persistence/Configurations/UserSecurityTokenConfiguration.cs` — mapping EF.
- `src/.../Api/Endpoints/Admin/AdminAccountEndpoints.cs` — endpoint + pagine HTML.
- `src/.../Api/Validation/SetPasswordRequestValidator.cs`, `ChangePasswordRequestValidator.cs`, `ResetRequestValidator.cs`.
- `src/.../Api/Http/AccountHtmlPages.cs` — HTML minimale set-password.
- Migration EF: `MakeEmailGlobalAndAddSecurityFields`, `AddUserSecurityTokens`.

**File modificati**
- `Core/Entities/User.cs` — `PasswordHash` nullable, `ActivatedAt`, `SecurityStamp`.
- `Core/Abstractions/Repositories/IUserRepository.cs` + `Infrastructure/.../UserRepository.cs` — lookup per email globale, per token, update password/stamp.
- `Core/Abstractions/Services/IJwtTokenGenerator.cs` + `Infrastructure/Auth/JwtTokenGenerator.cs` — param `securityStamp` + claim.
- `Infrastructure/Auth/AdminClaims.cs` — costante `SecurityStamp`.
- `Core/Dtos/Admin/AdminAuthDtos.cs` — `AdminLoginRequest` senza `TenantSlug`.
- `Api/Validation/AdminLoginRequestValidator.cs` — rimuove regola slug.
- `Infrastructure/Auth/AdminAuthService.cs` — login per email globale + stamp nel JWT.
- `Infrastructure/Persistence/Configurations/UserConfiguration.cs` — unique email globale, hash nullable.
- `Infrastructure/Email/EmailTemplateRenderer.cs` + `IEmailTemplateRenderer.cs` — template account.
- `Infrastructure/Email/EmailOutbox.cs` + `IEmailOutbox.cs` — enqueue account.
- `Core/Enums/EmailKind.cs` — nuovi valori.
- `Infrastructure/DependencyInjection.cs` — registra account service, stamp service, AccountSettings.
- `Api/Program.cs` — claim stamp in `OnTokenValidated`, policy rate-limit account, map endpoint.
- `Api/Endpoints/Admin/AdminEndpoints.cs` — registra `MapAdminAccountEndpoints`.
- `Api/Middleware/AdminContextMiddleware.cs` — esclude `/admin/account/activate` e `/admin/account/password/reset` (anonimi).
- `tools/.../TenantProvisioner.cs` + `Program.cs` + `ProvisioningModels.cs` (Result) — token+email, no password.
- `tests/.../Fixtures/TestData.cs` — seed utente admin attivo.
- Doc: `CLAUDE.md`, `Claude_Instructions/GUIDA_INTEGRAZIONE_API.md`, `Claude_Instructions/SICUREZZA_SQL_E_CREDENZIALI.md`, `Claude_Instructions/DEVELOPMENT_PLAN.md`.

---

## Phase 0 — Fondamenta: config e schema

### Task 1: AccountSettings (config)

**Files:**
- Create: `src/WebAgency_BookingSystem.Infrastructure/Auth/AccountSettings.cs`

- [ ] **Step 1: Creare la classe settings**

```csharp
// [INTENT]: Impostazioni dell'area account Owner lette dalla configurazione (sezione Account/env flat).
// Condivise tra API (link nelle email, policy password, scadenze token) e CLI di provisioning (link attivazione).
// PublicBaseUrl è l'URL pubblico del backend usato per costruire i link assoluti di attivazione/reset.

using Microsoft.Extensions.Configuration;

namespace WebAgency_BookingSystem.Infrastructure.Auth;

/// <summary>Parametri dell'onboarding/credenziali Owner.</summary>
public sealed record AccountSettings(
    string PublicBaseUrl,
    int ActivationTokenHours,
    int ResetTokenHours,
    int PasswordMinLength)
{
    /// <summary>Costruisce le impostazioni dalla configurazione con default ragionevoli.</summary>
    public static AccountSettings FromConfiguration(IConfiguration configuration)
    {
        string baseUrl = configuration["PUBLIC_BASE_URL"]
            ?? configuration["Account:PublicBaseUrl"]
            ?? "http://localhost:5022";

        int activation = configuration.GetValue<int?>("Account:ActivationTokenHours") ?? 72;
        int reset = configuration.GetValue<int?>("Account:ResetTokenHours") ?? 1;
        int minLen = configuration.GetValue<int?>("Account:PasswordMinLength") ?? 12;

        return new AccountSettings(baseUrl.TrimEnd('/'), activation, reset, minLen);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/WebAgency_BookingSystem.Infrastructure`
Expected: PASS (0 warning).

- [ ] **Step 3: Commit**

```bash
git add src/WebAgency_BookingSystem.Infrastructure/Auth/AccountSettings.cs
git commit -m "feat(account): AccountSettings (base URL, scadenze token, policy password)"
```

---

### Task 2: User entity — hash nullable, ActivatedAt, SecurityStamp, email globale

**Files:**
- Modify: `src/WebAgency_BookingSystem.Core/Entities/User.cs`
- Modify: `src/WebAgency_BookingSystem.Infrastructure/Persistence/Configurations/UserConfiguration.cs`

- [ ] **Step 1: Aggiornare l'entità User**

In `User.cs` cambiare il commento `[INTENT]` (email ora univoca globale) e sostituire/aggiungere proprietà:

```csharp
    /// <summary>Email di login, univoca a livello globale (un'email = un account).</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Hash bcrypt della password; null finché l'account non è stato attivato dall'Owner.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>Istante di attivazione dell'account (UTC); null se mai attivato.</summary>
    public DateTimeOffset? ActivatedAt { get; set; }

    /// <summary>Marca di sicurezza: cambia a ogni mutazione di password e invalida i JWT emessi prima.</summary>
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();
```

(rimuovere la vecchia `PasswordHash` non-nullable e la vecchia `Email` summary).

- [ ] **Step 2: Aggiornare il mapping EF**

In `UserConfiguration.cs`, sostituire le righe della password/email/indice:

```csharp
        builder.Property(u => u.Email).IsRequired().HasMaxLength(255);
        builder.Property(u => u.PasswordHash).HasMaxLength(255); // nullable: account non ancora attivato
        builder.Property(u => u.SecurityStamp).IsRequired();
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(50);
        builder.Property(u => u.Active).HasDefaultValue(true);

        // Email univoca GLOBALE (login per sola email): un'email = un account = un'attività.
        builder.HasIndex(u => u.Email).IsUnique();
```

Aggiornare il commento `[INTENT]` del file (email globale; password nullable fino ad attivazione).

- [ ] **Step 3: Generare la migration**

```bash
dotnet ef migrations add MakeEmailGlobalAndAddSecurityFields \
  --project src/WebAgency_BookingSystem.Infrastructure \
  --startup-project src/WebAgency_BookingSystem.Api
```

Expected: crea i file migration. **Verificare** che la migration: droppi l'indice `(tenant_id, email)`, crei l'indice unico su `email`, renda `password_hash` nullable, aggiunga `activated_at` e `security_stamp` (con default — vedi Step 4).

- [ ] **Step 4: Backfill `security_stamp` per righe esistenti**

Nel file migration `Up`, dopo `AddColumn<Guid>("security_stamp", ...)`, garantire un default non-vuoto sulle righe esistenti. Aggiungere a mano:

```csharp
    migrationBuilder.Sql("UPDATE users SET security_stamp = gen_random_uuid() WHERE security_stamp = '00000000-0000-0000-0000-000000000000';");
```

WHY: le righe pre-esistenti riceverebbero `Guid.Empty`; le inizializziamo a un valore casuale così il claim stamp non collide tra utenti.

- [ ] **Step 5: Build**

Run: `dotnet build`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/WebAgency_BookingSystem.Core/Entities/User.cs \
        src/WebAgency_BookingSystem.Infrastructure/Persistence/Configurations/UserConfiguration.cs \
        src/WebAgency_BookingSystem.Infrastructure/Persistence/Migrations/
git commit -m "feat(account): User email globale + hash nullable + ActivatedAt/SecurityStamp (migration)"
```

---

### Task 3: Entità UserSecurityToken + enum + mapping + migration

**Files:**
- Create: `src/WebAgency_BookingSystem.Core/Enums/SecurityTokenPurpose.cs`
- Create: `src/WebAgency_BookingSystem.Core/Entities/UserSecurityToken.cs`
- Create: `src/WebAgency_BookingSystem.Infrastructure/Persistence/Configurations/UserSecurityTokenConfiguration.cs`
- Modify: `src/WebAgency_BookingSystem.Infrastructure/Persistence/BookingSystemDbContext.cs` (aggiungere `DbSet`)

- [ ] **Step 1: Enum**

```csharp
// [INTENT]: Scopo di un token di sicurezza utente: attivazione iniziale dell'account o reset password.
// Persistito come stringa nella tabella user_security_token per leggibilità/stabilità.

namespace WebAgency_BookingSystem.Core.Enums;

/// <summary>Scopo del token di sicurezza utente.</summary>
public enum SecurityTokenPurpose
{
    /// <summary>Attivazione iniziale dell'account (prima impostazione password).</summary>
    Activation,

    /// <summary>Reset della password ("password dimenticata").</summary>
    PasswordReset,
}
```

- [ ] **Step 2: Entità**

```csharp
// [INTENT]: Token monouso a scadenza per attivazione account / reset password. In DB si conserva SOLO l'hash
// del token (mai il valore in chiaro, come per le API key): il confronto avviene per hash. UsedAt segna il
// consumo (monouso). Le query di validazione girano pre-auth (nessun tenant corrente) → IgnoreQueryFilters.

using WebAgency_BookingSystem.Core.Enums;

namespace WebAgency_BookingSystem.Core.Entities;

/// <summary>Token di sicurezza utente (attivazione/reset), conservato come hash, monouso e a scadenza.</summary>
public class UserSecurityToken
{
    /// <summary>Identificativo univoco (PK).</summary>
    public Guid Id { get; set; }

    /// <summary>Tenant proprietario (diagnostica/scoping); la validazione bypassa il global query filter.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Utente a cui il token appartiene.</summary>
    public Guid UserId { get; set; }

    /// <summary>Hash SHA-256 (hex) del token in chiaro. Mai il valore originale.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Scopo del token.</summary>
    public SecurityTokenPurpose Purpose { get; set; }

    /// <summary>Istante di scadenza (UTC): oltre, il token è rifiutato.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Istante di consumo (UTC); se valorizzato il token è già stato usato.</summary>
    public DateTimeOffset? UsedAt { get; set; }

    /// <summary>Istante di creazione (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    // ── Navigazione ───────────────────────────────────────────────────────────
    public User? User { get; set; }
}
```

- [ ] **Step 3: Mapping EF**

```csharp
// [INTENT]: Mapping EF Core di UserSecurityToken (tabella user_security_token). Enum come stringa. Indice su
// token_hash per la ricerca in validazione. FK all'utente in cascade.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Configurations;

internal sealed class UserSecurityTokenConfiguration : IEntityTypeConfiguration<UserSecurityToken>
{
    public void Configure(EntityTypeBuilder<UserSecurityToken> builder)
    {
        builder.ToTable("user_security_token");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(128);
        builder.Property(t => t.Purpose).IsRequired().HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(t => t.TokenHash);
        builder.HasIndex(t => new { t.UserId, t.Purpose });

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 4: DbSet nel DbContext**

In `BookingSystemDbContext.cs` aggiungere accanto agli altri `DbSet`:

```csharp
    public DbSet<UserSecurityToken> UserSecurityTokens => Set<UserSecurityToken>();
```

WHY (verifica): l'entità NON deve avere il global query filter tenant (le query pre-auth la leggono senza tenant). Confermare che il filtro globale è applicato solo alle entità che implementano l'interfaccia tenant-scoped del progetto; `UserSecurityToken` non la implementa, quindi è esente. Se il DbContext applica i filtri per convenzione su `TenantId`, escludere esplicitamente questa entità.

- [ ] **Step 5: Migration**

```bash
dotnet ef migrations add AddUserSecurityTokens \
  --project src/WebAgency_BookingSystem.Infrastructure \
  --startup-project src/WebAgency_BookingSystem.Api
```

- [ ] **Step 6: Build**

Run: `dotnet build`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/WebAgency_BookingSystem.Core/Enums/SecurityTokenPurpose.cs \
        src/WebAgency_BookingSystem.Core/Entities/UserSecurityToken.cs \
        src/WebAgency_BookingSystem.Infrastructure/Persistence/
git commit -m "feat(account): entita' UserSecurityToken (token attivazione/reset) + migration"
```

---

## Phase 1 — Token + email infra

### Task 4: SecurityTokenGenerator + repository utenti/token

**Files:**
- Create: `src/WebAgency_BookingSystem.Core/Security/SecurityTokenGenerator.cs`
- Create: `tests/WebAgency_BookingSystem.UnitTests/Security/SecurityTokenGeneratorTests.cs`
- Modify: `src/WebAgency_BookingSystem.Core/Abstractions/Repositories/IUserRepository.cs`
- Modify: `src/WebAgency_BookingSystem.Infrastructure/Persistence/Repositories/UserRepository.cs`

- [ ] **Step 1: Test del generatore (fallisce)**

```csharp
// [INTENT]: Verifica che SecurityTokenGenerator produca token opachi e hash deterministico/coerente con ApiKeyHasher.

using WebAgency_BookingSystem.Core.Security;
using Xunit;

namespace WebAgency_BookingSystem.UnitTests.Security;

public class SecurityTokenGeneratorTests
{
    [Fact]
    public void Generate_ProducesDistinctTokens_WithMatchingHash()
    {
        var a = SecurityTokenGenerator.Generate();
        var b = SecurityTokenGenerator.Generate();

        Assert.NotEqual(a.Token, b.Token);
        Assert.NotEqual(a.TokenHash, b.TokenHash);
        Assert.Equal(a.TokenHash, ApiKeyHasher.Hash(a.Token)); // hash riproducibile dal token in chiaro
        Assert.True(a.Token.Length >= 32);
    }
}
```

- [ ] **Step 2: Eseguire — deve fallire (tipo non esiste)**

Run: `dotnet test tests/WebAgency_BookingSystem.UnitTests --filter SecurityTokenGeneratorTests`
Expected: FAIL (compile error: SecurityTokenGenerator non definito).

- [ ] **Step 3: Implementare il generatore**

```csharp
// [INTENT]: Genera token di sicurezza opachi (attivazione/reset). Restituisce il valore in chiaro (da inserire
// nel link email, mostrato una sola volta) e l'hash SHA-256 da conservare. Riusa ApiKeyHasher per avere un'unica
// funzione di hash in tutto il progetto.

using System.Security.Cryptography;

namespace WebAgency_BookingSystem.Core.Security;

/// <summary>Esito della generazione: token in chiaro (per il link) e hash da salvare.</summary>
public readonly record struct GeneratedSecurityToken(string Token, string TokenHash);

/// <summary>Genera token di sicurezza casuali (256 bit) e il relativo hash di conservazione.</summary>
public static class SecurityTokenGenerator
{
    /// <summary>Crea un token casuale URL-safe e il suo hash SHA-256. Il token va comunicato una sola volta.</summary>
    public static GeneratedSecurityToken Generate()
    {
        string token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        return new GeneratedSecurityToken(token, ApiKeyHasher.Hash(token));
    }
}
```

- [ ] **Step 4: Eseguire — deve passare**

Run: `dotnet test tests/WebAgency_BookingSystem.UnitTests --filter SecurityTokenGeneratorTests`
Expected: PASS.

- [ ] **Step 5: Estendere `IUserRepository`**

Sostituire `GetByTenantAndEmailAsync` con `GetByEmailAsync` e aggiungere i metodi token/password. Nuovo contenuto dei membri:

```csharp
    /// <summary>Restituisce l'utente per email (univoca globale), oppure null. Bypassa il global query filter.</summary>
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);

    /// <summary>Restituisce l'utente tracked per id (pre-auth, bypassa il filtro tenant), o null.</summary>
    Task<User?> GetTrackedByIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Restituisce la SecurityStamp corrente dell'utente, o null se inesistente.</summary>
    Task<Guid?> GetSecurityStampAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Aggiunge un token di sicurezza invalidando quelli ancora attivi dello stesso scopo. NON persiste:
    /// il chiamante chiama <see cref="SaveChangesAsync"/> (così può accodare l'email nella stessa transazione).</summary>
    Task AddTokenInvalidatingPreviousAsync(UserSecurityToken token, CancellationToken ct = default);

    /// <summary>Restituisce un token valido (non scaduto, non usato) per hash+scopo, tracked, o null.</summary>
    Task<UserSecurityToken?> GetValidTokenAsync(string tokenHash, SecurityTokenPurpose purpose, CancellationToken ct = default);

    /// <summary>Persiste le modifiche tracked dal repository (entità caricate tramite i metodi tracked).</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
```

Mantenere `RegisterFailedAttemptAsync` e `RegisterSuccessfulLoginAsync`. Aggiungere `using WebAgency_BookingSystem.Core.Enums;`.

- [ ] **Step 6: Aggiornare `UserRepository`**

Sostituire `GetByTenantAndEmailAsync` e aggiungere i nuovi metodi:

```csharp
    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _db.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<User?> GetTrackedByIdAsync(Guid userId, CancellationToken ct = default) =>
        _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId, ct);

    public async Task<Guid?> GetSecurityStampAsync(Guid userId, CancellationToken ct = default)
    {
        var row = await _db.Users
            .AsNoTracking().IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => new { u.SecurityStamp })
            .FirstOrDefaultAsync(ct);
        return row?.SecurityStamp;
    }

    public async Task AddTokenInvalidatingPreviousAsync(UserSecurityToken token, CancellationToken ct = default)
    {
        // WHY: un solo token attivo per scopo: marchiamo "usati" i precedenti ancora validi prima di aggiungere.
        // NON salviamo qui: il chiamante chiama SaveChangesAsync così l'email può entrare nella stessa transazione.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<UserSecurityToken> previous = await _db.UserSecurityTokens
            .IgnoreQueryFilters()
            .Where(t => t.UserId == token.UserId && t.Purpose == token.Purpose && t.UsedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);
        foreach (UserSecurityToken t in previous)
        {
            t.UsedAt = now;
        }

        _db.UserSecurityTokens.Add(token);
    }

    public Task<UserSecurityToken?> GetValidTokenAsync(string tokenHash, SecurityTokenPurpose purpose, CancellationToken ct = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return _db.UserSecurityTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.Purpose == purpose
                                      && t.UsedAt == null && t.ExpiresAt > now, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
```

Aggiungere i `using` per `UserSecurityToken` e `SecurityTokenPurpose`. Aggiornare il commento `[INTENT]` (lookup per email globale).

- [ ] **Step 7: Build (i chiamanti di `GetByTenantAndEmailAsync` rompono — sistemati nel Task 6)**

Run: `dotnet build src/WebAgency_BookingSystem.Infrastructure`
Expected: può fallire SOLO per `AdminAuthService` (riferisce il vecchio metodo). Lasciare così: si sistema nel Task 6 (stesso build verde a fine fase). Se preferisci build verde subito, esegui Task 6 prima di committare.

- [ ] **Step 8: Commit**

```bash
git add src/WebAgency_BookingSystem.Core/Security/SecurityTokenGenerator.cs \
        tests/WebAgency_BookingSystem.UnitTests/Security/SecurityTokenGeneratorTests.cs \
        src/WebAgency_BookingSystem.Core/Abstractions/Repositories/IUserRepository.cs \
        src/WebAgency_BookingSystem.Infrastructure/Persistence/Repositories/UserRepository.cs
git commit -m "feat(account): generatore token sicurezza + repository (email globale, token, stamp)"
```

---

### Task 5: Email — nuovi tipi, template e accodamento account

**Files:**
- Modify: `src/WebAgency_BookingSystem.Core/Enums/EmailKind.cs`
- Modify: `src/WebAgency_BookingSystem.Infrastructure/Email/IEmailTemplateRenderer.cs`
- Modify: `src/WebAgency_BookingSystem.Infrastructure/Email/EmailTemplateRenderer.cs`
- Modify: `src/WebAgency_BookingSystem.Infrastructure/Email/IEmailOutbox.cs`
- Modify: `src/WebAgency_BookingSystem.Infrastructure/Email/EmailOutbox.cs`

- [ ] **Step 1: Nuovi EmailKind**

In `EmailKind.cs` aggiungere in fondo all'enum:

```csharp
    /// <summary>Invito ad attivare l'account Owner (link di attivazione).</summary>
    AccountActivation,

    /// <summary>Invito a reimpostare la password (link di reset).</summary>
    PasswordReset,

    /// <summary>Conferma di un'operazione su credenziali (attivazione/cambio/reset password).</summary>
    AccountSecurityConfirmation,
```

- [ ] **Step 2: Estendere il contratto renderer**

In `IEmailTemplateRenderer.cs` aggiungere:

```csharp
    /// <summary>Email con il link per attivare l'account e impostare la prima password.</summary>
    EmailMessage RenderAccountActivation(string businessName, string toEmail, string activationUrl);

    /// <summary>Email con il link per reimpostare la password.</summary>
    EmailMessage RenderPasswordReset(string businessName, string toEmail, string resetUrl);

    /// <summary>Email di conferma di un'operazione su credenziali (attivazione/cambio/reset).</summary>
    EmailMessage RenderAccountSecurityConfirmation(string businessName, string toEmail, string heading, string message);
```

- [ ] **Step 3: Implementare i template (riusa il Layout esistente)**

In `EmailTemplateRenderer.cs` aggiungere i metodi (usano `Layout`, `Encode`, `FooterHtml` già presenti). Per i link, una riga CTA dedicata:

```csharp
    public EmailMessage RenderAccountActivation(string businessName, string toEmail, string activationUrl)
    {
        string business = string.IsNullOrWhiteSpace(businessName) ? "BookingSystem" : businessName;
        string subject = $"Attiva il tuo account — {business}";
        string intro = "Il tuo account di gestione è stato creato. Imposta la tua password per attivarlo "
            + "(il link scade tra 72 ore).";
        string body = CtaHtml(activationUrl, "Attiva account e imposta password");
        string html = Layout(business, "Attiva il tuo account", intro, body, FooterHtml());
        string text = $"{intro}\n\nApri questo link per attivare l'account:\n{activationUrl}";
        return new EmailMessage(toEmail, business, subject, html, text);
    }

    public EmailMessage RenderPasswordReset(string businessName, string toEmail, string resetUrl)
    {
        string business = string.IsNullOrWhiteSpace(businessName) ? "BookingSystem" : businessName;
        string subject = $"Reimposta la password — {business}";
        string intro = "Abbiamo ricevuto una richiesta di reimpostazione password. Se non sei stato tu, ignora "
            + "questa email. Il link scade tra 1 ora.";
        string body = CtaHtml(resetUrl, "Reimposta password");
        string html = Layout(business, "Reimposta la password", intro, body, FooterHtml());
        string text = $"{intro}\n\nApri questo link per reimpostare la password:\n{resetUrl}";
        return new EmailMessage(toEmail, business, subject, html, text);
    }

    public EmailMessage RenderAccountSecurityConfirmation(string businessName, string toEmail, string heading, string message)
    {
        string business = string.IsNullOrWhiteSpace(businessName) ? "BookingSystem" : businessName;
        string subject = $"{heading} — {business}";
        string body = $"<tr><td style=\"padding:6px 12px;color:#111;font-size:14px;\">{Encode(message)}</td></tr>";
        string html = Layout(business, heading, Encode(message), body, FooterHtml());
        string text = $"{heading}\n\n{message}";
        return new EmailMessage(toEmail, business, subject, html, text);
    }

    // CTA a bottone, con fallback testuale dell'URL (alcuni client non rendono i bottoni).
    private static string CtaHtml(string url, string label) =>
        $"<tr><td style=\"padding:16px 12px;\">"
        + $"<a href=\"{Encode(url)}\" style=\"display:inline-block;background:#111827;color:#ffffff;"
        + $"text-decoration:none;padding:12px 20px;border-radius:6px;font-size:15px;font-weight:600;\">{Encode(label)}</a>"
        + $"<div style=\"margin-top:12px;color:#6b7280;font-size:12px;word-break:break-all;\">{Encode(url)}</div>"
        + "</td></tr>";
```

- [ ] **Step 4: Estendere `IEmailOutbox`**

In `IEmailOutbox.cs` aggiungere:

```csharp
    /// <summary>Accoda l'email di attivazione account (link). Va chiamato nella transazione del provisioning/creazione.</summary>
    void EnqueueAccountActivation(Guid tenantId, string businessName, string toEmail, string activationUrl);

    /// <summary>Accoda l'email di reset password (link).</summary>
    void EnqueuePasswordReset(Guid tenantId, string businessName, string toEmail, string resetUrl);

    /// <summary>Accoda l'email di conferma di un'operazione su credenziali.</summary>
    void EnqueueAccountSecurityConfirmation(Guid tenantId, string businessName, string toEmail, string heading, string message);
```

- [ ] **Step 5: Implementare in `EmailOutbox`**

Aggiungere un overload privato di `Enqueue` per messaggi senza booking, e i tre metodi:

```csharp
    public void EnqueueAccountActivation(Guid tenantId, string businessName, string toEmail, string activationUrl) =>
        EnqueueAccount(_renderer.RenderAccountActivation(businessName, toEmail, activationUrl), EmailKind.AccountActivation, tenantId);

    public void EnqueuePasswordReset(Guid tenantId, string businessName, string toEmail, string resetUrl) =>
        EnqueueAccount(_renderer.RenderPasswordReset(businessName, toEmail, resetUrl), EmailKind.PasswordReset, tenantId);

    public void EnqueueAccountSecurityConfirmation(Guid tenantId, string businessName, string toEmail, string heading, string message) =>
        EnqueueAccount(_renderer.RenderAccountSecurityConfirmation(businessName, toEmail, heading, message), EmailKind.AccountSecurityConfirmation, tenantId);

    private void EnqueueAccount(EmailMessage message, EmailKind kind, Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(message.ToEmail))
        {
            _logger.LogWarning("Email account '{Kind}' non accodata: destinatario assente.", kind);
            return;
        }

        _db.OutboxEmails.Add(new OutboxEmail
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BookingId = null, // email non legata a una prenotazione
            Kind = kind,
            Status = OutboxEmailStatus.Pending,
            ToEmail = message.ToEmail,
            ToName = message.ToName,
            Subject = message.Subject,
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody,
            Attempts = 0,
            NextAttemptAt = DateTimeOffset.UtcNow,
        });
    }
```

- [ ] **Step 6: Build**

Run: `dotnet build src/WebAgency_BookingSystem.Infrastructure`
Expected: PASS (a parte l'eventuale rottura di AdminAuthService del Task 4, sistemata nel Task 6).

- [ ] **Step 7: Commit**

```bash
git add src/WebAgency_BookingSystem.Core/Enums/EmailKind.cs src/WebAgency_BookingSystem.Infrastructure/Email/
git commit -m "feat(account): template ed enqueue email attivazione/reset/conferma credenziali"
```

---

## Phase 2 — Login per email globale + SecurityStamp

### Task 6: Login per email, claim stamp, validazione stamp

**Files:**
- Modify: `src/WebAgency_BookingSystem.Core/Dtos/Admin/AdminAuthDtos.cs`
- Modify: `src/WebAgency_BookingSystem.Api/Validation/AdminLoginRequestValidator.cs`
- Modify: `src/WebAgency_BookingSystem.Core/Abstractions/Services/IJwtTokenGenerator.cs`
- Modify: `src/WebAgency_BookingSystem.Infrastructure/Auth/JwtTokenGenerator.cs`
- Modify: `src/WebAgency_BookingSystem.Infrastructure/Auth/AdminClaims.cs`
- Modify: `src/WebAgency_BookingSystem.Infrastructure/Auth/AdminAuthService.cs`
- Create: `src/WebAgency_BookingSystem.Core/Abstractions/Services/IUserSecurityStampService.cs`
- Create: `src/WebAgency_BookingSystem.Infrastructure/Auth/UserSecurityStampService.cs`

- [ ] **Step 1: DTO login senza slug**

In `AdminAuthDtos.cs`:

```csharp
public sealed record AdminLoginRequest(string Email, string Password);
```

Aggiornare il commento `[INTENT]` e la `<summary>` (login per sola email globale).

- [ ] **Step 2: Validator senza slug**

In `AdminLoginRequestValidator.cs` rimuovere la `RuleFor(x => x.TenantSlug)`; lasciare Email + Password.

- [ ] **Step 3: AdminClaims + JWT generator (claim stamp)**

In `AdminClaims.cs` aggiungere:

```csharp
    public const string SecurityStamp = "security_stamp";
```

In `IJwtTokenGenerator.cs` cambiare la firma:

```csharp
    (string Token, DateTimeOffset ExpiresAt) Generate(Guid userId, Guid tenantId, UserRole role, Guid securityStamp);
```

In `JwtTokenGenerator.cs` aggiornare la firma e aggiungere il claim nell'array:

```csharp
                new Claim(AdminClaims.SecurityStamp, securityStamp.ToString()),
```

- [ ] **Step 4: Stamp service (cache)**

`IUserSecurityStampService.cs`:

```csharp
// [INTENT]: Verifica che la SecurityStamp portata da un JWT sia ancora quella corrente dell'utente, così un
// cambio password invalida i token precedenti. La lettura è cache-first per non interrogare il DB a ogni
// richiesta admin; Invalidate rimuove la voce di cache dopo una mutazione di password.

namespace WebAgency_BookingSystem.Core.Abstractions.Services;

/// <summary>Convalida/invalida la SecurityStamp degli utenti admin (per l'invalidazione dei JWT).</summary>
public interface IUserSecurityStampService
{
    /// <summary>True se <paramref name="stamp"/> coincide con la stamp corrente dell'utente (cache-first).</summary>
    Task<bool> IsCurrentAsync(Guid userId, Guid stamp, CancellationToken ct = default);

    /// <summary>Rimuove la voce di cache dell'utente: il prossimo controllo rileggerà dal DB.</summary>
    void Invalidate(Guid userId);
}
```

`UserSecurityStampService.cs`:

```csharp
// [INTENT]: Implementazione cache-first di IUserSecurityStampService. La stamp corrente dell'utente è cachata
// in IMemoryCache (chiave "user-stamp:{userId}", TTL breve) per evitare una query DB a ogni richiesta admin.
// Invalidate (chiamato dopo cambio/reset/attivazione password) rimuove la voce così i vecchi JWT smettono di valere.

using Microsoft.Extensions.Caching.Memory;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Abstractions.Services;

namespace WebAgency_BookingSystem.Infrastructure.Auth;

internal sealed class UserSecurityStampService : IUserSecurityStampService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IUserRepository _users;
    private readonly IMemoryCache _cache;

    public UserSecurityStampService(IUserRepository users, IMemoryCache cache)
    {
        _users = users;
        _cache = cache;
    }

    public async Task<bool> IsCurrentAsync(Guid userId, Guid stamp, CancellationToken ct = default)
    {
        Guid? current = await _cache.GetOrCreateAsync(CacheKey(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return await _users.GetSecurityStampAsync(userId, ct);
        });
        return current is Guid g && g == stamp;
    }

    public void Invalidate(Guid userId) => _cache.Remove(CacheKey(userId));

    private static string CacheKey(Guid userId) => $"user-stamp:{userId}";
}
```

- [ ] **Step 5: AdminAuthService — login per email + stamp nel JWT**

Sostituire il corpo di `LoginAsync`: niente più lookup tenant per slug; risolvi utente per email, poi il tenant da `user.TenantId`. Mantieni lockout/attivazione/messaggio neutro.

```csharp
    public async Task<Result<AdminTokenResponse>> LoginAsync(AdminLoginRequest request, CancellationToken ct = default)
    {
        Error invalid = Error.Unauthorized("unauthorized", "Credenziali non valide.");

        User? user = await _users.GetByEmailAsync(request.Email, ct);
        if (user is not { Active: true } || user.PasswordHash is null)
        {
            // PasswordHash null = account non ancora attivato → stesso messaggio neutro.
            _logger.LogWarning("Login admin fallito (utente inesistente/non attivo/non attivato)");
            return invalid;
        }

        Tenant? tenant = await _tenants.GetByIdAsync(user.TenantId, ct);
        if (tenant is not { Active: true })
        {
            _logger.LogWarning("Login admin fallito: tenant {TenantId} inesistente o disattivato", user.TenantId);
            return invalid;
        }

        if (user.LockoutEnd is DateTimeOffset until && until > DateTimeOffset.UtcNow)
        {
            _logger.LogWarning("Login admin bloccato (lockout attivo) per utente {UserId}", user.Id);
            return invalid;
        }

        if (!VerifyPassword(request.Password, user.PasswordHash))
        {
            await _users.RegisterFailedAttemptAsync(user.Id, MaxFailedAttempts, LockoutDuration, ct);
            _logger.LogWarning("Login admin fallito (password errata) per utente {UserId}", user.Id);
            return invalid;
        }

        await _users.RegisterSuccessfulLoginAsync(user.Id, ct);
        (string token, DateTimeOffset expiresAt) = _jwt.Generate(user.Id, tenant.Id, user.Role, user.SecurityStamp);
        _logger.LogInformation("Login admin riuscito: utente {UserId} tenant {TenantId}", user.Id, tenant.Id);

        return Result.Success(new AdminTokenResponse(token, "Bearer", expiresAt.ToString("o")));
    }
```

In `VerifyPassword` cambiare la firma a `string passwordHash` (non-null, già garantito dal chiamante). Aggiornare il commento `[INTENT]` (login per email globale).

Verificare che `ITenantRepository` esponga `GetByIdAsync` (usato già da `AdminContextMiddleware`): sì.

- [ ] **Step 6: Build**

Run: `dotnet build`
Expected: PASS (ora tutti i riferimenti al vecchio metodo sono risolti).

- [ ] **Step 7: Aggiornare i test unit esistenti di login**

Cercare i test che costruiscono `AdminLoginRequest(slug, email, pwd)` o mockano `GetByTenantAndEmailAsync`/`Generate(...)`:

Run: `dotnet test tests/WebAgency_BookingSystem.UnitTests --filter AdminAuth`
Aggiornarli a: `AdminLoginRequest(email, pwd)`, mock `GetByEmailAsync`, `GetByIdAsync` del tenant, e `Generate(userId, tenantId, role, stamp)`. Impostare `PasswordHash` non-null e `Active=true` sugli utenti di test.

- [ ] **Step 8: Eseguire la suite unit**

Run: `dotnet test tests/WebAgency_BookingSystem.UnitTests`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/WebAgency_BookingSystem.Core/ src/WebAgency_BookingSystem.Infrastructure/Auth/ \
        src/WebAgency_BookingSystem.Api/Validation/AdminLoginRequestValidator.cs \
        tests/WebAgency_BookingSystem.UnitTests/
git commit -m "feat(account): login per email globale + claim/validazione SecurityStamp"
```

---

## Phase 3 — Account service, endpoint, pagine

### Task 7: IAdminAccountService + implementazione

**Files:**
- Create: `src/WebAgency_BookingSystem.Core/Dtos/Admin/AdminAccountDtos.cs`
- Create: `src/WebAgency_BookingSystem.Core/Abstractions/Services/IAdminAccountService.cs`
- Create: `src/WebAgency_BookingSystem.Infrastructure/Services/Admin/AdminAccountService.cs`

- [ ] **Step 1: DTO**

```csharp
// [INTENT]: DTO dell'area account Owner: impostazione password da token (attivazione/reset), cambio password
// autenticato, richiesta reset. Record immutabili come da convenzioni.

namespace WebAgency_BookingSystem.Core.Dtos.Admin;

/// <summary>Imposta la password tramite token (attivazione o reset).</summary>
public sealed record SetPasswordRequest(string Token, string NewPassword);

/// <summary>Cambio password autenticato (Owner loggato).</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Richiesta di reset password ("password dimenticata").</summary>
public sealed record PasswordResetRequest(string Email);
```

- [ ] **Step 2: Contratto servizio**

```csharp
// [INTENT]: Operazioni sull'account Owner: attivazione (prima password da token), cambio password autenticato,
// richiesta reset (invio email neutro) e reset (nuova password da token). Tutte rigenerano la SecurityStamp e
// accodano l'email di conferma. I messaggi d'errore sono neutri dove serve (no enumerazione utenti/token).

using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.Core.Abstractions.Services;

/// <summary>Gestione self-service delle credenziali Owner.</summary>
public interface IAdminAccountService
{
    /// <summary>Attiva l'account impostando la prima password a partire da un token di attivazione valido.</summary>
    Task<Result> ActivateAsync(SetPasswordRequest request, CancellationToken ct = default);

    /// <summary>Cambia la password dell'utente autenticato dopo aver verificato quella corrente.</summary>
    Task<Result> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default);

    /// <summary>Avvia un reset password: se l'email esiste accoda l'email di reset. Esito SEMPRE neutro.</summary>
    Task<Result> RequestPasswordResetAsync(PasswordResetRequest request, CancellationToken ct = default);

    /// <summary>Reimposta la password a partire da un token di reset valido.</summary>
    Task<Result> ResetPasswordAsync(SetPasswordRequest request, CancellationToken ct = default);
}
```

- [ ] **Step 3: Implementazione**

```csharp
// [INTENT]: Implementazione di IAdminAccountService. Usa UserSecurityToken (hash) per attivazione/reset, BCrypt
// per gli hash password, l'outbox per le email (conferme + invito reset) e rigenera la SecurityStamp a ogni
// mutazione (invalidando i JWT). La risoluzione tenant/business name avviene via repository (pre-auth → no filtro).

using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Core.Security;
using WebAgency_BookingSystem.Infrastructure.Auth;
using WebAgency_BookingSystem.Infrastructure.Email;

namespace WebAgency_BookingSystem.Infrastructure.Services.Admin;

internal sealed class AdminAccountService : IAdminAccountService
{
    private readonly IUserRepository _users;
    private readonly ITenantRepository _tenants;
    private readonly IEmailOutbox _outbox;
    private readonly IUserSecurityStampService _stamps;
    private readonly AccountSettings _settings;
    private readonly ILogger<AdminAccountService> _logger;

    public AdminAccountService(
        IUserRepository users, ITenantRepository tenants, IEmailOutbox outbox,
        IUserSecurityStampService stamps, AccountSettings settings, ILogger<AdminAccountService> logger)
    {
        _users = users;
        _tenants = tenants;
        _outbox = outbox;
        _stamps = stamps;
        _settings = settings;
        _logger = logger;
    }

    public Task<Result> ActivateAsync(SetPasswordRequest request, CancellationToken ct = default) =>
        ApplyTokenPasswordAsync(request, SecurityTokenPurpose.Activation, markActivated: true,
            heading: "Account attivato", confirmation: "Il tuo account è stato attivato. Ora puoi accedere.", ct);

    public Task<Result> ResetPasswordAsync(SetPasswordRequest request, CancellationToken ct = default) =>
        ApplyTokenPasswordAsync(request, SecurityTokenPurpose.PasswordReset, markActivated: false,
            heading: "Password reimpostata", confirmation: "La tua password è stata reimpostata.", ct);

    private async Task<Result> ApplyTokenPasswordAsync(
        SetPasswordRequest request, SecurityTokenPurpose purpose, bool markActivated,
        string heading, string confirmation, CancellationToken ct)
    {
        Error invalid = Error.Validation("token_non_valido", "Link non valido o scaduto. Richiedine uno nuovo.");

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return invalid;
        }

        string hash = ApiKeyHasher.Hash(request.Token);
        UserSecurityToken? token = await _users.GetValidTokenAsync(hash, purpose, ct);
        if (token is null)
        {
            return invalid;
        }

        User? user = await _users.GetTrackedByIdAsync(token.UserId, ct);
        if (user is null)
        {
            return invalid;
        }

        // WHY: token e user sono tracked dallo stesso DbContext → una sola SaveChanges committa tutto insieme.
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.SecurityStamp = Guid.NewGuid();
        if (markActivated)
        {
            user.ActivatedAt = DateTimeOffset.UtcNow;
        }

        token.UsedAt = DateTimeOffset.UtcNow;

        Tenant? tenant = await _tenants.GetByIdAsync(user.TenantId, ct);
        _outbox.EnqueueAccountSecurityConfirmation(user.TenantId, tenant?.Name ?? string.Empty, user.Email, heading, confirmation);

        await _users.SaveChangesAsync(ct); // vedi Step 4: metodo aggiunto al repository
        _stamps.Invalidate(user.Id);

        _logger.LogInformation("Account: {Purpose} completato per utente {UserId}", purpose, user.Id);
        return Result.Success();
    }

    public async Task<Result> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        User? user = await _users.GetTrackedByIdAsync(userId, ct);
        if (user is null || user.PasswordHash is null)
        {
            return Error.Unauthorized("unauthorized", "Operazione non consentita.");
        }

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return Error.Validation("password_corrente_errata", "La password attuale non è corretta.");
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.SecurityStamp = Guid.NewGuid();

        Tenant? tenant = await _tenants.GetByIdAsync(user.TenantId, ct);
        _outbox.EnqueueAccountSecurityConfirmation(user.TenantId, tenant?.Name ?? string.Empty, user.Email,
            "Password modificata", "La password del tuo account è stata modificata.");

        await _users.SaveChangesAsync(ct);
        _stamps.Invalidate(user.Id);

        _logger.LogInformation("Account: cambio password per utente {UserId}", user.Id);
        return Result.Success();
    }

    public async Task<Result> RequestPasswordResetAsync(PasswordResetRequest request, CancellationToken ct = default)
    {
        // WHY: risposta SEMPRE di successo (neutra) per non rivelare se l'email è registrata.
        User? user = await _users.GetByEmailAsync(request.Email, ct);
        if (user is { Active: true } && user.PasswordHash is not null)
        {
            GeneratedSecurityToken generated = SecurityTokenGenerator.Generate();
            var token = new UserSecurityToken
            {
                Id = Guid.NewGuid(),
                TenantId = user.TenantId,
                UserId = user.Id,
                TokenHash = generated.TokenHash,
                Purpose = SecurityTokenPurpose.PasswordReset,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(_settings.ResetTokenHours),
                CreatedAt = DateTimeOffset.UtcNow,
            };
            await _users.AddTokenInvalidatingPreviousAsync(token, ct); // accoda il token, NON salva ancora

            Tenant? tenant = await _tenants.GetByIdAsync(user.TenantId, ct);
            string url = $"{_settings.PublicBaseUrl}/api/v1/admin/account/password/reset?token={generated.Token}";
            _outbox.EnqueuePasswordReset(user.TenantId, tenant?.Name ?? string.Empty, user.Email, url);

            // WHY: un solo SaveChanges committa token + riga outbox insieme (atomicità).
            await _users.SaveChangesAsync(ct);

            _logger.LogInformation("Account: reset password richiesto per utente {UserId}", user.Id);
        }
        else
        {
            _logger.LogInformation("Account: reset password richiesto per email non registrata (risposta neutra)");
        }

        return Result.Success();
    }
}
```

- [ ] **Step 4: Verifica del modello transazionale**

`SaveChangesAsync` è già stato aggiunto a `IUserRepository`/`UserRepository` nel Task 4 e `AddTokenInvalidatingPreviousAsync` NON salva. Confermare che ogni metodo del servizio fa **una sola** `SaveChangesAsync` a fine flusso, **dopo** aver accodato l'email via outbox (stesso DbContext) → token/password/outbox committati atomicamente. Nessuna modifica aggiuntiva al repository qui.

- [ ] **Step 5: Build**

Run: `dotnet build`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/WebAgency_BookingSystem.Core/Dtos/Admin/AdminAccountDtos.cs \
        src/WebAgency_BookingSystem.Core/Abstractions/Services/IAdminAccountService.cs \
        src/WebAgency_BookingSystem.Infrastructure/Services/Admin/AdminAccountService.cs
git commit -m "feat(account): AdminAccountService (attiva/cambia/reset password, conferme email)"
```

---

### Task 8: Endpoint, pagine HTML, validator, DI, rate limit

**Files:**
- Create: `src/WebAgency_BookingSystem.Api/Http/AccountHtmlPages.cs`
- Create: `src/WebAgency_BookingSystem.Api/Validation/SetPasswordRequestValidator.cs`
- Create: `src/WebAgency_BookingSystem.Api/Validation/ChangePasswordRequestValidator.cs`
- Create: `src/WebAgency_BookingSystem.Api/Validation/PasswordResetRequestValidator.cs`
- Create: `src/WebAgency_BookingSystem.Api/Endpoints/Admin/AdminAccountEndpoints.cs`
- Modify: `src/WebAgency_BookingSystem.Api/Endpoints/Admin/AdminEndpoints.cs`
- Modify: `src/WebAgency_BookingSystem.Api/Middleware/AdminContextMiddleware.cs`
- Modify: `src/WebAgency_BookingSystem.Infrastructure/DependencyInjection.cs`
- Modify: `src/WebAgency_BookingSystem.Api/Program.cs`

- [ ] **Step 1: Validator (policy password)**

`SetPasswordRequestValidator.cs` — la lunghezza minima viene da `AccountSettings`:

```csharp
// [INTENT]: Validazione dell'impostazione password da token (attivazione/reset). La lunghezza minima è
// configurabile (AccountSettings.PasswordMinLength) per centralizzare la policy.

using FluentValidation;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Infrastructure.Auth;

namespace WebAgency_BookingSystem.Api.Validation;

public sealed class SetPasswordRequestValidator : AbstractValidator<SetPasswordRequest>
{
    public SetPasswordRequestValidator(AccountSettings settings)
    {
        RuleFor(x => x.Token).NotEmpty().WithMessage("Token mancante.");
        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("La password è obbligatoria.")
            .MinimumLength(settings.PasswordMinLength)
            .WithMessage($"La password deve avere almeno {settings.PasswordMinLength} caratteri.");
    }
}
```

`ChangePasswordRequestValidator.cs`:

```csharp
// [INTENT]: Validazione del cambio password autenticato: password corrente presente, nuova conforme alla policy
// e diversa dalla corrente.

using FluentValidation;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Infrastructure.Auth;

namespace WebAgency_BookingSystem.Api.Validation;

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator(AccountSettings settings)
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("La password attuale è obbligatoria.");
        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("La nuova password è obbligatoria.")
            .MinimumLength(settings.PasswordMinLength)
            .WithMessage($"La password deve avere almeno {settings.PasswordMinLength} caratteri.")
            .NotEqual(x => x.CurrentPassword).WithMessage("La nuova password deve essere diversa da quella attuale.");
    }
}
```

`PasswordResetRequestValidator.cs`:

```csharp
// [INTENT]: Validazione della richiesta di reset: email presente e formalmente valida. L'esito del servizio è
// comunque neutro (non rivela se l'email è registrata).

using FluentValidation;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.Api.Validation;

public sealed class PasswordResetRequestValidator : AbstractValidator<PasswordResetRequest>
{
    public PasswordResetRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("L'email è obbligatoria.")
            .EmailAddress().WithMessage("Formato email non valido.");
    }
}
```

WHY: `AddValidatorsFromAssembly` (già in Program.cs) registra anche i validator con dipendenze risolte da DI, quindi `AccountSettings` deve essere registrato (Task 8 Step 6).

- [ ] **Step 2: Pagine HTML minimali**

`AccountHtmlPages.cs` — `// EXCEPTION: AD-09` documentata:

```csharp
// [INTENT]: Pagine HTML minimali servite dall'API per impostare/reimpostare la password (atterraggio dei link
// via email). Sono l'UNICA UI servita dal backend.
// EXCEPTION (AD-09): AD-09 vieta una Admin UI/dashboard nel backend; qui deroghiamo per le sole pagine tecniche
// di set-password, necessarie perché i link email devono atterrare da qualche parte senza obbligare ogni
// agenzia a re-implementarle. Nessuna gestione prenotazioni: solo un form che fa POST alla nostra API.

using System.Net;

namespace WebAgency_BookingSystem.Api.Http;

internal static class AccountHtmlPages
{
    /// <summary>Pagina con form per impostare la password; <paramref name="postPath"/> è l'endpoint POST target.</summary>
    public static string SetPasswordPage(string title, string token, string postPath)
    {
        string safeToken = WebUtility.HtmlEncode(token);
        return $$"""
        <!DOCTYPE html>
        <html lang="it"><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <title>{{WebUtility.HtmlEncode(title)}}</title>
        <style>body{font-family:Arial,sans-serif;background:#f4f4f5;margin:0;padding:40px}
        .card{max-width:420px;margin:auto;background:#fff;border:1px solid #e4e4e7;border-radius:8px;padding:24px}
        h1{font-size:20px;color:#111827}input{width:100%;padding:10px;margin:8px 0;border:1px solid #d1d5db;border-radius:6px;box-sizing:border-box}
        button{width:100%;padding:12px;background:#111827;color:#fff;border:0;border-radius:6px;font-size:15px;cursor:pointer}
        .msg{margin-top:12px;font-size:14px}</style></head>
        <body><div class="card"><h1>{{WebUtility.HtmlEncode(title)}}</h1>
        <form id="f"><input type="password" id="pwd" placeholder="Nuova password" minlength="12" required>
        <input type="password" id="pwd2" placeholder="Conferma password" required>
        <button type="submit">Conferma</button></form><div class="msg" id="m"></div></div>
        <script>
        const f=document.getElementById('f'),m=document.getElementById('m');
        f.onsubmit=async e=>{e.preventDefault();
          if(pwd.value!==pwd2.value){m.textContent='Le password non coincidono.';m.style.color='#b91c1c';return;}
          const r=await fetch('{{postPath}}',{method:'POST',headers:{'Content-Type':'application/json'},
            body:JSON.stringify({token:'{{safeToken}}',newPassword:pwd.value})});
          if(r.ok){m.style.color='#15803d';m.textContent='Fatto! Ora puoi accedere dal sito della tua attività.';f.style.display='none';}
          else{const j=await r.json().catch(()=>null);m.style.color='#b91c1c';m.textContent=(j&&j.error&&j.error.message)||'Errore. Il link potrebbe essere scaduto.';}
        };
        </script></body></html>
        """;
    }
}
```

WHY: il form invia JSON all'endpoint POST corrispondente; al successo invita a tornare al login sul sito del cliente (Modello A). Il token è iniettato lato server (già validato in formato dal path query).

- [ ] **Step 3: Endpoint**

`AdminAccountEndpoints.cs`:

```csharp
// [INTENT]: Endpoint dell'area account Owner. Anonimi (token nel corpo): attivazione e reset (GET pagina + POST).
// Autenticato (JWT): cambio password. Le pagine GET servono HTML minimale; i POST delegano a IAdminAccountService.

using System.Security.Claims;
using FluentValidation;
using Microsoft.IdentityModel.JsonWebTokens;
using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.Api.Endpoints.Admin;

internal static class AdminAccountEndpoints
{
    public static IEndpointRouteBuilder MapAdminAccountEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/admin/account").WithTags("Admin");

        // ── Attivazione (anonima, token) ──────────────────────────────────────
        group.MapGet("/activate", (string token) =>
            Results.Content(
                AccountHtmlPages.SetPasswordPage("Attiva il tuo account", token, "/api/v1/admin/account/activate"),
                "text/html"))
            .WithName("AdminAccountActivatePage")
            .WithSummary("Pagina di attivazione account")
            .WithDescription("Pagina HTML con form per impostare la prima password a partire dal token di attivazione.")
            .ExcludeFromDescription();

        group.MapPost("/activate", async (
            SetPasswordRequest request, IValidator<SetPasswordRequest> validator,
            IAdminAccountService account, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return validation.ToValidationProblem();
            }

            var result = await account.ActivateAsync(request, ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToErrorResult();
        })
            .WithName("AdminAccountActivate")
            .WithSummary("Attiva account (imposta prima password)")
            .WithDescription("Imposta la prima password dall'invito di attivazione. Token monouso a scadenza.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)
            .RequireRateLimiting(RateLimitingPolicies.AccountSecurity);

        // ── Reset (anonimo, token) ────────────────────────────────────────────
        group.MapPost("/password/reset-request", async (
            PasswordResetRequest request, IValidator<PasswordResetRequest> validator,
            IAdminAccountService account, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return validation.ToValidationProblem();
            }

            await account.RequestPasswordResetAsync(request, ct);
            return Results.Accepted(); // esito neutro: 202 a prescindere
        })
            .WithName("AdminAccountResetRequest")
            .WithSummary("Richiedi reset password")
            .WithDescription("Invia (se l'email è registrata) un link di reset. La risposta è sempre 202, per non rivelare l'esistenza dell'email.")
            .Produces(StatusCodes.Status202Accepted)
            .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)
            .RequireRateLimiting(RateLimitingPolicies.AccountSecurity);

        group.MapGet("/password/reset", (string token) =>
            Results.Content(
                AccountHtmlPages.SetPasswordPage("Reimposta la password", token, "/api/v1/admin/account/password/reset"),
                "text/html"))
            .WithName("AdminAccountResetPage")
            .ExcludeFromDescription();

        group.MapPost("/password/reset", async (
            SetPasswordRequest request, IValidator<SetPasswordRequest> validator,
            IAdminAccountService account, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return validation.ToValidationProblem();
            }

            var result = await account.ResetPasswordAsync(request, ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToErrorResult();
        })
            .WithName("AdminAccountReset")
            .WithSummary("Reimposta password (da token)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .RequireRateLimiting(RateLimitingPolicies.AccountSecurity);

        // ── Cambio password (JWT) ─────────────────────────────────────────────
        group.MapPost("/password", async (
            ChangePasswordRequest request, IValidator<ChangePasswordRequest> validator,
            ClaimsPrincipal principal, IAdminAccountService account, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return validation.ToValidationProblem();
            }

            string? sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (!Guid.TryParse(sub, out Guid userId))
            {
                return Results.Forbid();
            }

            var result = await account.ChangePasswordAsync(userId, request, ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToErrorResult();
        })
            .WithName("AdminAccountChangePassword")
            .WithSummary("Cambia password (Owner autenticato)")
            .WithDescription("Cambia la password verificando quella corrente. Invalida i token JWT emessi prima del cambio.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingPolicies.AccountSecurity);

        return app;
    }
}
```

WHY: `JwtRegisteredClaimNames.Sub` è il claim `sub` impostato dal generatore con lo userId; `NameClaimType = Sub` in Program.cs. Verificare che `ToValidationProblem`/`ToErrorResult` siano già gli helper usati altrove (sì: `validation.ToValidationProblem()`, `result.Error.ToErrorResult()`).

- [ ] **Step 4: Registrare il gruppo**

In `AdminEndpoints.cs` aggiungere dentro `MapAdminEndpoints`:

```csharp
        app.MapAdminAccountEndpoints();
```

- [ ] **Step 5: Escludere gli endpoint anonimi dal middleware tenant**

In `AdminContextMiddleware.cs`, ampliare `RequiresAdminTenant` per escludere anche le rotte account anonime:

```csharp
    // Rotte /api/v1/admin che richiedono tenant dal JWT: tutte tranne /auth e le rotte account ANONIME
    // (attivazione e reset, autenticate dal token nel corpo, non dal JWT).
    private static bool RequiresAdminTenant(PathString path) =>
        path.StartsWithSegments("/api/v1/admin")
        && !path.StartsWithSegments("/api/v1/admin/auth")
        && !path.StartsWithSegments("/api/v1/admin/account/activate")
        && !path.StartsWithSegments("/api/v1/admin/account/password/reset");
```

WHY: `/api/v1/admin/account/password` (cambio autenticato) NON è escluso → passa dal middleware e ottiene il tenant dal JWT, corretto. Gli endpoint anonimi sì.

- [ ] **Step 6: DI**

In `DependencyInjection.cs` (metodo `AddInfrastructure`), aggiungere dopo la registrazione dell'auth admin:

```csharp
        // Account Owner (onboarding/credenziali): impostazioni, validazione stamp, servizio account.
        services.AddSingleton(AccountSettings.FromConfiguration(configuration));
        services.AddScoped<IUserSecurityStampService, UserSecurityStampService>();
        services.AddScoped<IAdminAccountService, AdminAccountService>();
```

Aggiungere il `using WebAgency_BookingSystem.Infrastructure.Services.Admin;` (già presente) e assicurarsi che `Auth` sia importato (già presente).

- [ ] **Step 7: Program.cs — validazione stamp nel JWT + rate-limit policy**

In `Program.cs`, dentro `options.Events = new JwtBearerEvents { ... }`, aggiungere `OnTokenValidated`:

```csharp
            OnTokenValidated = async context =>
            {
                // WHY (SecurityStamp): invalida i JWT emessi prima di un cambio password. Confronto cache-first.
                string? sub = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                string? stampClaim = context.Principal?.FindFirst(AdminClaims.SecurityStamp)?.Value;
                if (!Guid.TryParse(sub, out Guid userId) || !Guid.TryParse(stampClaim, out Guid stamp))
                {
                    context.Fail("Token privo dei claim richiesti.");
                    return;
                }

                var stamps = context.HttpContext.RequestServices.GetRequiredService<IUserSecurityStampService>();
                if (!await stamps.IsCurrentAsync(userId, stamp, context.HttpContext.RequestAborted))
                {
                    context.Fail("Sessione non più valida.");
                }
            },
```

Aggiungere i `using`: `WebAgency_BookingSystem.Core.Abstractions.Services;` e (già presenti) `WebAgency_BookingSystem.Infrastructure.Auth;`, `Microsoft.IdentityModel.JsonWebTokens;`, `Microsoft.Extensions.DependencyInjection`.

Definire la nuova policy di rate-limit. In `AddRateLimiter(...)` aggiungere dopo `BookingCreation`:

```csharp
    // Account: login/attivazione/reset/cambio password — partizione per IP, limite stringente anti brute-force.
    options.AddPolicy(RateLimitingPolicies.AccountSecurity, httpContext =>
    {
        string ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetSlidingWindowLimiter($"account:{ip}", _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0,
        });
    });
```

Aggiungere la costante in `RateLimitingPolicies` (cercare il file: `Grep RateLimitingPolicies`):

```csharp
    public const string AccountSecurity = "account-security";
```

Applicare anche al login `RequireRateLimiting(RateLimitingPolicies.AccountSecurity)` in `AdminAuthEndpoints.cs` (aggiungere alla catena dell'endpoint `AdminLogin`).

- [ ] **Step 8: Build**

Run: `dotnet build`
Expected: PASS (0 warning).

- [ ] **Step 9: Commit**

```bash
git add src/WebAgency_BookingSystem.Api/ src/WebAgency_BookingSystem.Infrastructure/DependencyInjection.cs
git commit -m "feat(account): endpoint attiva/cambia/reset + pagine HTML + DI + rate limit"
```

---

## Phase 4 — Provisioning

### Task 9: Provisioning — niente password, token + email di attivazione

**Files:**
- Modify: `tools/WebAgency_BookingSystem.TenantProvisioning/TenantProvisioner.cs`
- Modify: `tools/WebAgency_BookingSystem.TenantProvisioning/Program.cs`

- [ ] **Step 1: Provisioner — utente senza password + token attivazione + email**

In `TenantProvisioner.cs`:
1. Il costruttore deve ricevere anche `IEmailOutbox` e `AccountSettings`. Cambiare:

```csharp
    private readonly BookingSystemDbContext _db;
    private readonly IEmailOutbox _outbox;
    private readonly AccountSettings _account;

    public TenantProvisioner(BookingSystemDbContext db, IEmailOutbox outbox, AccountSettings account)
    {
        _db = db;
        _outbox = outbox;
        _account = account;
    }
```

2. Sostituire il blocco creazione utente + password. Rimuovere `string adminPassword = GeneratePassword();` e la `User { PasswordHash = BCrypt... }`. Inserire:

```csharp
        var ownerUser = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Email = input.OwnerEmail!,
            PasswordHash = null,        // account non ancora attivato
            ActivatedAt = null,
            SecurityStamp = Guid.NewGuid(),
            Role = UserRole.Owner,
            Active = true,
        };
        _db.Users.Add(ownerUser);

        // Token di attivazione (hash in DB) + email con il link. Tutto nella stessa transazione del tenant.
        GeneratedSecurityToken activation = SecurityTokenGenerator.Generate();
        _db.UserSecurityTokens.Add(new UserSecurityToken
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = ownerUser.Id,
            TokenHash = activation.TokenHash,
            Purpose = SecurityTokenPurpose.Activation,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(_account.ActivationTokenHours),
            CreatedAt = nowUtc,
        });

        string activationUrl = $"{_account.PublicBaseUrl}/api/v1/admin/account/activate?token={activation.Token}";
        _outbox.EnqueueAccountActivation(tenant.Id, tenant.Name, input.OwnerEmail!, activationUrl);
```

3. Rimuovere `GeneratePassword()` (non più usato) se non referenziato altrove.
4. Cambiare `ProvisioningResult` per non esporre la password: sostituire `AdminPassword` con `OwnerEmail` (già c'è `AdminEmail`) — togliere `adminPassword` dal `return`:

```csharp
        return new ProvisioningResult(
            tenant.Id, tenant.Slug, apiKey, keyPrefix, input.OwnerEmail!,
            input.Services!.Count, staffCount, closures);
```

E aggiornare il record `ProvisioningResult` rimuovendo `string AdminPassword`.

Aggiungere i `using`: `WebAgency_BookingSystem.Core.Security;`, `WebAgency_BookingSystem.Core.Enums;`, `WebAgency_BookingSystem.Infrastructure.Email;`, `WebAgency_BookingSystem.Infrastructure.Auth;`.

- [ ] **Step 2: Program.cs del tool — risolvere i servizi e aggiornare l'output**

In `BuildHost`, aggiungere alla config in-memory il `PUBLIC_BASE_URL` (da env/arg) così `AccountSettings` ha l'URL giusto:

```csharp
        ["Account:PublicBaseUrl"] = Environment.GetEnvironmentVariable("PUBLIC_BASE_URL") ?? "http://localhost:5022",
```

Nel blocco `try`, costruire il provisioner risolvendo i servizi dallo scope:

```csharp
        var outbox = scope.ServiceProvider.GetRequiredService<WebAgency_BookingSystem.Infrastructure.Email.IEmailOutbox>();
        var accountSettings = scope.ServiceProvider.GetRequiredService<WebAgency_BookingSystem.Infrastructure.Auth.AccountSettings>();
        var provisioner = new TenantProvisioner(db, outbox, accountSettings);
```

In `PrintResult`, sostituire la sezione credenziali admin:

```csharp
    Console.WriteLine("=== ACCOUNT ADMIN (Owner) ===");
    Console.WriteLine($"Email:    {result.AdminEmail}");
    Console.WriteLine("Attivazione: email con link inviata all'Owner (coda outbox).");
    Console.WriteLine("L'Owner imposta la password dal link; nessuna password viene generata qui.");
```

WHY: l'email parte dal dispatcher dell'API (stesso DB). In ambienti dove l'API non gira durante il provisioning, la riga resta `Pending` in `outbox_email` ed è inviata appena il dispatcher parte.

- [ ] **Step 3: Build del tool**

Run: `dotnet build tools/WebAgency_BookingSystem.TenantProvisioning`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add tools/WebAgency_BookingSystem.TenantProvisioning/
git commit -m "feat(account): provisioning crea Owner senza password + email di attivazione"
```

---

## Phase 5 — Seed test, test di integrazione, docs

### Task 10: Seed utente admin + test di integrazione

**Files:**
- Modify: `tests/WebAgency_BookingSystem.IntegrationTests/Fixtures/TestData.cs`
- Create: `tests/WebAgency_BookingSystem.IntegrationTests/Account/AccountFlowTests.cs`

- [ ] **Step 1: Seed di un Owner attivo nel TestData**

In `TestData.cs` aggiungere una costante e seminare l'utente (in `SeedAsync`, dopo l'API key). Password nota per i test di login:

```csharp
    public static readonly Guid OwnerUserId = new("40000000-0000-0000-0000-000000000001");
    public const string OwnerEmail = "owner@test.example.it";
    public const string OwnerPassword = "TestPassword123!";
```

Nel blocco di seed (ramo "tenant nuovo"):

```csharp
        db.Users.Add(new User
        {
            Id = OwnerUserId, TenantId = TenantId, Email = OwnerEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(OwnerPassword),
            ActivatedAt = now, SecurityStamp = Guid.NewGuid(),
            Role = UserRole.Owner, Active = true, CreatedAt = now, UpdatedAt = now,
        });
```

Aggiungere `using WebAgency_BookingSystem.Core.Enums;` (UserRole) se mancante. In `EnsureLaterSeedAsync` aggiungere un guard analogo che inserisce l'Owner se assente (container riusato).

- [ ] **Step 2: Test di integrazione del flusso**

```csharp
// [INTENT]: Verifica end-to-end del flusso account: login per email, attivazione via token, cambio password con
// invalidazione del vecchio JWT, reset neutro. Usa l'HttpClient della factory e legge il DB per i token.

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Core.Security;
using WebAgency_BookingSystem.IntegrationTests.Fixtures;
using WebAgency_BookingSystem.Infrastructure.Persistence;
using Xunit;

namespace WebAgency_BookingSystem.IntegrationTests.Account;

[Collection("Integration")]
public class AccountFlowTests : IntegrationTestBase
{
    public AccountFlowTests(BookingSystemFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Login_WithSeededOwner_ReturnsToken()
    {
        var client = Fixture.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/admin/auth/token",
            new { email = TestData.OwnerEmail, password = TestData.OwnerPassword });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_InvalidatesOldToken()
    {
        var client = Fixture.Factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/admin/auth/token",
            new { email = TestData.OwnerEmail, password = TestData.OwnerPassword });
        var token = (await login.Content.ReadFromJsonAsync<TokenDto>())!.Token;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var change = await client.PostAsJsonAsync("/api/v1/admin/account/password",
            new { currentPassword = TestData.OwnerPassword, newPassword = "BrandNewPass456!" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        // Stesso token (vecchio stamp) ora deve essere rifiutato su una rotta admin protetta.
        var afterChange = await client.GetAsync("/api/v1/admin/api-keys");
        Assert.Equal(HttpStatusCode.Unauthorized, afterChange.StatusCode);

        // Ripristina la password per non sporcare gli altri test.
        await ResetSeedPasswordAsync();
    }

    [Fact]
    public async Task Activation_WithValidToken_SetsPassword()
    {
        // Crea un utente non attivato + token di attivazione direttamente nel DB.
        Guid userId = Guid.NewGuid();
        var generated = SecurityTokenGenerator.Generate();
        using (var scope = Fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();
            db.Users.Add(new User
            {
                Id = userId, TenantId = TestData.TenantId, Email = $"new-{userId:N}@test.it",
                PasswordHash = null, SecurityStamp = Guid.NewGuid(), Role = UserRole.Owner,
                Active = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            });
            db.UserSecurityTokens.Add(new UserSecurityToken
            {
                Id = Guid.NewGuid(), TenantId = TestData.TenantId, UserId = userId,
                TokenHash = generated.TokenHash, Purpose = SecurityTokenPurpose.Activation,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(72), CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var client = Fixture.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/admin/account/activate",
            new { token = generated.Token, newPassword = "Activated123!" });
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        // Riuso del token → rifiutato.
        var reuse = await client.PostAsJsonAsync("/api/v1/admin/account/activate",
            new { token = generated.Token, newPassword = "Another123!" });
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
    }

    [Fact]
    public async Task ResetRequest_IsNeutral_ForUnknownEmail()
    {
        var client = Fixture.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/admin/account/password/reset-request",
            new { email = "nobody@nowhere.it" });
        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
    }

    private async Task ResetSeedPasswordAsync()
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == TestData.OwnerUserId);
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(TestData.OwnerPassword);
        user.SecurityStamp = Guid.NewGuid();
        await db.SaveChangesAsync();
    }

    private sealed record TokenDto(string Token, string TokenType, string ExpiresAt);
}
```

WHY: il test `ChangePassword_InvalidatesOldToken` può risentire della TTL cache stamp (5 min). Per renderlo deterministico, il cambio password chiama `Invalidate` sull'utente → la cache è svuotata subito, quindi il vecchio token è rifiutato al primo controllo successivo. (Se la cache fosse condivisa e non invalidata, il test fallirebbe: è la verifica che `Invalidate` funzioni.)

- [ ] **Step 3: Eseguire i test di integrazione**

Run: `dotnet test tests/WebAgency_BookingSystem.IntegrationTests --filter AccountFlowTests`
Expected: PASS. (Richiede Docker per Testcontainers.)

- [ ] **Step 4: Eseguire l'intera suite**

Run: `dotnet test`
Expected: PASS (unit + integration). Sistemare eventuali test rotti dal cambio login.

- [ ] **Step 5: Commit**

```bash
git add tests/
git commit -m "test(account): seed Owner + flusso login/attivazione/cambio/reset"
```

---

### Task 11: Documentazione

**Files:**
- Modify: `CLAUDE.md`
- Modify: `Claude_Instructions/GUIDA_INTEGRAZIONE_API.md`
- Modify: `Claude_Instructions/SICUREZZA_SQL_E_CREDENZIALI.md`
- Modify: `Claude_Instructions/DEVELOPMENT_PLAN.md`

- [ ] **Step 1: CLAUDE.md**

- Nel sommario endpoint admin, sostituire la riga login e aggiungere il gruppo account:

```
POST   /api/v1/admin/auth/token              (body { email, password } — NIENTE più tenantSlug)
GET|POST /api/v1/admin/account/activate
POST   /api/v1/admin/account/password         (JWT — cambio password)
POST   /api/v1/admin/account/password/reset-request
GET|POST /api/v1/admin/account/password/reset
```

- Aggiungere alle "Note runtime": login per **email globale**; le pagine set-password sono servite dall'API (deroga AD-09); env `PUBLIC_BASE_URL` per i link email.
- Aggiungere riga di stato di feature (V2.3 onboarding credenziali Owner).

- [ ] **Step 2: GUIDA_INTEGRAZIONE_API.md**

Documentare il flusso Modello A: l'agenzia costruisce login + pannello sul sito del cliente; chiamano `POST /admin/auth/token` con `{ email, password }`; gestione del JWT; il cambio password via `POST /admin/account/password`; il flusso di attivazione/reset (link email → pagina API). Aggiornare ogni esempio di login che usava `tenantSlug`.

- [ ] **Step 3: SICUREZZA_SQL_E_CREDENZIALI.md**

Aggiornare §2: il gap "cambio password" è **chiuso**; descrivere il flusso implementato (attivazione via link, login email, cambio/reset, SecurityStamp). Spostare il "piano" in "implementato il 2026-06-16".

- [ ] **Step 4: DEVELOPMENT_PLAN.md**

Aggiungere voce di changelog e una sezione "V2.3 — Onboarding credenziali Owner" con le decisioni (D1–D5) e i task completati. Spuntare eventuali checkbox.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md Claude_Instructions/
git commit -m "docs(account): onboarding Owner, login email, gestione password (V2.3)"
```

---

## Note operative finali

- **Migrazioni in dev:** dopo il merge, eseguire `dotnet ef database update` per applicare `MakeEmailGlobalAndAddSecurityFields` e `AddUserSecurityTokens`.
- **Config nuova:** `PUBLIC_BASE_URL` (API e CLI) per i link; opzionali `Account:ActivationTokenHours`, `Account:ResetTokenHours`, `Account:PasswordMinLength`.
- **Thunder Client:** aggiornare la request di login (rimuovere `tenantSlug`) e aggiungere le request account — fuori dal percorso critico, tracciato in `Claude_Instructions/TEST_API_THUNDER_CLIENT.md`.
- **Ordine di esecuzione consigliato:** Task 1→11 in sequenza. La build resta verde a fine di ogni Task tranne la finestra Task 4→6 (login), come annotato; se serve build verde a ogni commit, accorpare 4–6.
- **Semplificazione rispetto allo spec (D5):** la pagina set-password, al successo, mostra un messaggio generico ("accedi dal sito della tua attività") invece di un link al `siteUrl` reale del tenant. Costruire il link effettivo richiederebbe di risolvere il tenant dal token nella GET della pagina. È un **follow-up** a basso valore (l'Owner sa qual è il proprio sito); tracciarlo come miglioramento, non bloccante.
