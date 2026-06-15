// [INTENT]: Implementazione admin della gestione API key (S4). Crea/elenca/revoca le chiavi del tenant corrente
// (dal JWT). Alla revoca rimuove anche la voce di cache della risoluzione (apikey:{hash}) così l'effetto è
// immediato invece di attendere la TTL. La chiave in chiaro esiste solo al momento della creazione.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Abstractions;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Security;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Services.Admin;

internal sealed class AdminApiKeyManager : IAdminApiKeyManager
{
    private readonly BookingSystemDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AdminApiKeyManager> _logger;

    public AdminApiKeyManager(
        BookingSystemDbContext db, ITenantContext tenantContext, IMemoryCache cache, ILogger<AdminApiKeyManager> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<ApiKeyResponse>>> ListAsync(CancellationToken ct = default)
    {
        Guid tenantId = _tenantContext.TenantId!.Value;
        List<TenantApiKey> keys = await _db.TenantApiKeys
            .AsNoTracking()
            .Where(k => k.TenantId == tenantId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);

        IReadOnlyList<ApiKeyResponse> response = keys
            .Select(k => new ApiKeyResponse(k.Id, k.KeyPrefix, k.Description, k.Active, k.CreatedAt.ToString("o")))
            .ToList();
        return Result.Success(response);
    }

    public async Task<Result<CreateApiKeyResponse>> CreateAsync(CreateApiKeyRequest request, CancellationToken ct = default)
    {
        Guid tenantId = _tenantContext.TenantId!.Value;
        GeneratedApiKey generated = ApiKeyGenerator.Generate();

        var entity = new TenantApiKey
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            KeyHash = generated.KeyHash,
            KeyPrefix = generated.KeyPrefix,
            Description = string.IsNullOrWhiteSpace(request.Description) ? "Chiave generata da Admin API" : request.Description,
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.TenantApiKeys.Add(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Admin: nuova API key {KeyId} (prefisso {Prefix}) per tenant {TenantId}", entity.Id, entity.KeyPrefix, tenantId);
        return Result.Success(new CreateApiKeyResponse(entity.Id, generated.ApiKey, generated.KeyPrefix));
    }

    public async Task<Result> RevokeAsync(Guid keyId, CancellationToken ct = default)
    {
        Guid tenantId = _tenantContext.TenantId!.Value;
        TenantApiKey? key = await _db.TenantApiKeys.FirstOrDefaultAsync(k => k.Id == keyId && k.TenantId == tenantId, ct);
        if (key is null)
        {
            return Error.NotFound("not_found", "API key non trovata.");
        }

        key.Active = false;
        await _db.SaveChangesAsync(ct);

        // Effetto immediato: rimuoviamo la voce di cache della risoluzione (vedi TenantRepository: "apikey:{hash}").
        _cache.Remove($"apikey:{key.KeyHash}");

        _logger.LogInformation("Admin: API key {KeyId} revocata per tenant {TenantId}", keyId, tenantId);
        return Result.Success();
    }
}
