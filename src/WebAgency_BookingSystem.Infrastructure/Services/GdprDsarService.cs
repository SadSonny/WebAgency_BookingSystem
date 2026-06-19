// [INTENT]: Servizio DSAR (GDPR 4.3) tenant-scoped. ExportAsync raccoglie le prenotazioni di un cliente (diritto
// d'accesso); EraseAsync anonimizza quelle prenotazioni ed elimina le email outbox del cliente (diritto all'oblio).
// L'isolamento per tenant è garantito dal global query filter (niente IgnoreQueryFilters qui). Ogni operazione
// scrive un audit PII-FREE (subjectRef = HMAC dell'email, mai l'email in chiaro) e persiste TUTTO in un solo
// SaveChangesAsync: su Postgres è un'unica transazione atomica; su EF InMemory funziona (no ExecuteUpdate).

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Abstractions;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Services;

internal sealed class GdprDsarService : IGdprDsarService
{
    private readonly BookingSystemDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly byte[] _hmacKey;
    private readonly ILogger<GdprDsarService> _logger;

    public GdprDsarService(
        BookingSystemDbContext db,
        ITenantContext tenantContext,
        IConfiguration configuration,
        ILogger<GdprDsarService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
        string secret = configuration["JWT_SECRET"] ?? configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret è richiesto per l'audit DSAR (chiave HMAC).");
        _hmacKey = Encoding.UTF8.GetBytes(secret);
    }

    public async Task<Result<CustomerDataExport>> ExportAsync(string email, CancellationToken ct = default)
    {
        string normalized = Normalize(email);
        // WHY: .ToLower() in una lambda EF Core viene tradotto in LOWER() SQL (culture-invariant su Postgres).
        // Le versioni culture-aware o StringComparison non sono traducibili da EF Core → soppressione necessaria.
#pragma warning disable CA1304, CA1311, CA1862
        List<Booking> bookings = await _db.Bookings
            .Where(b => b.CustomerEmail.ToLower() == normalized)
            .OrderBy(b => b.BookingDate).ThenBy(b => b.BookingTime)
            .ToListAsync(ct);
#pragma warning restore CA1304, CA1311, CA1862

        var items = bookings.Select(b => new BookingExportItem(
            b.Id,
            b.BookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            b.BookingTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            b.DurationMinutes,
            b.CustomerName,
            b.CustomerPhone,
            b.CustomerEmail,
            b.CustomerNotes,
            b.GdprConsent,
            b.GdprConsentAt,
            b.GdprConsentVersion,
            b.Status.ToString().ToLowerInvariant(),
            b.CreatedAt)).ToList();

        WriteAudit("customer_data_exported", normalized,
            JsonSerializer.Serialize(new { matched = items.Count, subjectRef = SubjectRef(normalized) }));
        await _db.SaveChangesAsync(ct);

        return new CustomerDataExport(normalized, items.Count, items);
    }

    public async Task<Result<ErasureResult>> EraseAsync(string email, CancellationToken ct = default)
    {
        string normalized = Normalize(email);

        // WHY: la cancellazione opera sulle poche righe di UN cliente → caricamento tracked + un solo SaveChanges
        // (atomico su Postgres). Niente ExecuteUpdate/Delete: non supportati da EF InMemory e non necessari qui.
        // WHY: .ToLower() nelle lambda EF Core traduce in LOWER() SQL (culture-invariant su Postgres).
        // Le versioni culture-aware o StringComparison non sono traducibili da EF Core → soppressione necessaria.
#pragma warning disable CA1304, CA1311, CA1862
        List<Booking> bookings = await _db.Bookings
            .Where(b => b.CustomerEmail.ToLower() == normalized && b.CustomerName != DataRetentionService.AnonymizedMarker)
            .ToListAsync(ct);

        // WHY: l'HTML congelato delle email outbox contiene PII → vanno eliminate, non solo le prenotazioni.
        List<OutboxEmail> outbox = await _db.OutboxEmails
            .Where(e => e.ToEmail.ToLower() == normalized)
            .ToListAsync(ct);
#pragma warning restore CA1304, CA1311, CA1862

        if (bookings.Count == 0 && outbox.Count == 0)
        {
            return Error.NotFound("customer_not_found", "Nessun dato trovato per l'email indicata.");
        }

        foreach (Booking b in bookings)
        {
            // WHY: stessi campi di DataRetentionService (marker condiviso) — anonimizzazione coerente.
            b.CustomerName = DataRetentionService.AnonymizedMarker;
            b.CustomerPhone = string.Empty;
            b.CustomerEmail = string.Empty;
            b.CustomerNotes = null;
        }
        _db.OutboxEmails.RemoveRange(outbox);

        WriteAudit("customer_data_erased", normalized,
            JsonSerializer.Serialize(new { anonymizedBookings = bookings.Count, purgedOutbox = outbox.Count, subjectRef = SubjectRef(normalized) }));

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("DSAR erase: {Bookings} prenotazioni anonimizzate, {Outbox} email outbox eliminate", bookings.Count, outbox.Count);
        return new ErasureResult(bookings.Count, outbox.Count);
    }

    private void WriteAudit(string action, string normalizedEmail, string metadata) =>
        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId ?? Guid.Empty,
            Action = action,
            Actor = "owner",
            Metadata = metadata,
            CreatedAt = DateTimeOffset.UtcNow,
        });

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    // WHY: subjectRef = HMAC-SHA256 dell'email (chiave server) → correlazione tra eventi sullo stesso soggetto
    // senza esporre l'email; non reversibile da chi legge il DB (a differenza di un hash non salato).
    private string SubjectRef(string normalizedEmail)
    {
        byte[] hash = HMACSHA256.HashData(_hmacKey, Encoding.UTF8.GetBytes(normalizedEmail));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
