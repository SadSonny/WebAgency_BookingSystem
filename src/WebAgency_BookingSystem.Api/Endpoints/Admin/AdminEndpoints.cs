// [INTENT]: Aggregatore degli endpoint admin (autenticazione JWT). Un solo punto da chiamare in Program.cs
// (MapAdminEndpoints). Elenca esplicitamente i gruppi di rotte admin esistenti.

namespace WebAgency_BookingSystem.Api.Endpoints.Admin;

/// <summary>
/// Registrazione centralizzata degli endpoint admin.
/// </summary>
internal static class AdminEndpoints
{
    /// <summary>Mappa tutti gli endpoint admin: auth, servizi, staff, orari, chiusure, prenotazioni.</summary>
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapAdminAuthEndpoints();
        app.MapAdminServiceEndpoints();
        app.MapAdminScheduleEndpoints();
        app.MapAdminStaffEndpoints();
        // Prenotazioni admin (6D) aggiunte nel blocco dedicato.
        return app;
    }
}
