// [INTENT]: Implementazione no-op di IEmailService per la V1 (AD-06): non invia email, registra solo un
// log informativo. In V2 verrà sostituita da BrevoEmailClient senza modifiche ai chiamanti. Per GDPR i log
// NON contengono dati personali del cliente: solo l'id della prenotazione.

using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Email;

/// <summary>
/// Stub che soddisfa il contratto <see cref="IEmailService"/> senza inviare nulla (V1 senza Brevo).
/// </summary>
internal sealed class EmailServiceStub : IEmailService
{
    private readonly ILogger<EmailServiceStub> _logger;

    public EmailServiceStub(ILogger<EmailServiceStub> logger) => _logger = logger;

    public Task SendBookingConfirmationAsync(Booking booking, CancellationToken ct = default)
    {
        _logger.LogInformation("[EmailStub] Conferma prenotazione non inviata (V1 no-op). BookingId={BookingId}", booking.Id);
        return Task.CompletedTask;
    }

    public Task SendOwnerNotificationAsync(Booking booking, CancellationToken ct = default)
    {
        _logger.LogInformation("[EmailStub] Notifica titolare non inviata (V1 no-op). BookingId={BookingId}", booking.Id);
        return Task.CompletedTask;
    }

    public Task SendCancellationConfirmationAsync(Booking booking, CancellationToken ct = default)
    {
        _logger.LogInformation("[EmailStub] Conferma disdetta non inviata (V1 no-op). BookingId={BookingId}", booking.Id);
        return Task.CompletedTask;
    }
}
