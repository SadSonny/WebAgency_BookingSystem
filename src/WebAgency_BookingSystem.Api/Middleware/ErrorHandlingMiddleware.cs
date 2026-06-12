// [INTENT]: Rete di sicurezza per le eccezioni NON gestite: le registra (con dettaglio nei log) e restituisce
// un 500 con envelope neutro { type: internal_error } senza esporre dettagli interni al client. Gli errori
// ATTESI viaggiano invece via Result e non passano di qui. Va registrato come primo middleware della pipeline.

using WebAgency_BookingSystem.Api.Http;

namespace WebAgency_BookingSystem.Api.Middleware;

/// <summary>
/// Cattura le eccezioni non gestite e produce una response 500 conforme al contratto di errore.
/// </summary>
public sealed class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (BadHttpRequestException ex)
        {
            // WHY: corpo JSON malformato o binding del body fallito producono questa eccezione. La mappiamo
            // all'envelope { type: bad_request } per coerenza con il contratto, invece del 500 generico.
            _logger.LogWarning(ex, "Richiesta malformata su {Method} {Path}", context.Request.Method, context.Request.Path);

            if (context.Response.HasStarted)
            {
                throw;
            }

            await HttpErrorWriter.WriteAsync(context, StatusCodes.Status400BadRequest,
                "bad_request", "Richiesta non valida o corpo della richiesta malformato.", context.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eccezione non gestita su {Method} {Path}", context.Request.Method, context.Request.Path);

            // WHY: se la response è già iniziata non possiamo riscrivere lo status; rilanciamo e lasciamo
            // che sia l'host a chiudere la connessione, evitando una risposta corrotta a metà.
            if (context.Response.HasStarted)
            {
                throw;
            }

            await HttpErrorWriter.WriteAsync(context, StatusCodes.Status500InternalServerError,
                "internal_error", "Si è verificato un errore interno.", context.RequestAborted);
        }
    }
}
