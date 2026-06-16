// [INTENT]: BackgroundService che purga periodicamente la tabella dei log applicativi (sink PostgreSQL) oltre la
// retention configurata (DatabaseLogging:RetentionDays, default 90 giorni). La tabella dei log NON è un'entità
// del modello EF: il DELETE è eseguito in SQL grezzo tramite il DbContext, con il valore di taglio PARAMETRICO e
// il nome tabella già validato (whitelist) in DatabaseLogSettings. Solo scheduling (giornaliero); un errore di un
// ciclo non interrompe il job. Se il sink DB è disattivato o manca la connection string, il job non fa nulla.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Api.Logging;

internal sealed class LogRetentionJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LogRetentionJob> _logger;
    private readonly DatabaseLogSettings _settings;

    public LogRetentionJob(IServiceScopeFactory scopeFactory, ILogger<LogRetentionJob> logger, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settings = DatabaseLogSettings.FromConfiguration(configuration);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled || string.IsNullOrWhiteSpace(_settings.ConnectionString))
        {
            // Nessun sink DB attivo → nessuna tabella di log da purgare.
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                BookingSystemDbContext db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();

                DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddDays(-_settings.RetentionDays);

                // WHY: la tabella log non è mappata in EF; il nome è validato (whitelist) in DatabaseLogSettings,
                // quindi concatenarlo come identificatore è sicuro. Il valore di taglio è PARAMETRICO ({0}); usiamo
                // una stringa NON interpolata (costruita a parte) per non incorrere nell'analyzer EF1002.
                string deleteSql = "DELETE FROM " + _settings.Table + " WHERE \"timestamp\" < {0}";
                int deleted = await db.Database.ExecuteSqlRawAsync(deleteSql, [cutoff], stoppingToken);

                if (deleted > 0)
                {
                    _logger.LogInformation(
                        "Retention log applicativi: rimosse {Count} righe più vecchie di {Days} giorni",
                        deleted, _settings.RetentionDays);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nel ciclo di retention dei log applicativi");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
