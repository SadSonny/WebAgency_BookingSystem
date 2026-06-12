// [INTENT]: Corpo della richiesta di creazione prenotazione (POST /api/v1/bookings). Date e ora sono
// stringhe (formati `yyyy-MM-dd` / `HH:mm`) per poter restituire errori di validazione 422 con messaggio
// dedicato in caso di formato errato, invece di un 400 generico di binding.

namespace WebAgency_BookingSystem.Core.Dtos.Public;

/// <summary>
/// Dati per creare una prenotazione. <see cref="StaffId"/> è opzionale (AD-04).
/// </summary>
public sealed record CreateBookingRequest(
    Guid ServiceId,
    Guid? StaffId,
    string Date,
    string Time,
    CustomerRequest Customer,
    bool GdprConsent);

/// <summary>
/// Dati anagrafici del cliente finale. <see cref="Notes"/> è opzionale.
/// </summary>
public sealed record CustomerRequest(
    string Name,
    string Phone,
    string Email,
    string? Notes);
