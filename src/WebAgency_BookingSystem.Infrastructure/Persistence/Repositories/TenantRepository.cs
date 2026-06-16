// [INTENT]: Implementazione EF Core di ITenantRepository. La risoluzione dell'API key bypassa il global
// query filter (IgnoreQueryFilters) perché avviene PRIMA che il tenant sia noto. Cache: risoluzione API key
// (R-15, TTL breve — la revoca diventa effettiva alla scadenza) e orari settimanali (R-22, per-tenant).

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Infrastructure.Persistence.Caching;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Repositories;

internal sealed class TenantRepository : ITenantRepository
{
    private static readonly TimeSpan ApiKeyTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan TenantDataTtl = TimeSpan.FromSeconds(30);

    private readonly BookingSystemDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ITenantCache _tenantCache;

    public TenantRepository(BookingSystemDbContext db, IMemoryCache cache, ITenantCache tenantCache)
    {
        _db = db;
        _cache = cache;
        _tenantCache = tenantCache;
    }

    public async Task<Tenant?> ResolveActiveByApiKeyHashAsync(string keyHash, CancellationToken ct = default)
    {
        string cacheKey = $"apikey:{keyHash}";
        if (_cache.TryGetValue(cacheKey, out Tenant? cached) && cached is not null)
        {
            return cached;
        }

        // WHY: il tenant non è ancora noto, quindi il query filter (TenantId == context.TenantId, null qui)
        // escluderebbe ogni riga. IgnoreQueryFilters permette la risoluzione; AsNoTracking restituisce entità
        // staccate, sicure da mettere in cache (condivisa tra richieste). Restringiamo a chiavi/tenant attivi.
        TenantApiKey? key = await _db.TenantApiKeys
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(k => k.Tenant)
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash && k.Active, ct);

        Tenant? tenant = key?.Tenant is { Active: true } active ? active : null;

        // WHY: cachiamo solo i risultati POSITIVI. Non cachiamo i negativi per non riempire la cache con chiavi
        // casuali (es. brute-force); quei tentativi sono comunque limitati dal rate limiter per IP (R-14).
        if (tenant is not null)
        {
            _cache.Set(cacheKey, tenant, ApiKeyTtl);
        }

        return tenant;
    }

    public Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<IReadOnlyList<string>> GetActiveSiteUrlsAsync(CancellationToken ct = default) =>
        // Tenant non è tenant-scoped (nessun global filter): query diretta sui tenant attivi con siteUrl valorizzato.
        await _db.Tenants
            .AsNoTracking()
            .Where(t => t.Active && t.SiteUrl != "")
            .Select(t => t.SiteUrl)
            .ToListAsync(ct);

    public Task<IReadOnlyList<TenantBusinessHours>> GetBusinessHoursAsync(Guid tenantId, CancellationToken ct = default) =>
        _tenantCache.GetOrCreateAsync<IReadOnlyList<TenantBusinessHours>>(
            tenantId, "business-hours", TenantDataTtl,
            async token => await _db.TenantBusinessHours
                .AsNoTracking()
                .Where(h => h.TenantId == tenantId)
                .OrderBy(h => h.DayOfWeek)
                .ToListAsync(token),
            ct);

    public async Task<IReadOnlyList<TenantSpecialClosure>> GetActiveSpecialClosuresAsync(
        Guid tenantId, DateOnly fromInclusive, CancellationToken ct = default) =>
        // Non cachiamo: dipende da fromInclusive (la data odierna) ed è meno frequente.
        await _db.TenantSpecialClosures
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.DateTo >= fromInclusive)
            .OrderBy(c => c.DateFrom)
            .ToListAsync(ct);
}
