// [INTENT]: Logica platform di gestione tenant: crea (delega a ITenantProvisioningService), elenca e dettaglio
// cross-tenant (IgnoreQueryFilters), attiva/disattiva tenant (con eviction cache API key), gestione API key
// cross-tenant, re-invio attivazione Owner (rigenerazione token + accoda email).

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Core.Dtos.Platform;
using WebAgency_BookingSystem.Core.Dtos.Provisioning;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Core.Provisioning;
using WebAgency_BookingSystem.Core.Security;
using WebAgency_BookingSystem.Infrastructure.Auth;
using WebAgency_BookingSystem.Infrastructure.Email;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Services.Platform;

internal sealed class PlatformTenantService : IPlatformTenantService
{
    private readonly BookingSystemDbContext _db;
    private readonly ITenantProvisioningService _provisioning;
    private readonly IMemoryCache _cache;
    private readonly IEmailOutbox _outbox;
    private readonly AccountSettings _account;

    public PlatformTenantService(
        BookingSystemDbContext db,
        ITenantProvisioningService provisioning,
        IMemoryCache cache,
        IEmailOutbox outbox,
        AccountSettings account)
    {
        _db = db;
        _provisioning = provisioning;
        _cache = cache;
        _outbox = outbox;
        _account = account;
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

    /// <inheritdoc/>
    public async Task<Result> SetActiveAsync(Guid tenantId, bool active, CancellationToken ct = default)
    {
        Core.Entities.Tenant? tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return Error.NotFound("not_found", "Tenant non trovato.");

        tenant.Active = active;
        await _db.SaveChangesAsync(ct);

        if (!active)
        {
            // WHY: la risoluzione tenant per API key è cachata (apikey:{hash}); senza evacuazione il tenant
            // disattivato resterebbe risolvibile fino alla TTL. Rimuoviamo le voci di tutte le sue chiavi.
            List<string> hashes = await _db.TenantApiKeys.AsNoTracking().IgnoreQueryFilters()
                .Where(k => k.TenantId == tenantId).Select(k => k.KeyHash).ToListAsync(ct);
            foreach (string h in hashes) _cache.Remove($"apikey:{h}");
        }

        return Result.Success();
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<ApiKeyResponse>>> ListApiKeysAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (!await _db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId, ct))
            return Error.NotFound("not_found", "Tenant non trovato.");

        List<ApiKeyResponse> keys = await _db.TenantApiKeys.AsNoTracking().IgnoreQueryFilters()
            .Where(k => k.TenantId == tenantId)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeyResponse(k.Id, k.KeyPrefix, k.Description, k.Active, k.CreatedAt.ToString("o")))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<ApiKeyResponse>>(keys);
    }

    /// <inheritdoc/>
    public async Task<Result<CreateApiKeyResponse>> CreateApiKeyAsync(Guid tenantId, string? description, CancellationToken ct = default)
    {
        if (!await _db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId, ct))
            return Error.NotFound("not_found", "Tenant non trovato.");

        GeneratedApiKey gen = ApiKeyGenerator.Generate();
        var entity = new Core.Entities.TenantApiKey
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            KeyHash = gen.KeyHash,
            KeyPrefix = gen.KeyPrefix,
            Description = string.IsNullOrWhiteSpace(description) ? "Chiave generata da Platform API" : description,
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.TenantApiKeys.Add(entity);
        await _db.SaveChangesAsync(ct);

        return Result.Success(new CreateApiKeyResponse(entity.Id, gen.ApiKey, gen.KeyPrefix));
    }

    /// <inheritdoc/>
    public async Task<Result> RevokeApiKeyAsync(Guid tenantId, Guid keyId, CancellationToken ct = default)
    {
        Core.Entities.TenantApiKey? key = await _db.TenantApiKeys.IgnoreQueryFilters()
            .FirstOrDefaultAsync(k => k.Id == keyId && k.TenantId == tenantId, ct);
        if (key is null) return Error.NotFound("not_found", "API key non trovata.");

        key.Active = false;
        await _db.SaveChangesAsync(ct);
        // WHY: evacuiamo la cache DOPO il commit; se il save fallisce la chiave resta attiva e la cache coerente.
        // Stesso pattern di AdminApiKeyManager.RevokeAsync (chiave "apikey:{hash}").
        _cache.Remove($"apikey:{key.KeyHash}");

        return Result.Success();
    }

    /// <inheritdoc/>
    public async Task<Result> ResendOwnerActivationAsync(Guid tenantId, CancellationToken ct = default)
    {
        Core.Entities.Tenant? tenant = await _db.Tenants.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return Error.NotFound("not_found", "Tenant non trovato.");

        Core.Entities.User? owner = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Role == UserRole.Owner, ct);
        if (owner is null) return Error.NotFound("not_found", "Owner non trovato.");

        GeneratedSecurityToken gen = SecurityTokenGenerator.Generate();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Invalida i token di attivazione attivi precedenti dell'Owner per evitare link orfani validi.
        List<Core.Entities.UserSecurityToken> previous = await _db.UserSecurityTokens.IgnoreQueryFilters()
            .Where(t => t.UserId == owner.Id && t.Purpose == SecurityTokenPurpose.Activation && t.UsedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);
        foreach (Core.Entities.UserSecurityToken t in previous) t.UsedAt = now;

        _db.UserSecurityTokens.Add(new Core.Entities.UserSecurityToken
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = owner.Id,
            TokenHash = gen.TokenHash,
            Purpose = SecurityTokenPurpose.Activation,
            ExpiresAt = now.AddHours(_account.ActivationTokenHours),
            CreatedAt = now,
        });

        string url = $"{_account.PublicBaseUrl}/api/v1/admin/account/activate?token={gen.Token}";
        _outbox.EnqueueAccountActivation(tenantId, tenant.Name, owner.Email, url);

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
