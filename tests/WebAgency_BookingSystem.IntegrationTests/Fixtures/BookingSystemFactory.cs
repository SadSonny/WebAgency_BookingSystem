// [INTENT]: WebApplicationFactory<Program> per i test di integrazione. Sovrascrive la connection string
// con quella del container PostgreSQL (Testcontainers) e fornisce le variabili minime richieste dall'app
// (JWT_SECRET, limiti rate), così il build dell'app non lancia al momento della creazione del client.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WebAgency_BookingSystem.IntegrationTests.Fixtures;

public sealed class BookingSystemFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public BookingSystemFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // WHY: DATABASE_URL ha priorità su ConnectionStrings:Database in DependencyInjection.cs.
                ["DATABASE_URL"] = _connectionString,
                // WHY: segreto JWT fisso di test (mai usato in produzione).
                ["JWT_SECRET"] = "integration-test-secret-key-32-chars-min!!",
                // WHY: limiti alti per evitare 429 durante le suite di test che battono molte richieste.
                ["RateLimiting:PermitPerMinute"] = "1000",
                ["RateLimiting:IpPermitPerMinute"] = "2000",
                ["RateLimiting:BookingPerMinute"] = "1000",
                // WHY: email disattivata per default nei test (no-op) → deterministico, nessun tentativo SMTP.
                // Chiave "flat" EMAIL_PROVIDER: EmailSettings la legge prima della sezione Email: di
                // appsettings.Development.json (che imposterebbe Mailpit), quindi ha priorità. Il test
                // dedicato la sovrascrive a "Mailpit" via WithWebHostBuilder.
                ["EMAIL_PROVIDER"] = "None",
            });
        });

        // Sopprimi il logging per non inquinare l'output xUnit con la pipeline Serilog.
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }
}
