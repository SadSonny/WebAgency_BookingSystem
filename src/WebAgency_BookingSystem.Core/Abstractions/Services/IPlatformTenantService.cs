// [INTENT]: Contratto platform per la gestione tenant: creazione (delega al provisioning),
// elenco paginato e dettaglio cross-tenant (ignora i global query filter), attivazione/disattivazione
// (con eviction cache API key), gestione API key cross-tenant, re-invio attivazione Owner.

using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Core.Dtos.Platform;
using WebAgency_BookingSystem.Core.Dtos.Provisioning;
using WebAgency_BookingSystem.Core.Provisioning;

namespace WebAgency_BookingSystem.Core.Abstractions.Services;

/// <summary>Operazioni platform di gestione dei tenant (visibilità cross-tenant).</summary>
public interface IPlatformTenantService
{
    /// <summary>
    /// Crea un nuovo tenant delegando a <see cref="ITenantProvisioningService"/>.
    /// Fallisce con Conflict se lo slug esiste già.
    /// </summary>
    Task<Result<ProvisioningOutput>> CreateAsync(ProvisioningInput input, CancellationToken ct = default);

    /// <summary>
    /// Restituisce la lista paginata di tutti i tenant (attivi e non), ordinati per data di creazione discendente.
    /// </summary>
    Task<PagedResponse<PlatformTenantSummary>> ListAsync(int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Restituisce il dettaglio di un singolo tenant tramite id. Fallisce con NotFound se non esiste.
    /// </summary>
    Task<Result<PlatformTenantSummary>> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Attiva o disattiva un tenant. Alla disattivazione evacua tutte le voci cache delle sue API key
    /// (<c>apikey:{hash}</c>) così l'effetto è immediato invece di attendere la TTL.
    /// Fallisce con NotFound se il tenant non esiste.
    /// </summary>
    Task<Result> SetActiveAsync(Guid tenantId, bool active, CancellationToken ct = default);

    /// <summary>
    /// Restituisce le API key di un tenant (senza il segreto). Fallisce con NotFound se il tenant non esiste.
    /// </summary>
    Task<Result<IReadOnlyList<ApiKeyResponse>>> ListApiKeysAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Crea una nuova API key per il tenant. La chiave in chiaro è restituita una sola volta.
    /// Fallisce con NotFound se il tenant non esiste.
    /// </summary>
    Task<Result<CreateApiKeyResponse>> CreateApiKeyAsync(Guid tenantId, string? description, CancellationToken ct = default);

    /// <summary>
    /// Revoca una API key del tenant e rimuove la voce cache corrispondente.
    /// Fallisce con NotFound se la chiave non esiste o non appartiene al tenant.
    /// </summary>
    Task<Result> RevokeApiKeyAsync(Guid tenantId, Guid keyId, CancellationToken ct = default);

    /// <summary>
    /// Invalida i token di attivazione attivi dell'Owner, ne genera uno nuovo e accoda l'email di attivazione.
    /// Fallisce con NotFound se il tenant o l'Owner non esistono.
    /// </summary>
    Task<Result> ResendOwnerActivationAsync(Guid tenantId, CancellationToken ct = default);
}
