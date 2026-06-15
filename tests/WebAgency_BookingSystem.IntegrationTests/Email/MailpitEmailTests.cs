// [INTENT]: Integration smoke test del sottosistema email end-to-end con OUTBOX (PH-3). Avvia un container
// Mailpit, configura l'API col trasporto Mailpit puntato al container, crea una prenotazione via HTTP (che
// ACCODA l'email nella outbox dentro la transazione), poi esegue il dispatch in modo deterministico e verifica
// che l'email di conferma venga effettivamente CATTURATA da Mailpit. Valida il wiring completo:
// enqueue outbox (in transazione) → dispatcher/processor → trasporto SMTP (MailKit) → Mailpit.

using System.Net;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebAgency_BookingSystem.Infrastructure.Email;
using WebAgency_BookingSystem.IntegrationTests.Fixtures;

namespace WebAgency_BookingSystem.IntegrationTests.Email;

public sealed class MailpitEmailTests : IntegrationTestBase, IAsyncLifetime
{
    private const int SmtpPort = 1025;
    private const int HttpPort = 8025;

    private readonly IContainer _mailpit = new ContainerBuilder("axllent/mailpit:latest")
        .WithExposedPort(SmtpPort)
        .WithExposedPort(HttpPort)
        .WithPortBinding(SmtpPort, assignRandomHostPort: true)
        .WithPortBinding(HttpPort, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(HttpPort).ForPath("/")))
        .Build();

    public MailpitEmailTests(BookingSystemFixture fixture) : base(fixture) { }

    public Task InitializeAsync() => _mailpit.StartAsync();

    public Task DisposeAsync() => _mailpit.DisposeAsync().AsTask();

    [Fact]
    public async Task prenotazione_valida_accoda_e_invia_email_di_conferma_catturata_da_mailpit()
    {
        await CleanupBookingsAsync();

        WebApplicationFactory<Program> factory = MailpitConfiguredFactory();
        using HttpClient client = ApiClient(factory);
        var body = BookingBody(TestData.ServiceMultiId, TestData.FutureMonday, "10:00");

        var response = await client.PostAsync("/api/v1/bookings", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // PH-3: l'email è stata accodata nella outbox dentro la transazione. Eseguiamo il dispatch in modo
        // DETERMINISTICO (chiamando il processor) invece di attendere il timer del BackgroundService.
        int processed = await DispatchOutboxAsync(factory);
        Assert.True(processed > 0, "Il dispatcher non ha trovato email pendenti in outbox.");

        JsonElement messages = await PollMessagesAsync(expectedAtLeast: 1);

        bool hasConfirmation = messages.EnumerateArray().Any(m =>
            m.GetProperty("Subject").GetString()!.Contains("Conferma prenotazione")
            && m.GetProperty("To").EnumerateArray().Any(to =>
                to.GetProperty("Address").GetString() == "test@example.it"));

        Assert.True(hasConfirmation, "Mailpit non ha catturato l'email di conferma al cliente.");
    }

    // Factory dell'API col TRASPORTO email sostituito (ConfigureTestServices) e puntato al container Mailpit.
    // WHY: sovrascrivere i servizi è più robusto che combattere la precedenza delle sorgenti di configurazione.
    private WebApplicationFactory<Program> MailpitConfiguredFactory()
    {
        var settings = new EmailSettings
        {
            Provider = EmailProvider.Mailpit,
            SenderEmail = "noreply@test.local",
            SenderName = "BookingSystem Test",
            // 127.0.0.1 (IPv4): "localhost" può risolvere prima ::1 (IPv6), non pubblicato da Docker.
            SmtpHost = "127.0.0.1",
            SmtpPort = _mailpit.GetMappedPublicPort(SmtpPort),
            BrevoApiKey = string.Empty,
        };

        return Fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<EmailSettings>();
                services.RemoveAll<IEmailSender>();
                services.AddSingleton(settings);
                services.AddScoped<IEmailSender, MailpitEmailSender>();
            }));
    }

    private static HttpClient ApiClient(WebApplicationFactory<Program> factory)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestData.RawApiKey);
        return client;
    }

    private static async Task<int> DispatchOutboxAsync(WebApplicationFactory<Program> factory)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IOutboxEmailProcessor>();
        return await processor.ProcessPendingAsync();
    }

    private async Task<JsonElement> PollMessagesAsync(int expectedAtLeast)
    {
        using var http = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_mailpit.GetMappedPublicPort(HttpPort)}"),
        };

        // Timeout complessivo ~10s: 20 tentativi × 500ms.
        for (int attempt = 0; attempt < 20; attempt++)
        {
            using var doc = JsonDocument.Parse(await http.GetStringAsync("/api/v1/messages"));
            JsonElement messages = doc.RootElement.GetProperty("messages");
            if (messages.GetArrayLength() >= expectedAtLeast)
            {
                return messages.Clone();
            }

            await Task.Delay(500);
        }

        throw new Xunit.Sdk.XunitException("Timeout: Mailpit non ha ricevuto email entro il limite.");
    }
}
