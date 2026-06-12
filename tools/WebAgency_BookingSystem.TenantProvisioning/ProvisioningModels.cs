// [INTENT]: Modelli per deserializzare il file JSON di provisioning (step 7.1). Tutti i campi sono nullable
// per poter validare la presenza con messaggi chiari (step 7.2) invece di far fallire la deserializzazione.
// `localId`/`serviceLocalId` sono identificatori INTERNI al file usati per collegare staff↔servizi; il CLI
// genera gli UUID reali (vedi nota nella spec 05).

namespace WebAgency_BookingSystem.TenantProvisioning;

internal sealed record ProvisioningInput(
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

internal sealed record BookingRulesInput(
    int? MinAdvanceHours,
    int? MinCancellationHours,
    int? VisibleDaysAhead,
    bool? StaffChoiceEnabled,
    string? NotificationMethod);

internal sealed record BusinessHoursInput(
    int DayOfWeek,
    bool IsOpen,
    string? OpenTime,
    string? CloseTime,
    string? BreakStart,
    string? BreakEnd);

internal sealed record SpecialClosureInput(
    string? DateFrom,
    string? DateTo,
    string? Reason);

// Buffer per-servizio (AD-03): opzionale nel file; di default disabilitato.
internal sealed record ServiceInput(
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

internal sealed record StaffInput(
    string? LocalId,
    string? Name,
    string? Role,
    string? Specialization,
    string? PhotoUrl,
    int? DisplayOrder,
    IReadOnlyList<StaffBusinessHoursInput>? BusinessHours,
    IReadOnlyList<StaffServiceInput>? Services);

internal sealed record StaffBusinessHoursInput(
    int DayOfWeek,
    bool IsAvailable,
    string? StartTime,
    string? EndTime,
    string? BreakStart,
    string? BreakEnd);

internal sealed record StaffServiceInput(
    string? ServiceLocalId,
    decimal? PriceOverride);
