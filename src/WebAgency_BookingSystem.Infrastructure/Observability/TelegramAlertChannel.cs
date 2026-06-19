// [INTENT]: Canale di alert reale via Bot API di Telegram. Riceve un IHttpClientFactory (client denominato
// "ops-telegram" con BaseAddress https://api.telegram.org/bot<token>/ configurata in DI) e fa POST a sendMessage.
// WHY: usa il factory e crea il client per invio per rispettare la rotazione degli HttpClient pur essendo un
// singleton; un fallimento di recapito è loggato e MAI propagato, così il loop del monitor non si interrompe e
// l'errore originale non viene mascherato.

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Observability;

namespace WebAgency_BookingSystem.Infrastructure.Observability;

internal sealed class TelegramAlertChannel : IOpsAlertChannel
{
    /// <summary>Nome del client HttpClientFactory con BaseAddress della Bot API.</summary>
    public const string HttpClientName = "ops-telegram";

    private readonly IHttpClientFactory _factory;
    private readonly string _chatId;
    private readonly ILogger<TelegramAlertChannel> _logger;

    public TelegramAlertChannel(IHttpClientFactory factory, string chatId, ILogger<TelegramAlertChannel> logger)
    {
        _factory = factory;
        _chatId = chatId;
        _logger = logger;
    }

    public async Task SendAsync(OpsAlert alert, CancellationToken ct = default)
    {
        try
        {
            HttpClient http = _factory.CreateClient(HttpClientName);
            var payload = new { chat_id = _chatId, text = $"[{alert.Kind}] {alert.Title}\n{alert.Detail}" };
            using HttpResponseMessage resp = await http.PostAsJsonAsync("sendMessage", payload, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogError("Invio alert Telegram fallito: HTTP {Status}", (int)resp.StatusCode);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Eccezione nell'invio dell'alert Telegram");
        }
    }
}
