// [INTENT]: Test di concorrenza per il lock advisory BLOCCANTE su servizi con parallelSlots > 1 (PH-2).
// Verificano che prenotazioni legittime concorrenti sullo stesso slot NON ricevano un 409 spurio (il lock
// bloccante le accoda invece di rigettarle), ma che la capacità resti comunque rispettata (oltre N posti → 409).
// ServiceMultiId ha parallelSlots = 2.

using System.Net;
using WebAgency_BookingSystem.IntegrationTests.Fixtures;

namespace WebAgency_BookingSystem.IntegrationTests.Booking;

public sealed class ParallelSlotsConcurrencyTests : IntegrationTestBase
{
    public ParallelSlotsConcurrencyTests(BookingSystemFixture fixture) : base(fixture) { }

    [Fact]
    public async Task due_richieste_simultanee_su_slot_capienza_2_entrambe_201()
    {
        await CleanupBookingsAsync();

        using var client1 = AuthenticatedClient();
        using var client2 = AuthenticatedClient();

        var body1 = BookingBody(TestData.ServiceMultiId, TestData.FutureMonday, "16:00");
        var body2 = BookingBody(TestData.ServiceMultiId, TestData.FutureMonday, "16:00");

        var responses = await Task.WhenAll(
            client1.PostAsync("/api/v1/bookings", body1),
            client2.PostAsync("/api/v1/bookings", body2));

        var statuses = responses.Select(r => (int)r.StatusCode).ToArray();

        // WHY: con parallelSlots=2 e slot vuoto, entrambe le prenotazioni sono legittime. Il lock bloccante
        // le serializza ma nessuna deve ricevere un 409 spurio (era il bug R-17 col try+singolo-retry).
        Assert.Equal(2, statuses.Count(s => s == 201));
    }

    [Fact]
    public async Task tre_richieste_simultanee_su_slot_capienza_2_due_201_una_409()
    {
        await CleanupBookingsAsync();

        using var client1 = AuthenticatedClient();
        using var client2 = AuthenticatedClient();
        using var client3 = AuthenticatedClient();

        var body1 = BookingBody(TestData.ServiceMultiId, TestData.FutureMonday, "17:00");
        var body2 = BookingBody(TestData.ServiceMultiId, TestData.FutureMonday, "17:00");
        var body3 = BookingBody(TestData.ServiceMultiId, TestData.FutureMonday, "17:00");

        var responses = await Task.WhenAll(
            client1.PostAsync("/api/v1/bookings", body1),
            client2.PostAsync("/api/v1/bookings", body2),
            client3.PostAsync("/api/v1/bookings", body3));

        var statuses = responses.Select(r => (int)r.StatusCode).ToArray();

        // WHY: capacità = 2 → esattamente due 201 (posti disponibili) e un 409 (capienza esaurita alla
        // ri-verifica sotto lock). Mai tre 201 (over-booking) né tre 409 (falsi negativi).
        Assert.Equal(2, statuses.Count(s => s == 201));
        Assert.Equal(1, statuses.Count(s => s == 409));
    }
}
