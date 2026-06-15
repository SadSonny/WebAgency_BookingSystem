// [INTENT]: Unit test della logica promemoria (T2.3): accoda solo gli appuntamenti Confermati, futuri, entro
// la finestra di anticipo del tenant, con notifiche email attive; marca ReminderSentAt per non re-inviare.
// EF InMemory + IEmailOutbox mockato (verifica accodamento senza rendering).

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WebAgency_BookingSystem.Core.Abstractions;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Infrastructure.Email;
using WebAgency_BookingSystem.Infrastructure.Persistence;
using WebAgency_BookingSystem.Infrastructure.Services;

namespace WebAgency_BookingSystem.UnitTests.Services;

public class ReminderEnqueuerTests
{
    private static BookingSystemDbContext NewDb()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns((Guid?)null); // l'enqueuer usa IgnoreQueryFilters
        DbContextOptions<BookingSystemDbContext> options = new DbContextOptionsBuilder<BookingSystemDbContext>()
            .UseInMemoryDatabase($"reminder-tests-{Guid.NewGuid()}")
            .Options;
        return new BookingSystemDbContext(options, tenantContext);
    }

    private static Tenant Tenant(int reminderHours, string notify = "email") => new()
    {
        Id = Guid.NewGuid(), Slug = "t", Name = "Salone", Timezone = "Europe/Rome",
        NotificationMethod = notify, ReminderHoursBefore = reminderHours, Active = true,
    };

    private static Booking Booking(Tenant tenant, DateOnly date, TimeOnly time)
    {
        // WHY: l'enqueuer fa Include(b.Service) e Include(b.Tenant); Booking→Service è una relazione REQUIRED
        // (INNER join) → il booking deve avere un Service reale, altrimenti verrebbe escluso dalla query.
        var service = new Service { Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "Servizio", DurationMinutes = 30, Active = true };
        return new Booking
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, ServiceId = service.Id, Service = service,
            BookingDate = date, BookingTime = time, DurationMinutes = 30,
            CustomerName = "Mario", CustomerEmail = "mario@example.it", CustomerPhone = "+39",
            Status = BookingStatus.Confirmed, CancellationToken = Guid.NewGuid(),
            Tenant = tenant,
        };
    }

    private static (ReminderEnqueuer Sut, IEmailOutbox Outbox, BookingSystemDbContext Db) Sut(BookingSystemDbContext db)
    {
        var outbox = Substitute.For<IEmailOutbox>();
        return (new ReminderEnqueuer(db, outbox, NullLogger<ReminderEnqueuer>.Instance), outbox, db);
    }

    private static readonly DateOnly Tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

    [Fact]
    public async Task appuntamento_entro_finestra_accoda_e_marca_inviato()
    {
        using BookingSystemDbContext db = NewDb();
        var tenant = Tenant(reminderHours: 240); // finestra ampia → domani è sempre dentro
        var booking = Booking(tenant, Tomorrow, new TimeOnly(10, 0));
        db.Tenants.Add(tenant);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var (sut, outbox, _) = Sut(db);
        int n = await sut.EnqueueDueRemindersAsync();

        Assert.Equal(1, n);
        outbox.Received(1).EnqueueReminder(booking);
        Assert.NotNull(booking.ReminderSentAt);
    }

    [Fact]
    public async Task appuntamento_troppo_lontano_non_accoda()
    {
        using BookingSystemDbContext db = NewDb();
        var tenant = Tenant(reminderHours: 1); // finestra 1h
        var booking = Booking(tenant, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), new TimeOnly(10, 0));
        db.Tenants.Add(tenant);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var (sut, outbox, _) = Sut(db);
        int n = await sut.EnqueueDueRemindersAsync();

        Assert.Equal(0, n);
        outbox.DidNotReceive().EnqueueReminder(Arg.Any<Booking>());
        Assert.Null(booking.ReminderSentAt);
    }

    [Fact]
    public async Task notifiche_disattivate_non_accoda()
    {
        using BookingSystemDbContext db = NewDb();
        var tenant = Tenant(reminderHours: 240, notify: "none");
        var booking = Booking(tenant, Tomorrow, new TimeOnly(10, 0));
        db.Tenants.Add(tenant);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var (sut, outbox, _) = Sut(db);
        int n = await sut.EnqueueDueRemindersAsync();

        Assert.Equal(0, n);
        outbox.DidNotReceive().EnqueueReminder(Arg.Any<Booking>());
    }

    [Fact]
    public async Task appuntamento_passato_non_accoda()
    {
        using BookingSystemDbContext db = NewDb();
        var tenant = Tenant(reminderHours: 240);
        var booking = Booking(tenant, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), new TimeOnly(10, 0));
        db.Tenants.Add(tenant);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var (sut, outbox, _) = Sut(db);
        int n = await sut.EnqueueDueRemindersAsync();

        Assert.Equal(0, n);
        outbox.DidNotReceive().EnqueueReminder(Arg.Any<Booking>());
    }
}
