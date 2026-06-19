// [INTENT]: Astrazione del canale di recapito degli alert operativi. Implementazioni: LogOnly (fallback) e
// Telegram (reale). Disaccoppia il rilevamento (scanner) dal trasporto, sostituibile via DI.

namespace WebAgency_BookingSystem.Core.Observability;

/// <summary>Recapita un alert operativo. Le implementazioni NON devono propagare eccezioni di trasporto.</summary>
public interface IOpsAlertChannel
{
    /// <summary>Invia l'alert sul canale configurato. Un fallimento di trasporto è loggato, non propagato.</summary>
    Task SendAsync(OpsAlert alert, CancellationToken ct = default);
}
