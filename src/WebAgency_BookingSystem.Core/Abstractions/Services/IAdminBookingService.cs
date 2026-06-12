// [INTENT]: Operazioni admin sulle prenotazioni del tenant corrente: elenco filtrato (6.3) e aggiornamento
// di stato, es. no-show (6.4). Tenant-scoped via ITenantContext (dal JWT).

using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.Core.Abstractions.Services;

/// <summary>
/// Gestione amministrativa delle prenotazioni.
/// </summary>
public interface IAdminBookingService
{
    /// <summary>Elenca le prenotazioni del tenant filtrate per data/staff/servizio/stato, ordinate per data e ora.</summary>
    Task<Result<IReadOnlyList<AdminBookingResponse>>> ListAsync(AdminBookingFilter filter, CancellationToken ct = default);

    /// <summary>Aggiorna lo stato di una prenotazione (registra l'audit). 404 se non trovata, 422 se stato non valido.</summary>
    Task<Result<AdminBookingResponse>> UpdateStatusAsync(Guid id, UpdateBookingStatusRequest request, CancellationToken ct = default);
}
