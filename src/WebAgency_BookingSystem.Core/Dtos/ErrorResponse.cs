// [INTENT]: Envelope unico di errore per tutte le response API, come da contratto (spec 03):
// { type, message, errors? }. `type` è un codice snake_case stabile, `message` è in italiano per
// l'utente finale, `errors` è presente solo per gli errori di validazione (422) con dettaglio per campo.

namespace WebAgency_BookingSystem.Core.Dtos;

/// <summary>
/// Corpo standard di una risposta di errore. Serializzato con naming camelCase.
/// </summary>
/// <param name="Type">Codice errore snake_case, es. <c>slot_unavailable</c>, <c>validation_error</c>.</param>
/// <param name="Message">Messaggio leggibile in italiano.</param>
/// <param name="Errors">Dettaglio per campo, presente solo nei <c>validation_error</c> (422); altrimenti null.</param>
public sealed record ErrorResponse(
    string Type,
    string Message,
    IReadOnlyDictionary<string, string[]>? Errors = null);
