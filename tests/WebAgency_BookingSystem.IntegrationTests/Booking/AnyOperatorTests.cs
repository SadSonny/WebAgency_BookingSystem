// [INTENT]: Integration test del modello "qualsiasi operatore" (T1.2): prenotando un servizio legato a un
// operatore SENZA specificare lo staff, il sistema auto-assegna un operatore qualificato libero (la
// prenotazione risulta con uno staff concreto). Verifica anche che la disponibilità "qualsiasi" offra slot.

using System.Net;
using System.Text.Json;
using WebAgency_BookingSystem.IntegrationTests.Fixtures;

namespace WebAgency_BookingSystem.IntegrationTests.Booking;

public sealed class AnyOperatorTests : IntegrationTestBase
{
    public AnyOperatorTests(BookingSystemFixture fixture) : base(fixture) { }

    [Fact]
    public async Task prenotazione_qualsiasi_operatore_auto_assegna_un_operatore()
    {
        await CleanupBookingsAsync();
        using HttpClient client = AuthenticatedClient();

        // ServiceSingle è erogato dall'operatore di test; non passiamo staffId → "qualsiasi".
        var body = BookingBody(TestData.ServiceSingleId, TestData.FutureMonday, "14:00");
        var create = await client.PostAsync("/api/v1/bookings", body);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        string bookingId = created.RootElement.GetProperty("bookingId").GetString()!;
        string token = created.RootElement.GetProperty("cancellationToken").GetString()!;

        // Il dettaglio deve riportare un operatore concreto (auto-assegnato), non null.
        var detail = await client.GetAsync($"/api/v1/bookings/{bookingId}?token={token}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var doc = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());

        JsonElement staff = doc.RootElement.GetProperty("staff");
        Assert.Equal(JsonValueKind.Object, staff.ValueKind);
        Assert.Equal(TestData.StaffId.ToString(), staff.GetProperty("id").GetString());
    }

    [Fact]
    public async Task disponibilita_qualsiasi_operatore_offre_slot()
    {
        await CleanupBookingsAsync();
        using HttpClient client = AuthenticatedClient();
        DateOnly day = TestData.FutureMonday;

        var resp = await client.GetAsync(
            $"/api/v1/availability?serviceId={TestData.ServiceSingleId}&dateFrom={day:yyyy-MM-dd}&dateTo={day:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        JsonElement dayEl = doc.RootElement.EnumerateArray()
            .First(d => d.GetProperty("date").GetString() == day.ToString("yyyy-MM-dd"));
        // Gli slot "qualsiasi" hanno staffId null (l'operatore si sceglie alla prenotazione) e ce n'è almeno uno libero.
        Assert.Contains(dayEl.GetProperty("slots").EnumerateArray(),
            s => s.GetProperty("available").GetBoolean() && s.GetProperty("staffId").ValueKind == JsonValueKind.Null);
    }
}
