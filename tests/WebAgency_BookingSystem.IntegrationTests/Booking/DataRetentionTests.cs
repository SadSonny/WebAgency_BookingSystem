// [INTENT]: Integration test della retention GDPR (S2): le prenotazioni oltre la finestra di retention vengono
// ANONIMIZZATE (PII rimossa, riga conservata), quelle recenti restano intatte; le email outbox inviate e datate
// vengono PURGATE, quelle recenti restano. Usa ExecuteUpdate/Delete → richiede DB reale (Testcontainers).

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Infrastructure.Persistence;
using WebAgency_BookingSystem.Infrastructure.Services;
using WebAgency_BookingSystem.IntegrationTests.Fixtures;
using BookingEntity = WebAgency_BookingSystem.Core.Entities.Booking;

namespace WebAgency_BookingSystem.IntegrationTests.Booking;

public sealed class DataRetentionTests : IntegrationTestBase
{
    public DataRetentionTests(BookingSystemFixture fixture) : base(fixture) { }

    [Fact]
    public async Task anonimizza_prenotazioni_vecchie_e_purga_outbox_inviate_datate()
    {
        await CleanupBookingsAsync();
        await ClearOutboxAsync();

        Guid oldBookingId = Guid.NewGuid();
        Guid recentBookingId = Guid.NewGuid();
        Guid oldOutboxId = Guid.NewGuid();
        Guid recentOutboxId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        using (IServiceScope scope = Fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();
            db.Bookings.Add(NewBooking(oldBookingId, DateOnly.FromDateTime(now.UtcDateTime).AddDays(-400)));   // oltre 365gg
            db.Bookings.Add(NewBooking(recentBookingId, DateOnly.FromDateTime(now.UtcDateTime).AddDays(-10))); // recente
            db.OutboxEmails.Add(NewOutbox(oldOutboxId, OutboxEmailStatus.Sent, now.AddDays(-40)));   // inviata, datata
            db.OutboxEmails.Add(NewOutbox(recentOutboxId, OutboxEmailStatus.Sent, now.AddDays(-5))); // inviata, recente
            await db.SaveChangesAsync();
        }

        using (IServiceScope scope = Fixture.Factory.Services.CreateScope())
        {
            var retention = scope.ServiceProvider.GetRequiredService<IDataRetentionService>();
            await retention.PurgeAsync();
        }

        using (IServiceScope scope = Fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();
            BookingEntity old = await db.Bookings.IgnoreQueryFilters().FirstAsync(b => b.Id == oldBookingId);
            BookingEntity recent = await db.Bookings.IgnoreQueryFilters().FirstAsync(b => b.Id == recentBookingId);

            Assert.Equal("[rimosso]", old.CustomerName);
            Assert.Equal(string.Empty, old.CustomerEmail);
            Assert.Equal("Mario Rossi", recent.CustomerName); // intatta

            bool oldOutboxExists = await db.OutboxEmails.IgnoreQueryFilters().AnyAsync(e => e.Id == oldOutboxId);
            bool recentOutboxExists = await db.OutboxEmails.IgnoreQueryFilters().AnyAsync(e => e.Id == recentOutboxId);
            Assert.False(oldOutboxExists);  // purgata
            Assert.True(recentOutboxExists); // conservata
        }

        await CleanupBookingsAsync();
        await ClearOutboxAsync();
    }

    private static BookingEntity NewBooking(Guid id, DateOnly date) => new()
    {
        Id = id, TenantId = TestData.TenantId, ServiceId = TestData.ServiceSingleId,
        BookingDate = date, BookingTime = new TimeOnly(10, 0), DurationMinutes = 30,
        CustomerName = "Mario Rossi", CustomerPhone = "+39 333 1112223", CustomerEmail = "mario@example.it",
        CustomerNotes = "nota", Status = BookingStatus.Completed, CancellationToken = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static OutboxEmail NewOutbox(Guid id, OutboxEmailStatus status, DateTimeOffset sentAt) => new()
    {
        Id = id, TenantId = TestData.TenantId, Kind = EmailKind.BookingConfirmation, Status = status,
        ToEmail = "mario@example.it", ToName = "Mario", Subject = "x", HtmlBody = "<p>PII</p>", TextBody = "PII",
        SentAt = sentAt, NextAttemptAt = sentAt, CreatedAt = sentAt, UpdatedAt = sentAt,
    };

    private async Task ClearOutboxAsync()
    {
        using IServiceScope scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM outbox_email WHERE tenant_id = {0}", TestData.TenantId);
    }
}
