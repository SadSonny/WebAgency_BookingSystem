// [INTENT]: Logica di invio dei promemoria pre-appuntamento (T2.3), estratta dal BackgroundService per essere
// SCOPED e testabile in isolamento (come IExpiredBookingCleaner). Accoda nella outbox i promemoria dovuti e
// marca Booking.ReminderSentAt per non re-inviarli. Operazione CROSS-tenant.

namespace WebAgency_BookingSystem.Infrastructure.Services;

/// <summary>Accoda i promemoria per gli appuntamenti imminenti non ancora promemoria-ti. Restituisce il numero accodato.</summary>
internal interface IReminderEnqueuer
{
    Task<int> EnqueueDueRemindersAsync(CancellationToken ct = default);
}
