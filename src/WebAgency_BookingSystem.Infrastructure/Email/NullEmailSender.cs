// [INTENT]: Trasporto email no-op (PH-3), usato quando Email:Provider = None (default sicuro: nessun provider
// configurato). Non invia nulla ma NON fallisce: il dispatcher marca le righe outbox come inviate, così la coda
// non si accumula in ambienti senza email configurata (es. provisioning/CLI, test che non verificano l'email).

using Microsoft.Extensions.Logging;

namespace WebAgency_BookingSystem.Infrastructure.Email;

/// <summary>Trasporto fittizio che "accetta" ogni email senza inviarla (provider non configurato).</summary>
internal sealed class NullEmailSender : IEmailSender
{
    private readonly ILogger<NullEmailSender> _logger;

    public NullEmailSender(ILogger<NullEmailSender> logger) => _logger = logger;

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        // WHY: nessuna PII nel log (solo l'oggetto, contenuto controllato dai template). Provider non configurato.
        _logger.LogInformation("[EmailNull] Provider email non configurato: email '{Subject}' non inviata (no-op).", message.Subject);
        return Task.CompletedTask;
    }
}
