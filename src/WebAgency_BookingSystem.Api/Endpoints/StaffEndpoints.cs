// [INTENT]: Endpoint lista staff (GET /api/v1/staff?serviceId=). Restituisce lo staff attivo del tenant,
// opzionalmente filtrato per servizio. Se serviceId è indicato ma il servizio non esiste/non è attivo → 404.

using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Public;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Api.Endpoints;

internal static class StaffEndpoints
{
    public static IEndpointRouteBuilder MapStaffEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/staff", async (Guid? serviceId, IServiceRepository services, IStaffRepository staff, CancellationToken ct) =>
        {
            IReadOnlyList<Staff> list;
            if (serviceId is Guid id)
            {
                Service? service = await services.GetActiveByIdAsync(id, ct);
                if (service is null)
                {
                    return Error.NotFound("not_found", "Servizio non trovato o non attivo.").ToErrorResult();
                }

                list = await staff.GetActiveByServiceAsync(id, ct);
            }
            else
            {
                list = await staff.GetActiveAsync(ct);
            }

            var response = list.Select(s => new StaffResponse(
                s.Id, s.Name, s.Role, s.Specialization, s.PhotoUrl, s.Active)).ToList();

            return Results.Ok(response);
        })
        .WithName("GetStaff")
        .WithSummary("Lista staff attivo")
        .WithDescription("Restituisce lo staff attivo del tenant. Con il parametro serviceId filtra chi esegue quel servizio.")
        .WithTags("Staff")
        .RequireRateLimiting(RateLimitingPolicies.PublicApi)
        .Produces<IReadOnlyList<StaffResponse>>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        return app;
    }
}
