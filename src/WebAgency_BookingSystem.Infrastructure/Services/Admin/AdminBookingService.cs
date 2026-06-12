// [INTENT]: Implementazione admin delle prenotazioni (6.3 lista filtrata, 6.4 aggiornamento stato). Usa
// IgnoreQueryFilters + filtro tenant esplicito per includere anche service/staff soft-deleted nei riferimenti
// (vista gestionale). L'aggiornamento di stato registra l'audit con l'azione e l'attore corretti.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Abstractions;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Core.Dtos.Public;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Services.Admin;

internal sealed class AdminBookingService : IAdminBookingService
{
    private readonly BookingSystemDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminBookingService> _logger;

    public AdminBookingService(BookingSystemDbContext db, ITenantContext tenantContext, ILogger<AdminBookingService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<AdminBookingResponse>>> ListAsync(AdminBookingFilter filter, CancellationToken ct = default)
    {
        Guid tenantId = _tenantContext.TenantId!.Value;

        IQueryable<Booking> query = _db.Bookings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId)
            .Include(b => b.Service)
            .Include(b => b.Staff);

        if (filter.DateFrom is DateOnly from)
        {
            query = query.Where(b => b.BookingDate >= from);
        }

        if (filter.DateTo is DateOnly to)
        {
            query = query.Where(b => b.BookingDate <= to);
        }

        if (filter.StaffId is Guid staffId)
        {
            query = query.Where(b => b.StaffId == staffId);
        }

        if (filter.ServiceId is Guid serviceId)
        {
            query = query.Where(b => b.ServiceId == serviceId);
        }

        if (filter.Status is BookingStatus status)
        {
            query = query.Where(b => b.Status == status);
        }

        List<Booking> bookings = await query
            .OrderBy(b => b.BookingDate).ThenBy(b => b.BookingTime)
            .ToListAsync(ct);

        IReadOnlyList<AdminBookingResponse> response = bookings.Select(Map).ToList();
        return Result.Success(response);
    }

    public async Task<Result<AdminBookingResponse>> UpdateStatusAsync(
        Guid id, UpdateBookingStatusRequest request, CancellationToken ct = default)
    {
        if (!BookingStatusExtensions.TryParseApi(request.Status, out BookingStatus newStatus))
        {
            return Error.Validation("validation_error", "Stato non valido (ammessi: confirmed, cancelled, no_show, completed).");
        }

        Guid tenantId = _tenantContext.TenantId!.Value;
        Booking? booking = await _db.Bookings
            .IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId && b.Id == id)
            .Include(b => b.Service)
            .Include(b => b.Staff)
            .FirstOrDefaultAsync(ct);

        if (booking is null)
        {
            return Error.NotFound("not_found", "Prenotazione non trovata.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        booking.Status = newStatus;
        string action;
        switch (newStatus)
        {
            case BookingStatus.NoShow:
                booking.NoShowMarkedAt = now;
                action = "booking_no_show";
                break;
            case BookingStatus.Cancelled:
                booking.CancelledAt = now;
                booking.CancellationReason = "owner";
                action = "booking_cancelled_by_owner";
                break;
            case BookingStatus.Completed:
                action = "booking_completed";
                break;
            default:
                action = "booking_status_updated";
                break;
        }

        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BookingId = booking.Id,
            Action = action,
            Actor = "owner",
            CreatedAt = now,
        });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Admin: prenotazione {BookingId} aggiornata a stato {Status}", booking.Id, newStatus);

        return Result.Success(Map(booking));
    }

    private static AdminBookingResponse Map(Booking b) => new(
        b.Id,
        b.BookingDate.ToString("yyyy-MM-dd"),
        b.BookingTime.ToString("HH:mm"),
        b.DurationMinutes,
        b.Status.ToApiString(),
        new BookingServiceRef(b.ServiceId, b.Service?.Name ?? string.Empty),
        b.StaffId is Guid staffId ? new BookingStaffRef(staffId, b.Staff?.Name ?? string.Empty) : null,
        new AdminCustomerInfo(b.CustomerName, b.CustomerPhone, b.CustomerEmail, b.CustomerNotes),
        b.PriceAtBooking,
        b.CreatedAt.ToString("o"));
}
