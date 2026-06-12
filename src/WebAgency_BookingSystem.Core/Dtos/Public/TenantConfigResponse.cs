// [INTENT]: Configurazione pubblica del tenant (GET /api/v1/tenant/config) usata dal widget di prenotazione
// del frontend per validare lato client. Gli orari sono stringhe locali del tenant (mai UTC).

namespace WebAgency_BookingSystem.Core.Dtos.Public;

/// <summary>
/// Configurazione e regole di prenotazione del tenant, più orari settimanali e chiusure straordinarie.
/// </summary>
/// <param name="BufferMinutes">
/// Sempre 0: il buffer è configurato per-servizio (AD-03). Campo mantenuto per compatibilità di contratto.
/// </param>
public sealed record TenantConfigResponse(
    Guid TenantId,
    string Name,
    string Timezone,
    bool StaffChoiceEnabled,
    int MinAdvanceHours,
    int MinCancellationHours,
    int VisibleDaysAhead,
    int BufferMinutes,
    IReadOnlyList<BusinessHoursResponse> BusinessHours,
    IReadOnlyList<SpecialClosureResponse> SpecialClosures);

/// <summary>
/// Orario di apertura del tenant per un giorno della settimana. Gli orari sono <c>HH:mm</c> locali o null.
/// </summary>
/// <param name="DayOfWeek">0=Domenica .. 6=Sabato.</param>
public sealed record BusinessHoursResponse(
    int DayOfWeek,
    bool IsOpen,
    string? OpenTime,
    string? CloseTime,
    string? BreakStart,
    string? BreakEnd);

/// <summary>
/// Chiusura straordinaria. Le date sono <c>yyyy-MM-dd</c> locali del tenant.
/// </summary>
public sealed record SpecialClosureResponse(
    string DateFrom,
    string DateTo,
    string? Reason);
