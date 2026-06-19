// [INTENT]: BackgroundService che, ogni PollSeconds, invoca OpsAlertScanner.RunOnceAsync. Si occupa solo dello
// scheduling: la logica (watermark, transizioni, digest) è tutta nello scanner (testabile). Un errore di un ciclo
// non interrompe il job: si logga e si ritenta al tick successivo. All'avvio, se il canale Telegram è stato
// richiesto ma è ripiegato su LogOnly per credenziali mancanti, emette un warning una sola volta.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WebAgency_BookingSystem.Infrastructure.Observability;

internal sealed class OpsAlertMonitorJob : BackgroundService
{
    private readonly OpsAlertScanner _scanner;
    private readonly ILogger<OpsAlertMonitorJob> _logger;
    private readonly TimeSpan _interval;
    private readonly bool _fellBackToLogOnly;

    public OpsAlertMonitorJob(OpsAlertScanner scanner, ILogger<OpsAlertMonitorJob> logger, TimeSpan interval, bool fellBackToLogOnly)
    {
        _scanner = scanner;
        _logger = logger;
        _interval = interval;
        _fellBackToLogOnly = fellBackToLogOnly;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_fellBackToLogOnly)
        {
            _logger.LogWarning(
                "Canale alert Telegram richiesto ma credenziali mancanti (token/chat id): uso il canale LogOnly.");
        }

        using var timer = new PeriodicTimer(_interval);
        do
        {
            try
            {
                await _scanner.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nel ciclo del monitor OPS");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
