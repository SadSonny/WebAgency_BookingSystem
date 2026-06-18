// [INTENT]: Risolve il tenant corrente dall'header X-Api-Key e lo inietta nel contesto (ITenantContext +
// HttpContext.Items), prerequisito per tutti gli endpoint tenant-scoped. 401 se la chiave manca, 403 se non
// è valida o il tenant è disattivato. Esclude /health (no auth), /admin (JWT tenant) e /platform (JWT platform)
// e le rotte non /api/v1.

using Serilog.Context;
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
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

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
            // WHY: utile per diagnosticare integrazioni frontend mal configurate. Non logghiamo materiale
            // della chiave (qui assente comunque). Warning perché è un accesso negato, non un flusso normale.
            _logger.LogWarning("Accesso negato: API key mancante su {Method} {Path}",
                context.Request.Method, context.Request.Path);
            await HttpErrorWriter.WriteAsync(context, StatusCodes.Status401Unauthorized,
                "unauthorized", "API key mancante.", context.RequestAborted);
            return;
        }

        string keyHash = ApiKeyHasher.Hash(apiKey);
        Tenant? tenant = await tenantRepository.ResolveActiveByApiKeyHashAsync(keyHash, context.RequestAborted);
        if (tenant is null)
        {
            // WHY: rilevante per il monitoraggio di sicurezza (chiavi compromesse/revocate, brute-force).
            // Non logghiamo la chiave in chiaro; il suo hash è sufficiente a correlare senza esporla.
            _logger.LogWarning("Accesso negato: API key non valida (hash {KeyHash}) su {Path}",
                keyHash, context.Request.Path);
            await HttpErrorWriter.WriteAsync(context, StatusCodes.Status403Forbidden,
                "forbidden", "API key non valida.", context.RequestAborted);
            return;
        }

        tenantContext.SetTenant(tenant);

        // WHY: da qui in poi tutti i log della richiesta portano il TenantId, per correlare le operazioni
        // a un tenant specifico senza doverlo passare manualmente a ogni log.
        using (LogContext.PushProperty("TenantId", tenant.Id))
        {
            await _next(context);
        }
    }

    // WHY: la risoluzione si applica solo agli endpoint pubblici tenant-scoped. /health è una liveness probe
    // senza auth; /admin usa JWT tenant; /platform usa JWT agency-admin (nessuna X-Api-Key); tutto ciò che
    // non è /api/v1 (es. /scalar, /openapi) è escluso.
    private static bool RequiresApiKey(PathString path)
    {
        if (!path.StartsWithSegments("/api/v1"))
        {
            return false;
        }

        if (path.StartsWithSegments("/api/v1/health") ||
            path.StartsWithSegments("/api/v1/admin") ||
            path.StartsWithSegments("/api/v1/platform"))
        {
            return false;
        }

        return true;
    }
}
