// [INTENT]: Configurazione del monitor OPS, risolta da IConfiguration con la convenzione del progetto
// (variabile d'ambiente prima, sezione appsettings come fallback — vedi EmailSettings). Risolve il canale
// EFFETTIVO: se è richiesto Telegram ma mancano token o chat id, ripiega su LogOnly (FellBackToLogOnly=true)
// senza far fallire l'avvio (l'alerting non è critico). MinLevel è mappato sull'insieme dei livelli.

using Microsoft.Extensions.Configuration;

namespace WebAgency_BookingSystem.Infrastructure.Observability;

/// <summary>Canale di recapito degli alert selezionabile via configurazione.</summary>
internal enum OpsAlertChannelKind
{
    /// <summary>Solo log applicativo (nessuna notifica esterna). Default e fallback.</summary>
    LogOnly,

    /// <summary>Notifica via Bot API di Telegram.</summary>
    Telegram,
}

/// <summary>Impostazioni immutabili del monitor OPS, costruite una volta all'avvio.</summary>
internal sealed class OpsAlertOptions
{
    private const int DefaultPollSeconds = 60;
    private const int MinPollSeconds = 10;

    public required bool Enabled { get; init; }
    public required OpsAlertChannelKind Channel { get; init; }
    public required bool FellBackToLogOnly { get; init; }
    public required int PollSeconds { get; init; }
    public required string[] Levels { get; init; }
    public required string TelegramBotToken { get; init; }
    public required string TelegramChatId { get; init; }

    /// <summary>Costruisce le impostazioni dalla configurazione, risolvendo il canale effettivo.</summary>
    public static OpsAlertOptions FromConfiguration(IConfiguration configuration)
    {
        bool enabled = configuration.GetValue<bool?>("Ops:Alerting:Enabled") ?? true;

        string channelRaw = Coalesce(configuration["OPS_ALERT_CHANNEL"], configuration["Ops:Alerting:Channel"])
            ?? nameof(OpsAlertChannelKind.LogOnly);
        OpsAlertChannelKind requested = Enum.TryParse(channelRaw, ignoreCase: true, out OpsAlertChannelKind parsed)
            ? parsed
            : OpsAlertChannelKind.LogOnly;

        string token = Coalesce(configuration["OPS_ALERT_TELEGRAM_BOT_TOKEN"], configuration["Ops:Alerting:Telegram:BotToken"]) ?? string.Empty;
        string chatId = Coalesce(configuration["OPS_ALERT_TELEGRAM_CHAT_ID"], configuration["Ops:Alerting:Telegram:ChatId"]) ?? string.Empty;

        // WHY: l'alerting non è critico; un Telegram mal configurato non deve impedire l'avvio. Ripieghiamo su
        // LogOnly e segnaliamo il degrado (loggato dal job all'avvio), invece di lanciare come fa Brevo.
        bool telegramReady = requested == OpsAlertChannelKind.Telegram
            && !string.IsNullOrWhiteSpace(token)
            && !string.IsNullOrWhiteSpace(chatId);
        bool fellBack = requested == OpsAlertChannelKind.Telegram && !telegramReady;
        OpsAlertChannelKind effective = telegramReady ? OpsAlertChannelKind.Telegram : OpsAlertChannelKind.LogOnly;

        int poll = configuration.GetValue<int?>("Ops:Alerting:PollSeconds") ?? DefaultPollSeconds;
        string minLevel = configuration["Ops:Alerting:MinLevel"] ?? "Error";

        return new OpsAlertOptions
        {
            Enabled = enabled,
            Channel = effective,
            FellBackToLogOnly = fellBack,
            PollSeconds = Math.Max(poll, MinPollSeconds),
            Levels = LevelsAtOrAbove(minLevel),
            TelegramBotToken = token,
            TelegramChatId = chatId,
        };
    }

    // WHY: il sink Serilog scrive 'level' come testo ("Warning","Error","Fatal"). Mappiamo il minimo richiesto
    // sull'insieme dei livelli da considerare; default Error (include Fatal).
    private static string[] LevelsAtOrAbove(string minLevel) => minLevel.Trim().ToLowerInvariant() switch
    {
        "fatal" => ["Fatal"],
        "warning" => ["Warning", "Error", "Fatal"],
        _ => ["Error", "Fatal"],
    };

    private static string? Coalesce(string? primary, string? fallback) =>
        !string.IsNullOrWhiteSpace(primary) ? primary
        : !string.IsNullOrWhiteSpace(fallback) ? fallback
        : null;
}
