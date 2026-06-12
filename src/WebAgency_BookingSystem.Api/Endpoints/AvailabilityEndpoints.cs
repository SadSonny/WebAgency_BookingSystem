// [INTENT]: Endpoint disponibilità (GET /api/v1/availability). Delega all'IAvailabilityService l'intero
// algoritmo (validazione semantica, generazione slot, capacità) e mappa il Result allo status HTTP. I
// parametri di query obbligatori sono validati nel handler e, se mancanti/malformati, producono l'envelope
// d'errore { type: bad_request } (R-31), coerente col contratto invece del 400 di binding di default.

using System.Globalization;
using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Public;

namespace WebAgency_BookingSystem.Api.Endpoints;

internal static class AvailabilityEndpoints
{
    public static IEndpointRouteBuilder MapAvailabilityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/availability", async (
            Guid? serviceId, Guid? staffId, string? dateFrom, string? dateTo,
            IAvailabilityService availability, CancellationToken ct) =>
        {
            if (serviceId is not Guid service)
            {
                return ResultMapping.BadRequest("Il parametro serviceId è obbligatorio.");
            }

            if (!TryParseDate(dateFrom, out DateOnly from))
            {
                return ResultMapping.BadRequest("Il parametro dateFrom è obbligatorio nel formato yyyy-MM-dd.");
            }

            if (!TryParseDate(dateTo, out DateOnly to))
            {
                return ResultMapping.BadRequest("Il parametro dateTo è obbligatorio nel formato yyyy-MM-dd.");
            }

            var request = new AvailabilityRequest(service, staffId, from, to);
            var result = await availability.GetAvailabilityAsync(request, ct);
            return result.Match(days => Results.Ok(days));
        })
        .WithName("GetAvailability")
        .WithSummary("Disponibilità slot per servizio")
        .WithDescription("""
            Restituisce gli slot prenotabili (granularità 15 min) per il servizio nell'intervallo richiesto
            (max 31 giorni). Con staffId la disponibilità è individuale; senza, è aggregata sui parallelSlots
            del servizio. I giorni chiusi non sono inclusi; i giorni pieni sono inclusi con slot non disponibili.
            """)
        .WithTags("Disponibilità")
        .RequireRateLimiting(RateLimitingPolicies.PublicApi)
        .Produces<IReadOnlyList<AvailabilityDayResponse>>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
