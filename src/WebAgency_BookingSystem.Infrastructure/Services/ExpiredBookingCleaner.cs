// [INTENT]: Logica pura del cleanup prenotazioni scadute, separata dallo scheduling (BackgroundService).
// Questa separazione permette di testare il comportamento in isolation senza dipendere dall'infrastruttura
// di hosting (BackgroundService non è direttamente invocabile dai test).

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Services;

internal interface IExpiredBookingCleaner
{
    /// <summary>
    /// Cerca le prenotazioni Confirmed la cui data+ora locale del tenant è nel passato e le segna NoShow.
    /// Restituisce il numero di prenotazioni aggiornate.
    /// </summary>
    Task<int> CleanupExpiredAsync(CancellationToken ct = default);
}

internal sealed class ExpiredBookingCleaner : IExpiredBookingCleaner
{
    private readonly BookingSystemDbContext _db;
    private readonly ILogger<ExpiredBookingCleaner> _logger;

    public ExpiredBookingCleaner(BookingSystemDbContext db, ILogger<ExpiredBookingCleaner> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<int> CleanupExpiredAsync(CancellationToken ct = default)
    {
        var utcNow = DateTimeOffset.UtcNow;

        // WHY: IgnoreQueryFilters() è necessario perché il global query filter filtra per tenant_id
        // tramite ITenantContext. Il cleanup è un'operazione cross-tenant priva di contesto tenant.
        var candidates = await _db.Bookings
            .IgnoreQueryFilters()
            .Include(b => b.Tenant)
            .Where(b => b.Status == BookingStatus.Confirmed)
            .ToListAsync(ct);

        int count = 0;
        foreach (var booking in candidates)
        {
            if (booking.Tenant is null) continue;

            TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(booking.Tenant.Timezone);
            DateTime tenantNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow.UtcDateTime, tz);
            DateTime bookingMoment = booking.BookingDate.ToDateTime(booking.BookingTime);

            if (bookingMoment >= tenantNow) continue;

            booking.Status = BookingStatus.NoShow;
            booking.NoShowMarkedAt = utcNow;
            booking.UpdatedAt = utcNow;
            count++;
        }

        if (count > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Cleanup prenotazioni: {Count} scadute segnate come NoShow", count);
        }

        return count;
    }
}
