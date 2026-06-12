// [INTENT]: Per le rotte admin autenticate (escluso /admin/auth), popola ITenantContext leggendo il claim
// tenant_id dal JWT e caricando il tenant. È l'equivalente del TenantResolutionMiddleware per il layer admin:
// senza di esso il global query filter del DbContext non avrebbe un tenant su cui filtrare. 403 se il claim
// è assente o il tenant è disattivato. Va registrato DOPO UseAuthentication/UseAuthorization.

using Serilog.Context;
using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Infrastructure.Auth;

namespace WebAgency_BookingSystem.Api.Middleware;

/// <summary>
/// Risolve il tenant corrente per le rotte admin a partire dal JWT.
/// </summary>
public sealed class AdminContextMiddleware
{
    private readonly RequestDelegate _next;

    public AdminContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, ITenantRepository tenants)
    {
        if (!RequiresAdminTenant(context.Request.Path) || context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        string? tenantIdClaim = context.User.FindFirst(AdminClaims.TenantId)?.Value;
        if (!Guid.TryParse(tenantIdClaim, out Guid tenantId))
        {
            await HttpErrorWriter.WriteAsync(context, StatusCodes.Status403Forbidden,
                "forbidden", "Token privo di un tenant valido.", context.RequestAborted);
            return;
        }

        Tenant? tenant = await tenants.GetByIdAsync(tenantId, context.RequestAborted);
        if (tenant is not { Active: true })
        {
            await HttpErrorWriter.WriteAsync(context, StatusCodes.Status403Forbidden,
                "forbidden", "Tenant non valido o disattivato.", context.RequestAborted);
            return;
        }

        tenantContext.SetTenant(tenant);
        using (LogContext.PushProperty("TenantId", tenant.Id))
        {
            await _next(context);
        }
    }

    // Si applica alle rotte /api/v1/admin tranne /api/v1/admin/auth (login, anonimo e senza tenant).
    private static bool RequiresAdminTenant(PathString path) =>
        path.StartsWithSegments("/api/v1/admin") && !path.StartsWithSegments("/api/v1/admin/auth");
}
