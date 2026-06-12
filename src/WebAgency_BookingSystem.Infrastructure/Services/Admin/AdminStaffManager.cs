// [INTENT]: Implementazione admin dello staff (6.9-6.12). Gestisce lo staff e, in modo coordinato, le sue
// associazioni ai servizi (staff_services) e i suoi orari (staff_business_hours), sostituendoli in blocco su
// update. Valida che i serviceId appartengano a servizi attivi del tenant. Mutazioni atomiche (un SaveChanges)
// con invalidazione della cache pubblica (R-22).

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Abstractions;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Infrastructure.Persistence;
using WebAgency_BookingSystem.Infrastructure.Persistence.Caching;

namespace WebAgency_BookingSystem.Infrastructure.Services.Admin;

internal sealed class AdminStaffManager : IAdminStaffManager
{
    private readonly BookingSystemDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantCache _cache;
    private readonly ILogger<AdminStaffManager> _logger;

    public AdminStaffManager(
        BookingSystemDbContext db, ITenantContext tenantContext, ITenantCache cache, ILogger<AdminStaffManager> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<StaffAdminResponse>>> ListAsync(CancellationToken ct = default)
    {
        List<Staff> staff = await _db.Staff
            .AsNoTracking()
            .OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name)
            .ToListAsync(ct);

        List<StaffService> links = await _db.StaffServices.AsNoTracking().ToListAsync(ct);
        List<StaffBusinessHours> hours = await _db.StaffBusinessHours.AsNoTracking().ToListAsync(ct);

        ILookup<Guid, StaffService> linksByStaff = links.ToLookup(l => l.StaffId);
        ILookup<Guid, StaffBusinessHours> hoursByStaff = hours.ToLookup(h => h.StaffId);

        IReadOnlyList<StaffAdminResponse> response = staff.Select(s => Map(
            s,
            linksByStaff[s.Id].Select(l => new StaffServiceAssignment(l.ServiceId, l.PriceOverride)).ToList(),
            hoursByStaff[s.Id].Select(MapHours).ToList())).ToList();

        return Result.Success(response);
    }

    public async Task<Result<StaffAdminResponse>> CreateAsync(StaffWriteRequest request, CancellationToken ct = default)
    {
        Result validation = await ValidateServiceIdsAsync(request, ct);
        if (validation.IsFailure)
        {
            return Result.Failure<StaffAdminResponse>(validation.Error);
        }

        Guid tenantId = _tenantContext.TenantId!.Value;
        var staff = new Staff { Id = Guid.NewGuid(), TenantId = tenantId };
        ApplyCore(staff, request);
        _db.Staff.Add(staff);

        AddAssociations(tenantId, staff.Id, request);

        await _db.SaveChangesAsync(ct);
        _cache.Invalidate(tenantId);

        _logger.LogInformation("Admin: staff creato {StaffId}", staff.Id);
        return Result.Success(MapFromRequest(staff, request));
    }

    public async Task<Result<StaffAdminResponse>> UpdateAsync(Guid id, StaffWriteRequest request, CancellationToken ct = default)
    {
        Staff? staff = await _db.Staff.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (staff is null)
        {
            return Error.NotFound("not_found", "Staff non trovato.");
        }

        Result validation = await ValidateServiceIdsAsync(request, ct);
        if (validation.IsFailure)
        {
            return Result.Failure<StaffAdminResponse>(validation.Error);
        }

        Guid tenantId = _tenantContext.TenantId!.Value;
        ApplyCore(staff, request);

        // Sostituzione in blocco di servizi e orari dello staff.
        List<StaffService> existingLinks = await _db.StaffServices.Where(l => l.StaffId == id).ToListAsync(ct);
        List<StaffBusinessHours> existingHours = await _db.StaffBusinessHours.Where(h => h.StaffId == id).ToListAsync(ct);
        _db.StaffServices.RemoveRange(existingLinks);
        _db.StaffBusinessHours.RemoveRange(existingHours);
        AddAssociations(tenantId, id, request);

        await _db.SaveChangesAsync(ct);
        _cache.Invalidate(tenantId);

        _logger.LogInformation("Admin: staff aggiornato {StaffId}", id);
        return Result.Success(MapFromRequest(staff, request));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        Staff? staff = await _db.Staff.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (staff is null)
        {
            return Error.NotFound("not_found", "Staff non trovato.");
        }

        staff.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        _cache.Invalidate(_tenantContext.TenantId!.Value);

        _logger.LogInformation("Admin: staff eliminato (soft) {StaffId}", id);
        return Result.Success();
    }

    private async Task<Result> ValidateServiceIdsAsync(StaffWriteRequest request, CancellationToken ct)
    {
        List<Guid> requested = request.Services?.Select(s => s.ServiceId).Distinct().ToList() ?? [];
        if (requested.Count == 0)
        {
            return Result.Success();
        }

        List<Guid> existing = await _db.Services
            .Where(s => requested.Contains(s.Id) && s.Active)
            .Select(s => s.Id)
            .ToListAsync(ct);

        List<Guid> missing = requested.Except(existing).ToList();
        return missing.Count == 0
            ? Result.Success()
            : Error.Validation("validation_error", $"Servizi non validi o non attivi: {string.Join(", ", missing)}.");
    }

    private void AddAssociations(Guid tenantId, Guid staffId, StaffWriteRequest request)
    {
        foreach (StaffServiceAssignment assignment in request.Services ?? [])
        {
            _db.StaffServices.Add(new StaffService
            {
                Id = Guid.NewGuid(),
                StaffId = staffId,
                ServiceId = assignment.ServiceId,
                TenantId = tenantId,
                PriceOverride = assignment.PriceOverride,
            });
        }

        foreach (StaffBusinessHoursItem h in request.BusinessHours ?? [])
        {
            _db.StaffBusinessHours.Add(new StaffBusinessHours
            {
                Id = Guid.NewGuid(),
                StaffId = staffId,
                TenantId = tenantId,
                DayOfWeek = (DayOfWeekIndex)h.DayOfWeek,
                IsAvailable = h.IsAvailable,
                StartTime = ToTime(h.StartTime),
                EndTime = ToTime(h.EndTime),
                BreakStart = ToTime(h.BreakStart),
                BreakEnd = ToTime(h.BreakEnd),
            });
        }
    }

    private static void ApplyCore(Staff staff, StaffWriteRequest request)
    {
        staff.Name = request.Name;
        staff.Role = request.Role;
        staff.Specialization = request.Specialization;
        staff.PhotoUrl = request.PhotoUrl;
        staff.Active = request.Active ?? true;
        staff.DisplayOrder = request.DisplayOrder ?? 0;
    }

    private static StaffAdminResponse MapFromRequest(Staff s, StaffWriteRequest request) => Map(
        s,
        request.Services ?? [],
        request.BusinessHours ?? []);

    private static StaffAdminResponse Map(
        Staff s, IReadOnlyList<StaffServiceAssignment> services, IReadOnlyList<StaffBusinessHoursItem> hours) =>
        new(s.Id, s.Name, s.Role, s.Specialization, s.PhotoUrl, s.Active, s.DisplayOrder, services, hours);

    private static StaffBusinessHoursItem MapHours(StaffBusinessHours h) => new(
        (int)h.DayOfWeek, h.IsAvailable,
        h.StartTime?.ToString("HH:mm"), h.EndTime?.ToString("HH:mm"),
        h.BreakStart?.ToString("HH:mm"), h.BreakEnd?.ToString("HH:mm"));

    private static TimeOnly? ToTime(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);
}
