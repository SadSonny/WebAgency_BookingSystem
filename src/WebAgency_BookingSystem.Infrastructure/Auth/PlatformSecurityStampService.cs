// [INTENT]: Implementazione cache-first di IPlatformSecurityStampService. La stamp corrente del PlatformAdmin è
// cachata in IMemoryCache (chiave "platform-stamp:{id}", TTL breve) per evitare una query DB a ogni richiesta
// platform. Invalidate (chiamato dopo cambio/reset/attivazione password) rimuove la voce così i vecchi JWT cessano.
// WHY (D2): duplica deliberatamente UserSecurityStampService su uno store separato (IPlatformAdminRepository) per
// isolare l'identità di piattaforma da quella tenant — nessuna astrazione condivisa per scelta di design.

using Microsoft.Extensions.Caching.Memory;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Abstractions.Services;

namespace WebAgency_BookingSystem.Infrastructure.Auth;

internal sealed class PlatformSecurityStampService : IPlatformSecurityStampService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IPlatformAdminRepository _admins;
    private readonly IMemoryCache _cache;

    public PlatformSecurityStampService(IPlatformAdminRepository admins, IMemoryCache cache)
    {
        _admins = admins;
        _cache = cache;
    }

    public async Task<bool> IsCurrentAsync(Guid platformAdminId, Guid stamp, CancellationToken ct = default)
    {
        Guid? current = await _cache.GetOrCreateAsync(CacheKey(platformAdminId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return await _admins.GetSecurityStampAsync(platformAdminId, ct);
        });
        return current is Guid g && g == stamp;
    }

    public void Invalidate(Guid platformAdminId) => _cache.Remove(CacheKey(platformAdminId));

    private static string CacheKey(Guid platformAdminId) => $"platform-stamp:{platformAdminId}";
}
