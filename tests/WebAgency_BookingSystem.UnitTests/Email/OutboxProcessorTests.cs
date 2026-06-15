// [INTENT]: Unit test della logica di dispatch outbox (PH-3): transizioni di stato Sent/retry/Failed e
// backoff, senza Postgres reale (EF InMemory + trasporto stub). Coprono il successo, il fallimento transitorio
// (resta Pending con NextAttemptAt spostato avanti) e l'esaurimento dei tentativi (Failed).

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WebAgency_BookingSystem.Core.Abstractions;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Infrastructure.Email;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.UnitTests.Email;

public class OutboxProcessorTests
{
    // Trasporto stub: configurabile per riuscire o lanciare, e conta gli invii.
    private sealed class StubSender(bool throws) : IEmailSender
    {
        public int Calls { get; private set; }

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Calls++;
            return throws ? throw new InvalidOperationException("provider down") : Task.CompletedTask;
        }
    }

    private static BookingSystemDbContext NewDb()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns((Guid?)null); // il processor usa IgnoreQueryFilters
        DbContextOptions<BookingSystemDbContext> options = new DbContextOptionsBuilder<BookingSystemDbContext>()
            .UseInMemoryDatabase($"outbox-tests-{Guid.NewGuid()}")
            .Options;
        return new BookingSystemDbContext(options, tenantContext);
    }

    private static OutboxEmail Pending(int attempts = 0) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        Kind = EmailKind.BookingConfirmation,
        Status = OutboxEmailStatus.Pending,
        ToEmail = "cliente@example.it",
        ToName = "Cliente",
        Subject = "Conferma prenotazione",
        HtmlBody = "<p>ok</p>",
        TextBody = "ok",
        Attempts = attempts,
        NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1), // eleggibile
    };

    [Fact]
    public async Task invio_riuscito_marca_sent()
    {
        using BookingSystemDbContext db = NewDb();
        OutboxEmail row = Pending();
        db.OutboxEmails.Add(row);
        await db.SaveChangesAsync();

        var sut = new EmailOutboxProcessor(db, new StubSender(throws: false), NullLogger<EmailOutboxProcessor>.Instance);
        int processed = await sut.ProcessPendingAsync();

        Assert.Equal(1, processed);
        Assert.Equal(OutboxEmailStatus.Sent, row.Status);
        Assert.NotNull(row.SentAt);
        Assert.Null(row.LastError);
    }

    [Fact]
    public async Task fallimento_transitorio_resta_pending_e_pospone_il_retry()
    {
        using BookingSystemDbContext db = NewDb();
        OutboxEmail row = Pending();
        db.OutboxEmails.Add(row);
        await db.SaveChangesAsync();

        var sut = new EmailOutboxProcessor(db, new StubSender(throws: true), NullLogger<EmailOutboxProcessor>.Instance);
        await sut.ProcessPendingAsync();

        Assert.Equal(OutboxEmailStatus.Pending, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.Equal("provider down", row.LastError);
        Assert.True(row.NextAttemptAt > DateTimeOffset.UtcNow, "Il retry deve essere posticipato nel futuro (backoff).");
    }

    [Fact]
    public async Task esaurimento_tentativi_marca_failed()
    {
        using BookingSystemDbContext db = NewDb();
        OutboxEmail row = Pending(attempts: 4); // il 5° tentativo è l'ultimo (MaxAttempts = 5)
        db.OutboxEmails.Add(row);
        await db.SaveChangesAsync();

        var sut = new EmailOutboxProcessor(db, new StubSender(throws: true), NullLogger<EmailOutboxProcessor>.Instance);
        await sut.ProcessPendingAsync();

        Assert.Equal(OutboxEmailStatus.Failed, row.Status);
        Assert.Equal(5, row.Attempts);
    }

    [Fact]
    public async Task ignora_le_email_non_ancora_eleggibili()
    {
        using BookingSystemDbContext db = NewDb();
        OutboxEmail future = Pending();
        future.NextAttemptAt = DateTimeOffset.UtcNow.AddHours(1); // non ancora eleggibile
        db.OutboxEmails.Add(future);
        await db.SaveChangesAsync();

        var sender = new StubSender(throws: false);
        var sut = new EmailOutboxProcessor(db, sender, NullLogger<EmailOutboxProcessor>.Instance);
        int processed = await sut.ProcessPendingAsync();

        Assert.Equal(0, processed);
        Assert.Equal(0, sender.Calls);
        Assert.Equal(OutboxEmailStatus.Pending, future.Status);
    }
}
