// [INTENT]: Implementazione EF Core di IServiceRepository. Le query sono già filtrate per tenant e per
// soft delete dal global query filter del DbContext. GetStaffIdsByServiceAsync evita N+1 con una sola
// query e raggruppa in memoria.

using Microsoft.EntityFrameworkCore;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Repositories;

internal sealed class ServiceRepository : IServiceRepository
{
    private readonly BookingSystemDbContext _db;

    public ServiceRepository(BookingSystemDbContext db) => _db = db;

    public async Task<IReadOnlyList<Service>> GetActiveAsync(CancellationToken ct = default) =>
        await _db.Services
            .Where(s => s.Active)
            .OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name)
            .ToListAsync(ct);

    public Task<Service?> GetActiveByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Services.FirstOrDefaultAsync(s => s.Id == id && s.Active, ct);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetStaffIdsByServiceAsync(
        IReadOnlyCollection<Guid> serviceIds, CancellationToken ct = default)
    {
        if (serviceIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<Guid>>();
        }

        // WHY: join su _db.Staff (filtrato per tenant/soft-delete) + filtro Active, così esponiamo solo
        // staff effettivamente erogabili. Una sola query, raggruppamento in memoria.
        var pairs = await (
            from ss in _db.StaffServices
            join s in _db.Staff on ss.StaffId equals s.Id
            where serviceIds.Contains(ss.ServiceId) && s.Active
            select new { ss.ServiceId, StaffId = s.Id })
            .ToListAsync(ct);

        return pairs
            .GroupBy(p => p.ServiceId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<Guid>)g.Select(x => x.StaffId).ToList());
    }
}
