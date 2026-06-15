// [INTENT]: Integration test dello spostamento prenotazione (T2.2): una prenotazione confermata si sposta a un
// nuovo slot libero (200, dettaglio aggiornato) e lo slot vecchio torna disponibile; spostare su uno slot già
// occupato dallo stesso operatore fallisce con 409.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WebAgency_BookingSystem.IntegrationTests.Fixtures;

namespace WebAgency_BookingSystem.IntegrationTests.Booking;

public sealed class RescheduleTests : IntegrationTestBase
{
    public RescheduleTests(BookingSystemFixture fixture) : base(fixture) { }

    private static async Task<(string Id, string Token)> CreateAsync(HttpClient client, string time)
    {
        var resp = await client.PostAsync("/api/v1/bookings",
            BookingBody(TestData.ServiceSingleId, TestData.FutureMonday, time, TestData.StaffId));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return (doc.RootElement.GetProperty("bookingId").GetString()!, doc.RootElement.GetProperty("cancellationToken").GetString()!);
    }

    [Fact]
    public async Task spostamento_su_slot_libero_aggiorna_la_prenotazione()
    {
        await CleanupBookingsAsync();
        using HttpClient client = AuthenticatedClient();
        (string id, string token) = await CreateAsync(client, "10:00");

        var resp = await client.PutAsJsonAsync(
            $"/api/v1/bookings/{id}/reschedule?token={token}", new { date = TestData.FutureMonday.ToString("yyyy-MM-dd"), time = "11:30" });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("11:30", doc.RootElement.GetProperty("time").GetString());

        // Il vecchio slot (10:00) è di nuovo prenotabile.
        var rebook = await client.PostAsync("/api/v1/bookings",
            BookingBody(TestData.ServiceSingleId, TestData.FutureMonday, "10:00", TestData.StaffId));
        Assert.Equal(HttpStatusCode.Created, rebook.StatusCode);
    }

    [Fact]
    public async Task spostamento_su_slot_occupato_fallisce_409()
    {
        await CleanupBookingsAsync();
        using HttpClient client = AuthenticatedClient();
        (string id, string token) = await CreateAsync(client, "10:00");
        await CreateAsync(client, "12:00"); // occupa le 12:00 con lo stesso operatore

        var resp = await client.PutAsJsonAsync(
            $"/api/v1/bookings/{id}/reschedule?token={token}", new { date = TestData.FutureMonday.ToString("yyyy-MM-dd"), time = "12:00" });

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }
}
