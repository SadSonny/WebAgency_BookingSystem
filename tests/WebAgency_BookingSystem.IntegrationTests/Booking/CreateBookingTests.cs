// [INTENT]: Test di integrazione per POST /api/v1/bookings. Verifica i 5 casi principali: prenotazione
// valida → 201, slot pieno (parallelSlots=1) → 409, giorno chiuso → 409, data nel passato → 409,
// JSON malformato → 400. Ogni test pulisce le prenotazioni per evitare interferenze.

using System.Net;
using System.Text;
using System.Text.Json;
using WebAgency_BookingSystem.IntegrationTests.Fixtures;

namespace WebAgency_BookingSystem.IntegrationTests.Booking;

public sealed class CreateBookingTests : IntegrationTestBase
{
    public CreateBookingTests(BookingSystemFixture fixture) : base(fixture) { }

    [Fact]
    public async Task prenotazione_valida_restituisce_201_con_booking_id_e_cancellation_token()
    {
        await CleanupBookingsAsync();
        using var client = AuthenticatedClient();
        var body = BookingBody(TestData.ServiceMultiId, TestData.FutureMonday, "10:00");

        var response = await client.PostAsync("/api/v1/bookings", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(json.TryGetProperty("bookingId", out var bookingId));
        Assert.NotEqual(Guid.Empty, bookingId.GetGuid());
        Assert.True(json.TryGetProperty("cancellationToken", out _));
        Assert.Equal("confirmed", json.GetProperty("status").GetString());
    }

    [Fact]
    public async Task slot_pieno_su_servizio_singolo_restituisce_409()
    {
        await CleanupBookingsAsync();
        using var client = AuthenticatedClient();
        // parallelSlots=1: primo booking OK, secondo sullo stesso slot → 409.
        var body1 = BookingBody(TestData.ServiceSingleId, TestData.FutureMonday, "11:00");
        var body2 = BookingBody(TestData.ServiceSingleId, TestData.FutureMonday, "11:00");

        var first  = await client.PostAsync("/api/v1/bookings", body1);
        var second = await client.PostAsync("/api/v1/bookings", body2);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var json = JsonDocument.Parse(await second.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("slot_unavailable", json.GetProperty("type").GetString());
    }

    [Fact]
    public async Task giorno_chiuso_domenica_restituisce_422_validation_error()
    {
        // WHY: il giorno chiuso non esiste come slot valido → CheckBookingRulesAsync ritorna
        // Error.Validation (non Error.Conflict) → 422, non 409.
        using var client = AuthenticatedClient();
        // ServiceParallel è senza operatori → percorso a parallelSlots: giorno chiuso ⇒ finestra nulla ⇒ 422
        // (validation), distinto dal 409 di capacità. Con un servizio legato a operatori il "tutti chiusi" sarebbe 409.
        var body = BookingBody(TestData.ServiceParallelId, TestData.NextSunday, "10:00");

        var response = await client.PostAsync("/api/v1/bookings", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("validation_error", json.GetProperty("type").GetString());
    }

    [Fact]
    public async Task data_nel_passato_restituisce_422_validation_error()
    {
        // WHY: slot nel passato viola MinAdvanceHours → Error.Validation → 422.
        using var client = AuthenticatedClient();
        var yesterday = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        var body = BookingBody(TestData.ServiceMultiId, yesterday, "10:00");

        var response = await client.PostAsync("/api/v1/bookings", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("validation_error", json.GetProperty("type").GetString());
    }

    [Fact]
    public async Task json_malformato_restituisce_400_bad_request()
    {
        using var client = AuthenticatedClient();
        var malformed = new StringContent("{ questa non è json valida }", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/bookings", malformed);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("bad_request", json.GetProperty("type").GetString());
    }
}
