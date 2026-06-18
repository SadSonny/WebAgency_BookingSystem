// [INTENT]: Endpoint dell'area account Owner. Anonimi (token nel corpo): attivazione e reset (GET pagina + POST).
// Autenticato (JWT): cambio password. Le pagine GET servono HTML minimale; i POST delegano a IAdminAccountService.

using System.Security.Claims;
using FluentValidation;
using Microsoft.IdentityModel.JsonWebTokens;
using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Admin;

using WebAgency_BookingSystem.Infrastructure.Auth;

namespace WebAgency_BookingSystem.Api.Endpoints.Admin;

internal static class AdminAccountEndpoints
{
    public static IEndpointRouteBuilder MapAdminAccountEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/admin/account").WithTags("Admin");

        group.MapGet("/activate", (string token) =>
            Results.Content(
                AccountHtmlPages.SetPasswordPage("Attiva il tuo account", token, "/api/v1/admin/account/activate"),
                "text/html"))
            .WithName("AdminAccountActivatePage")
            .WithSummary("Pagina di attivazione account")
            .WithDescription("Pagina HTML con form per impostare la prima password a partire dal token di attivazione.")
            .ExcludeFromDescription();

        group.MapPost("/activate", async (
            SetPasswordRequest request, IValidator<SetPasswordRequest> validator,
            IAdminAccountService account, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return validation.ToValidationProblem();
            }

            var result = await account.ActivateAsync(request, ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToErrorResult();
        })
            .WithName("AdminAccountActivate")
            .WithSummary("Attiva account (imposta prima password)")
            .WithDescription("Imposta la prima password dall'invito di attivazione. Token monouso a scadenza.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)
            .RequireRateLimiting(RateLimitingPolicies.AccountSecurity);

        group.MapPost("/password/reset-request", async (
            PasswordResetRequest request, IValidator<PasswordResetRequest> validator,
            IAdminAccountService account, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return validation.ToValidationProblem();
            }

            await account.RequestPasswordResetAsync(request, ct);
            return Results.Accepted();
        })
            .WithName("AdminAccountResetRequest")
            .WithSummary("Richiedi reset password")
            .WithDescription("Invia (se l'email è registrata) un link di reset. La risposta è sempre 202, per non rivelare l'esistenza dell'email.")
            .Produces(StatusCodes.Status202Accepted)
            .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)
            .RequireRateLimiting(RateLimitingPolicies.AccountSecurity);

        group.MapGet("/password/reset", (string token) =>
            Results.Content(
                AccountHtmlPages.SetPasswordPage("Reimposta la password", token, "/api/v1/admin/account/password/reset"),
                "text/html"))
            .WithName("AdminAccountResetPage")
            .ExcludeFromDescription();

        group.MapPost("/password/reset", async (
            SetPasswordRequest request, IValidator<SetPasswordRequest> validator,
            IAdminAccountService account, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return validation.ToValidationProblem();
            }

            var result = await account.ResetPasswordAsync(request, ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToErrorResult();
        })
            .WithName("AdminAccountReset")
            .WithSummary("Reimposta password (da token)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)
            .RequireRateLimiting(RateLimitingPolicies.AccountSecurity);

        group.MapPost("/password", async (
            ChangePasswordRequest request, IValidator<ChangePasswordRequest> validator,
            ClaimsPrincipal principal, IAdminAccountService account, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return validation.ToValidationProblem();
            }

            string? sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (!Guid.TryParse(sub, out Guid userId))
            {
                return Results.Forbid();
            }

            var result = await account.ChangePasswordAsync(userId, request, ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToErrorResult();
        })
            .WithName("AdminAccountChangePassword")
            .WithSummary("Cambia password (Owner autenticato)")
            .WithDescription("Cambia la password verificando quella corrente. Invalida i token JWT emessi prima del cambio.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .RequireAuthorization(AdminClaims.AdminPolicy)
            .RequireRateLimiting(RateLimitingPolicies.AccountSecurity);

        return app;
    }
}
