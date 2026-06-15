// [INTENT]: BackgroundService che esegue periodicamente la pulizia GDPR (S2) delegando a IDataRetentionService.
// Intervallo configurabile (Gdpr:PollHours, default 24). Solo scheduling + scope (servizio e DbContext scoped,
// job singleton). Un errore di un ciclo non interrompe il job.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WebAgency_BookingSystem.Infrastructure.Services;

internal sealed class DataRetentionJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DataRetentionJob> _logger;
    private readonly TimeSpan _interval;

    public DataRetentionJob(IServiceScopeFactory scopeFactory, ILogger<DataRetentionJob> logger, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        int hours = int.TryParse(configuration["Gdpr:PollHours"], out int h) && h > 0 ? h : 24;
        _interval = TimeSpan.FromHours(hours);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        do
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IDataRetentionService>();
                await service.PurgeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nel ciclo di retention GDPR");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
