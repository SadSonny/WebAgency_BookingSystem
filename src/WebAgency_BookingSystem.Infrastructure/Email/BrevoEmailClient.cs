// [INTENT]: Provider email per la PRODUZIONE (AD-10). Invia tramite l'API REST transazionale di Brevo
// (POST /v3/smtp/email). L'HttpClient è configurato in DI (BaseAddress + header api-key), così questa classe
// si occupa solo di comporre il payload e gestire l'esito. Richiede un mittente verificato lato Brevo.

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace WebAgency_BookingSystem.Infrastructure.Email;

/// <summary>
/// Invio email via API REST di Brevo (produzione). Mappa <see cref="EmailMessage"/> sul payload v3.
/// </summary>
internal sealed class BrevoEmailClient : RenderedEmailService
{
    private readonly HttpClient _http;
    private readonly EmailSettings _settings;

    public BrevoEmailClient(
        HttpClient http, IEmailTemplateRenderer renderer, EmailSettings settings, ILogger<BrevoEmailClient> logger)
        : base(renderer, logger)
    {
        _http = http;
        _settings = settings;
    }

    protected override async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var payload = new BrevoEmailPayload(
            Sender: new BrevoContact(_settings.SenderEmail, _settings.SenderName),
            To: [new BrevoContact(message.ToEmail, message.ToName)],
            Subject: message.Subject,
            HtmlContent: message.HtmlBody,
            TextContent: message.TextBody);

        using HttpResponseMessage response = await _http.PostAsJsonAsync("v3/smtp/email", payload, ct);
        // WHY: in caso di errore HTTP solleviamo, così la base (RenderedEmailService) lo cattura, lo logga e
        // NON fa fallire la prenotazione. Il messaggio dell'eccezione non contiene PII del cliente.
        response.EnsureSuccessStatusCode();
    }

    // Payload conforme allo schema Brevo v3 (proprietà JSON camelCase via JsonPropertyName).
    private sealed record BrevoEmailPayload(
        [property: System.Text.Json.Serialization.JsonPropertyName("sender")] BrevoContact Sender,
        [property: System.Text.Json.Serialization.JsonPropertyName("to")] IReadOnlyList<BrevoContact> To,
        [property: System.Text.Json.Serialization.JsonPropertyName("subject")] string Subject,
        [property: System.Text.Json.Serialization.JsonPropertyName("htmlContent")] string HtmlContent,
        [property: System.Text.Json.Serialization.JsonPropertyName("textContent")] string TextContent);

    private sealed record BrevoContact(
        [property: System.Text.Json.Serialization.JsonPropertyName("email")] string Email,
        [property: System.Text.Json.Serialization.JsonPropertyName("name")] string Name);
}
