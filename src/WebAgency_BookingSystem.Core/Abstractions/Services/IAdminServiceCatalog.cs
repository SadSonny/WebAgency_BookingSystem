// [INTENT]: Operazioni admin sui servizi del tenant corrente (6.5-6.8): elenco (inclusi inattivi), creazione,
// aggiornamento, eliminazione soft. Tenant-scoped tramite ITenantContext (popolato dal JWT). Le mutazioni
// invalidano la cache pubblica del tenant (R-22).

using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.Core.Abstractions.Services;

/// <summary>
/// Gestione amministrativa del catalogo servizi.
/// </summary>
public interface IAdminServiceCatalog
{
    /// <summary>Elenca i servizi del tenant (inclusi gli inattivi, esclusi i soft-deleted), ordinati.</summary>
    Task<Result<IReadOnlyList<ServiceAdminResponse>>> ListAsync(CancellationToken ct = default);

    /// <summary>Crea un nuovo servizio e restituisce la sua rappresentazione.</summary>
    Task<Result<ServiceAdminResponse>> CreateAsync(ServiceWriteRequest request, CancellationToken ct = default);

    /// <summary>Aggiorna (sostituzione completa) un servizio esistente. 404 se non trovato.</summary>
    Task<Result<ServiceAdminResponse>> UpdateAsync(Guid id, ServiceWriteRequest request, CancellationToken ct = default);

    /// <summary>Elimina (soft delete) un servizio. 404 se non trovato.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
}
