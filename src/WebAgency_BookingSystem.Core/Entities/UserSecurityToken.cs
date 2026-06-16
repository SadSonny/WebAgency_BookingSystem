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
