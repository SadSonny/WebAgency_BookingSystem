// [INTENT]: Test di integrazione per la logica di buffer per servizio (AD-03, D-10).
// Verifica che con BufferPosition=After e BufferMinutes=15, uno slot a 30min dall'inizio
// sia bloccato (cade dentro il buffer), mentre quello a 45min sia libero.

using System.Net;
using WebAgency_BookingSystem.IntegrationTests.Fixtures;

namespace WebAgency_BookingSystem.IntegrationTests.Booking;

public sealed class BufferTests : IntegrationTestBase
{
    public BufferTests(BookingSystemFixture fixture) : base(fixture) { }

    [Fact]
    public async Task buffer_After_15min_blocca_slot_a_30min_e_libera_quello_a_45min()
    {
        // WHY: ServiceBufferId ha DurationMinutes=30, BufferPosition=After, BufferMinutes=15.
        // Prenotato 10:00 → finestra occupata = [10:00 – 10:45) (30min + 15min buffer).
        // Slot 10:30: occEnd=11:15, bEnd=10:45 → 10:30 < 10:45 → overlap → 409 slot_unavailable.
        // Slot 10:45: occStart=10:45, bEnd=10:45 → 10:45 < 10:45 è false → nessun overlap → 201.
        await CleanupBookingsAsync();
        using var client = AuthenticatedClient();

        var first = await client.PostAsync("/api/v1/bookings",
            BookingBody(TestData.ServiceBufferId, TestData.FutureMonday, "10:00"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var blocked = await client.PostAsync("/api/v1/bookings",
            BookingBody(TestData.ServiceBufferId, TestData.FutureMonday, "10:30"));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        var free = await client.PostAsync("/api/v1/bookings",
            BookingBody(TestData.ServiceBufferId, TestData.FutureMonday, "10:45"));
        Assert.Equal(HttpStatusCode.Created, free.StatusCode);
    }
}
