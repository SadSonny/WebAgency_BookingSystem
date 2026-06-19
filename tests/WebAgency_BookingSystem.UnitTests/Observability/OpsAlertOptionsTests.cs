// [INTENT]: Unit test di OpsAlertOptions.FromConfiguration: default sicuri, precedenza env > sezione,
// mappatura MinLevel→Levels, e fallback a LogOnly quando Telegram è selezionato senza credenziali.

using Microsoft.Extensions.Configuration;
using WebAgency_BookingSystem.Infrastructure.Observability;

namespace WebAgency_BookingSystem.UnitTests.Observability;

public class OpsAlertOptionsTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Default_abilitato_logonly_poll60_livello_error()
    {
        OpsAlertOptions o = OpsAlertOptions.FromConfiguration(Config([]));

        Assert.True(o.Enabled);
        Assert.Equal(OpsAlertChannelKind.LogOnly, o.Channel);
        Assert.Equal(60, o.PollSeconds);
        Assert.Equal(["Error", "Fatal"], o.Levels);
    }

    [Fact]
    public void MinLevel_warning_include_warning_error_fatal()
    {
        OpsAlertOptions o = OpsAlertOptions.FromConfiguration(Config(new() { ["Ops:Alerting:MinLevel"] = "warning" }));

        Assert.Equal(["Warning", "Error", "Fatal"], o.Levels);
    }

    [Fact]
    public void Telegram_con_credenziali_resta_telegram()
    {
        OpsAlertOptions o = OpsAlertOptions.FromConfiguration(Config(new()
        {
            ["Ops:Alerting:Channel"] = "Telegram",
            ["OPS_ALERT_TELEGRAM_BOT_TOKEN"] = "123:abc",
            ["OPS_ALERT_TELEGRAM_CHAT_ID"] = "999",
        }));

        Assert.Equal(OpsAlertChannelKind.Telegram, o.Channel);
        Assert.False(o.FellBackToLogOnly);
        Assert.Equal("123:abc", o.TelegramBotToken);
        Assert.Equal("999", o.TelegramChatId);
    }

    [Fact]
    public void Telegram_senza_credenziali_fallback_logonly()
    {
        OpsAlertOptions o = OpsAlertOptions.FromConfiguration(Config(new() { ["Ops:Alerting:Channel"] = "Telegram" }));

        Assert.Equal(OpsAlertChannelKind.LogOnly, o.Channel);
        Assert.True(o.FellBackToLogOnly);
    }

    [Fact]
    public void Env_channel_ha_priorita_sulla_sezione()
    {
        OpsAlertOptions o = OpsAlertOptions.FromConfiguration(Config(new()
        {
            ["OPS_ALERT_CHANNEL"] = "LogOnly",
            ["Ops:Alerting:Channel"] = "Telegram",
            ["OPS_ALERT_TELEGRAM_BOT_TOKEN"] = "123:abc",
            ["OPS_ALERT_TELEGRAM_CHAT_ID"] = "999",
        }));

        Assert.Equal(OpsAlertChannelKind.LogOnly, o.Channel);
    }

    [Fact]
    public void PollSeconds_minimo_10()
    {
        OpsAlertOptions o = OpsAlertOptions.FromConfiguration(Config(new() { ["Ops:Alerting:PollSeconds"] = "3" }));

        Assert.Equal(10, o.PollSeconds);
    }
}
