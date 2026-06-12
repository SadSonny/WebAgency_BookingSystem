// [INTENT]: Risposta alla disdetta cliente (DELETE /api/v1/bookings/{id}?token=...).

namespace WebAgency_BookingSystem.Core.Dtos.Public;

/// <summary>
/// Esito della disdetta di una prenotazione.
/// </summary>
/// <param name="Status">Nuovo stato, <c>cancelled</c>.</param>
/// <param name="Message">Messaggio di conferma in italiano.</param>
public sealed record CancelBookingResponse(
    Guid BookingId,
    string Status,
    string Message);
