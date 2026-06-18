// [INTENT]: Endpoint platform di gestione tenant (crea/lista/dettaglio). Protetti da policy PlatformAdmin.
// Crea: valida col ProvisioningValidator (422) poi delega al service (409 su slug duplicato).
// Lista: paginata (page/pageSize query param). Dettaglio: per Guid.

using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Platform;
using WebAgency_BookingSystem.Core.Dtos.Provisioning;
using WebAgency_BookingSystem.Core.Provisioning;
using WebAgency_BookingSystem.Infrastructure.Auth;

namespace WebAgency_BookingSystem.Api.Endpoints.Platform;

internal static class PlatformTenantEndpoints
{
    public static IEndpointRouteBuilder MapPlatformTenantEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/platform/tenants")
            .WithTags("Platform")
            .RequireAuthorization(AdminClaims.PlatformPolicy);

        group.MapPost("", async (ProvisioningInput input, IPlatformTenantService svc, CancellationToken ct) =>
        {
            // WHY: la validazione del provisioning è una lista piatta di messaggi → 422 con errors["provisioning"].
            // Non si usa FluentValidation qui perché ProvisioningValidator è condiviso con la CLI.
            IReadOnlyList<string> errors = ProvisioningValidator.Validate(input);
            if (errors.Count > 0)
            {
                return Results.Json(
                    new ErrorResponse(
                        "validation_error",
                        "Input di provisioning non valido.",
                        new Dictionary<string, string[]> { ["provisioning"] = [.. errors] }),
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            Result<ProvisioningOutput> result = await svc.CreateAsync(input, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : result.Error.ToErrorResult();
        })
        .WithName("PlatformCreateTenant")
        .WithSummary("Crea tenant")
        .WithDescription("Esegue il provisioning completo di un nuovo tenant (servizi, staff, orari, chiusure, API key, Owner). Fallisce con 409 se lo slug esiste già, con 422 se l'input non è valido.")
        .Produces<ProvisioningOutput>(StatusCodes.Status201Created)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("", async (int? page, int? pageSize, IPlatformTenantService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(page ?? 1, pageSize ?? 50, ct)))
        .WithName("PlatformListTenants")
        .WithSummary("Elenca tenant (paginato)")
        .WithDescription("Restituisce tutti i tenant (attivi e non) in ordine di creazione discendente. Usa i query param `page` e `pageSize` (default 1/50, max pageSize 200).")
        .Produces<PagedResponse<PlatformTenantSummary>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", async (Guid id, IPlatformTenantService svc, CancellationToken ct) =>
        {
            Result<PlatformTenantSummary> result = await svc.GetAsync(id, ct);
            return result.Match(t => Results.Ok(t));
        })
        .WithName("PlatformGetTenant")
        .WithSummary("Dettaglio tenant")
        .Produces<PlatformTenantSummary>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        return app;
    }
}
