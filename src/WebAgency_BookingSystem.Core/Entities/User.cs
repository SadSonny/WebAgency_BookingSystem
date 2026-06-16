// [INTENT]: Utente admin di un tenant (AD-02). Multi-utente con ruoli, autenticazione JWT (layer admin).
// La password è conservata solo come hash (bcrypt, generato nel layer Infrastructure/CLI). Email univoca
// a livello globale (un'email = un account = un'attività). PasswordHash nullable fino ad attivazione account.

using WebAgency_BookingSystem.Core.Enums;

namespace WebAgency_BookingSystem.Core.Entities;

/// <summary>
/// Account amministrativo associato a un tenant, usato per l'autenticazione admin via JWT.
/// </summary>
public class User : IAuditableEntity
{
    /// <summary>Identificativo univoco (PK).</summary>
    public Guid Id { get; set; }

    /// <summary>Tenant di appartenenza.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Email di login, univoca a livello globale (un'email = un account).</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Hash bcrypt della password; null finché l'account non è stato attivato dall'Owner.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>Istante di attivazione dell'account (UTC); null se mai attivato.</summary>
    public DateTimeOffset? ActivatedAt { get; set; }

    /// <summary>Marca di sicurezza: cambia a ogni mutazione di password e invalida i JWT emessi prima.</summary>
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();

    /// <summary>Ruolo di autorizzazione.</summary>
    public UserRole Role { get; set; } = UserRole.Owner;

    /// <summary>Se false, l'utente non può autenticarsi.</summary>
    public bool Active { get; set; } = true;

    /// <summary>Istante dell'ultimo login andato a buon fine (UTC); null se mai loggato.</summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Tentativi di login falliti consecutivi (S3). Azzerato a ogni login riuscito.</summary>
    public int FailedAccessCount { get; set; }

    /// <summary>Se valorizzato e nel futuro, l'account è bloccato fino a questo istante (UTC) — S3.</summary>
    public DateTimeOffset? LockoutEnd { get; set; }

    /// <summary>Istante di creazione (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Istante dell'ultimo aggiornamento (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    // ── Navigazione ───────────────────────────────────────────────────────────
    public Tenant? Tenant { get; set; }
}
