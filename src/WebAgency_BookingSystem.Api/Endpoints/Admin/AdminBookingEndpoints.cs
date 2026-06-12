// [INTENT]: Endpoint admin prenotazioni (6.3 lista con filtri, 6.4 PATCH stato), protetti da JWT. I filtri
// sono query param opzionali; lo stato (filtro e aggiornamento) è validato contro i valori del contratto.

using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Core.Enums;

namespace WebAgency_BookingSystem.Api.Endpoints.Admin;

internal static class AdminBookingEndpoints
{
    public static IEndpointRouteBuilder MapAdminBookingEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/admin/bookings").WithTags("Admin").RequireAuthorization();

        group.MapGet("", async (
            DateOnly? dateFrom, DateOnly? dateTo, Guid? staffId, Guid? serviceId, string? status,
            IAdminBookingService bookings, CancellationToken ct) =>
        {
            BookingStatus? statusFilter = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!BookingStatusExtensions.TryParseApi(status, out BookingStatus parsed))
                {
                    return ResultMapping.BadRequest("Parametro status non valido (ammessi: confirmed, cancelled, no_show, completed).");
                }

                statusFilter = parsed;
            }

            var filter = new AdminBookingFilter(dateFrom, dateTo, staffId, serviceId, statusFilter);
            var result = await bookings.ListAsync(filter, ct);
            return result.Match(list => Results.Ok(list));
        })
        .WithName("AdminListBookings")
        .WithSummary("Lista prenotazioni (admin)")
        .WithDescription("Elenca le prenotazioni del tenant filtrabili per dateFrom/dateTo, staffId, serviceId, status.")
        .Produces<IReadOnlyList<AdminBookingResponse>>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        group.MapPatch("/{id:guid}", async (
            Guid id, UpdateBookingStatusRequest request, IAdminBookingService bookings, CancellationToken ct) =>
        {
            var result = await bookings.UpdateStatusAsync(id, request, ct);
            return result.Match(booking => Results.Ok(booking));
        })
        .WithName("AdminUpdateBookingStatus")
        .WithSummary("Aggiorna stato prenotazione (admin)")
        .WithDescription("Aggiorna lo stato di una prenotazione (es. no_show, completed, cancelled).")
        .Produces<AdminBookingResponse>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
