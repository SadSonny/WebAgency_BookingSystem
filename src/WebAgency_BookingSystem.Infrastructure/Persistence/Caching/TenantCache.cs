// [INTENT]: Implementazione di ITenantCache su IMemoryCache. Mantiene un CancellationTokenSource per tenant:
// ogni voce è legata al token del proprio tenant, così Invalidate(tenantId) cancella in blocco tutte le sue
// voci (prefix-invalidation pulita, che IMemoryCache non offre nativamente). TTL come rete di sicurezza.

using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Caching;

internal sealed class TenantCache : ITenantCache
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _tenantTokens = new();

    public TenantCache(IMemoryCache cache) => _cache = cache;

    public async Task<T> GetOrCreateAsync<T>(
        Guid tenantId, string key, TimeSpan ttl, Func<CancellationToken, Task<T>> factory, CancellationToken ct = default)
    {
        string cacheKey = $"tenant:{tenantId}:{key}";
        if (_cache.TryGetValue(cacheKey, out T? cached) && cached is not null)
        {
            return cached;
        }

        T value = await factory(ct);

        CancellationTokenSource cts = _tenantTokens.GetOrAdd(tenantId, _ => new CancellationTokenSource());
        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(ttl)
            .AddExpirationToken(new CancellationChangeToken(cts.Token));

        _cache.Set(cacheKey, value, options);
        return value;
    }

    public void Invalidate(Guid tenantId)
    {
        if (_tenantTokens.TryRemove(tenantId, out CancellationTokenSource? cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }
}
