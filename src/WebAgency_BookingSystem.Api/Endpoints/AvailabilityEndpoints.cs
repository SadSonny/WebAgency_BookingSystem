// [INTENT]: Endpoint disponibilità (GET /api/v1/availability). Delega all'IAvailabilityService l'intero
// algoritmo (validazione input, generazione slot, capacità) e mappa il Result allo status HTTP. I parametri
// di data sono bindati come DateOnly dalla query (yyyy-MM-dd); formati errati danno 400 di binding.

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
            Guid serviceId, Guid? staffId, DateOnly dateFrom, DateOnly dateTo,
            IAvailabilityService availability, CancellationToken ct) =>
        {
            var request = new AvailabilityRequest(serviceId, staffId, dateFrom, dateTo);
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
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
