// [INTENT]: Endpoint configurazione tenant (GET /api/v1/tenant/config). Restituisce regole di prenotazione,
// orari settimanali (sempre 7 giorni) e chiusure straordinarie future, usate dal widget frontend per la
// validazione lato client. Il tenant è già risolto dal middleware. bufferMinutes è 0 (buffer per-servizio, AD-03).

using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Public;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;

namespace WebAgency_BookingSystem.Api.Endpoints;

internal static class TenantConfigEndpoints
{
    public static IEndpointRouteBuilder MapTenantConfigEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/tenant/config", async (HttpContext http, ITenantRepository tenants, CancellationToken ct) =>
        {
            Tenant tenant = http.CurrentTenant();
            IReadOnlyList<TenantBusinessHours> hours = await tenants.GetBusinessHoursAsync(tenant.Id, ct);
            IReadOnlyList<TenantSpecialClosure> closures = await tenants.GetActiveSpecialClosuresAsync(
                tenant.Id, DateOnly.FromDateTime(DateTime.UtcNow), ct);

            var response = new TenantConfigResponse(
                tenant.Id,
                tenant.Name,
                tenant.Timezone,
                tenant.StaffChoiceEnabled,
                tenant.MinAdvanceHours,
                tenant.MinCancellationHours,
                tenant.VisibleDaysAhead,
                BufferMinutes: 0,
                BuildWeeklyHours(hours),
                closures.Select(c => new SpecialClosureResponse(
                    c.DateFrom.ToString("yyyy-MM-dd"), c.DateTo.ToString("yyyy-MM-dd"), c.Reason)).ToList());

            return Results.Ok(response);
        })
        .WithName("GetTenantConfig")
        .WithSummary("Configurazione pubblica del tenant")
        .WithDescription("Regole di prenotazione, orari settimanali (7 giorni) e chiusure straordinarie future.")
        .WithTags("Sistema")
        .RequireRateLimiting(RateLimitingPolicies.PublicApi)
        .Produces<TenantConfigResponse>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status403Forbidden);

        return app;
    }

    // Sempre 7 elementi (0=Dom..6=Sab). I giorni assenti dal DB sono restituiti come chiusi.
    private static List<BusinessHoursResponse> BuildWeeklyHours(IReadOnlyList<TenantBusinessHours> hours)
    {
        Dictionary<DayOfWeekIndex, TenantBusinessHours> byDay = hours.ToDictionary(h => h.DayOfWeek);
        var week = new List<BusinessHoursResponse>(7);

        for (int day = 0; day <= 6; day++)
        {
            if (byDay.TryGetValue((DayOfWeekIndex)day, out TenantBusinessHours? h) && h.IsOpen)
            {
                week.Add(new BusinessHoursResponse(day, true,
                    Format(h.OpenTime), Format(h.CloseTime), Format(h.BreakStart), Format(h.BreakEnd)));
            }
            else
            {
                week.Add(new BusinessHoursResponse(day, false, null, null, null, null));
            }
        }

        return week;
    }

    private static string? Format(TimeOnly? time) => time?.ToString("HH:mm");
}
