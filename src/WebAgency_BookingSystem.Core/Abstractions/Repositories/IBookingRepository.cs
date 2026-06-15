// [INTENT]: Accesso alle prenotazioni del tenant corrente. Espone le query di sovrapposizione usate
// dall'algoritmo di disponibilità (per servizio e per staff) e il lookup per id+token usato dagli endpoint
// pubblici di consultazione/disdetta. La creazione atomica con advisory lock è gestita dal BookingService.

using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Core.Abstractions.Repositories;

/// <summary>
/// Repository delle prenotazioni.
/// </summary>
public interface IBookingRepository
{
    /// <summary>
    /// Prenotazioni confermate di un servizio nell'intervallo di date (estremi inclusi). Usato per
    /// contare le sovrapposizioni nella disponibilità aggregata (senza staff).
    /// </summary>
    Task<IReadOnlyList<Booking>> GetConfirmedByServiceInRangeAsync(
        Guid serviceId, DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct = default);

    /// <summary>
    /// Prenotazioni confermate di uno staff nell'intervallo di date (estremi inclusi), per qualsiasi
    /// servizio. Usato per la disponibilità per-staff (uno staff non può essere in due posti).
    /// </summary>
    Task<IReadOnlyList<Booking>> GetConfirmedByStaffInRangeAsync(
        Guid staffId, DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct = default);

    /// <summary>(P1/P2) Prenotazioni confermate di PIÙ staff nell'intervallo, in un'unica query.</summary>
    Task<IReadOnlyList<Booking>> GetConfirmedByStaffIdsInRangeAsync(
        IReadOnlyCollection<Guid> staffIds, DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct = default);

    /// <summary>
    /// Restituisce la prenotazione che combacia con id + cancellation token, oppure null. La verifica
    /// del token avviene a livello query per non rivelare l'esistenza dell'id con token errato.
    /// </summary>
    Task<Booking?> GetByIdAndTokenAsync(Guid bookingId, Guid token, CancellationToken ct = default);

    /// <summary>Accoda una nuova prenotazione al contesto (la persistenza avviene col commit della transazione).</summary>
    Task AddAsync(Booking booking, CancellationToken ct = default);
}
