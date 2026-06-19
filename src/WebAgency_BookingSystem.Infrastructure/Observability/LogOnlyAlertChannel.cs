// [INTENT]: Canale di alert di fallback (e default in dev/test): non notifica nessun servizio esterno, ma scrive
// l'alert con il marcatore [OPS-ALERT] sui sink Serilog (console + DB). Resta visibile su Railway anche quando il
// DB è giù (il sink console non dipende dal DB). È il punto di aggancio per un futuro canale reale.

using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Observability;

namespace WebAgency_BookingSystem.Infrastructure.Observability;

internal sealed class LogOnlyAlertChannel : IOpsAlertChannel
{
    private readonly ILogger<LogOnlyAlertChannel> _logger;

    public LogOnlyAlertChannel(ILogger<LogOnlyAlertChannel> logger) => _logger = logger;

    public Task SendAsync(OpsAlert alert, CancellationToken ct = default)
    {
        // WHY: DbRecovered è una buona notizia (Warning); ErrorDigest/DbDown sono problemi attivi (Error).
        LogLevel level = alert.Kind == OpsAlertKind.DbRecovered ? LogLevel.Warning : LogLevel.Error;
        _logger.Log(level, "[OPS-ALERT] {Kind}: {Title} — {Detail}", alert.Kind, alert.Title, alert.Detail);
        return Task.CompletedTask;
    }
}
