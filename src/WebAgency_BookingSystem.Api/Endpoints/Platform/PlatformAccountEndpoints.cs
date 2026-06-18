// [INTENT]: Endpoint account agency-admin: setup/break-glass (anonimo, gated da setup token) e cambio password (JWT).

using System.Security.Claims;
using FluentValidation;
using Microsoft.IdentityModel.JsonWebTokens;
using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Core.Dtos.Platform;
using WebAgency_BookingSystem.Infrastructure.Auth;

namespace WebAgency_BookingSystem.Api.Endpoints.Platform;

internal static class PlatformAccountEndpoints
{
    public static IEndpointRouteBuilder MapPlatformAccountEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/platform").WithTags("Platform");

        group.MapPost("/setup", async (
            PlatformSetupRequest request, IValidator<PlatformSetupRequest> validator,
            IPlatformAccountService account, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return validation.ToValidationProblem();
            var result = await account.SetupAsync(request, ct);
            return result.IsSuccess
                ? Results.Json(new { created = result.Value }, statusCode: StatusCodes.Status200OK)
                : result.Error.ToErrorResult();
        })
        .WithName("PlatformSetup")
        .WithSummary("Setup/break-glass agency-admin")
        .WithDescription("Crea o reimposta la password dell'agency-admin per email. Gated da PLATFORM_SETUP_TOKEN; " +
                         "restituisce 404 se la variabile d'ambiente non è configurata (endpoint disabilitato).")
        .Produces(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)
        .RequireRateLimiting(RateLimitingPolicies.AccountSecurity);

        group.MapPost("/account/password", async (
            ChangePasswordRequest request, IValidator<ChangePasswordRequest> validator,
            ClaimsPrincipal principal, IPlatformAccountService account, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return validation.ToValidationProblem();

            // WHY: il claim "sub" è il PlatformAdminId emesso da IJwtTokenGenerator.GeneratePlatform.
            // Se non è parsabile come Guid, il token è malformato → Forbid (non dovrebbe mai accadere
            // con JWT validi firmati dal sistema, ma è un guard obbligatorio).
            string? sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (!Guid.TryParse(sub, out Guid id)) return Results.Forbid();

            var result = await account.ChangePasswordAsync(id, request, ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToErrorResult();
        })
        .WithName("PlatformChangePassword")
        .WithSummary("Cambia password agency-admin")
        .WithDescription("Cambia la password dell'agency-admin autenticato. Richiede JWT di piattaforma. " +
                         "Rigenera la SecurityStamp invalidando i JWT precedenti.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .RequireAuthorization(AdminClaims.PlatformPolicy)
        .RequireRateLimiting(RateLimitingPolicies.AccountSecurity);

        return app;
    }
}
