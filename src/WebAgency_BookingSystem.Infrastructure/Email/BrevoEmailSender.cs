// [INTENT]: Trasporto email per la PRODUZIONE (AD-10): invia tramite l'API REST transazionale di Brevo
// (POST /v3/smtp/email). L'HttpClient è configurato in DI (BaseAddress + header api-key), così questa classe
// compone solo il payload e verifica l'esito. Richiede un mittente verificato lato Brevo. In caso di errore
// HTTP solleva (EnsureSuccessStatusCode): è il dispatcher outbox a gestire il retry.

using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace WebAgency_BookingSystem.Infrastructure.Email;

/// <summary>Invio email via API REST di Brevo (produzione). Mappa <see cref="EmailMessage"/> sul payload v3.</summary>
internal sealed class BrevoEmailSender : IEmailSender
{
    private readonly HttpClient _http;
    private readonly EmailSettings _settings;

    public BrevoEmailSender(HttpClient http, EmailSettings settings)
    {
        _http = http;
        _settings = settings;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var payload = new BrevoEmailPayload(
            Sender: new BrevoContact(_settings.SenderEmail, _settings.SenderName),
            To: [new BrevoContact(message.ToEmail, message.ToName)],
            Subject: message.Subject,
            HtmlContent: message.HtmlBody,
            TextContent: message.TextBody);

        using HttpResponseMessage response = await _http.PostAsJsonAsync("v3/smtp/email", payload, ct);
        response.EnsureSuccessStatusCode();
    }

    // Payload conforme allo schema Brevo v3 (proprietà JSON camelCase via JsonPropertyName).
    private sealed record BrevoEmailPayload(
        [property: JsonPropertyName("sender")] BrevoContact Sender,
        [property: JsonPropertyName("to")] IReadOnlyList<BrevoContact> To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("htmlContent")] string HtmlContent,
        [property: JsonPropertyName("textContent")] string TextContent);

    private sealed record BrevoContact(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("name")] string Name);
}
