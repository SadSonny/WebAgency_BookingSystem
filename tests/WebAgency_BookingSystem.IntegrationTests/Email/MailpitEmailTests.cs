// [INTENT]: Integration smoke test del sottosistema email end-to-end (8.x). Avvia un container Mailpit,
// configura l'API col provider Mailpit puntato al container, crea una prenotazione via HTTP e verifica che
// l'email di conferma venga effettivamente CATTURATA da Mailpit (via la sua HTTP API). Valida così il wiring
// completo: renderer → trasporto SMTP (MailKit) → invio fire-and-forget.

using System.Net;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebAgency_BookingSystem.Core.Abstractions.Services;
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
    public async Task prenotazione_valida_genera_email_di_conferma_catturata_da_mailpit()
    {
        await CleanupBookingsAsync();

        using HttpClient client = MailpitConfiguredClient();
        var body = BookingBody(TestData.ServiceMultiId, TestData.FutureMonday, "10:00");

        var response = await client.PostAsync("/api/v1/bookings", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // WHY: l'invio è fire-and-forget post-commit → l'email arriva poco dopo la risposta. Facciamo polling
        // sulla HTTP API di Mailpit con timeout invece di una sleep fissa (più robusto e veloce).
        JsonElement messages = await PollMessagesAsync(expectedAtLeast: 1);

        bool hasConfirmation = messages.EnumerateArray().Any(m =>
            m.GetProperty("Subject").GetString()!.Contains("Conferma prenotazione")
            && m.GetProperty("To").EnumerateArray().Any(to =>
                to.GetProperty("Address").GetString() == "test@example.it"));

        Assert.True(hasConfirmation, "Mailpit non ha catturato l'email di conferma al cliente.");
    }

    // Client dell'API col provider email sostituito direttamente nei servizi (ConfigureTestServices), puntato
    // al container Mailpit. WHY: sovrascrivere i servizi è più robusto che combattere la precedenza delle
    // sorgenti di configurazione in WebApplicationFactory (l'appsettings dell'ambiente di test vincerebbe).
    private HttpClient MailpitConfiguredClient()
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

        WebApplicationFactory<Program> factory = Fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<EmailSettings>();
                services.RemoveAll<IEmailService>();
                services.AddSingleton(settings);
                services.AddScoped<IEmailService, MailpitEmailService>();
            }));

        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestData.RawApiKey);
        return client;
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
