// [INTENT]: Accesso allo staff del tenant corrente, ai suoi orari e alla verifica che eroghi un servizio.
// Tutte le query sono tenant-scoped tramite il global query filter del DbContext.

using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Core.Abstractions.Repositories;

/// <summary>
/// Repository dei membri dello staff.
/// </summary>
public interface IStaffRepository
{
    /// <summary>Restituisce lo staff attivo ordinato per display_order, name.</summary>
    Task<IReadOnlyList<Staff>> GetActiveAsync(CancellationToken ct = default);

    /// <summary>Restituisce lo staff attivo che eroga il servizio indicato.</summary>
    Task<IReadOnlyList<Staff>> GetActiveByServiceAsync(Guid serviceId, CancellationToken ct = default);

    /// <summary>Restituisce un membro dello staff attivo per Id, oppure null.</summary>
    Task<Staff?> GetActiveByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>True se lo staff indicato è abilitato a erogare il servizio indicato.</summary>
    Task<bool> ExecutesServiceAsync(Guid staffId, Guid serviceId, CancellationToken ct = default);

    /// <summary>
    /// Restituisce gli orari settimanali dello staff. Lista vuota se lo staff non ha orari propri
    /// (in tal caso la disponibilità usa gli orari del tenant).
    /// </summary>
    Task<IReadOnlyList<StaffBusinessHours>> GetBusinessHoursAsync(Guid staffId, CancellationToken ct = default);

    /// <summary>
    /// Restituisce le assenze dell'operatore (T1.1) che intersecano il range [fromInclusive, toInclusive].
    /// Usato dalla disponibilità per escludere giorni interi e fasce orarie indisponibili.
    /// </summary>
    Task<IReadOnlyList<StaffTimeOff>> GetTimeOffInRangeAsync(
        Guid staffId, DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct = default);

    /// <summary>(P1/P2) Orari settimanali di PIÙ operatori in un'unica query.</summary>
    Task<IReadOnlyList<StaffBusinessHours>> GetBusinessHoursForStaffAsync(
        IReadOnlyCollection<Guid> staffIds, CancellationToken ct = default);

    /// <summary>(P1/P2) Assenze di PIÙ operatori che intersecano il range, in un'unica query.</summary>
    Task<IReadOnlyList<StaffTimeOff>> GetTimeOffForStaffInRangeAsync(
        IReadOnlyCollection<Guid> staffIds, DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct = default);

    /// <summary>(P2) Sottoinsieme di <paramref name="staffIds"/> che esegue TUTTI i <paramref name="serviceIds"/>, in un'unica query.</summary>
    Task<IReadOnlySet<Guid>> GetStaffExecutingAllAsync(
        IReadOnlyCollection<Guid> staffIds, IReadOnlyCollection<Guid> serviceIds, CancellationToken ct = default);
}
