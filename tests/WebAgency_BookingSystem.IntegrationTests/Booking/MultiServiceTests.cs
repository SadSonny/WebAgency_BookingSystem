// [INTENT]: Integration test dell'appuntamento multi-servizio (T1.3): prenotando un servizio principale +
// servizi aggiuntivi, l'appuntamento ha durata TOTALE (somma), è assegnato a UN solo operatore che esegue
// tutti i servizi, e il dettaglio elenca tutti i servizi in ordine. Verifica anche che il blocco continuo
// occupi lo slot (una seconda prenotazione sovrapposta è rifiutata).

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WebAgency_BookingSystem.IntegrationTests.Fixtures;

namespace WebAgency_BookingSystem.IntegrationTests.Booking;

public sealed class MultiServiceTests : IntegrationTestBase
{
    public MultiServiceTests(BookingSystemFixture fixture) : base(fixture) { }

    [Fact]
    public async Task appuntamento_multi_servizio_somma_durata_un_operatore_ed_elenca_servizi()
    {
        await CleanupBookingsAsync();
        using HttpClient client = AuthenticatedClient();

        // Principale ServiceSingle (30') + aggiuntivo ServiceMulti (30') = 60', entrambi eseguiti dall'operatore.
        var payload = new
        {
            serviceId = TestData.ServiceSingleId,
            staffId = (Guid?)null,
            date = TestData.FutureMonday.ToString("yyyy-MM-dd"),
            time = "15:00",
            customer = new { name = "Combo Cliente", phone = "+39 333 0000000", email = "combo@example.it" },
            gdprConsent = true,
            additionalServiceIds = new[] { TestData.ServiceMultiId },
        };

        var create = await client.PostAsJsonAsync("/api/v1/bookings", payload);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var createdDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        string bookingId = createdDoc.RootElement.GetProperty("bookingId").GetString()!;
        string token = createdDoc.RootElement.GetProperty("cancellationToken").GetString()!;

        var detail = await client.GetAsync($"/api/v1/bookings/{bookingId}?token={token}");
        using var doc = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        JsonElement root = doc.RootElement;

        Assert.Equal(60, root.GetProperty("durationMin").GetInt32());
        Assert.Equal(2, root.GetProperty("services").GetArrayLength());
        Assert.Equal(JsonValueKind.Object, root.GetProperty("staff").ValueKind); // operatore assegnato
        Assert.Equal(TestData.StaffId.ToString(), root.GetProperty("staff").GetProperty("id").GetString());
    }

    [Fact]
    public async Task blocco_continuo_occupa_lo_slot_sovrapposto()
    {
        await CleanupBookingsAsync();
        using HttpClient client = AuthenticatedClient();

        // Appuntamento 60' alle 15:00 (15:00–16:00) per l'unico operatore.
        var combo = new
        {
            serviceId = TestData.ServiceSingleId,
            staffId = (Guid?)TestData.StaffId,
            date = TestData.FutureMonday.ToString("yyyy-MM-dd"),
            time = "15:00",
            customer = new { name = "Combo", phone = "+39 333 0000000", email = "combo@example.it" },
            gdprConsent = true,
            additionalServiceIds = new[] { TestData.ServiceMultiId },
        };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/bookings", combo)).StatusCode);

        // Una prenotazione singola alle 15:30 (dentro il blocco 15:00–16:00) dello stesso operatore → 409.
        var overlap = BookingBody(TestData.ServiceSingleId, TestData.FutureMonday, "15:30", TestData.StaffId);
        var resp = await client.PostAsync("/api/v1/bookings", overlap);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }
}
