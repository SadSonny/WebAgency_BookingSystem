// [INTENT]: Implementazione del dispatch outbox (PH-3). Carica le email Pending eleggibili (CROSS-tenant, via
// IgnoreQueryFilters) e le invia col trasporto configurato (IEmailSender). Successo → Sent; fallimento →
// retry con backoff esponenziale fino a MaxAttempts, poi Failed. Ogni invio è isolato (un errore non blocca
// gli altri). I log non contengono PII (solo id/tipo/tentativi).

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Email;

internal sealed class EmailOutboxProcessor : IOutboxEmailProcessor
{
    private const int BatchSize = 50;
    private const int MaxAttempts = 5;
    private const int BaseBackoffSeconds = 60;
    private const int MaxBackoffSeconds = 3600;
    private const int MaxErrorLength = 1000;

    private readonly BookingSystemDbContext _db;
    private readonly IEmailSender _sender;
    private readonly ILogger<EmailOutboxProcessor> _logger;

    public EmailOutboxProcessor(BookingSystemDbContext db, IEmailSender sender, ILogger<EmailOutboxProcessor> logger)
    {
        _db = db;
        _sender = sender;
        _logger = logger;
    }

    public async Task<int> ProcessPendingAsync(CancellationToken ct = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // WHY: IgnoreQueryFilters perché il dispatcher gira fuori da una richiesta (ITenantContext.TenantId è
        // null): senza, il filtro tenant escluderebbe ogni riga. Ordiniamo per eleggibilità e limitiamo il batch.
        List<OutboxEmail> pending = await _db.OutboxEmails
            .IgnoreQueryFilters()
            .Where(e => e.Status == OutboxEmailStatus.Pending && e.NextAttemptAt <= now)
            .OrderBy(e => e.NextAttemptAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            return 0;
        }

        foreach (OutboxEmail email in pending)
        {
            await TrySendAsync(email, ct);
        }

        await _db.SaveChangesAsync(ct);
        return pending.Count;
    }

    private async Task TrySendAsync(OutboxEmail email, CancellationToken ct)
    {
        var message = new EmailMessage(email.ToEmail, email.ToName, email.Subject, email.HtmlBody, email.TextBody);
        try
        {
            await _sender.SendAsync(message, ct);
            email.Status = OutboxEmailStatus.Sent;
            email.SentAt = DateTimeOffset.UtcNow;
            email.LastError = null;
            _logger.LogInformation("Email outbox inviata. Id={Id} Kind={Kind} BookingId={BookingId}",
                email.Id, email.Kind, email.BookingId);
        }
        catch (Exception ex)
        {
            email.Attempts++;
            email.LastError = Truncate(ex.Message, MaxErrorLength);

            if (email.Attempts >= MaxAttempts)
            {
                email.Status = OutboxEmailStatus.Failed;
                _logger.LogError(ex, "Email outbox FALLITA definitivamente dopo {Attempts} tentativi. Id={Id} Kind={Kind}",
                    email.Attempts, email.Id, email.Kind);
            }
            else
            {
                email.NextAttemptAt = DateTimeOffset.UtcNow.Add(Backoff(email.Attempts));
                _logger.LogWarning(ex, "Invio email outbox fallito (tentativo {Attempts}/{Max}); ritento dopo {Next:o}. Id={Id}",
                    email.Attempts, MaxAttempts, email.NextAttemptAt, email.Id);
            }
        }
    }

    // Backoff esponenziale: 60s, 120s, 240s, … con tetto a 1h.
    private static TimeSpan Backoff(int attempts)
    {
        double seconds = BaseBackoffSeconds * Math.Pow(2, attempts - 1);
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxBackoffSeconds));
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
