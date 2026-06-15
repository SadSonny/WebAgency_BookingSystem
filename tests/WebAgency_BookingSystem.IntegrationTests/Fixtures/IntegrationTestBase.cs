// [INTENT]: Classe base per tutti i test di integrazione. Fornisce un HttpClient autenticato con la
// chiave API di test e il metodo CleanupBookingsAsync per ripristinare lo stato tra test che creano
// prenotazioni, senza dover riavviare il container o ricaricare il seed.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Json;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.IntegrationTests.Fixtures;

[Collection("Integration")]
public abstract class IntegrationTestBase
{
    protected BookingSystemFixture Fixture { get; }

    protected IntegrationTestBase(BookingSystemFixture fixture)
    {
        Fixture = fixture;
    }

    /// <summary>Crea un HttpClient con X-Api-Key già impostata.</summary>
    protected HttpClient AuthenticatedClient()
    {
        var client = Fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestData.RawApiKey);
        return client;
    }

    /// <summary>Crea un corpo JSON per POST /api/v1/bookings.</summary>
    protected static StringContent BookingBody(Guid serviceId, DateOnly date, string time, Guid? staffId = null) =>
        new(JsonSerializer.Serialize(new
        {
            serviceId,
            staffId,
            date = date.ToString("yyyy-MM-dd"),
            time,
            customer = new { name = "Test Cliente", phone = "+391234567890", email = "test@example.it" },
            gdprConsent = true,
        }), Encoding.UTF8, "application/json");

    /// <summary>Elimina prenotazioni e audit log del tenant di test per isolare i test tra loro.</summary>
    protected async Task CleanupBookingsAsync()
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();

        // WHY: TRUNCATE non supporta WHERE; DELETE con filtro tenant_id è più corretto anche se in test
        // esiste un solo tenant. L'ordine (audit prima di bookings) evita FK violations.
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM audit_log WHERE tenant_id = {0}", TestData.TenantId);
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM outbox_email WHERE tenant_id = {0}", TestData.TenantId);
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM bookings WHERE tenant_id = {0}", TestData.TenantId);
    }
}
