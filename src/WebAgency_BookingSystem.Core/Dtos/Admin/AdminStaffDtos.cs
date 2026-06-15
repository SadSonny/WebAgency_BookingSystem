// [INTENT]: DTO admin per la gestione dello staff (step 2.8 per 6.9-6.12). Il write request include anche le
// associazioni ai servizi (quali servizi eroga, con eventuale override prezzo) e gli orari dello staff, così
// la creazione/aggiornamento è completa. La response li riespone per il pannello.

namespace WebAgency_BookingSystem.Core.Dtos.Admin;

/// <summary>Rappresentazione completa di un membro dello staff per il pannello admin.</summary>
public sealed record StaffAdminResponse(
    Guid Id,
    string Name,
    string? Role,
    string? Specialization,
    string? PhotoUrl,
    bool Active,
    int DisplayOrder,
    IReadOnlyList<StaffServiceAssignment> Services,
    IReadOnlyList<StaffBusinessHoursItem> BusinessHours);

/// <summary>Corpo per creare (POST) o sostituire (PUT) un membro dello staff.</summary>
public sealed record StaffWriteRequest(
    string Name,
    string? Role,
    string? Specialization,
    string? PhotoUrl,
    bool? Active,
    int? DisplayOrder,
    IReadOnlyList<StaffServiceAssignment>? Services,
    IReadOnlyList<StaffBusinessHoursItem>? BusinessHours);

/// <summary>Associazione staff↔servizio con eventuale override di prezzo (null = usa base_price).</summary>
public sealed record StaffServiceAssignment(Guid ServiceId, decimal? PriceOverride);

/// <summary>Orario di lavoro dello staff per un giorno (0=Dom..6=Sab). Orari <c>HH:mm</c> o null se non disponibile.</summary>
public sealed record StaffBusinessHoursItem(
    int DayOfWeek,
    bool IsAvailable,
    string? StartTime,
    string? EndTime,
    string? BreakStart,
    string? BreakEnd);

/// <summary>Corpo per creare un'assenza dell'operatore (T1.1). Date <c>yyyy-MM-dd</c>; orari <c>HH:mm</c> o
/// entrambi null per giornata intera.</summary>
public sealed record StaffTimeOffRequest(
    string DateFrom,
    string DateTo,
    string? StartTime,
    string? EndTime,
    string? Reason);

/// <summary>Assenza dell'operatore esposta all'admin.</summary>
public sealed record StaffTimeOffResponse(
    Guid Id,
    string DateFrom,
    string DateTo,
    string? StartTime,
    string? EndTime,
    string? Reason);
