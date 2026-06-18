// [INTENT]: Endpoint platform di gestione tenant (crea/lista/dettaglio/attiva-disattiva + API key cross-tenant
// + re-invio attivazione Owner). Protetti da policy PlatformAdmin.
// Crea: valida col ProvisioningValidator (422) poi delega al service (409 su slug duplicato).
// Lista: paginata (page/pageSize query param). Dettaglio: per Guid.
// Deactivate: imposta Active=false ed evacua la cache delle API key del tenant.

using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Admin;
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

        group.MapPost("/{id:guid}/deactivate", async (Guid id, IPlatformTenantService svc, CancellationToken ct) =>
        {
            Result r = await svc.SetActiveAsync(id, false, ct);
            return r.IsSuccess ? Results.NoContent() : r.Error.ToErrorResult();
        })
        .WithName("PlatformDeactivateTenant")
        .WithSummary("Disattiva tenant")
        .WithDescription("Imposta Active=false ed evacua immediatamente la cache delle API key del tenant, così le richieste in ingresso smettono di risolverlo senza attendere la TTL.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/reactivate", async (Guid id, IPlatformTenantService svc, CancellationToken ct) =>
        {
            Result r = await svc.SetActiveAsync(id, true, ct);
            return r.IsSuccess ? Results.NoContent() : r.Error.ToErrorResult();
        })
        .WithName("PlatformReactivateTenant")
        .WithSummary("Riattiva tenant")
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/api-keys", async (Guid id, IPlatformTenantService svc, CancellationToken ct) =>
        {
            Result<IReadOnlyList<ApiKeyResponse>> r = await svc.ListApiKeysAsync(id, ct);
            return r.Match(list => Results.Ok(list));
        })
        .WithName("PlatformListTenantApiKeys")
        .WithSummary("Elenca API key del tenant")
        .Produces<IReadOnlyList<ApiKeyResponse>>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/api-keys", async (Guid id, CreateApiKeyRequest request, IPlatformTenantService svc, CancellationToken ct) =>
        {
            Result<CreateApiKeyResponse> r = await svc.CreateApiKeyAsync(id, request.Description, ct);
            return r.IsSuccess ? Results.Json(r.Value, statusCode: StatusCodes.Status201Created) : r.Error.ToErrorResult();
        })
        .WithName("PlatformCreateTenantApiKey")
        .WithSummary("Crea API key per il tenant")
        .Produces<CreateApiKeyResponse>(StatusCodes.Status201Created)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}/api-keys/{keyId:guid}", async (Guid id, Guid keyId, IPlatformTenantService svc, CancellationToken ct) =>
        {
            Result r = await svc.RevokeApiKeyAsync(id, keyId, ct);
            return r.IsSuccess ? Results.NoContent() : r.Error.ToErrorResult();
        })
        .WithName("PlatformRevokeTenantApiKey")
        .WithSummary("Revoca API key del tenant")
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/owner/resend-activation", async (Guid id, IPlatformTenantService svc, CancellationToken ct) =>
        {
            Result r = await svc.ResendOwnerActivationAsync(id, ct);
            return r.IsSuccess ? Results.Accepted() : r.Error.ToErrorResult();
        })
        .WithName("PlatformResendOwnerActivation")
        .WithSummary("Re-invia attivazione Owner")
        .WithDescription("Invalida i token di attivazione attivi dell'Owner, ne genera uno nuovo e accoda l'email di attivazione. Risponde 202 anche se l'email è solo accodata (invio asincrono via outbox).")
        .Produces(StatusCodes.Status202Accepted)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        return app;
    }
}
