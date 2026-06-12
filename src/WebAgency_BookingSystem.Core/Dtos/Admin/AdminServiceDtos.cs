// [INTENT]: DTO admin per la gestione dei servizi (step 2.8 per 6.5-6.8). La response include anche i campi
// non esposti al pubblico (active, buffer, displayOrder). Lo stesso record di scrittura serve sia per la
// creazione (POST) sia per l'aggiornamento completo (PUT).

namespace WebAgency_BookingSystem.Core.Dtos.Admin;

/// <summary>
/// Rappresentazione completa di un servizio per il pannello admin.
/// </summary>
public sealed record ServiceAdminResponse(
    Guid Id,
    string Name,
    string? Category,
    string? Description,
    int DurationMinutes,
    decimal? BasePrice,
    int ParallelSlots,
    bool BufferEnabled,
    int BufferMinutes,
    string BufferPosition,
    bool Active,
    int DisplayOrder);

/// <summary>
/// Corpo per creare (POST) o sostituire (PUT) un servizio. I campi opzionali hanno default sensati.
/// </summary>
public sealed record ServiceWriteRequest(
    string Name,
    string? Category,
    string? Description,
    int DurationMinutes,
    decimal? BasePrice,
    int? ParallelSlots,
    bool? BufferEnabled,
    int? BufferMinutes,
    string? BufferPosition,
    bool? Active,
    int? DisplayOrder);
