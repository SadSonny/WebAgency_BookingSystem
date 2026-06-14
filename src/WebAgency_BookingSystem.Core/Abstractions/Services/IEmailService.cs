// [INTENT]: Contratto per l'invio delle email transazionali. In V1 esiste solo un'implementazione no-op
// (EmailServiceStub); in V2 sarà implementato con Brevo (AD-06). L'interfaccia stabile permette lo swap
// senza modifiche ai chiamanti. Le chiamate sono pensate per essere fire-and-forget post-commit.

using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Core.Abstractions.Services;

/// <summary>
/// Invia le notifiche email legate al ciclo di vita di una prenotazione. Le implementazioni non devono
/// lanciare eccezioni che impattino il flusso di prenotazione (l'invio è accessorio, non transazionale).
/// I chiamanti devono valorizzare le proprietà di navigazione richieste dai template
/// (<see cref="Booking.Tenant"/>, <see cref="Booking.Service"/>, ed eventuale <see cref="Booking.Staff"/>).
/// </summary>
public interface IEmailService
{
    /// <summary>Invia al cliente la conferma della prenotazione appena creata.</summary>
    Task SendBookingConfirmationAsync(Booking booking, CancellationToken ct = default);

    /// <summary>Notifica al titolare la nuova prenotazione (se il tenant ha le notifiche attive).</summary>
    Task SendOwnerNotificationAsync(Booking booking, CancellationToken ct = default);

    /// <summary>Invia al cliente la conferma dell'avvenuta disdetta.</summary>
    Task SendCancellationConfirmationAsync(Booking booking, CancellationToken ct = default);
}
