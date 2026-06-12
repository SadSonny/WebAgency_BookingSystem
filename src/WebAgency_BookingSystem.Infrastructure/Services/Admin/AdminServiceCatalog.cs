// [INTENT]: Implementazione admin del catalogo servizi (6.5-6.8). Tenant-scoped: il global query filter del
// DbContext (tenant dal JWT + soft-delete) restringe automaticamente le query. Le mutazioni invalidano la
// cache pubblica del tenant (R-22), così la lista servizi pubblica non resta stantia.

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

internal sealed class AdminServiceCatalog : IAdminServiceCatalog
{
    private readonly BookingSystemDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantCache _cache;
    private readonly ILogger<AdminServiceCatalog> _logger;

    public AdminServiceCatalog(
        BookingSystemDbContext db, ITenantContext tenantContext, ITenantCache cache, ILogger<AdminServiceCatalog> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<ServiceAdminResponse>>> ListAsync(CancellationToken ct = default)
    {
        List<Service> entities = await _db.Services
            .AsNoTracking()
            .OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name)
            .ToListAsync(ct);

        IReadOnlyList<ServiceAdminResponse> services = entities.Select(Map).ToList();
        return Result.Success(services);
    }

    public async Task<Result<ServiceAdminResponse>> CreateAsync(ServiceWriteRequest request, CancellationToken ct = default)
    {
        Guid tenantId = _tenantContext.TenantId!.Value;
        var service = new Service { Id = Guid.NewGuid(), TenantId = tenantId };
        Apply(service, request);

        _db.Services.Add(service);
        await _db.SaveChangesAsync(ct);
        _cache.Invalidate(tenantId);

        _logger.LogInformation("Admin: servizio creato {ServiceId}", service.Id);
        return Result.Success(Map(service));
    }

    public async Task<Result<ServiceAdminResponse>> UpdateAsync(Guid id, ServiceWriteRequest request, CancellationToken ct = default)
    {
        Service? service = await _db.Services.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (service is null)
        {
            return Error.NotFound("not_found", "Servizio non trovato.");
        }

        Apply(service, request);
        await _db.SaveChangesAsync(ct);
        _cache.Invalidate(_tenantContext.TenantId!.Value);

        _logger.LogInformation("Admin: servizio aggiornato {ServiceId}", service.Id);
        return Result.Success(Map(service));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        Service? service = await _db.Services.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (service is null)
        {
            return Error.NotFound("not_found", "Servizio non trovato.");
        }

        service.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        _cache.Invalidate(_tenantContext.TenantId!.Value);

        _logger.LogInformation("Admin: servizio eliminato (soft) {ServiceId}", service.Id);
        return Result.Success();
    }

    private static void Apply(Service service, ServiceWriteRequest request)
    {
        service.Name = request.Name;
        service.Category = request.Category;
        service.Description = request.Description;
        service.DurationMinutes = request.DurationMinutes;
        service.BasePrice = request.BasePrice;
        service.ParallelSlots = request.ParallelSlots ?? 1;
        service.BufferEnabled = request.BufferEnabled ?? false;
        service.BufferMinutes = request.BufferMinutes ?? 0;
        service.BufferPosition = Enum.TryParse(request.BufferPosition, out BufferPosition bp) ? bp : BufferPosition.After;
        service.Active = request.Active ?? true;
        service.DisplayOrder = request.DisplayOrder ?? 0;
    }

    private static ServiceAdminResponse Map(Service s) => new(
        s.Id, s.Name, s.Category, s.Description, s.DurationMinutes, s.BasePrice, s.ParallelSlots,
        s.BufferEnabled, s.BufferMinutes, s.BufferPosition.ToString(), s.Active, s.DisplayOrder);
}
