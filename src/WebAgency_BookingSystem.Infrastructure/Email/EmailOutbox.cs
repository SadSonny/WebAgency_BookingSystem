// [INTENT]: Implementazione di IEmailOutbox (PH-3). Rende il contenuto via IEmailTemplateRenderer e aggiunge
// una riga OutboxEmail (stato Pending, eleggibile subito) al DbContext condiviso col chiamante, così l'email è
// committata nella STESSA transazione della prenotazione. Se il destinatario è assente (es. titolare senza
// OwnerEmail) NON accoda nulla: niente da inviare, niente riga inutile in coda.

using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Email;

internal sealed class EmailOutbox : IEmailOutbox
{
    private readonly BookingSystemDbContext _db;
    private readonly IEmailTemplateRenderer _renderer;
    private readonly ILogger<EmailOutbox> _logger;

    public EmailOutbox(BookingSystemDbContext db, IEmailTemplateRenderer renderer, ILogger<EmailOutbox> logger)
    {
        _db = db;
        _renderer = renderer;
        _logger = logger;
    }

    public void EnqueueBookingConfirmation(Booking booking) =>
        Enqueue(_renderer.RenderBookingConfirmation(booking), EmailKind.BookingConfirmation, booking);

    public void EnqueueOwnerNotification(Booking booking) =>
        Enqueue(_renderer.RenderOwnerNotification(booking), EmailKind.OwnerNotification, booking);

    public void EnqueueCancellationConfirmation(Booking booking) =>
        Enqueue(_renderer.RenderCancellationConfirmation(booking), EmailKind.CancellationConfirmation, booking);

    public void EnqueueReminder(Booking booking) =>
        Enqueue(_renderer.RenderReminder(booking), EmailKind.Reminder, booking);

    private void Enqueue(EmailMessage message, EmailKind kind, Booking booking)
    {
        if (string.IsNullOrWhiteSpace(message.ToEmail))
        {
            // WHY: destinatario assente (es. titolare senza OwnerEmail) → niente da inviare, non è un errore.
            _logger.LogWarning("Email '{Kind}' non accodata: destinatario assente. BookingId={BookingId}", kind, booking.Id);
            return;
        }

        _db.OutboxEmails.Add(new OutboxEmail
        {
            Id = Guid.NewGuid(),
            TenantId = booking.TenantId,
            BookingId = booking.Id,
            Kind = kind,
            Status = OutboxEmailStatus.Pending,
            ToEmail = message.ToEmail,
            ToName = message.ToName,
            Subject = message.Subject,
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody,
            Attempts = 0,
            NextAttemptAt = DateTimeOffset.UtcNow, // eleggibile immediatamente al primo giro del dispatcher
            // CreatedAt/UpdatedAt valorizzati dal TimestampInterceptor.
        });
    }
}
