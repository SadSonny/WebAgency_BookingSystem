// [INTENT]: Cache per-tenant di dati quasi-statici (orari, servizi, staff) con invalidazione esplicita per
// tenant (R-22). Le voci sono legate a un token di cancellazione per-tenant: quando l'Admin CRUD (6.x)
// modifica i dati di un tenant deve chiamare Invalidate(tenantId) per scartare in blocco la sua cache,
// evitando dati stantii. Finché l'Admin non esiste, il TTL breve è comunque la rete di sicurezza.

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Caching;

/// <summary>
/// Cache in-memory con chiavi namespaced per tenant e invalidazione per tenant.
/// </summary>
public interface ITenantCache
{
    /// <summary>
    /// Restituisce il valore in cache per (tenant, key) oppure lo produce con <paramref name="factory"/>,
    /// memorizzandolo con scadenza assoluta <paramref name="ttl"/> e legandolo all'invalidazione del tenant.
    /// </summary>
    Task<T> GetOrCreateAsync<T>(
        Guid tenantId, string key, TimeSpan ttl, Func<CancellationToken, Task<T>> factory, CancellationToken ct = default);

    /// <summary>Invalida in blocco tutte le voci memorizzate per il tenant indicato.</summary>
    void Invalidate(Guid tenantId);
}
