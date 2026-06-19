// [INTENT]: Astrazione del self-check di connettività al database, usata dal monitor OPS per rilevare le
// transizioni down/recovery. Separata dalla sorgente log per testabilità.

namespace WebAgency_BookingSystem.Core.Observability;

/// <summary>Verifica se il database è raggiungibile in questo momento.</summary>
public interface IDbHealthProbe
{
    /// <summary>True se la connessione al DB riesce; false altrimenti (non lancia).</summary>
    Task<bool> CanConnectAsync(CancellationToken ct = default);
}
