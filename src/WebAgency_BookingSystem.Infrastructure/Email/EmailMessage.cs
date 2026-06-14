// [INTENT]: Messaggio email già renderizzato, pronto per la spedizione. Disaccoppia il CONTENUTO (prodotto da
// IEmailTemplateRenderer) dal TRASPORTO (Mailpit/Brevo): i provider ricevono questo record e devono solo
// inviarlo, senza conoscere i template. Porta sia l'HTML sia un'alternativa testuale (deliverability).

namespace WebAgency_BookingSystem.Infrastructure.Email;

/// <summary>
/// Email pronta all'invio: destinatario, oggetto e corpo (HTML + testo). Immutabile.
/// </summary>
/// <param name="ToEmail">Indirizzo del destinatario.</param>
/// <param name="ToName">Nome visualizzato del destinatario.</param>
/// <param name="Subject">Oggetto dell'email.</param>
/// <param name="HtmlBody">Corpo in HTML.</param>
/// <param name="TextBody">Corpo in testo semplice (fallback per client che non renderizzano HTML).</param>
internal sealed record EmailMessage(
    string ToEmail,
    string ToName,
    string Subject,
    string HtmlBody,
    string TextBody);
