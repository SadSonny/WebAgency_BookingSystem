// [INTENT]: Traduce il Result<T> di dominio in IResult HTTP. Centralizza la mappatura ErrorType -> status
// code (la categoria dell'errore decide lo status; il codice dell'errore diventa il campo `type` della
// response). Così gli endpoint restano dichiarativi: result.Match(value => TypedResults.Ok(value)).

using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos;

namespace WebAgency_BookingSystem.Api.Http;

/// <summary>
/// Estensioni di mappatura da <see cref="Result"/>/<see cref="Error"/> a <see cref="IResult"/>.
/// </summary>
internal static class ResultMapping
{
    /// <summary>Mappa la categoria di errore allo status HTTP previsto dal contratto (spec 03).</summary>
    public static int ToStatusCode(this ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status422UnprocessableEntity,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status500InternalServerError,
    };

    /// <summary>Costruisce la response di errore (envelope { type, message }) a partire da un <see cref="Error"/>.</summary>
    public static IResult ToErrorResult(this Error error) =>
        Results.Json(new ErrorResponse(error.Code, error.Message), statusCode: error.Type.ToStatusCode());

    /// <summary>
    /// Risposta 400 <c>bad_request</c> per richieste malformate (parametri obbligatori mancanti o non
    /// parsabili), nell'envelope standard del contratto.
    /// </summary>
    public static IResult BadRequest(string message) =>
        Results.Json(new ErrorResponse("bad_request", message), statusCode: StatusCodes.Status400BadRequest);

    /// <summary>
    /// Restituisce <paramref name="onSuccess"/> applicato al valore se il Result è positivo, altrimenti
    /// la response di errore mappata.
    /// </summary>
    public static IResult Match<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : result.Error.ToErrorResult();
}
