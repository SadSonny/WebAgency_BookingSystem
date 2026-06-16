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

        // WHY: i limiti di rate vengono letti da Program.cs in fase di registrazione servizi, dove la config
        // in-memory del factory non è sempre già applicata. Le variabili d'ambiente (lette da CreateBuilder e
        // controllate per prime) sono garantite: le alziamo per non triggerare 429 durante la suite.
        Environment.SetEnvironmentVariable("RATE_LIMIT_PER_MINUTE", "100000");
        Environment.SetEnvironmentVariable("RATE_LIMIT_IP_PER_MINUTE", "100000");
        Environment.SetEnvironmentVariable("RATE_LIMIT_BOOKING_PER_MINUTE", "100000");
        // WHY: l'IP client è null in WebApplicationFactory → tutte le chiamate account condividono una sola
        // partizione. Senza alzare il limite, la suite account triggererebbe 429 spuri.
        Environment.SetEnvironmentVariable("RATE_LIMIT_ACCOUNT_PER_MINUTE", "100000");
        // WHY: i TokenValidationParameters JWT sono costruiti all'avvio da builder.Configuration; il generatore
        // legge il segreto per-richiesta. Per garantire che GENERAZIONE e VALIDAZIONE usino lo STESSO segreto
        // (altrimenti IDX10517 signature failed) lo fissiamo come variabile d'ambiente, letta per prima da
        // entrambi i percorsi, anziché solo nella config in-memory (che il path di validazione all'avvio può
        // non vedere ancora).
        Environment.SetEnvironmentVariable("JWT_SECRET", "integration-test-secret-key-32-chars-min!!");
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
                ["RateLimiting:AccountPerMinute"] = "100000",
                // WHY: email disattivata per default nei test (no-op) → deterministico, nessun tentativo SMTP.
                // Chiave "flat" EMAIL_PROVIDER: EmailSettings la legge prima della sezione Email: di
                // appsettings.Development.json (che imposterebbe Mailpit), quindi ha priorità. Il test
                // dedicato la sovrascrive a "Mailpit" via WithWebHostBuilder.
                ["EMAIL_PROVIDER"] = "None",
                // WHY: il sink dei log su DB non è oggetto dei test; lo disattiviamo per isolare la suite
                // (niente scritture di log nel container né auto-create della tabella logs).
                ["DatabaseLogging:Enabled"] = "false",
            });
        });

        // Sopprimi il logging per non inquinare l'output xUnit con la pipeline Serilog.
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }
}
