// [INTENT]: Membro dello staff nella lista pubblica (GET /api/v1/staff). `PhotoUrl` può essere null:
// il frontend gestisce il caso senza foto.

namespace WebAgency_BookingSystem.Core.Dtos.Public;

/// <summary>
/// Membro dello staff attivo del tenant.
/// </summary>
public sealed record StaffResponse(
    Guid Id,
    string Name,
    string? Role,
    string? Specialization,
    string? PhotoUrl,
    bool Active);
