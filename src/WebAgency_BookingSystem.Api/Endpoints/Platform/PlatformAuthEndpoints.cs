// [INTENT]: Endpoint di login agency-admin (POST /api/v1/platform/auth/token). Anonimo, niente tenant.

using FluentValidation;
using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Core.Dtos.Platform;

namespace WebAgency_BookingSystem.Api.Endpoints.Platform;

internal static class PlatformAuthEndpoints
{
    public static IEndpointRouteBuilder MapPlatformAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/platform/auth/token", async (
            PlatformLoginRequest request, IValidator<PlatformLoginRequest> validator,
            IPlatformAuthService auth, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return validation.ToValidationProblem();
            var result = await auth.LoginAsync(request, ct);
            return result.Match(token => Results.Ok(token));
        })
        .WithName("PlatformLogin").WithSummary("Login agency-admin").WithTags("Platform")
        .Produces<AdminTokenResponse>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)
        .RequireRateLimiting(RateLimitingPolicies.AccountSecurity);
        return app;
    }
}
