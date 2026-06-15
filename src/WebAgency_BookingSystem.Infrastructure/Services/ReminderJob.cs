// [INTENT]: BackgroundService che, a intervalli regolari (Reminder:PollMinutes, default 15), delega a
// IReminderEnqueuer l'accodamento dei promemoria dovuti (T2.3). Solo scheduling + scope (enqueuer e DbContext
// scoped, job singleton). Un errore di un ciclo non interrompe il job.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WebAgency_BookingSystem.Infrastructure.Services;

internal sealed class ReminderJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReminderJob> _logger;
    private readonly TimeSpan _interval;

    public ReminderJob(IServiceScopeFactory scopeFactory, ILogger<ReminderJob> logger, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        int minutes = int.TryParse(configuration["Reminder:PollMinutes"], out int m) && m > 0 ? m : 15;
        _interval = TimeSpan.FromMinutes(minutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        do
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                var enqueuer = scope.ServiceProvider.GetRequiredService<IReminderEnqueuer>();
                await enqueuer.EnqueueDueRemindersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nel ciclo dei promemoria pre-appuntamento");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
