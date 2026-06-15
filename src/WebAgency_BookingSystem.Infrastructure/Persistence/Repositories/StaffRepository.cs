// [INTENT]: Implementazione EF Core di IStaffRepository. Query filtrate per tenant/soft-delete dal global
// query filter. Il filtro per servizio passa per la tabella di associazione staff_services.

using Microsoft.EntityFrameworkCore;
using WebAgency_BookingSystem.Core.Abstractions;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Infrastructure.Persistence;
using WebAgency_BookingSystem.Infrastructure.Persistence.Caching;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Repositories;

internal sealed class StaffRepository : IStaffRepository
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly BookingSystemDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantCache _cache;

    public StaffRepository(BookingSystemDbContext db, ITenantContext tenantContext, ITenantCache cache)
    {
        _db = db;
        _tenantContext = tenantContext;
        _cache = cache;
    }

    public Task<IReadOnlyList<Staff>> GetActiveAsync(CancellationToken ct = default) =>
        _cache.GetOrCreateAsync<IReadOnlyList<Staff>>(
            _tenantContext.TenantId!.Value, "active-staff", Ttl,
            async token => await _db.Staff
                .AsNoTracking()
                .Where(s => s.Active)
                .OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name)
                .ToListAsync(token),
            ct);

    public async Task<IReadOnlyList<Staff>> GetActiveByServiceAsync(Guid serviceId, CancellationToken ct = default) =>
        await (
            from s in _db.Staff
            join ss in _db.StaffServices on s.Id equals ss.StaffId
            where s.Active && ss.ServiceId == serviceId
            orderby s.DisplayOrder, s.Name
            select s)
            .ToListAsync(ct);

    public Task<Staff?> GetActiveByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Staff.FirstOrDefaultAsync(s => s.Id == id && s.Active, ct);

    public Task<bool> ExecutesServiceAsync(Guid staffId, Guid serviceId, CancellationToken ct = default) =>
        _db.StaffServices.AnyAsync(ss => ss.StaffId == staffId && ss.ServiceId == serviceId, ct);

    public async Task<IReadOnlyList<StaffBusinessHours>> GetBusinessHoursAsync(Guid staffId, CancellationToken ct = default) =>
        await _db.StaffBusinessHours
            .Where(h => h.StaffId == staffId)
            .OrderBy(h => h.DayOfWeek)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<StaffTimeOff>> GetTimeOffInRangeAsync(
        Guid staffId, DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct = default) =>
        // Intersezione di intervalli: l'assenza [DateFrom..DateTo] interseca [from..to] se DateFrom <= to && DateTo >= from.
        await _db.StaffTimeOff
            .AsNoTracking()
            .Where(t => t.StaffId == staffId && t.DateFrom <= toInclusive && t.DateTo >= fromInclusive)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<StaffBusinessHours>> GetBusinessHoursForStaffAsync(
        IReadOnlyCollection<Guid> staffIds, CancellationToken ct = default) =>
        await _db.StaffBusinessHours
            .AsNoTracking()
            .Where(h => staffIds.Contains(h.StaffId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<StaffTimeOff>> GetTimeOffForStaffInRangeAsync(
        IReadOnlyCollection<Guid> staffIds, DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct = default) =>
        await _db.StaffTimeOff
            .AsNoTracking()
            .Where(t => staffIds.Contains(t.StaffId) && t.DateFrom <= toInclusive && t.DateTo >= fromInclusive)
            .ToListAsync(ct);

    public async Task<IReadOnlySet<Guid>> GetStaffExecutingAllAsync(
        IReadOnlyCollection<Guid> staffIds, IReadOnlyCollection<Guid> serviceIds, CancellationToken ct = default)
    {
        // Conta per operatore quanti dei servizi richiesti esegue; chi li esegue tutti ha count == serviceIds.Count.
        int required = serviceIds.Count;
        List<Guid> matching = await _db.StaffServices
            .AsNoTracking()
            .Where(ss => staffIds.Contains(ss.StaffId) && serviceIds.Contains(ss.ServiceId))
            .GroupBy(ss => ss.StaffId)
            .Where(g => g.Select(x => x.ServiceId).Distinct().Count() == required)
            .Select(g => g.Key)
            .ToListAsync(ct);
        return matching.ToHashSet();
    }
}
