// [INTENT]: Job periodico che segna come NoShow le prenotazioni Confirmed scadute (data+ora nel passato
// nel timezone del tenant). Gira come BackgroundService; intervallo configurabile via CleanupJob:IntervalMinutes
// (default 60 minuti). Usa IServiceScopeFactory per creare un scope EF per ogni ciclo, evitando il riuso
// di DbContext tra iterazioni (il DbContext è scoped, il BackgroundService è singleton).

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Services;

internal sealed class ExpiredBookingCleanupJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExpiredBookingCleanupJob> _logger;
    private readonly TimeSpan _interval;

    public ExpiredBookingCleanupJob(
        IServiceScopeFactory scopeFactory,
        ILogger<ExpiredBookingCleanupJob> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        int minutes = int.TryParse(configuration["CleanupJob:IntervalMinutes"], out var m) ? m : 60;
        // WHY: non permettiamo un intervallo < 1 minuto per evitare busy-loop accidentali da config errata.
        _interval = TimeSpan.FromMinutes(minutes > 0 ? minutes : 60);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // WHY: il primo ciclo parte subito all'avvio, senza attendere l'intero intervallo.
        // I cicli successivi rispettano il delay configurato.
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCleanupAsync(stoppingToken);
            await Task.Delay(_interval, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();
            var utcNow = DateTimeOffset.UtcNow;

            // WHY: IgnoreQueryFilters() è necessario perché il global query filter filtra per tenant_id
            // tramite ITenantContext. Il cleanup è un'operazione cross-tenant priva di contesto tenant.
            var candidates = await db.Bookings
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
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("Cleanup prenotazioni: {Count} scadute segnate come NoShow", count);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown normale — non è un errore
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore durante il cleanup prenotazioni scadute");
        }
    }
}
