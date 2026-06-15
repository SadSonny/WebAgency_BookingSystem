// [INTENT]: Integration test delle assenze operatore (T1.1) sull'effetto pubblico: una assenza a giornata
// intera esclude il giorno dalla disponibilità dell'operatore e fa rifiutare la prenotazione; una assenza a
// fascia oraria rende non disponibili solo gli slot sovrapposti. L'assenza è seminata direttamente in DB.

using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Infrastructure.Persistence;
using WebAgency_BookingSystem.IntegrationTests.Fixtures;

namespace WebAgency_BookingSystem.IntegrationTests.Booking;

public sealed class StaffTimeOffTests : IntegrationTestBase
{
    public StaffTimeOffTests(BookingSystemFixture fixture) : base(fixture) { }

    [Fact]
    public async Task assenza_giornata_intera_esclude_il_giorno_e_blocca_la_prenotazione()
    {
        await CleanupBookingsAsync();
        await ClearTimeOffAsync();
        DateOnly day = TestData.FutureMonday;
        await SeedTimeOffAsync(day, day, startTime: null, endTime: null);

        using HttpClient client = AuthenticatedClient();

        // Disponibilità dell'operatore per quel giorno: il giorno NON deve comparire.
        var avail = await client.GetAsync(
            $"/api/v1/availability?serviceId={TestData.ServiceSingleId}&staffId={TestData.StaffId}&dateFrom={day:yyyy-MM-dd}&dateTo={day:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, avail.StatusCode);
        using var doc = JsonDocument.Parse(await avail.Content.ReadAsStringAsync());
        Assert.DoesNotContain(doc.RootElement.EnumerateArray(),
            d => d.GetProperty("date").GetString() == day.ToString("yyyy-MM-dd"));

        // Prenotazione con quell'operatore quel giorno: rifiutata (422).
        var body = BookingBody(TestData.ServiceSingleId, day, "10:00", TestData.StaffId);
        var booking = await client.PostAsync("/api/v1/bookings", body);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, booking.StatusCode);

        await ClearTimeOffAsync();
    }

    [Fact]
    public async Task assenza_a_fascia_rende_indisponibili_solo_gli_slot_sovrapposti()
    {
        await CleanupBookingsAsync();
        await ClearTimeOffAsync();
        DateOnly day = TestData.FutureMonday;
        await SeedTimeOffAsync(day, day, new TimeOnly(11, 0), new TimeOnly(12, 0));

        using HttpClient client = AuthenticatedClient();
        var avail = await client.GetAsync(
            $"/api/v1/availability?serviceId={TestData.ServiceSingleId}&staffId={TestData.StaffId}&dateFrom={day:yyyy-MM-dd}&dateTo={day:yyyy-MM-dd}");
        using var doc = JsonDocument.Parse(await avail.Content.ReadAsStringAsync());

        JsonElement slots = doc.RootElement.EnumerateArray()
            .First(d => d.GetProperty("date").GetString() == day.ToString("yyyy-MM-dd"))
            .GetProperty("slots");

        Assert.False(SlotAvailable(slots, "11:00"), "Lo slot 11:00 (in assenza) deve essere non disponibile.");
        Assert.True(SlotAvailable(slots, "10:30"), "Lo slot 10:30 (fuori assenza) deve restare disponibile.");

        await ClearTimeOffAsync();
    }

    private static bool SlotAvailable(JsonElement slots, string time) =>
        slots.EnumerateArray().Any(s => s.GetProperty("time").GetString() == time && s.GetProperty("available").GetBoolean());

    private async Task SeedTimeOffAsync(DateOnly from, DateOnly to, TimeOnly? startTime, TimeOnly? endTime)
    {
        using IServiceScope scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();
        db.StaffTimeOff.Add(new StaffTimeOff
        {
            Id = Guid.NewGuid(),
            TenantId = TestData.TenantId,
            StaffId = TestData.StaffId,
            DateFrom = from,
            DateTo = to,
            StartTime = startTime,
            EndTime = endTime,
            Reason = "test",
        });
        await db.SaveChangesAsync();
    }

    private async Task ClearTimeOffAsync()
    {
        using IServiceScope scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM staff_time_off WHERE tenant_id = {0}", TestData.TenantId);
    }
}
