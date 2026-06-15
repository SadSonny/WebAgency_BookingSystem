// [INTENT]: Endpoint prenotazioni pubbliche: creazione (POST), consultazione (GET) e disdetta (DELETE) via
// cancellation token. La validazione del corpo (POST) avviene con FluentValidation prima del service; la
// logica atomica (lock, regole, disponibilità) è nel BookingService. Consultazione/disdetta usano id + token.

using FluentValidation;
using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Public;
using WebAgency_BookingSystem.Core.Security;

namespace WebAgency_BookingSystem.Api.Endpoints;

internal static class BookingEndpoints
{
    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/bookings", async (
            CreateBookingRequest request, IValidator<CreateBookingRequest> validator,
            IBookingService bookings, HttpContext http, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return validation.ToValidationProblem();
            }

            string? anonymizedIp = IpAnonymizer.Anonymize(http.Connection.RemoteIpAddress?.ToString());
            var result = await bookings.CreateAsync(request, anonymizedIp, ct);

            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : result.Error.ToErrorResult();
        })
        .WithName("CreateBooking")
        .WithSummary("Crea una prenotazione")
        .WithDescription("""
            Crea una prenotazione in modo atomico: verifica la disponibilità sotto advisory lock per evitare
            doppie prenotazioni sullo stesso slot. Restituisce 409 se lo slot non è più disponibile, 422 se i
            dati o le regole (anticipo, finestra prenotabile) non sono soddisfatti.
            """)
        .WithTags("Prenotazioni")
        // S1: la creazione usa un limite per-chiave più stringente (anti-spam); più basso del PublicApi, quindi
        // lo sostituisce. Il GlobalLimiter per IP resta comunque applicato.
        .RequireRateLimiting(RateLimitingPolicies.BookingCreation)
        .Produces<CreateBookingResponse>(StatusCodes.Status201Created)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        app.MapGet("/api/v1/bookings/{id:guid}", async (
            Guid id, Guid? token, IBookingService bookings, CancellationToken ct) =>
        {
            if (token is not Guid accessToken)
            {
                return ResultMapping.BadRequest("Il parametro token è obbligatorio.");
            }

            var result = await bookings.GetByTokenAsync(id, accessToken, ct);
            return result.Match(detail => Results.Ok(detail));
        })
        .WithName("GetBooking")
        .WithSummary("Dettaglio prenotazione")
        .WithDescription("Restituisce il dettaglio di una prenotazione validando id + token. 404 neutro se non combaciano.")
        .WithTags("Prenotazioni")
        .RequireRateLimiting(RateLimitingPolicies.PublicApi)
        .Produces<BookingDetailResponse>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        app.MapPut("/api/v1/bookings/{id:guid}/reschedule", async (
            Guid id, Guid? token, RescheduleBookingRequest request, IBookingService bookings, CancellationToken ct) =>
        {
            if (token is not Guid accessToken)
            {
                return ResultMapping.BadRequest("Il parametro token è obbligatorio.");
            }

            var result = await bookings.RescheduleAsync(id, accessToken, request.Date, request.Time, ct);
            return result.Match(detail => Results.Ok(detail));
        })
        .WithName("RescheduleBooking")
        .WithSummary("Sposta una prenotazione")
        .WithDescription("""
            Sposta una prenotazione confermata a una nuova data/ora (servizi e operatore invariati), via id + token.
            Ri-verifica la disponibilità del nuovo slot sotto advisory lock. 403 oltre il preavviso minimo,
            409 se il nuovo slot non è disponibile, 404 neutro se id/token non combaciano.
            """)
        .WithTags("Prenotazioni")
        .RequireRateLimiting(RateLimitingPolicies.PublicApi)
        .Produces<BookingDetailResponse>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        app.MapDelete("/api/v1/bookings/{id:guid}", async (
            Guid id, Guid? token, IBookingService bookings, CancellationToken ct) =>
        {
            if (token is not Guid accessToken)
            {
                return ResultMapping.BadRequest("Il parametro token è obbligatorio.");
            }

            var result = await bookings.CancelAsync(id, accessToken, ct);
            return result.Match(cancel => Results.Ok(cancel));
        })
        .WithName("CancelBooking")
        .WithSummary("Disdici una prenotazione")
        .WithDescription("Disdetta da parte del cliente via id + token. 403 se oltre il preavviso minimo, 404 neutro se id/token non combaciano.")
        .WithTags("Prenotazioni")
        .RequireRateLimiting(RateLimitingPolicies.PublicApi)
        .Produces<CancelBookingResponse>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
