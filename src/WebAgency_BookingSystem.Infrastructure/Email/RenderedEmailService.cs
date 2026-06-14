// [INTENT]: Classe base dei provider email "veri" (Mailpit/Brevo). Implementa IEmailService delegando il
// CONTENUTO a IEmailTemplateRenderer e il TRASPORTO al metodo astratto SendAsync delle sottoclassi (Template
// Method). Centralizza qui la garanzia di contratto: l'invio NON deve mai lanciare verso i chiamanti (è
// accessorio, non transazionale) e i log non contengono PII (solo BookingId), per GDPR.

using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Email;

/// <summary>
/// Base condivisa dei provider email: rende il messaggio dal renderer e lo invia in sicurezza (senza propagare
/// eccezioni). Le sottoclassi forniscono solo il trasporto concreto via <see cref="SendAsync"/>.
/// </summary>
internal abstract class RenderedEmailService : IEmailService
{
    private readonly IEmailTemplateRenderer _renderer;
    private readonly ILogger _logger;

    protected RenderedEmailService(IEmailTemplateRenderer renderer, ILogger logger)
    {
        _renderer = renderer;
        _logger = logger;
    }

    public Task SendBookingConfirmationAsync(Booking booking, CancellationToken ct = default) =>
        SendSafelyAsync(_renderer.RenderBookingConfirmation(booking), booking.Id, "conferma prenotazione", ct);

    public Task SendOwnerNotificationAsync(Booking booking, CancellationToken ct = default) =>
        SendSafelyAsync(_renderer.RenderOwnerNotification(booking), booking.Id, "notifica titolare", ct);

    public Task SendCancellationConfirmationAsync(Booking booking, CancellationToken ct = default) =>
        SendSafelyAsync(_renderer.RenderCancellationConfirmation(booking), booking.Id, "conferma disdetta", ct);

    /// <summary>Invia il messaggio già renderizzato tramite il trasporto concreto del provider.</summary>
    protected abstract Task SendAsync(EmailMessage message, CancellationToken ct);

    private async Task SendSafelyAsync(EmailMessage message, Guid bookingId, string kind, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message.ToEmail))
        {
            // WHY: destinatario assente (es. titolare senza OwnerEmail configurata) → niente da inviare,
            // ma non è un errore: logghiamo a Warning e usciamo.
            _logger.LogWarning("Email '{Kind}' non inviata: destinatario assente. BookingId={BookingId}", kind, bookingId);
            return;
        }

        try
        {
            await SendAsync(message, ct);
            _logger.LogInformation("Email '{Kind}' inviata. BookingId={BookingId}", kind, bookingId);
        }
        catch (Exception ex)
        {
            // WHY: l'invio è accessorio (contratto IEmailService) → un guasto del provider non deve propagarsi
            // al flusso di prenotazione. Logghiamo l'errore (senza PII) e proseguiamo.
            _logger.LogError(ex, "Invio email '{Kind}' fallito. BookingId={BookingId}", kind, bookingId);
        }
    }
}
