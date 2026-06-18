// [INTENT]: Endpoint admin CRUD servizi (6.5-6.8), protetti da JWT. Tenant risolto dal JWT (AdminContextMiddleware).
// Validazione FluentValidation sul corpo; la logica e l'invalidazione cache sono nel catalogo admin.

using FluentValidation;
using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Infrastructure.Auth;

namespace WebAgency_BookingSystem.Api.Endpoints.Admin;

internal static class AdminServiceEndpoints
{
    public static IEndpointRouteBuilder MapAdminServiceEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/admin/services").WithTags("Admin").RequireAuthorization(AdminClaims.AdminPolicy);

        group.MapGet("", async (IAdminServiceCatalog catalog, CancellationToken ct) =>
        {
            var result = await catalog.ListAsync(ct);
            return result.Match(list => Results.Ok(list));
        })
        .WithName("AdminListServices")
        .WithSummary("Lista servizi (admin)")
        .WithDescription("Elenca i servizi del tenant, inclusi gli inattivi (esclusi i soft-deleted).")
        .Produces<IReadOnlyList<ServiceAdminResponse>>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        group.MapPost("", async (
            ServiceWriteRequest request, IValidator<ServiceWriteRequest> validator,
            IAdminServiceCatalog catalog, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return validation.ToValidationProblem();
            }

            var result = await catalog.CreateAsync(request, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : result.Error.ToErrorResult();
        })
        .WithName("AdminCreateService")
        .WithSummary("Crea servizio (admin)")
        .Produces<ServiceAdminResponse>(StatusCodes.Status201Created)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/{id:guid}", async (
            Guid id, ServiceWriteRequest request, IValidator<ServiceWriteRequest> validator,
            IAdminServiceCatalog catalog, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return validation.ToValidationProblem();
            }

            var result = await catalog.UpdateAsync(id, request, ct);
            return result.Match(service => Results.Ok(service));
        })
        .WithName("AdminUpdateService")
        .WithSummary("Aggiorna servizio (admin)")
        .Produces<ServiceAdminResponse>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/{id:guid}", async (Guid id, IAdminServiceCatalog catalog, CancellationToken ct) =>
        {
            var result = await catalog.DeleteAsync(id, ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToErrorResult();
        })
        .WithName("AdminDeleteService")
        .WithSummary("Elimina servizio (admin, soft delete)")
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        return app;
    }
}
