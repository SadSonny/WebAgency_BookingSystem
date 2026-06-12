// [INTENT]: Risposta alla creazione prenotazione (201). Restituisce il token di disdetta, da conservare
// lato client per consultare/disdire la prenotazione senza autenticazione.

namespace WebAgency_BookingSystem.Core.Dtos.Public;

/// <summary>
/// Esito della creazione di una prenotazione.
/// </summary>
/// <param name="Status">Stato della prenotazione, es. <c>confirmed</c>.</param>
/// <param name="CancellationToken">Token opaco per consultazione/disdetta pubblica.</param>
public sealed record CreateBookingResponse(
    Guid BookingId,
    string Status,
    Guid CancellationToken);
