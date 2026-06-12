// [INTENT]: Astrazione del tenant corrente per la richiesta in corso. Popolata dal TenantResolutionMiddleware
// dopo aver risolto l'API key, e letta dal DbContext per applicare il global query filter su tenant_id.
// Disaccoppia il DbContext (Infrastructure) dal meccanismo HTTP di risoluzione (Api).

namespace WebAgency_BookingSystem.Core.Abstractions;

/// <summary>
/// Fornisce l'identità del tenant risolto per la richiesta corrente (scoped per-request).
/// </summary>
public interface ITenantContext
{
    /// <summary>Id del tenant corrente, oppure null se non ancora risolto (es. endpoint senza auth).</summary>
    Guid? TenantId { get; }

    /// <summary>True se un tenant è stato risolto per la richiesta corrente.</summary>
    bool IsResolved { get; }

    /// <summary>
    /// Imposta il tenant corrente. Chiamato una sola volta dal middleware di risoluzione.
    /// Garantisce che tutte le query successive siano filtrate su questo tenant.
    /// </summary>
    void SetTenant(Guid tenantId);
}
