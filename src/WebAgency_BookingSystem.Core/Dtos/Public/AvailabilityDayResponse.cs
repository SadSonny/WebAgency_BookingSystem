// [INTENT]: Disponibilità di un singolo giorno (elemento della response di GET /api/v1/availability).
// I giorni completamente chiusi non sono inclusi; i giorni aperti sono inclusi anche se tutti gli slot
// risultano non disponibili. Orari come stringhe locali del tenant.

namespace WebAgency_BookingSystem.Core.Dtos.Public;

/// <summary>
/// Slot disponibili (e non) per una data.
/// </summary>
/// <param name="Date">Data in formato <c>yyyy-MM-dd</c> locale del tenant.</param>
public sealed record AvailabilityDayResponse(
    string Date,
    IReadOnlyList<SlotResponse> Slots);

/// <summary>
/// Singolo slot orario.
/// </summary>
/// <param name="Time">Ora di inizio in formato <c>HH:mm</c> locale del tenant.</param>
/// <param name="StaffId">Staff dello slot; null se disponibilità aggregata (nessuno staff in input).</param>
/// <param name="Available">True se prenotabile.</param>
public sealed record SlotResponse(
    string Time,
    Guid? StaffId,
    bool Available);
