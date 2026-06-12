// [INTENT]: Implementazione EF Core di ITenantRepository. La risoluzione dell'API key bypassa il global
// query filter (IgnoreQueryFilters) perché avviene PRIMA che il tenant sia noto; tutte le altre query
// restano filtrate sul tenant corrente.

using Microsoft.EntityFrameworkCore;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Repositories;

internal sealed class TenantRepository : ITenantRepository
{
    private readonly BookingSystemDbContext _db;

    public TenantRepository(BookingSystemDbContext db) => _db = db;

    public Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<Tenant?> ResolveActiveByApiKeyHashAsync(string keyHash, CancellationToken ct = default)
    {
        // WHY: il tenant non è ancora noto, quindi il query filter (TenantId == context.TenantId, null qui)
        // escluderebbe ogni riga. IgnoreQueryFilters permette la risoluzione; restringiamo comunque a chiavi
        // attive e tenant attivo. È l'unico punto che legge attraverso i tenant ed è di sola lettura.
        TenantApiKey? key = await _db.TenantApiKeys
            .IgnoreQueryFilters()
            .Include(k => k.Tenant)
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash && k.Active, ct);

        return key?.Tenant is { Active: true } tenant ? tenant : null;
    }

    public async Task<IReadOnlyList<TenantBusinessHours>> GetBusinessHoursAsync(Guid tenantId, CancellationToken ct = default) =>
        await _db.TenantBusinessHours
            .Where(h => h.TenantId == tenantId)
            .OrderBy(h => h.DayOfWeek)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TenantSpecialClosure>> GetActiveSpecialClosuresAsync(
        Guid tenantId, DateOnly fromInclusive, CancellationToken ct = default) =>
        await _db.TenantSpecialClosures
            .Where(c => c.TenantId == tenantId && c.DateTo >= fromInclusive)
            .OrderBy(c => c.DateFrom)
            .ToListAsync(ct);
}
