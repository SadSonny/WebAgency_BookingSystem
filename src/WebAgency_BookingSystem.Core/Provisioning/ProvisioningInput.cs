// [INTENT]: Modelli condivisi (CLI + API platform) per deserializzare il file JSON di provisioning.
// Tutti i campi sono nullable per consentire validazione esplicita con messaggi chiari (ProvisioningValidator)
// invece di far fallire la deserializzazione JSON. `localId`/`serviceLocalId` sono identificatori INTERNI al
// file usati per collegare staff↔servizi; il servizio genera gli UUID reali.

namespace WebAgency_BookingSystem.Core.Provisioning;

/// <summary>Input completo per il provisioning di un nuovo tenant.</summary>
public sealed record ProvisioningInput(
    string? Slug,
    string? Name,
    string? SiteUrl,
    string? OwnerEmail,
    string? Timezone,
    BookingRulesInput? BookingRules,
    IReadOnlyList<BusinessHoursInput>? BusinessHours,
    IReadOnlyList<SpecialClosureInput>? SpecialClosures,
    IReadOnlyList<ServiceInput>? Services,
    IReadOnlyList<StaffInput>? Staff);

/// <summary>Regole di prenotazione configurabili per tenant.</summary>
public sealed record BookingRulesInput(
    int? MinAdvanceHours,
    int? MinCancellationHours,
    int? VisibleDaysAhead,
    bool? StaffChoiceEnabled,
    string? NotificationMethod);

/// <summary>Orario di apertura giornaliero del tenant.</summary>
public sealed record BusinessHoursInput(
    int DayOfWeek,
    bool IsOpen,
    string? OpenTime,
    string? CloseTime,
    string? BreakStart,
    string? BreakEnd);

/// <summary>Chiusura straordinaria per un intervallo di date.</summary>
public sealed record SpecialClosureInput(
    string? DateFrom,
    string? DateTo,
    string? Reason);

/// <summary>
/// Servizio offerto dal tenant. Buffer per-servizio (AD-03): opzionale nel file; di default disabilitato.
/// </summary>
public sealed record ServiceInput(
    string? LocalId,
    string? Name,
    string? Category,
    string? Description,
    int? DurationMinutes,
    decimal? BasePrice,
    int? ParallelSlots,
    int? DisplayOrder,
    bool? BufferEnabled,
    int? BufferMinutes,
    string? BufferPosition);

/// <summary>Membro dello staff del tenant.</summary>
public sealed record StaffInput(
    string? LocalId,
    string? Name,
    string? Role,
    string? Specialization,
    string? PhotoUrl,
    int? DisplayOrder,
    IReadOnlyList<StaffBusinessHoursInput>? BusinessHours,
    IReadOnlyList<StaffServiceInput>? Services);

/// <summary>Orario di disponibilità di un membro dello staff per un giorno della settimana.</summary>
public sealed record StaffBusinessHoursInput(
    int DayOfWeek,
    bool IsAvailable,
    string? StartTime,
    string? EndTime,
    string? BreakStart,
    string? BreakEnd);

/// <summary>Associazione staff↔servizio con eventuale prezzo personalizzato.</summary>
public sealed record StaffServiceInput(
    string? ServiceLocalId,
    decimal? PriceOverride);
