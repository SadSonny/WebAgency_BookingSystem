// [INTENT]: Provider email per lo SVILUPPO (AD-10). Invia via SMTP a Mailpit, che CATTURA i messaggi in una
// web UI senza recapitarli a destinatari reali: così si testano i template a colpo d'occhio senza verificare
// alcun mittente. NON va usato in produzione (non consegna nulla). Connessione in chiaro su porta 1025.

using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace WebAgency_BookingSystem.Infrastructure.Email;

/// <summary>
/// Invio email via SMTP verso Mailpit (sviluppo). Cattura i messaggi, non li recapita.
/// </summary>
internal sealed class MailpitEmailService : RenderedEmailService
{
    private readonly EmailSettings _settings;

    public MailpitEmailService(
        IEmailTemplateRenderer renderer, EmailSettings settings, ILogger<MailpitEmailService> logger)
        : base(renderer, logger) => _settings = settings;

    protected override async Task SendAsync(EmailMessage message, CancellationToken ct)
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
