// [INTENT]: Entry point della CLI di provisioning tenant. Legge e valida un file JSON, costruisce il layer
// Infrastructure (DbContext + interceptor + outbox), esegue il provisioning in transazione e stampa l'output
// con i segreti generati (API key) da mostrare UNA SOLA VOLTA. L'Owner riceve un link di attivazione via email
// (accodato nell'outbox) e imposta la password autonomamente — nessuna password viene generata qui.
// Codici di uscita: 0 successo, 1 errore di runtime (DB/provisioning), 2 errore di input (argomenti/file/validazione).

using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WebAgency_BookingSystem.Infrastructure;
using WebAgency_BookingSystem.Infrastructure.Persistence;
using WebAgency_BookingSystem.TenantProvisioning;

return await Run(args);

static async Task<int> Run(string[] args)
{
    (string? inputPath, string? connection, bool isUpdate) = ParseArgs(args);

    if (isUpdate)
    {
        Console.Error.WriteLine("La modalità --update non è ancora supportata in V1. Usa il provisioning in modalità CREA.");
        return 2;
    }

    if (string.IsNullOrWhiteSpace(inputPath))
    {
        PrintUsage();
        return 2;
    }

    if (!File.Exists(inputPath))
    {
        Console.Error.WriteLine($"File di input non trovato: {inputPath}");
        return 2;
    }

    var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    ProvisioningInput? input;
    try
    {
        string json = await File.ReadAllTextAsync(inputPath);
        input = JsonSerializer.Deserialize<ProvisioningInput>(json, jsonOptions);
    }
    catch (JsonException ex)
    {
        Console.Error.WriteLine($"JSON non valido: {ex.Message}");
        return 2;
    }

    if (input is null)
    {
        Console.Error.WriteLine("Il file JSON è vuoto o non rappresenta un input valido.");
        return 2;
    }

    IReadOnlyList<string> errors = ProvisioningValidator.Validate(input);
    if (errors.Count > 0)
    {
        Console.Error.WriteLine("Validazione fallita:");
        foreach (string error in errors)
        {
            Console.Error.WriteLine($"  - {error}");
        }

        return 2;
    }

    string? resolvedConnection = connection ?? Environment.GetEnvironmentVariable("DATABASE_URL");
    if (string.IsNullOrWhiteSpace(resolvedConnection))
    {
        Console.Error.WriteLine("Connection string mancante: passa --connection \"...\" oppure imposta DATABASE_URL.");
        return 2;
    }

    try
    {
        using IHost host = BuildHost(resolvedConnection);
        using IServiceScope scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();

        Console.WriteLine("=== PROVISIONING TENANT ===");
        Console.WriteLine($"Slug: {input.Slug}");
        Console.WriteLine("Modalità: CREA NUOVO");
        Console.WriteLine();
        Console.WriteLine("✓ Validazione input completata");

        var outbox = scope.ServiceProvider.GetRequiredService<WebAgency_BookingSystem.Infrastructure.Email.IEmailOutbox>();
        var accountSettings = scope.ServiceProvider.GetRequiredService<WebAgency_BookingSystem.Infrastructure.Auth.AccountSettings>();
        var provisioner = new TenantProvisioner(db, outbox, accountSettings);
        ProvisioningResult result = await provisioner.CreateAsync(input, CancellationToken.None);

        PrintResult(result);
        return 0;
    }
    catch (ProvisioningException ex)
    {
        Console.Error.WriteLine($"Provisioning interrotto: {ex.Message}");
        return 1;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Errore durante il provisioning: {ex.Message}");
        return 1;
    }
}

static IHost BuildHost(string connection)
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Database"] = connection,
        ["Account:PublicBaseUrl"] = Environment.GetEnvironmentVariable("PUBLIC_BASE_URL") ?? "http://localhost:5022",
    });
    builder.Services.AddInfrastructure(builder.Configuration);
    return builder.Build();
}

static (string? Input, string? Connection, bool Update) ParseArgs(string[] args)
{
    string? input = null;
    string? connection = null;
    bool update = false;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--input" or "--file" when i + 1 < args.Length:
                input = args[++i];
                break;
            case "--connection" when i + 1 < args.Length:
                connection = args[++i];
                break;
            case "--update":
                update = true;
                break;
            default:
                break;
        }
    }

    return (input, connection, update);
}

static void PrintResult(ProvisioningResult result)
{
    Console.WriteLine($"✓ Tenant creato (id: {result.TenantId})");
    Console.WriteLine($"✓ Servizi creati: {result.ServiceCount}");
    Console.WriteLine($"✓ Staff creato: {result.StaffCount}");
    Console.WriteLine($"✓ Chiusure straordinarie inserite: {result.ClosureCount}");
    Console.WriteLine("✓ API key generata");
    Console.WriteLine("✓ Utente admin (Owner) creato");
    Console.WriteLine();
    Console.WriteLine("=== API KEY (mostrare UNA SOLA VOLTA) ===");
    Console.WriteLine($"Chiave:   {result.ApiKey}");
    Console.WriteLine($"Prefisso: {result.KeyPrefix}");
    Console.WriteLine();
    Console.WriteLine("=== ACCOUNT ADMIN (Owner) ===");
    Console.WriteLine($"Email:    {result.AdminEmail}");
    Console.WriteLine("Attivazione: email con link inviata all'Owner (coda outbox).");
    Console.WriteLine("L'Owner imposta la password dal link; nessuna password viene generata qui.");
    Console.WriteLine();
    Console.WriteLine("Da inserire nel frontend:");
    Console.WriteLine($"  VITE_BOOKING_API_KEY={result.ApiKey}");
    Console.WriteLine("  VITE_BOOKING_API_URL=<url-del-backend>");
    Console.WriteLine();
    Console.WriteLine("⚠  Salva questi segreti: non saranno più visibili.");
    Console.WriteLine();
    Console.WriteLine("=== PROVISIONING COMPLETATO ===");
}

static void PrintUsage()
{
    Console.Error.WriteLine("Uso:");
    Console.Error.WriteLine("  TenantProvisioning --input <file.json> [--connection \"<connstr>\"]");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  --input | --file   Percorso del file JSON di provisioning (obbligatorio).");
    Console.Error.WriteLine("  --connection       Connection string PostgreSQL (oppure variabile DATABASE_URL).");
}
