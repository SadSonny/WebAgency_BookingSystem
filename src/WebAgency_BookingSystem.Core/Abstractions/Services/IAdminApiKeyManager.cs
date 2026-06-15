// [INTENT]: Gestione amministrativa delle API key del tenant corrente (S4): elenco, creazione (rotazione) e
// revoca. Permette di ruotare una chiave compromessa senza ricorrere alla CLI/DB. Tenant-scoped via ITenantContext.

using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.Core.Abstractions.Services;

/// <summary>
/// Gestione delle API key del tenant (lista, creazione, revoca).
/// </summary>
public interface IAdminApiKeyManager
{
    /// <summary>Elenca le API key del tenant (prefisso + metadati, mai il segreto), più recenti prima.</summary>
    Task<Result<IReadOnlyList<ApiKeyResponse>>> ListAsync(CancellationToken ct = default);

    /// <summary>Crea una nuova API key e restituisce il valore in chiaro UNA sola volta.</summary>
    Task<Result<CreateApiKeyResponse>> CreateAsync(CreateApiKeyRequest request, CancellationToken ct = default);

    /// <summary>Revoca (disattiva) una API key del tenant. 404 se non appartiene al tenant. Effettiva entro la TTL cache.</summary>
    Task<Result> RevokeAsync(Guid keyId, CancellationToken ct = default);
}
