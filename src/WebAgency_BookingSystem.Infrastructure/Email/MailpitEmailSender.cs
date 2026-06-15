// [INTENT]: Trasporto email per lo SVILUPPO (AD-10): invia via SMTP a Mailpit, che CATTURA i messaggi in una
// web UI senza recapitarli a destinatari reali (zero verifica mittente). NON va usato in produzione (non
// consegna nulla). Connessione in chiaro su porta 1025. Le eccezioni propagano: il dispatcher outbox le gestisce.

using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace WebAgency_BookingSystem.Infrastructure.Email;

/// <summary>Invio email via SMTP verso Mailpit (sviluppo). Cattura i messaggi, non li recapita.</summary>
internal sealed class MailpitEmailSender : IEmailSender
{
    private readonly EmailSettings _settings;

    public MailpitEmailSender(EmailSettings settings) => _settings = settings;

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_settings.SenderName, _settings.SenderEmail));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToEmail));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        // WHY: Mailpit espone uno SMTP di sviluppo senza TLS né autenticazione → SecureSocketOptions.None.
        using var client = new SmtpClient();
        await client.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, SecureSocketOptions.None, ct);
        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(quit: true, ct);
    }
}
