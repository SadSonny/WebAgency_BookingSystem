// [INTENT]: Endpoint lista servizi (GET /api/v1/services). Restituisce i servizi attivi del tenant con gli
// Id degli staff che li eseguono (per il filtro frontend). Il prezzo è il base_price del servizio.

using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Public;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Api.Endpoints;

internal static class ServiceEndpoints
{
    public static IEndpointRouteBuilder MapServiceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/services", async (IServiceRepository services, CancellationToken ct) =>
        {
            IReadOnlyList<Service> active = await services.GetActiveAsync(ct);
            IReadOnlyList<Guid> ids = active.Select(s => s.Id).ToList();
            IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> staffIds = await services.GetStaffIdsByServiceAsync(ids, ct);

            var response = active.Select(s => new ServiceResponse(
                s.Id,
                s.Name,
                s.Category,
                s.DurationMinutes,
                s.BasePrice,
                s.Description,
                staffIds.TryGetValue(s.Id, out IReadOnlyList<Guid>? linked) ? linked : [],
                s.Active)).ToList();

            return Results.Ok(response);
        })
        .WithName("GetServices")
        .WithSummary("Lista servizi attivi")
        .WithDescription("Restituisce i servizi attivi del tenant, ordinati, con gli Id degli staff che li eseguono.")
        .WithTags("Servizi")
        .RequireRateLimiting(RateLimitingPolicies.PublicApi)
        .Produces<IReadOnlyList<ServiceResponse>>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status403Forbidden);

        return app;
    }
}
