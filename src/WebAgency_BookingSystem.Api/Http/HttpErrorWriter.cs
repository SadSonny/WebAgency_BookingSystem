// [INTENT]: Scrive l'envelope di errore standard ({ type, message }) direttamente sulla HttpResponse.
// Usato dai middleware (tenant resolution, error handling, rate limiter) che operano fuori dal modello
// degli endpoint e quindi non possono restituire un IResult.

using WebAgency_BookingSystem.Core.Dtos;

namespace WebAgency_BookingSystem.Api.Http;

/// <summary>
/// Helper per serializzare un <see cref="ErrorResponse"/> nella pipeline middleware.
/// </summary>
internal static class HttpErrorWriter
{
    /// <summary>
    /// Imposta lo status code e scrive il corpo di errore JSON, se la response non è già iniziata.
    /// </summary>
    public static async Task WriteAsync(HttpContext context, int statusCode, string type, string message, CancellationToken ct = default)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new ErrorResponse(type, message), ct);
    }
}
