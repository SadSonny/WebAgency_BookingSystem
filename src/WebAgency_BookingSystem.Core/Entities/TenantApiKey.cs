// [INTENT]: Chiave API pubblica di un tenant. Nel DB si conserva SOLO l'hash SHA-256 della chiave
// (mai il valore in chiaro, mostrato una sola volta al provisioning). La risoluzione X-Api-Key -> tenant
// avviene hashando l'header ricevuto e cercando KeyHash tra le chiavi attive.

namespace WebAgency_BookingSystem.Core.Entities;

/// <summary>
/// Credenziale API pubblica associata a un tenant. Revocabile e rigenerabile singolarmente.
/// </summary>
public class TenantApiKey
{
    /// <summary>Identificativo univoco della chiave (PK).</summary>
    public Guid Id { get; set; }

    /// <summary>Tenant proprietario della chiave.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Hash SHA-256 della chiave in chiaro. Univoco. Mai reversibile.</summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>Primi caratteri della chiave per identificazione visiva, es. <c>a3f7b2c1</c>.</summary>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>Descrizione libera, es. "Chiave sito produzione".</summary>
    public string? Description { get; set; }

    /// <summary>Se false, la chiave non risolve più alcun tenant.</summary>
    public bool Active { get; set; } = true;

    /// <summary>Istante di creazione (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Istante di revoca (UTC), null se mai revocata.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    // ── Navigazione ───────────────────────────────────────────────────────────
    public Tenant? Tenant { get; set; }
}
