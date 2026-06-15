// [INTENT]: Corpo della richiesta di spostamento prenotazione (PUT /api/v1/bookings/{id}/reschedule, T2.2).
// Cambia solo data/ora; servizi e operatore restano invariati. Formati `yyyy-MM-dd` / `HH:mm`.

namespace WebAgency_BookingSystem.Core.Dtos.Public;

/// <summary>Nuova data/ora a cui spostare la prenotazione.</summary>
public sealed record RescheduleBookingRequest(string Date, string Time);
