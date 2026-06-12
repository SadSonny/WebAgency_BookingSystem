// [INTENT]: Utente admin di un tenant (AD-02). Multi-utente con ruoli, autenticazione JWT (layer admin).
// La password è conservata solo come hash (bcrypt, generato nel layer Infrastructure/CLI). Email univoca
// per tenant.

using WebAgency_BookingSystem.Core.Enums;

namespace WebAgency_BookingSystem.Core.Entities;

/// <summary>
/// Account amministrativo associato a un tenant, usato per l'autenticazione admin via JWT.
/// </summary>
public class User
{
    /// <summary>Identificativo univoco (PK).</summary>
    public Guid Id { get; set; }

    /// <summary>Tenant di appartenenza.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Email di login, univoca all'interno del tenant.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Hash bcrypt della password. Mai la password in chiaro.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Ruolo di autorizzazione.</summary>
    public UserRole Role { get; set; } = UserRole.Owner;

    /// <summary>Se false, l'utente non può autenticarsi.</summary>
    public bool Active { get; set; } = true;

    /// <summary>Istante dell'ultimo login andato a buon fine (UTC); null se mai loggato.</summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Istante di creazione (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Istante dell'ultimo aggiornamento (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    // ── Navigazione ───────────────────────────────────────────────────────────
    public Tenant? Tenant { get; set; }
}
