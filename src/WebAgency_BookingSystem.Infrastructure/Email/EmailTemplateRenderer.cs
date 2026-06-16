// [INTENT]: Implementazione dei template email (AD-11): HTML inline + alternativa testuale, in italiano, con
// layout sobrio/neutro (branding definitivo rimandato — vedi piano 8.7). Tutti i dati dinamici provenienti dal
// cliente sono HTML-encoded per prevenire injection. Data/ora sono GIÀ locali del tenant sulla prenotazione,
// quindi non serve alcuna conversione di timezone qui.

using System.Globalization;
using System.Net;
using System.Text;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Email;

/// <summary>
/// Renderer dei template transazionali. Stateless e thread-safe: registrabile come singleton.
/// </summary>
internal sealed class EmailTemplateRenderer : IEmailTemplateRenderer
{
    // WHY: la prenotazione porta data/ora nel fuso del tenant e i testi sono in italiano → formattiamo con
    // it-IT (es. "lunedì 15 giugno 2026") per coerenza con il pubblico finale.
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("it-IT");

    public EmailMessage RenderBookingConfirmation(Booking booking)
    {
        string business = BusinessName(booking);
        string subject = $"Conferma prenotazione — {business}";
        string intro = $"Ciao {Encode(booking.CustomerName)}, la tua prenotazione è confermata. Ecco il riepilogo:";

        string html = Layout(business, "Prenotazione confermata", intro, DetailRowsHtml(booking), FooterHtml());
        string text = TextBody("Prenotazione confermata", intro, booking);
        return new EmailMessage(booking.CustomerEmail, booking.CustomerName, subject, html, text);
    }

    public EmailMessage RenderOwnerNotification(Booking booking)
    {
        string business = BusinessName(booking);
        string ownerEmail = booking.Tenant?.OwnerEmail ?? string.Empty;
        string subject = $"Nuova prenotazione — {business}";
        string intro = $"Hai ricevuto una nuova prenotazione da {Encode(booking.CustomerName)} "
            + $"({Encode(booking.CustomerPhone)}). Dettagli:";

        string html = Layout(business, "Nuova prenotazione", intro, DetailRowsHtml(booking, includeCustomer: true), FooterHtml());
        string text = TextBody("Nuova prenotazione", intro, booking, includeCustomer: true);
        return new EmailMessage(ownerEmail, business, subject, html, text);
    }

    public EmailMessage RenderReminder(Booking booking)
    {
        string business = BusinessName(booking);
        string subject = $"Promemoria appuntamento — {business}";
        string intro = $"Ciao {Encode(booking.CustomerName)}, ti ricordiamo il tuo appuntamento. Ecco il riepilogo:";

        string html = Layout(business, "Promemoria appuntamento", intro, DetailRowsHtml(booking), FooterHtml());
        string text = TextBody("Promemoria appuntamento", intro, booking);
        return new EmailMessage(booking.CustomerEmail, booking.CustomerName, subject, html, text);
    }

    public EmailMessage RenderCancellationConfirmation(Booking booking)
    {
        string business = BusinessName(booking);
        string subject = $"Prenotazione disdetta — {business}";
        string intro = $"Ciao {Encode(booking.CustomerName)}, la tua prenotazione è stata disdetta. "
            + "Riepilogo della prenotazione annullata:";

        string html = Layout(business, "Prenotazione disdetta", intro, DetailRowsHtml(booking), FooterHtml());
        string text = TextBody("Prenotazione disdetta", intro, booking);
        return new EmailMessage(booking.CustomerEmail, booking.CustomerName, subject, html, text);
    }

    public EmailMessage RenderAccountActivation(string businessName, string toEmail, string activationUrl)
    {
        string business = string.IsNullOrWhiteSpace(businessName) ? "BookingSystem" : businessName;
        string subject = $"Attiva il tuo account — {business}";
        string intro = "Il tuo account di gestione è stato creato. Imposta la tua password per attivarlo "
            + "(il link scade tra 72 ore).";
        string body = CtaHtml(activationUrl, "Attiva account e imposta password");
        string html = Layout(business, "Attiva il tuo account", intro, body, FooterHtml());
        string text = $"{intro}\n\nApri questo link per attivare l'account:\n{activationUrl}";
        return new EmailMessage(toEmail, business, subject, html, text);
    }

    public EmailMessage RenderPasswordReset(string businessName, string toEmail, string resetUrl)
    {
        string business = string.IsNullOrWhiteSpace(businessName) ? "BookingSystem" : businessName;
        string subject = $"Reimposta la password — {business}";
        string intro = "Abbiamo ricevuto una richiesta di reimpostazione password. Se non sei stato tu, ignora "
            + "questa email. Il link scade tra 1 ora.";
        string body = CtaHtml(resetUrl, "Reimposta password");
        string html = Layout(business, "Reimposta la password", intro, body, FooterHtml());
        string text = $"{intro}\n\nApri questo link per reimpostare la password:\n{resetUrl}";
        return new EmailMessage(toEmail, business, subject, html, text);
    }

    public EmailMessage RenderAccountSecurityConfirmation(string businessName, string toEmail, string heading, string message)
    {
        string business = string.IsNullOrWhiteSpace(businessName) ? "BookingSystem" : businessName;
        string subject = $"{heading} — {business}";
        // Il messaggio appare una sola volta, come paragrafo introduttivo; nessuna riga di dettaglio aggiuntiva.
        string html = Layout(business, heading, Encode(message), string.Empty, FooterHtml());
        string text = $"{heading}\n\n{message}";
        return new EmailMessage(toEmail, business, subject, html, text);
    }

    // CTA a bottone, con fallback testuale dell'URL (alcuni client non rendono i bottoni).
    private static string CtaHtml(string url, string label) =>
        $"<tr><td style=\"padding:16px 12px;\">"
        + $"<a href=\"{Encode(url)}\" style=\"display:inline-block;background:#111827;color:#ffffff;"
        + $"text-decoration:none;padding:12px 20px;border-radius:6px;font-size:15px;font-weight:600;\">{Encode(label)}</a>"
        + $"<div style=\"margin-top:12px;color:#6b7280;font-size:12px;word-break:break-all;\">{Encode(url)}</div>"
        + "</td></tr>";

    // ── Helpers di rendering ──────────────────────────────────────────────────

    private static string BusinessName(Booking booking) =>
        string.IsNullOrWhiteSpace(booking.Tenant?.Name) ? "BookingSystem" : booking.Tenant!.Name;

    private static string DetailRowsHtml(Booking booking, bool includeCustomer = false)
    {
        var sb = new StringBuilder();
        AppendRow(sb, "Servizio", Encode(booking.Service?.Name ?? string.Empty));
        AppendRow(sb, "Data", Encode(FormatDate(booking.BookingDate)));
        AppendRow(sb, "Ora", Encode(booking.BookingTime.ToString("HH:mm", Culture)));
        AppendRow(sb, "Durata", $"{booking.DurationMinutes} min");

        if (booking.Staff is not null)
        {
            AppendRow(sb, "Operatore", Encode(booking.Staff.Name));
        }

        if (booking.PriceAtBooking is decimal price)
        {
            AppendRow(sb, "Prezzo", Encode(price.ToString("C", Culture)));
        }

        if (includeCustomer)
        {
            AppendRow(sb, "Cliente", Encode(booking.CustomerName));
            AppendRow(sb, "Telefono", Encode(booking.CustomerPhone));
            AppendRow(sb, "Email", Encode(booking.CustomerEmail));
            if (!string.IsNullOrWhiteSpace(booking.CustomerNotes))
            {
                AppendRow(sb, "Note", Encode(booking.CustomerNotes));
            }
        }

        AppendRow(sb, "Codice", booking.Id.ToString());
        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, string label, string value) =>
        sb.Append("<tr>")
          .Append("<td style=\"padding:6px 12px;color:#666;font-size:14px;\">").Append(label).Append("</td>")
          .Append("<td style=\"padding:6px 12px;color:#111;font-size:14px;font-weight:600;\">").Append(value).Append("</td>")
          .Append("</tr>");

    private static string Layout(string business, string heading, string intro, string detailRows, string footer) =>
        $$"""
        <!DOCTYPE html>
        <html lang="it">
        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
        <body style="margin:0;padding:0;background:#f4f4f5;font-family:Arial,Helvetica,sans-serif;">
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f4f4f5;padding:24px 0;">
            <tr><td align="center">
              <table role="presentation" width="560" cellpadding="0" cellspacing="0" style="background:#ffffff;border-radius:8px;overflow:hidden;border:1px solid #e4e4e7;">
                <tr><td style="background:#111827;padding:20px 24px;color:#ffffff;font-size:18px;font-weight:700;">{{Encode(business)}}</td></tr>
                <tr><td style="padding:24px;">
                  <h1 style="margin:0 0 12px;font-size:20px;color:#111827;">{{Encode(heading)}}</h1>
                  <p style="margin:0 0 20px;font-size:15px;color:#374151;line-height:1.5;">{{intro}}</p>
                  <table role="presentation" cellpadding="0" cellspacing="0" style="width:100%;border-collapse:collapse;background:#f9fafb;border-radius:6px;">
                    {{detailRows}}
                  </table>
                </td></tr>
                <tr><td style="padding:16px 24px;border-top:1px solid #e4e4e7;color:#9ca3af;font-size:12px;line-height:1.5;">{{footer}}</td></tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;

    private static string FooterHtml() =>
        "Email automatica, non rispondere a questo messaggio. I tuoi dati sono trattati nel rispetto del GDPR "
        + "e usati solo per la gestione della prenotazione.";

    private static string TextBody(string heading, string intro, Booking booking, bool includeCustomer = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine(heading).AppendLine();
        sb.AppendLine(StripTags(intro)).AppendLine();
        sb.Append("Servizio: ").AppendLine(booking.Service?.Name ?? string.Empty);
        sb.Append("Data: ").AppendLine(FormatDate(booking.BookingDate));
        sb.Append("Ora: ").AppendLine(booking.BookingTime.ToString("HH:mm", Culture));
        sb.Append("Durata: ").Append(booking.DurationMinutes).AppendLine(" min");

        if (booking.Staff is not null)
        {
            sb.Append("Operatore: ").AppendLine(booking.Staff.Name);
        }

        if (booking.PriceAtBooking is decimal price)
        {
            sb.Append("Prezzo: ").AppendLine(price.ToString("C", Culture));
        }

        if (includeCustomer)
        {
            sb.Append("Cliente: ").AppendLine(booking.CustomerName);
            sb.Append("Telefono: ").AppendLine(booking.CustomerPhone);
            sb.Append("Email: ").AppendLine(booking.CustomerEmail);
        }

        sb.Append("Codice: ").AppendLine(booking.Id.ToString());
        return sb.ToString();
    }

    private static string FormatDate(DateOnly date) =>
        date.ToString("dddd d MMMM yyyy", Culture);

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    // L'intro testuale è già senza tag, ma normalizziamo eventuali entità HTML introdotte da Encode.
    private static string StripTags(string value) => WebUtility.HtmlDecode(value);
}
