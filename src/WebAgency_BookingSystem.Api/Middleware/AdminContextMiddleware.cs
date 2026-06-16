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

    // Rotte /api/v1/admin che richiedono tenant dal JWT: tutte tranne /auth e le rotte account ANONIME
    // (attivazione e reset, autenticate dal token nel corpo, non dal JWT).
    // WHY: /api/v1/admin/account/password (cambio password autenticato) NON è escluso → ottiene il tenant dal
    // JWT (corretto). "reset-request" è un segmento diverso da "reset", quindi va escluso esplicitamente.
    private static bool RequiresAdminTenant(PathString path) =>
        path.StartsWithSegments("/api/v1/admin")
        && !path.StartsWithSegments("/api/v1/admin/auth")
        && !path.StartsWithSegments("/api/v1/admin/account/activate")
        && !path.StartsWithSegments("/api/v1/admin/account/password/reset")
        && !path.StartsWithSegments("/api/v1/admin/account/password/reset-request");
}
