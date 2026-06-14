// [INTENT]: Configurazione del sottosistema email, risolta da IConfiguration con la convenzione del progetto
// (variabile d'ambiente prima, sezione appsettings come fallback — vedi JwtSettings). Decide il provider per
// ambiente (AD-10): Mailpit in sviluppo, Brevo in produzione, None → stub no-op. Validazione fail-fast: se il
// provider è Brevo ma mancano API key o mittente, l'avvio fallisce con un messaggio chiaro.

using Microsoft.Extensions.Configuration;

namespace WebAgency_BookingSystem.Infrastructure.Email;

/// <summary>Provider di trasporto email selezionabile via configurazione.</summary>
internal enum EmailProvider
{
    /// <summary>Nessun invio reale: usa lo stub no-op (default sicuro).</summary>
    None,

    /// <summary>SMTP verso Mailpit per lo sviluppo (cattura, non recapita).</summary>
    Mailpit,

    /// <summary>API REST di Brevo per la produzione.</summary>
    Brevo,
}

/// <summary>
/// Impostazioni immutabili del sottosistema email. Costruite una sola volta all'avvio via
/// <see cref="FromConfiguration"/> e registrate come singleton.
/// </summary>
internal sealed class EmailSettings
{
    private const int DefaultSmtpPort = 1025;
    private const string DefaultSenderEmail = "noreply@localhost";
    private const string DefaultSenderName = "BookingSystem";

    public required EmailProvider Provider { get; init; }
    public required string SenderEmail { get; init; }
    public required string SenderName { get; init; }
    public required string SmtpHost { get; init; }
    public required int SmtpPort { get; init; }
    public required string BrevoApiKey { get; init; }

    /// <summary>Costruisce le impostazioni dalla configurazione. Lancia se il provider Brevo è incompleto.</summary>
    public static EmailSettings FromConfiguration(IConfiguration configuration)
    {
        string providerRaw = configuration["EMAIL_PROVIDER"]
            ?? configuration["Email:Provider"]
            ?? nameof(EmailProvider.None);
        EmailProvider provider = Enum.TryParse(providerRaw, ignoreCase: true, out EmailProvider parsed)
            ? parsed
            : EmailProvider.None;

        string senderEmail = Coalesce(configuration["BREVO_SENDER_EMAIL"], configuration["Email:SenderEmail"])
            ?? DefaultSenderEmail;
        string senderName = Coalesce(configuration["BREVO_SENDER_NAME"], configuration["Email:SenderName"])
            ?? DefaultSenderName;
        string smtpHost = Coalesce(configuration["SMTP_HOST"], configuration["Email:Smtp:Host"]) ?? "localhost";
        int smtpPort = int.TryParse(Coalesce(configuration["SMTP_PORT"], configuration["Email:Smtp:Port"]), out int port)
            ? port
            : DefaultSmtpPort;
        string brevoApiKey = Coalesce(configuration["BREVO_API_KEY"], configuration["Email:Brevo:ApiKey"])
            ?? string.Empty;

        // WHY: in produzione Brevo non può funzionare senza API key + mittente verificato → meglio fallire
        // all'avvio con un messaggio chiaro che inviare silenziosamente nulla.
        if (provider == EmailProvider.Brevo)
        {
            if (string.IsNullOrWhiteSpace(brevoApiKey))
            {
                throw new InvalidOperationException(
                    "Provider email Brevo selezionato ma BREVO_API_KEY (o Email:Brevo:ApiKey) è mancante.");
            }

            if (string.IsNullOrWhiteSpace(senderEmail) || senderEmail == DefaultSenderEmail)
            {
                throw new InvalidOperationException(
                    "Provider email Brevo selezionato ma BREVO_SENDER_EMAIL (o Email:SenderEmail) è mancante.");
            }
        }

        return new EmailSettings
        {
            Provider = provider,
            SenderEmail = senderEmail,
            SenderName = senderName,
            SmtpHost = smtpHost,
            SmtpPort = smtpPort,
            BrevoApiKey = brevoApiKey,
        };
    }

    private static string? Coalesce(string? primary, string? fallback) =>
        !string.IsNullOrWhiteSpace(primary) ? primary
        : !string.IsNullOrWhiteSpace(fallback) ? fallback
        : null;
}
