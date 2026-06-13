// [INTENT]: Fixture xUnit condivisa tra le classi di test di integrazione (ICollectionFixture). Avvia
// un container PostgreSQL reale via Testcontainers, applica le migrazioni EF e semina i dati fissi.
// Il container dura per tutta la suite e viene distrutto in DisposeAsync.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.IntegrationTests.Fixtures;

public sealed class BookingSystemFixture : IAsyncLifetime
{
    // WHY: il costruttore senza parametri di PostgreSqlBuilder è deprecato dalla 4.x.
    // Il nome dell'immagine si passa ora direttamente al costruttore.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("bookingsystem_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public BookingSystemFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        Factory = new BookingSystemFactory(_postgres.GetConnectionString());

        // Applica le migrazioni EF al DB del container e semina i dati fissi una sola volta.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();
        await db.Database.MigrateAsync();
        await TestData.SeedAsync(db);
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

/// <summary>
/// Definisce la collection xUnit che condivide <see cref="BookingSystemFixture"/> tra le classi di test.
/// </summary>
[CollectionDefinition("Integration")]
public class IntegrationTestGroup : ICollectionFixture<BookingSystemFixture> { }
