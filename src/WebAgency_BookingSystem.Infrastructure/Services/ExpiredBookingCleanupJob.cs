// [INTENT]: Job periodico che delega a IExpiredBookingCleaner la logica di cleanup, occupandosi solo
// dello scheduling (intervallo configurabile via CleanupJob:IntervalMinutes, default 60 minuti).
// Usa IServiceScopeFactory per creare uno scope scoped per ogni ciclo (il DbContext è scoped,
// il BackgroundService è singleton).

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var cleaner = scope.ServiceProvider.GetRequiredService<IExpiredBookingCleaner>();
                await cleaner.CleanupExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // shutdown normale — non è un errore
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore durante il cleanup prenotazioni scadute");
            }

            await Task.Delay(_interval, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
