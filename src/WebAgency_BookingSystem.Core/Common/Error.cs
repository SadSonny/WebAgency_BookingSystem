// [INTENT]: Errore di business immutabile usato dal pattern Result. Trasporta un codice macchina
// (stabile, per i client), un messaggio in italiano (per l'utente finale) e una categoria
// (ErrorType) che il layer API usa per scegliere lo status HTTP. I metodi factory evitano di
// ripetere ovunque la scelta della categoria.

namespace WebAgency_BookingSystem.Core.Common;

/// <summary>
/// Rappresenta un fallimento atteso di un'operazione di dominio. I messaggi sono in italiano
/// perché destinati alle response API verso il cliente finale.
/// </summary>
/// <param name="Code">Codice stabile e leggibile da macchina, es. <c>booking.slot_unavailable</c>.</param>
/// <param name="Message">Descrizione in italiano per l'utente finale.</param>
/// <param name="Type">Categoria che determina lo status HTTP nel layer API.</param>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    /// <summary>Assenza di errore. Usato internamente da un <see cref="Result"/> di successo.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    /// <summary>Crea un errore di validazione (→ 400).</summary>
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    /// <summary>Crea un errore di risorsa non trovata (→ 404).</summary>
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    /// <summary>Crea un errore di conflitto, es. slot non più disponibile (→ 409).</summary>
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    /// <summary>Crea un errore di autenticazione (→ 401).</summary>
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    /// <summary>Crea un errore di autorizzazione (→ 403).</summary>
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    /// <summary>Crea un errore generico non previsto (→ 500).</summary>
    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);
}
