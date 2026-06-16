// [INTENT]: Endpoint di login admin (POST /api/v1/admin/auth/token). Anonimo (non richiede JWT) e senza
// risoluzione tenant: il tenant è identificato dallo slug nel corpo. Valida l'input e delega all'IAdminAuthService.

using FluentValidation;
using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.Api.Endpoints.Admin;

internal static class AdminAuthEndpoints
{
    public static IEndpointRouteBuilder MapAdminAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/admin/auth/token", async (
            AdminLoginRequest request, IValidator<AdminLoginRequest> validator,
            IAdminAuthService auth, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return validation.ToValidationProblem();
            }

            var result = await auth.LoginAsync(request, ct);
            return result.Match(token => Results.Ok(token));
        })
        .WithName("AdminLogin")
        .WithSummary("Login admin")
        .WithDescription("Autentica un utente admin (tenant slug + email + password) e restituisce un token JWT.")
        .WithTags("Admin")
        .Produces<AdminTokenResponse>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)
        .RequireRateLimiting(RateLimitingPolicies.AccountSecurity);

        return app;
    }
}
