// [INTENT]: Logica platform di gestione tenant: crea (delega a ITenantProvisioningService), elenca e dettaglio
// cross-tenant (IgnoreQueryFilters). La creazione riutilizza l'unica fonte di verità del provisioning.

using Microsoft.EntityFrameworkCore;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Platform;
using WebAgency_BookingSystem.Core.Dtos.Provisioning;
using WebAgency_BookingSystem.Core.Provisioning;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Services.Platform;

internal sealed class PlatformTenantService : IPlatformTenantService
{
    private readonly BookingSystemDbContext _db;
    private readonly ITenantProvisioningService _provisioning;

    public PlatformTenantService(BookingSystemDbContext db, ITenantProvisioningService provisioning)
    {
        _db = db;
        _provisioning = provisioning;
    }

    /// <inheritdoc/>
    public Task<Result<ProvisioningOutput>> CreateAsync(ProvisioningInput input, CancellationToken ct = default) =>
        _provisioning.CreateAsync(input, ct);

    /// <inheritdoc/>
    public async Task<PagedResponse<PlatformTenantSummary>> ListAsync(int page, int pageSize, CancellationToken ct = default)
    {
        // WHY: clamp lato servizio per evitare pagine vuote (page < 1) o dataset enormi (pageSize > 200).
        int p = page < 1 ? 1 : page;
        int size = pageSize is < 1 or > 200 ? 50 : pageSize;

        // WHY: IgnoreQueryFilters è necessario perché Tenant non ha un global query filter (è la tabella
        // radice di risoluzione tenant), ma lo includiamo per coerenza e per proteggere da futuri filtri.
        IQueryable<Core.Entities.Tenant> q = _db.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .OrderByDescending(t => t.CreatedAt);

        int total = await q.CountAsync(ct);
        List<PlatformTenantSummary> items = await q
            .Skip((p - 1) * size)
            .Take(size)
            .Select(t => new PlatformTenantSummary(
                t.Id, t.Slug, t.Name, t.SiteUrl, t.OwnerEmail, t.Active,
                t.CreatedAt.ToString("o")))
            .ToListAsync(ct);

        return new PagedResponse<PlatformTenantSummary>(items, p, size, total);
    }

    /// <inheritdoc/>
    public async Task<Result<PlatformTenantSummary>> GetAsync(Guid id, CancellationToken ct = default)
    {
        PlatformTenantSummary? summary = await _db.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(t => t.Id == id)
            .Select(t => new PlatformTenantSummary(
                t.Id, t.Slug, t.Name, t.SiteUrl, t.OwnerEmail, t.Active,
                t.CreatedAt.ToString("o")))
            .FirstOrDefaultAsync(ct);

        return summary is null
            ? Error.NotFound("not_found", "Tenant non trovato.")
            : Result.Success(summary);
    }
}
