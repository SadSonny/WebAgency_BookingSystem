// [INTENT]: Accesso ai servizi del tenant corrente (già filtrati per tenant_id dal DbContext) e alle
// associazioni staff-servizio necessarie per esporre gli staffIds nella lista servizi.

using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Core.Abstractions.Repositories;

/// <summary>
/// Repository dei servizi prenotabili.
/// </summary>
public interface IServiceRepository
{
    /// <summary>Restituisce i servizi attivi (non eliminati) ordinati per display_order, name.</summary>
    Task<IReadOnlyList<Service>> GetActiveAsync(CancellationToken ct = default);

    /// <summary>Restituisce un servizio attivo per Id, oppure null se inesistente/non attivo/eliminato.</summary>
    Task<Service?> GetActiveByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Mappa ciascun serviceId richiesto agli Id degli staff (attivi) che lo erogano.
    /// Evita query N+1 nella composizione della lista servizi.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetStaffIdsByServiceAsync(
        IReadOnlyCollection<Guid> serviceIds, CancellationToken ct = default);
}
