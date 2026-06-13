// [INTENT]: Test di integrazione per il lock advisory PostgreSQL (pg_try_advisory_xact_lock). Due
// richieste simultanee sullo stesso slot con parallelSlots=1: il lock garantisce che una ottenga 201
// e l'altra 409. Usa Task.WhenAll con due HttpClient distinti per la concorrenza reale.

using System.Net;
using WebAgency_BookingSystem.IntegrationTests.Fixtures;

namespace WebAgency_BookingSystem.IntegrationTests.Booking;

public sealed class AdvisoryLockTests : IntegrationTestBase
{
    public AdvisoryLockTests(BookingSystemFixture fixture) : base(fixture) { }

    [Fact]
    public async Task due_richieste_simultanee_stessa_slot_una_201_laltra_409()
    {
        await CleanupBookingsAsync();

        // WHY: due HttpClient distinti sono necessari per la concorrenza HTTP reale.
        // Un solo client non può fare due POST simultanei sullo stesso socket.
        using var client1 = AuthenticatedClient();
        using var client2 = AuthenticatedClient();

        // WHY: creiamo due body separati perché StringContent non è riutilizzabile dopo il primo invio.
        var body1 = BookingBody(TestData.ServiceSingleId, TestData.FutureMonday, "15:00");
        var body2 = BookingBody(TestData.ServiceSingleId, TestData.FutureMonday, "15:00");

        // WHY: Task.WhenAll restituisce HttpResponseMessage[], non una tupla.
        var responses = await Task.WhenAll(
            client1.PostAsync("/api/v1/bookings", body1),
            client2.PostAsync("/api/v1/bookings", body2)
        );

        var statuses = responses.Select(r => (int)r.StatusCode).ToArray();

        // WHY: il lock advisory garantisce esattamente un 201 e un 409 —
        // non è possibile avere due 201 (doppia prenotazione) né due 409 (falso negativo).
        Assert.Contains(201, statuses);
        Assert.Contains(409, statuses);
    }
}
