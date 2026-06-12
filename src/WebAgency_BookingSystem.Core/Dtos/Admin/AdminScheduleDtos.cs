// [INTENT]: DTO admin per orari settimanali (6.13) e chiusure straordinarie (6.14). Entrambi gli endpoint
// PUT sostituiscono in blocco l'intero set (più semplice e prevedibile di un diff). Orari/date come stringhe.

namespace WebAgency_BookingSystem.Core.Dtos.Admin;

/// <summary>Orario di apertura del tenant per un giorno (0=Dom..6=Sab). Orari <c>HH:mm</c> o null se chiuso.</summary>
public sealed record BusinessHoursItem(
    int DayOfWeek,
    bool IsOpen,
    string? OpenTime,
    string? CloseTime,
    string? BreakStart,
    string? BreakEnd);

/// <summary>Sostituisce in blocco gli orari settimanali del tenant.</summary>
public sealed record SetBusinessHoursRequest(IReadOnlyList<BusinessHoursItem> Days);

/// <summary>Chiusura straordinaria. Date <c>yyyy-MM-dd</c>.</summary>
public sealed record ClosureItem(string DateFrom, string DateTo, string? Reason);

/// <summary>Sostituisce in blocco le chiusure straordinarie del tenant.</summary>
public sealed record SetClosuresRequest(IReadOnlyList<ClosureItem> Closures);
