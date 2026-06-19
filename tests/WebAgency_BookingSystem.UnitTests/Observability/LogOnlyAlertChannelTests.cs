// [INTENT]: Unit test di LogOnlyAlertChannel: l'alert è loggato col marcatore [OPS-ALERT] e il livello di log
// dipende dal Kind (DbRecovered = Warning, gli altri = Error). Usa un ILogger di cattura.

using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Observability;
using WebAgency_BookingSystem.Infrastructure.Observability;

namespace WebAgency_BookingSystem.UnitTests.Observability;

public class LogOnlyAlertChannelTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    [Fact]
    public async Task error_digest_logga_a_livello_error_con_marcatore()
    {
        var logger = new CapturingLogger<LogOnlyAlertChannel>();
        var sut = new LogOnlyAlertChannel(logger);

        await sut.SendAsync(new OpsAlert(OpsAlertKind.ErrorDigest, "3 errori", "dettaglio", DateTimeOffset.UtcNow));

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, logger.Entries[0].Level);
        Assert.Contains("[OPS-ALERT]", logger.Entries[0].Message);
    }

    [Fact]
    public async Task db_recovered_logga_a_livello_warning()
    {
        var logger = new CapturingLogger<LogOnlyAlertChannel>();
        var sut = new LogOnlyAlertChannel(logger);

        await sut.SendAsync(new OpsAlert(OpsAlertKind.DbRecovered, "DB ok", "ripristinato", DateTimeOffset.UtcNow));

        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
    }
}
