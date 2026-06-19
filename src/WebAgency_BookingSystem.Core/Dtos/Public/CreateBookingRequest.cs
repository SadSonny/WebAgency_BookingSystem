// [INTENT]: Corpo della richiesta di creazione prenotazione (POST /api/v1/bookings). Date e ora sono
// stringhe (formati `yyyy-MM-dd` / `HH:mm`) per poter restituire errori di validazione 422 con messaggio
// dedicato in caso di formato errato, invece di un 400 generico di binding.

namespace WebAgency_BookingSystem.Core.Dtos.Public;

/// <summary>
/// Dati per creare una prenotazione. <see cref="StaffId"/> è opzionale (AD-04).
/// </summary>
/// <param name="ServiceId">Servizio principale (primo della sequenza).</param>
/// <param name="AdditionalServiceIds">Servizi aggiuntivi dell'appuntamento (T1.3), in ordine, svolti
/// consecutivamente dallo STESSO operatore dopo il principale. Null/vuoto = appuntamento a servizio singolo.</param>
/// <param name="GdprConsentVersion">Versione dell'informativa mostrata al cliente (opzionale); salvata come prova del consenso.</param>
public sealed record CreateBookingRequest(
    Guid ServiceId,
    Guid? StaffId,
    string Date,
    string Time,
    CustomerRequest Customer,
    bool GdprConsent,
    IReadOnlyList<Guid>? AdditionalServiceIds = null,
    string? GdprConsentVersion = null);

/// <summary>
/// Dati anagrafici del cliente finale. <see cref="Notes"/> è opzionale.
/// </summary>
public sealed record CustomerRequest(
    string Name,
    string Phone,
    string Email,
    string? Notes);
