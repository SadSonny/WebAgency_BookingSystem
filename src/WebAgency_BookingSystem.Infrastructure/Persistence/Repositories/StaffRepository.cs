// [INTENT]: Implementazione EF Core di IStaffRepository. Query filtrate per tenant/soft-delete dal global
// query filter. Il filtro per servizio passa per la tabella di associazione staff_services.

using Microsoft.EntityFrameworkCore;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Repositories;

internal sealed class StaffRepository : IStaffRepository
{
    private readonly BookingSystemDbContext _db;

    public StaffRepository(BookingSystemDbContext db) => _db = db;

    public async Task<IReadOnlyList<Staff>> GetActiveAsync(CancellationToken ct = default) =>
        await _db.Staff
            .Where(s => s.Active)
            .OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name)
            .ToListAsync(ct);

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
}
