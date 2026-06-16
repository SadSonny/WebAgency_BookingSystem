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
