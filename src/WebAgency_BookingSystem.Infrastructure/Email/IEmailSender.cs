// [INTENT]: Trasporto email di basso livello (PH-3): invia un EmailMessage GIÀ renderizzato. Disaccoppia il
// "come si invia" (SMTP Mailpit dev / REST Brevo prod / no-op) dal "cosa si invia" (renderer) e dal "quando"
// (dispatcher outbox). A differenza del vecchio IEmailService, qui un fallimento DEVE propagare un'eccezione:
// è il dispatcher a deciderne il retry. Selezionato per ambiente in DI (AD-10).

namespace WebAgency_BookingSystem.Infrastructure.Email;

/// <summary>
/// Invia un messaggio email già composto. Lancia in caso di errore di trasporto (il chiamante gestisce il retry).
/// </summary>
internal interface IEmailSender
{
    /// <summary>Invia il messaggio. Solleva un'eccezione se il provider non accetta/recapita l'email.</summary>
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
