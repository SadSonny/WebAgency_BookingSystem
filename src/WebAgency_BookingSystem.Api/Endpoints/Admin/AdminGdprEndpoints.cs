// [INTENT]: Endpoint admin DSAR (GDPR 4.3), protetti da JWT. Export (diritto d'accesso) ed erase (diritto
// all'oblio) dei dati di un cliente identificato per email. La logica è in IGdprDsarService; qui solo routing,
// validazione della richiesta di erase e mappatura del Result.

using FluentValidation;
using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Infrastructure.Auth;

namespace WebAgency_BookingSystem.Api.Endpoints.Admin;

internal static class AdminGdprEndpoints
{
    public static IEndpointRouteBuilder MapAdminGdprEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/admin/gdpr").WithTags("Admin").RequireAuthorization(AdminClaims.AdminPolicy);

        group.MapGet("/customer", async (string email, IGdprDsarService dsar, CancellationToken ct) =>
        {
            var result = await dsar.ExportAsync(email, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToErrorResult();
        })
        .WithName("AdminGdprExportCustomer")
        .WithSummary("Esporta i dati di un cliente (GDPR)")
        .WithDescription("Diritto d'accesso: restituisce tutte le prenotazioni del cliente con l'email indicata. Riesce sempre (lista vuota se non ci sono dati). L'accesso è tracciato in audit_log.")
        .Produces<CustomerDataExport>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        group.MapPost("/customer/erase", async (EraseCustomerRequest request, IValidator<EraseCustomerRequest> validator, IGdprDsarService dsar, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return validation.ToValidationProblem();
            }

            var result = await dsar.EraseAsync(request.Email, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToErrorResult();
        })
        .WithName("AdminGdprEraseCustomer")
        .WithSummary("Cancella i dati di un cliente (GDPR)")
        .WithDescription("Diritto all'oblio: anonimizza le prenotazioni ed elimina le email outbox del cliente con l'email indicata. 404 se non c'è nulla da cancellare. L'azione è tracciata in audit_log.")
        .Produces<ErasureResult>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
