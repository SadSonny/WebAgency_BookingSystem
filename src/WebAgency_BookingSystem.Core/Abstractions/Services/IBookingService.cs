// [INTENT]: Contratto delle operazioni sulle prenotazioni pubbliche: creazione atomica (con advisory lock
// per prevenire race condition sullo stesso slot), consultazione e disdetta tramite token. Tutti gli esiti
// attesi (slot occupato, regole violate, non trovato, fuori termine) sono veicolati via Result.

using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Public;

namespace WebAgency_BookingSystem.Core.Abstractions.Services;

/// <summary>
/// Gestisce creazione, consultazione e disdetta delle prenotazioni lato cliente finale.
/// </summary>
public interface IBookingService
{
    /// <summary>
    /// Crea una prenotazione in modo atomico: ri-verifica la disponibilità dentro la transazione protetta
    /// da advisory lock e applica le regole di business (anticipo minimo, finestra visibile). Restituisce
    /// un fallimento <see cref="ErrorType.Conflict"/> se lo slot non è più disponibile.
    /// </summary>
    /// <param name="request">Dati della prenotazione.</param>
    /// <param name="clientIpAnonymized">IP del cliente già anonimizzato (/24), per l'audit log; può essere null.</param>
    /// <param name="ct">Token di cancellazione.</param>
    Task<Result<CreateBookingResponse>> CreateAsync(
        CreateBookingRequest request, string? clientIpAnonymized, CancellationToken ct = default);

    /// <summary>
    /// Restituisce il dettaglio di una prenotazione validando id + token. In caso di mismatch restituisce
    /// un <see cref="ErrorType.NotFound"/> neutro (non rivela l'esistenza dell'id).
    /// </summary>
    Task<Result<BookingDetailResponse>> GetByTokenAsync(
        Guid bookingId, Guid token, CancellationToken ct = default);

    /// <summary>
    /// Disdice una prenotazione validando id + token e il preavviso minimo. Fallisce con
    /// <see cref="ErrorType.Forbidden"/> se il termine di disdetta è superato, o con
    /// <see cref="ErrorType.Validation"/> se la prenotazione non è in stato disdicibile.
    /// </summary>
    Task<Result<CancelBookingResponse>> CancelAsync(
        Guid bookingId, Guid token, CancellationToken ct = default);
}
