// [INTENT]: Implementazione EF Core di IBookingRepository. Le query di sovrapposizione restituiscono le
// sole prenotazioni confermate (le disdette non occupano slot). Tutto è filtrato sul tenant corrente dal
// global query filter. La persistenza (SaveChanges) è gestita dal BookingService nella sua transazione.

using Microsoft.EntityFrameworkCore;
using WebAgency_BookingSystem.Core.Abstractions;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Repositories;

internal sealed class BookingRepository : IBookingRepository
{
    private readonly BookingSystemDbContext _db;
    private readonly ITenantContext _tenantContext;

    public BookingRepository(BookingSystemDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<Booking>> GetConfirmedByServiceInRangeAsync(
        Guid serviceId, DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct = default) =>
        await _db.Bookings
            .Where(b => b.ServiceId == serviceId
                && b.Status == BookingStatus.Confirmed
                && b.BookingDate >= fromInclusive
                && b.BookingDate <= toInclusive)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Booking>> GetConfirmedByStaffInRangeAsync(
        Guid staffId, DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct = default) =>
        await _db.Bookings
            .Where(b => b.StaffId == staffId
                && b.Status == BookingStatus.Confirmed
                && b.BookingDate >= fromInclusive
                && b.BookingDate <= toInclusive)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Booking>> GetConfirmedByStaffIdsInRangeAsync(
        IReadOnlyCollection<Guid> staffIds, DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct = default) =>
        await _db.Bookings
            .Where(b => b.StaffId != null
                && staffIds.Contains(b.StaffId.Value)
                && b.Status == BookingStatus.Confirmed
                && b.BookingDate >= fromInclusive
                && b.BookingDate <= toInclusive)
            .ToListAsync(ct);

    public Task<Booking?> GetByIdAndTokenAsync(Guid bookingId, Guid token, CancellationToken ct = default) =>
        // WHY (R-28): IgnoreQueryFilters per includere Service/Staff anche se SOFT-DELETED (vista storica del
        // dettaglio: il nome del servizio/staff deve restare visibile). Lo scoping per tenant è preservato in
        // modo esplicito col filtro su TenantId, quindi non c'è rischio di leak cross-tenant.
        _db.Bookings
            .IgnoreQueryFilters()
            .Where(b => b.TenantId == _tenantContext.TenantId)
            .Include(b => b.Service)
            .Include(b => b.Staff)
            .Include(b => b.Items.OrderBy(i => i.Sequence)).ThenInclude(i => i.Service)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.CancellationToken == token, ct);

    public async Task AddAsync(Booking booking, CancellationToken ct = default) =>
        await _db.Bookings.AddAsync(booking, ct);
}
