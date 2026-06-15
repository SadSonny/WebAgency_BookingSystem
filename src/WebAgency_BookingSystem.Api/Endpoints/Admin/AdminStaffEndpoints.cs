// [INTENT]: Endpoint admin CRUD staff (6.9-6.12), protetti da JWT. Tenant dal JWT. Validazione FluentValidation
// sul corpo; la logica (servizi/orari/soft-delete + invalidazione cache) è nel manager admin.

using FluentValidation;
using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.Api.Endpoints.Admin;

internal static class AdminStaffEndpoints
{
    public static IEndpointRouteBuilder MapAdminStaffEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/admin/staff").WithTags("Admin").RequireAuthorization();

        group.MapGet("", async (IAdminStaffManager manager, CancellationToken ct) =>
        {
            var result = await manager.ListAsync(ct);
            return result.Match(list => Results.Ok(list));
        })
        .WithName("AdminListStaff")
        .WithSummary("Lista staff (admin)")
        .WithDescription("Elenca lo staff del tenant (inclusi gli inattivi), con servizi erogati e orari.")
        .Produces<IReadOnlyList<StaffAdminResponse>>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        group.MapPost("", async (
            StaffWriteRequest request, IValidator<StaffWriteRequest> validator,
            IAdminStaffManager manager, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return validation.ToValidationProblem();
            }

            var result = await manager.CreateAsync(request, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : result.Error.ToErrorResult();
        })
        .WithName("AdminCreateStaff")
        .WithSummary("Crea staff (admin)")
        .Produces<StaffAdminResponse>(StatusCodes.Status201Created)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/{id:guid}", async (
            Guid id, StaffWriteRequest request, IValidator<StaffWriteRequest> validator,
            IAdminStaffManager manager, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return validation.ToValidationProblem();
            }

            var result = await manager.UpdateAsync(id, request, ct);
            return result.Match(staff => Results.Ok(staff));
        })
        .WithName("AdminUpdateStaff")
        .WithSummary("Aggiorna staff (admin)")
        .Produces<StaffAdminResponse>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/{id:guid}", async (Guid id, IAdminStaffManager manager, CancellationToken ct) =>
        {
            var result = await manager.DeleteAsync(id, ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToErrorResult();
        })
        .WithName("AdminDeleteStaff")
        .WithSummary("Elimina staff (admin, soft delete)")
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        // ── Assenze operatore (T1.1) ────────────────────────────────────────────
        group.MapGet("/{id:guid}/time-off", async (Guid id, IAdminStaffManager manager, CancellationToken ct) =>
        {
            var result = await manager.ListTimeOffAsync(id, ct);
            return result.Match(list => Results.Ok(list));
        })
        .WithName("AdminListStaffTimeOff")
        .WithSummary("Assenze operatore (admin)")
        .WithDescription("Elenca le assenze (ferie, malattia, permessi) di un operatore.")
        .Produces<IReadOnlyList<StaffTimeOffResponse>>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/time-off", async (
            Guid id, StaffTimeOffRequest request, IAdminStaffManager manager, CancellationToken ct) =>
        {
            var result = await manager.AddTimeOffAsync(id, request, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : result.Error.ToErrorResult();
        })
        .WithName("AdminAddStaffTimeOff")
        .WithSummary("Aggiungi assenza operatore (admin)")
        .WithDescription("Crea un'assenza: giornata intera (orari null) o fascia oraria (startTime+endTime).")
        .Produces<StaffTimeOffResponse>(StatusCodes.Status201Created)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/{id:guid}/time-off/{timeOffId:guid}", async (
            Guid id, Guid timeOffId, IAdminStaffManager manager, CancellationToken ct) =>
        {
            var result = await manager.DeleteTimeOffAsync(id, timeOffId, ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToErrorResult();
        })
        .WithName("AdminDeleteStaffTimeOff")
        .WithSummary("Elimina assenza operatore (admin)")
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        return app;
    }
}
