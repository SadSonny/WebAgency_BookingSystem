// [INTENT]: Implementazione admin di orari (6.13) e chiusure (6.14): sostituzione in blocco (rimuove gli
// esistenti e ricrea dal request) in un'unica transazione (SaveChanges). L'input è già validato dai validator
// degli endpoint, quindi qui si parsa con ParseExact. Le modifiche agli orari invalidano la cache pubblica (R-22).

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

internal sealed class AdminScheduleManager : IAdminScheduleManager
{
    private readonly BookingSystemDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantCache _cache;
    private readonly ILogger<AdminScheduleManager> _logger;

    public AdminScheduleManager(
        BookingSystemDbContext db, ITenantContext tenantContext, ITenantCache cache, ILogger<AdminScheduleManager> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result> SetBusinessHoursAsync(SetBusinessHoursRequest request, CancellationToken ct = default)
    {
        Guid tenantId = _tenantContext.TenantId!.Value;

        List<TenantBusinessHours> existing = await _db.TenantBusinessHours.Where(h => h.TenantId == tenantId).ToListAsync(ct);
        _db.TenantBusinessHours.RemoveRange(existing);

        foreach (BusinessHoursItem day in request.Days)
        {
            _db.TenantBusinessHours.Add(new TenantBusinessHours
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DayOfWeek = (DayOfWeekIndex)day.DayOfWeek,
                IsOpen = day.IsOpen,
                OpenTime = ToTime(day.OpenTime),
                CloseTime = ToTime(day.CloseTime),
                BreakStart = ToTime(day.BreakStart),
                BreakEnd = ToTime(day.BreakEnd),
            });
        }

        await _db.SaveChangesAsync(ct);
        _cache.Invalidate(tenantId);

        _logger.LogInformation("Admin: orari settimanali aggiornati per tenant {TenantId}", tenantId);
        return Result.Success();
    }

    public async Task<Result> SetClosuresAsync(SetClosuresRequest request, CancellationToken ct = default)
    {
        Guid tenantId = _tenantContext.TenantId!.Value;

        List<TenantSpecialClosure> existing = await _db.TenantSpecialClosures.Where(c => c.TenantId == tenantId).ToListAsync(ct);
        _db.TenantSpecialClosures.RemoveRange(existing);

        foreach (ClosureItem closure in request.Closures)
        {
            _db.TenantSpecialClosures.Add(new TenantSpecialClosure
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DateFrom = ToDate(closure.DateFrom),
                DateTo = ToDate(closure.DateTo),
                Reason = closure.Reason,
            });
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Admin: chiusure straordinarie aggiornate per tenant {TenantId}", tenantId);
        return Result.Success();
    }

    private static TimeOnly? ToTime(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);

    private static DateOnly ToDate(string value) =>
        DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
