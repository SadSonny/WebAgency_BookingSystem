// [INTENT]: Risolve il tenant corrente dall'header X-Api-Key e lo inietta nel contesto (ITenantContext +
// HttpContext.Items), prerequisito per tutti gli endpoint tenant-scoped. 401 se la chiave manca, 403 se non
// è valida o il tenant è disattivato. Esclude /health (no auth) e /admin (JWT futuro) e le rotte non /api/v1.

using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Security;

namespace WebAgency_BookingSystem.Api.Middleware;

/// <summary>
/// Middleware di risoluzione del tenant tramite API key. Si applica solo alle rotte pubbliche tenant-scoped.
/// </summary>
public sealed class TenantResolutionMiddleware
{
    private const string ApiKeyHeader = "X-Api-Key";
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, ITenantRepository tenantRepository)
    {
        if (!RequiresApiKey(context.Request.Path))
        {
            await _next(context);
            return;
        }

        string? apiKey = context.Request.Headers[ApiKeyHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            await HttpErrorWriter.WriteAsync(context, StatusCodes.Status401Unauthorized,
                "unauthorized", "API key mancante.", context.RequestAborted);
            return;
        }

        string keyHash = ApiKeyHasher.Hash(apiKey);
        Tenant? tenant = await tenantRepository.ResolveActiveByApiKeyHashAsync(keyHash, context.RequestAborted);
        if (tenant is null)
        {
            await HttpErrorWriter.WriteAsync(context, StatusCodes.Status403Forbidden,
                "forbidden", "API key non valida.", context.RequestAborted);
            return;
        }

        tenantContext.SetTenant(tenant.Id);
        context.Items[HttpContextExtensions.TenantItemKey] = tenant;

        await _next(context);
    }

    // WHY: la risoluzione si applica solo agli endpoint pubblici tenant-scoped. /health è una liveness probe
    // senza auth; /admin userà JWT separato; tutto ciò che non è /api/v1 (es. /scalar, /openapi) è escluso.
    private static bool RequiresApiKey(PathString path)
    {
        if (!path.StartsWithSegments("/api/v1"))
        {
            return false;
        }

        if (path.StartsWithSegments("/api/v1/health") || path.StartsWithSegments("/api/v1/admin"))
        {
            return false;
        }

        return true;
    }
}
