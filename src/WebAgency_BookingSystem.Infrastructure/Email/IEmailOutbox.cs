// [INTENT]: Accodamento delle email transazionali nella outbox (PH-3). I metodi RENDONO il contenuto e
// AGGIUNGONO una riga al DbContext SENZA salvarla: l'accodamento partecipa così alla transazione del chiamante
// (BookingService), garantendo che email e prenotazione vengano committate atomicamente. L'invio effettivo è
// demandato al dispatcher in background. Pubblica perché iniettata in BookingService e mockata negli unit test.

using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Email;

/// <summary>
/// Accoda le email legate al ciclo di vita di una prenotazione. Le navigation <see cref="Booking.Tenant"/> e
/// <see cref="Booking.Service"/> devono essere valorizzate. Non effettua SaveChanges: lo fa il chiamante.
/// </summary>
public interface IEmailOutbox
{
    /// <summary>Accoda la conferma di prenotazione destinata al cliente.</summary>
    void EnqueueBookingConfirmation(Booking booking);

    /// <summary>Accoda la notifica di nuova prenotazione destinata al titolare.</summary>
    void EnqueueOwnerNotification(Booking booking);

    /// <summary>Accoda la conferma di disdetta destinata al cliente.</summary>
    void EnqueueCancellationConfirmation(Booking booking);
}
