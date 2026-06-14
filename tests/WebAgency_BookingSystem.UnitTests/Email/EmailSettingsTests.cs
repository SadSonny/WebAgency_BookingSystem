// [INTENT]: Unit test di EmailSettings.FromConfiguration (8.0). Verificano la selezione del provider, la
// precedenza variabile d'ambiente > sezione appsettings, i default SMTP e la validazione fail-fast su Brevo
// (API key / mittente mancanti) che deve impedire un avvio mal configurato in produzione.

using Microsoft.Extensions.Configuration;
using WebAgency_BookingSystem.Infrastructure.Email;

namespace WebAgency_BookingSystem.UnitTests.Email;

public class EmailSettingsTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Default_e_None_se_provider_non_configurato()
    {
        EmailSettings settings = EmailSettings.FromConfiguration(Config([]));

        Assert.Equal(EmailProvider.None, settings.Provider);
        Assert.Equal("localhost", settings.SmtpHost);
        Assert.Equal(1025, settings.SmtpPort);
    }

    [Fact]
    public void Provider_parsato_case_insensitive()
    {
        EmailSettings settings = EmailSettings.FromConfiguration(Config(new() { ["Email:Provider"] = "mailpit" }));

        Assert.Equal(EmailProvider.Mailpit, settings.Provider);
    }

    [Fact]
    public void Variabile_flat_ha_priorita_sulla_sezione()
    {
        EmailSettings settings = EmailSettings.FromConfiguration(Config(new()
        {
            ["EMAIL_PROVIDER"] = "Brevo",
            ["BREVO_API_KEY"] = "xkeysib-flat",
            ["Email:Brevo:ApiKey"] = "from-section",
            ["BREVO_SENDER_EMAIL"] = "noreply@dominio.it",
        }));

        Assert.Equal("xkeysib-flat", settings.BrevoApiKey);
    }

    [Fact]
    public void Brevo_senza_api_key_lancia()
    {
        IConfiguration config = Config(new()
        {
            ["Email:Provider"] = "Brevo",
            ["Email:SenderEmail"] = "noreply@dominio.it",
        });

        Assert.Throws<InvalidOperationException>(() => EmailSettings.FromConfiguration(config));
    }

    [Fact]
    public void Brevo_senza_mittente_lancia()
    {
        IConfiguration config = Config(new()
        {
            ["Email:Provider"] = "Brevo",
            ["Email:Brevo:ApiKey"] = "xkeysib-abc",
        });

        Assert.Throws<InvalidOperationException>(() => EmailSettings.FromConfiguration(config));
    }

    [Fact]
    public void Brevo_completo_non_lancia_e_popola_le_impostazioni()
    {
        EmailSettings settings = EmailSettings.FromConfiguration(Config(new()
        {
            ["Email:Provider"] = "Brevo",
            ["Email:Brevo:ApiKey"] = "xkeysib-abc",
            ["Email:SenderEmail"] = "noreply@dominio.it",
            ["Email:SenderName"] = "Salone",
        }));

        Assert.Equal(EmailProvider.Brevo, settings.Provider);
        Assert.Equal("noreply@dominio.it", settings.SenderEmail);
        Assert.Equal("Salone", settings.SenderName);
    }
}
