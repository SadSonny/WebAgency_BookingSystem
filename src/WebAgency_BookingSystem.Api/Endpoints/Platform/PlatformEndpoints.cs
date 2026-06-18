// [INTENT]: Registrazione centralizzata degli endpoint /api/v1/platform.
namespace WebAgency_BookingSystem.Api.Endpoints.Platform;

internal static class PlatformEndpoints
{
    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPlatformAuthEndpoints();
        app.MapPlatformAccountEndpoints();
        app.MapPlatformTenantEndpoints();
        return app;
    }
}
