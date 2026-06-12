// [INTENT]: Servizio nella lista pubblica (GET /api/v1/services). Il prezzo è il base_price del servizio
// (nessun contesto staff in questo endpoint). `StaffIds` elenca gli staff che eseguono il servizio,
// usato dal frontend per il filtro staff.

namespace WebAgency_BookingSystem.Core.Dtos.Public;

/// <summary>
/// Servizio attivo offerto dal tenant.
/// </summary>
/// <param name="DurationMin">Durata in minuti.</param>
/// <param name="Price">Prezzo base; null se non impostato.</param>
/// <param name="StaffIds">Id degli staff che erogano il servizio.</param>
public sealed record ServiceResponse(
    Guid Id,
    string Name,
    string? Category,
    int DurationMin,
    decimal? Price,
    string? Description,
    IReadOnlyList<Guid> StaffIds,
    bool Active);
