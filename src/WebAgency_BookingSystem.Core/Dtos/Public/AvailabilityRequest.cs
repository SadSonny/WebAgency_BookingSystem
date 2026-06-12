// [INTENT]: Parametri della query di disponibilità (GET /api/v1/availability), già parsati in tipi forti.
// Le date sono DateOnly locali del tenant. StaffId è opzionale: se null la disponibilità è aggregata sui
// parallelSlots del servizio (AD-04).

namespace WebAgency_BookingSystem.Core.Dtos.Public;

/// <summary>
/// Richiesta di disponibilità per un servizio in un intervallo di date (max 31 giorni).
/// </summary>
public sealed record AvailabilityRequest(
    Guid ServiceId,
    Guid? StaffId,
    DateOnly DateFrom,
    DateOnly DateTo);
