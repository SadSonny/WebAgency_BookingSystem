// [INTENT]: Contratto platform per la gestione tenant: creazione (delega al provisioning),
// elenco paginato e dettaglio cross-tenant (ignora i global query filter).

using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos;
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
}
