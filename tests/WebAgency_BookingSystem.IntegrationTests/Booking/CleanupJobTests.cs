// [INTENT]: Test di integrazione per IExpiredBookingCleaner. Verifica che una prenotazione Confirmed
// con data+ora nel passato venga marcata NoShow, e che una prenotazione futura non venga toccata.
// Chiama IExpiredBookingCleaner direttamente (senza passare dall'HTTP) per testare la logica in isolation
// dallo scheduling del BackgroundService.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebAgency_BookingSystem.Core.Enums;
using BookingEntity = WebAgency_BookingSystem.Core.Entities.Booking;
using WebAgency_BookingSystem.Infrastructure.Persistence;
using WebAgency_BookingSystem.Infrastructure.Services;
using WebAgency_BookingSystem.IntegrationTests.Fixtures;

namespace WebAgency_BookingSystem.IntegrationTests.Booking;

public sealed class CleanupJobTests : IntegrationTestBase
{
    public CleanupJobTests(BookingSystemFixture fixture) : base(fixture) { }

    [Fact]
    public async Task prenotazione_scaduta_viene_marcata_noshow()
    {
        await CleanupBookingsAsync();

        // Inserisce direttamente una prenotazione Confirmed nel passato (ieri ore 09:00).
        var bookingId = await InsertPastBookingAsync(DateOnly.FromDateTime(DateTime.Today.AddDays(-1)), new TimeOnly(9, 0));

        using var scope = Fixture.Factory.Services.CreateScope();
        var cleaner = scope.ServiceProvider.GetRequiredService<IExpiredBookingCleaner>();
        int updated = await cleaner.CleanupExpiredAsync();

        Assert.Equal(1, updated);

        var db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();
        var booking = await db.Bookings.IgnoreQueryFilters().SingleAsync(b => b.Id == bookingId);
        Assert.Equal(BookingStatus.NoShow, booking.Status);
        Assert.NotNull(booking.NoShowMarkedAt);
    }

    [Fact]
    public async Task prenotazione_futura_non_viene_toccata()
    {
        await CleanupBookingsAsync();

        var bookingId = await InsertPastBookingAsync(TestData.FutureMonday, new TimeOnly(10, 0));

        using var scope = Fixture.Factory.Services.CreateScope();
        var cleaner = scope.ServiceProvider.GetRequiredService<IExpiredBookingCleaner>();
        int updated = await cleaner.CleanupExpiredAsync();

        Assert.Equal(0, updated);

        var db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();
        var booking = await db.Bookings.IgnoreQueryFilters().SingleAsync(b => b.Id == bookingId);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Null(booking.NoShowMarkedAt);
    }

    // WHY: inseriamo la prenotazione direttamente nel DB per bypassare le validazioni dell'endpoint
    // (es. MinAdvanceHours) che impedirebbero di creare una prenotazione nel passato via HTTP.
    private async Task<Guid> InsertPastBookingAsync(DateOnly date, TimeOnly time)
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();
        var now = DateTimeOffset.UtcNow;

        var booking = new BookingEntity
        {
            Id = Guid.NewGuid(),
            TenantId = TestData.TenantId,
            ServiceId = TestData.ServiceSingleId,
            StaffId = TestData.StaffId,
            BookingDate = date,
            BookingTime = time,
            DurationMinutes = 30,
            CustomerName = "Cliente Cleanup Test",
            CustomerPhone = "+390000000000",
            CustomerEmail = "cleanup@test.it",
            GdprConsent = true,
            GdprConsentAt = now,
            Status = BookingStatus.Confirmed,
            CancellationToken = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking.Id;
    }
}
