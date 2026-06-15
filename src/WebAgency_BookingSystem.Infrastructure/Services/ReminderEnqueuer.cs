// [INTENT]: Implementazione dei promemoria pre-appuntamento (T2.3). Trova le prenotazioni Confermate non ancora
// promemoria-te il cui ISTANTE di inizio cade entro la finestra di anticipo del tenant (ReminderHoursBefore) e
// non è già passato; accoda il promemoria nella outbox (atomico col SaveChanges) e marca ReminderSentAt.
// CROSS-tenant (IgnoreQueryFilters) → join al tenant per timezone/anticipo/notifiche.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Infrastructure.Email;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Services;

internal sealed class ReminderEnqueuer : IReminderEnqueuer
{
    private readonly BookingSystemDbContext _db;
    private readonly IEmailOutbox _outbox;
    private readonly ILogger<ReminderEnqueuer> _logger;

    public ReminderEnqueuer(BookingSystemDbContext db, IEmailOutbox outbox, ILogger<ReminderEnqueuer> logger)
    {
        _db = db;
        _outbox = outbox;
        _logger = logger;
    }

    public async Task<int> EnqueueDueRemindersAsync(CancellationToken ct = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // WHY (P3): restringiamo lo scan. Calcoliamo l'anticipo massimo tra i tenant con reminder attivo: nessun
        // appuntamento oltre quella finestra può essere "dovuto" ora. Se nessun tenant ha i reminder attivi,
        // usciamo subito. Il margine (+2 giorni) copre fusi orari e l'arrotondamento ore→giorni.
        int? maxReminderHours = await _db.Tenants
            .Where(t => t.Active && t.NotificationMethod == "email" && t.ReminderHoursBefore > 0)
            .Select(t => (int?)t.ReminderHoursBefore)
            .MaxAsync(ct);
        if (maxReminderHours is null)
        {
            return 0;
        }

        DateOnly fromDate = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1);
        DateOnly toDate = DateOnly.FromDateTime(now.UtcDateTime).AddDays((maxReminderHours.Value / 24) + 2);

        // La finestra di anticipo precisa (timezone-aware) è poi applicata in memoria. Cross-tenant:
        // IgnoreQueryFilters + include Tenant/Service. L'indice (status, reminder_sent_at, booking_date) supporta il filtro.
        List<Booking> candidates = await _db.Bookings
            .IgnoreQueryFilters()
            .Include(b => b.Tenant)
            .Include(b => b.Service)
            .Where(b => b.Status == BookingStatus.Confirmed
                && b.ReminderSentAt == null
                && b.BookingDate >= fromDate
                && b.BookingDate <= toDate)
            .ToListAsync(ct);

        int enqueued = 0;
        foreach (Booking booking in candidates)
        {
            Tenant? tenant = booking.Tenant;
            if (tenant is null
                || !string.Equals(tenant.NotificationMethod, "email", StringComparison.OrdinalIgnoreCase)
                || tenant.ReminderHoursBefore <= 0)
            {
                continue; // promemoria disattivati per questo tenant
            }

            DateTimeOffset bookingInstant = TenantTime.ToInstant(booking.BookingDate, booking.BookingTime, tenant.Timezone);
            if (bookingInstant <= now)
            {
                continue; // appuntamento già iniziato/passato → niente promemoria tardivo
            }

            if (bookingInstant > now.AddHours(tenant.ReminderHoursBefore))
            {
                continue; // troppo presto: non ancora entro la finestra di anticipo
            }

            // Tenant e Service sono già caricati (tracked) → il renderer li legge senza problemi.
            _outbox.EnqueueReminder(booking);
            booking.ReminderSentAt = now;
            enqueued++;
        }

        if (enqueued > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Promemoria accodati: {Count}", enqueued);
        }

        return enqueued;
    }
}
