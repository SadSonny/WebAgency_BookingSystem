// [INTENT]: Cuore del monitor OPS: una singola scansione (RunOnceAsync) stateful. Sonda il DB; se giù segnala la
// transizione (una sola volta) e si ferma (non può leggere la tabella logs); se su, ripristina lo stato e legge i
// nuovi errori oltre il watermark, li aggrega in un unico ErrorDigest e li invia. È singleton (mantiene watermark
// e flag DB tra i tick); il BackgroundService si limita a chiamarlo sul timer.

using WebAgency_BookingSystem.Core.Observability;

namespace WebAgency_BookingSystem.Infrastructure.Observability;

internal sealed class OpsAlertScanner
{
    private const int SampleSize = 5;
    private const int MaxMessageLength = 200;

    private readonly ILogErrorSource _logErrors;
    private readonly IDbHealthProbe _dbHealth;
    private readonly IOpsAlertChannel _channel;
    private readonly string[] _levels;

    private DateTimeOffset _watermark;
    private bool _dbWasDown;

    public OpsAlertScanner(
        ILogErrorSource logErrors,
        IDbHealthProbe dbHealth,
        IOpsAlertChannel channel,
        string[] levels,
        DateTimeOffset startedAt)
    {
        _logErrors = logErrors;
        _dbHealth = dbHealth;
        _channel = channel;
        _levels = levels;
        _watermark = startedAt; // WHY: si parte da "ora": al riavvio non si rialerta lo storico.
    }

    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        bool up = await _dbHealth.CanConnectAsync(ct);
        if (!up)
        {
            if (!_dbWasDown)
            {
                _dbWasDown = true;
                await _channel.SendAsync(new OpsAlert(
                    OpsAlertKind.DbDown,
                    "Database irraggiungibile",
                    "Il self-check di connettività al database è fallito.",
                    DateTimeOffset.UtcNow), ct);
            }

            return; // WHY: con il DB giù non si può leggere la tabella logs; ricontrolleremo al prossimo tick.
        }

        if (_dbWasDown)
        {
            _dbWasDown = false;
            await _channel.SendAsync(new OpsAlert(
                OpsAlertKind.DbRecovered,
                "Database di nuovo raggiungibile",
                "La connessione al database è stata ripristinata.",
                DateTimeOffset.UtcNow), ct);
        }

        IReadOnlyList<LogError> errors = await _logErrors.GetSinceAsync(_watermark, _levels, ct);
        if (errors.Count == 0)
        {
            return;
        }

        DateTimeOffset previous = _watermark;
        _watermark = errors.Max(e => e.Timestamp);

        string sample = string.Join("\n", errors
            .Select(e => e.Message)
            .Distinct()
            .Take(SampleSize)
            .Select(m => "• " + Truncate(m, MaxMessageLength)));

        await _channel.SendAsync(new OpsAlert(
            OpsAlertKind.ErrorDigest,
            $"{errors.Count} errori applicativi",
            $"Dal {previous:o}:\n{sample}",
            DateTimeOffset.UtcNow), ct);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : string.Concat(value.AsSpan(0, max), "…");
}
