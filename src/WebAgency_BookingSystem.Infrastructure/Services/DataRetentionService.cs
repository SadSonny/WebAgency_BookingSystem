// [INTENT]: Implementazione retention/erasure GDPR (S2). (1) ANONIMIZZA i dati personali (nome/telefono/email/
// note) delle prenotazioni la cui data è oltre Gdpr:RetentionDays, conservando la riga per le statistiche.
// (2) PURGA le righe OutboxEmail già inviate più vecchie di Gdpr:OutboxRetentionDays (contengono PII nell'HTML).
// Usa ExecuteUpdate/ExecuteDelete (bulk) cross-tenant con IgnoreQueryFilters (nessun tenant nel contesto del job).

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Services;

internal sealed class DataRetentionService : IDataRetentionService
{
    // Marcatore dei dati anonimizzati: evita di rielaborare le stesse righe a ogni ciclo.
    internal const string AnonymizedMarker = "[rimosso]";

    private readonly BookingSystemDbContext _db;
    private readonly ILogger<DataRetentionService> _logger;
    private readonly int _bookingRetentionDays;
    private readonly int _outboxRetentionDays;

    public DataRetentionService(BookingSystemDbContext db, IConfiguration configuration, ILogger<DataRetentionService> logger)
    {
        _db = db;
        _logger = logger;
        _bookingRetentionDays = configuration.GetValue<int?>("Gdpr:RetentionDays") ?? 365;
        _outboxRetentionDays = configuration.GetValue<int?>("Gdpr:OutboxRetentionDays") ?? 30;
    }

    public async Task<(int AnonymizedBookings, int PurgedOutbox)> PurgeAsync(CancellationToken ct = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateOnly bookingCutoff = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-_bookingRetentionDays);

        // (1) Anonimizzazione bulk delle prenotazioni oltre la retention non ancora anonimizzate.
        int anonymized = await _db.Bookings
            .IgnoreQueryFilters()
            .Where(b => b.BookingDate < bookingCutoff && b.CustomerName != AnonymizedMarker)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.CustomerName, AnonymizedMarker)
                .SetProperty(b => b.CustomerPhone, string.Empty)
                .SetProperty(b => b.CustomerEmail, string.Empty)
                .SetProperty(b => b.CustomerNotes, (string?)null), ct);

        // (2) Purga delle email outbox inviate e datate (contengono PII nell'HTML congelato).
        DateTimeOffset outboxCutoff = now.AddDays(-_outboxRetentionDays);
        int purged = await _db.OutboxEmails
            .IgnoreQueryFilters()
            .Where(e => e.Status == OutboxEmailStatus.Sent && e.SentAt != null && e.SentAt < outboxCutoff)
            .ExecuteDeleteAsync(ct);

        if (anonymized > 0 || purged > 0)
        {
            _logger.LogInformation("GDPR retention: {Anonymized} prenotazioni anonimizzate, {Purged} email outbox purgate", anonymized, purged);
        }

        return (anonymized, purged);
    }
}
