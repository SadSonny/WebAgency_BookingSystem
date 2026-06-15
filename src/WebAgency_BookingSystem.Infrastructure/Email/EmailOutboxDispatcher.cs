// [INTENT]: BackgroundService che, a intervalli regolari (Email:Outbox:PollSeconds, default 30s), delega a
// IOutboxEmailProcessor l'invio delle email Pending della outbox (PH-3). Si occupa solo dello scheduling e
// dello scope (il processor e il DbContext sono scoped, il BackgroundService è singleton). Un errore di un
// ciclo non interrompe il job: si logga e si ritenta al tick successivo.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WebAgency_BookingSystem.Infrastructure.Email;

internal sealed class EmailOutboxDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailOutboxDispatcher> _logger;
    private readonly TimeSpan _interval;

    public EmailOutboxDispatcher(IServiceScopeFactory scopeFactory, ILogger<EmailOutboxDispatcher> logger, TimeSpan interval)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = interval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        do
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IOutboxEmailProcessor>();
                await processor.ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nel ciclo di dispatch della outbox email");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
