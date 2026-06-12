// [INTENT]: Endpoint di health (no auth, no rate limiting). Separa LIVENESS da READINESS (R-23):
// - /api/v1/health/live → liveness: l'app è viva, SENZA toccare il DB (probe frequenti non caricano il DB).
// - /api/v1/health      → readiness: verifica la connessione al DB (200 se sano, 503 altrimenti).
// Per il liveness probe della piattaforma puntare a /health/live; per il readiness probe a /health.

using WebAgency_BookingSystem.Core.Dtos.Public;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Api.Endpoints;

internal static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/health/live", () =>
            Results.Ok(new HealthResponse("ok", DateTimeOffset.UtcNow.ToString("o"))))
        .WithName("GetLiveness")
        .WithSummary("Liveness probe")
        .WithDescription("Indica che il processo è vivo. Non verifica dipendenze esterne (nessun accesso al DB).")
        .WithTags("Sistema")
        .Produces<HealthResponse>(StatusCodes.Status200OK);

        app.MapGet("/api/v1/health", async (BookingSystemDbContext db, CancellationToken ct) =>
        {
            bool dbReachable = await db.Database.CanConnectAsync(ct);
            var response = new HealthResponse(dbReachable ? "ok" : "unavailable", DateTimeOffset.UtcNow.ToString("o"));
            return dbReachable
                ? Results.Ok(response)
                : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
        })
        .WithName("GetReadiness")
        .WithSummary("Readiness probe")
        .WithDescription("Verifica la raggiungibilità del database. Restituisce 200 se il backend è pronto, 503 altrimenti.")
        .WithTags("Sistema")
        .Produces<HealthResponse>(StatusCodes.Status200OK)
        .Produces<HealthResponse>(StatusCodes.Status503ServiceUnavailable);

        return app;
    }
}
