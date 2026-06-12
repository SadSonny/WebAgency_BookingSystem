// [INTENT]: Endpoint admin per orari settimanali (6.13) e chiusure straordinarie (6.14), protetti da JWT.
// Entrambi sostituiscono in blocco l'intero set; validazione FluentValidation, logica nel manager admin.

using FluentValidation;
using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.Api.Endpoints.Admin;

internal static class AdminScheduleEndpoints
{
    public static IEndpointRouteBuilder MapAdminScheduleEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/admin").WithTags("Admin").RequireAuthorization();

        group.MapPut("/business-hours", async (
            SetBusinessHoursRequest request, IValidator<SetBusinessHoursRequest> validator,
            IAdminScheduleManager manager, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return validation.ToValidationProblem();
            }

            var result = await manager.SetBusinessHoursAsync(request, ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToErrorResult();
        })
        .WithName("AdminSetBusinessHours")
        .WithSummary("Imposta orari settimanali (admin)")
        .WithDescription("Sostituisce in blocco gli orari settimanali del tenant.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/closures", async (
            SetClosuresRequest request, IValidator<SetClosuresRequest> validator,
            IAdminScheduleManager manager, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return validation.ToValidationProblem();
            }

            var result = await manager.SetClosuresAsync(request, ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToErrorResult();
        })
        .WithName("AdminSetClosures")
        .WithSummary("Imposta chiusure straordinarie (admin)")
        .WithDescription("Sostituisce in blocco le chiusure straordinarie del tenant.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
