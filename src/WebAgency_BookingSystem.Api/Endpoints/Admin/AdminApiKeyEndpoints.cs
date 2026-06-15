// [INTENT]: Endpoint admin per la rotazione/revoca delle API key del tenant (S4), protetti da JWT. Permettono
// di generare una nuova chiave (mostrata una sola volta), elencarle (solo prefisso/metadati) e revocarle.

using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.Api.Endpoints.Admin;

internal static class AdminApiKeyEndpoints
{
    public static IEndpointRouteBuilder MapAdminApiKeyEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/admin/api-keys").WithTags("Admin").RequireAuthorization();

        group.MapGet("", async (IAdminApiKeyManager keys, CancellationToken ct) =>
        {
            var result = await keys.ListAsync(ct);
            return result.Match(list => Results.Ok(list));
        })
        .WithName("AdminListApiKeys")
        .WithSummary("Lista API key (admin)")
        .WithDescription("Elenca le API key del tenant: solo prefisso e metadati, mai il segreto.")
        .Produces<IReadOnlyList<ApiKeyResponse>>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        group.MapPost("", async (CreateApiKeyRequest request, IAdminApiKeyManager keys, CancellationToken ct) =>
        {
            var result = await keys.CreateAsync(request, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : result.Error.ToErrorResult();
        })
        .WithName("AdminCreateApiKey")
        .WithSummary("Crea API key (admin)")
        .WithDescription("Genera una nuova API key. Il valore in chiaro è restituito UNA sola volta: salvalo subito.")
        .Produces<CreateApiKeyResponse>(StatusCodes.Status201Created)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{id:guid}", async (Guid id, IAdminApiKeyManager keys, CancellationToken ct) =>
        {
            var result = await keys.RevokeAsync(id, ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToErrorResult();
        })
        .WithName("AdminRevokeApiKey")
        .WithSummary("Revoca API key (admin)")
        .WithDescription("Disattiva una API key. Effettiva entro la TTL di cache della risoluzione (qui rimossa subito dalla cache).")
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        return app;
    }
}
