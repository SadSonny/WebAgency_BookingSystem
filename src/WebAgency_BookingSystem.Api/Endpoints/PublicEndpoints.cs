// [INTENT]: Aggregatore degli endpoint pubblici. Un solo punto da chiamare in Program.cs (MapPublicEndpoints)
// che registra in ordine tutti i gruppi di rotte pubbliche. Tenere qui l'elenco rende immediato sapere
// quali endpoint pubblici esistono.

namespace WebAgency_BookingSystem.Api.Endpoints;

/// <summary>
/// Registrazione centralizzata degli endpoint pubblici (API key).
/// </summary>
internal static class PublicEndpoints
{
    /// <summary>Mappa tutti gli endpoint pubblici: health, tenant config, servizi, staff, disponibilità, prenotazioni.</summary>
    public static IEndpointRouteBuilder MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthEndpoints();
        app.MapTenantConfigEndpoints();
        app.MapServiceEndpoints();
        app.MapStaffEndpoints();
        app.MapAvailabilityEndpoints();
        app.MapBookingEndpoints();
        return app;
    }
}
