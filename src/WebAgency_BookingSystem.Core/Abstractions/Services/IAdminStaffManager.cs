// [INTENT]: Operazioni admin sullo staff del tenant corrente (6.9-6.12): elenco (inclusi inattivi), creazione,
// aggiornamento (con sostituzione di servizi e orari), eliminazione soft. Tenant-scoped via ITenantContext.
// Le mutazioni invalidano la cache pubblica del tenant (R-22). La validazione DB-dipendente (serviceId del
// tenant) è veicolata via Result.

using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.Core.Abstractions.Services;

/// <summary>
/// Gestione amministrativa dello staff.
/// </summary>
public interface IAdminStaffManager
{
    /// <summary>Elenca lo staff del tenant (inclusi gli inattivi, esclusi i soft-deleted), con servizi e orari.</summary>
    Task<Result<IReadOnlyList<StaffAdminResponse>>> ListAsync(CancellationToken ct = default);

    /// <summary>Crea un membro dello staff con le sue associazioni a servizi e orari.</summary>
    Task<Result<StaffAdminResponse>> CreateAsync(StaffWriteRequest request, CancellationToken ct = default);

    /// <summary>Aggiorna un membro dello staff (sostituendo servizi e orari). 404 se non trovato.</summary>
    Task<Result<StaffAdminResponse>> UpdateAsync(Guid id, StaffWriteRequest request, CancellationToken ct = default);

    /// <summary>Elimina (soft delete) un membro dello staff. 404 se non trovato.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
}
