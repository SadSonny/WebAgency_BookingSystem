// [INTENT]: Contratto del calcolo di disponibilità (GET /api/v1/availability). Incapsula l'intero algoritmo
// (orari, chiusure, pausa, anticipo minimo, capacità per servizio/staff) e restituisce gli slot per giorno.
// Gli errori di validazione (servizio/staff/range non validi) sono veicolati via Result, non eccezioni.

using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Public;

namespace WebAgency_BookingSystem.Core.Abstractions.Services;

/// <summary>
/// Calcola gli slot prenotabili per un servizio in un intervallo di date.
/// </summary>
public interface IAvailabilityService
{
    /// <summary>
    /// Restituisce la disponibilità per giorno. In caso di input non valido (servizio inesistente,
    /// staff non abilitato, range oltre 31 giorni o invertito) restituisce un <see cref="Result"/>
    /// di fallimento con l'<see cref="Error"/> appropriato. I giorni chiusi non sono inclusi; i giorni
    /// aperti senza slot disponibili sono inclusi con tutti gli slot a <c>available: false</c>.
    /// </summary>
    Task<Result<IReadOnlyList<AvailabilityDayResponse>>> GetAvailabilityAsync(
        AvailabilityRequest request, CancellationToken ct = default);
}
