// [INTENT]: Astrazione del tenant corrente per la richiesta in corso. Popolata dal TenantResolutionMiddleware
// dopo aver risolto l'API key, e letta dal DbContext per il global query filter e dai servizi che hanno
// bisogno delle regole del tenant. Espone l'entità Tenant già caricata in fase di risoluzione, evitando query
// ridondanti a valle (R-21). Disaccoppia il DbContext (Infrastructure) dal meccanismo HTTP di risoluzione (Api).

using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Core.Abstractions;

/// <summary>
/// Fornisce il tenant risolto per la richiesta corrente (scoped per-request).
/// </summary>
public interface ITenantContext
{
    /// <summary>Tenant corrente, oppure null se non ancora risolto (es. endpoint senza auth).</summary>
    Tenant? Tenant { get; }

    /// <summary>Id del tenant corrente, oppure null se non ancora risolto.</summary>
    Guid? TenantId { get; }

    /// <summary>True se un tenant è stato risolto per la richiesta corrente.</summary>
    bool IsResolved { get; }

    /// <summary>
    /// Imposta il tenant corrente (con l'entità già caricata). Chiamato una sola volta dal middleware di
    /// risoluzione. Garantisce che tutte le query successive siano filtrate su questo tenant e che i servizi
    /// possano leggerne le regole senza ricaricarlo dal DB.
    /// </summary>
    void SetTenant(Tenant tenant);
}
