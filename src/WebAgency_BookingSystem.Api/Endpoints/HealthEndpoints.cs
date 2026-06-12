// [INTENT]: Endpoint di health check (GET /api/v1/health, no auth). Usato da Railway come liveness probe.
// Verifica la connessione al DB: se raggiungibile 200, altrimenti 503. Non richiede tenant né rate limiting.

using WebAgency_BookingSystem.Core.Dtos.Public;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Api.Endpoints;

internal static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/health", async (BookingSystemDbContext db, CancellationToken ct) =>
        {
            bool dbReachable = await db.Database.CanConnectAsync(ct);
            var response = new HealthResponse(dbReachable ? "ok" : "unavailable", DateTimeOffset.UtcNow.ToString("o"));
            return dbReachable
                ? Results.Ok(response)
                : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
        })
        .WithName("GetHealth")
        .WithSummary("Health check del backend")
        .WithDescription("Verifica la raggiungibilità del database. Restituisce 200 se il backend è sano, 503 altrimenti.")
        .WithTags("Sistema")
        .Produces<HealthResponse>(StatusCodes.Status200OK)
        .Produces<HealthResponse>(StatusCodes.Status503ServiceUnavailable);

        return app;
    }
}
