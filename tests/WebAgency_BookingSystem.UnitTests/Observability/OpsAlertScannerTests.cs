// [INTENT]: Unit test del cuore del monitor OPS (OpsAlertScanner.RunOnceAsync) con sorgente/probe/canale fake:
// digest aggregato, campione troncato a 5, nessun alert senza errori, avanzamento del watermark, e transizioni
// DB down/recovery (alert una sola volta per transizione; con DB giù i log NON vengono letti).

using NSubstitute;
using WebAgency_BookingSystem.Core.Observability;
using WebAgency_BookingSystem.Infrastructure.Observability;

namespace WebAgency_BookingSystem.UnitTests.Observability;

public class OpsAlertScannerTests
{
    private static readonly string[] Levels = ["Error", "Fatal"];
    private static readonly DateTimeOffset T0 = new(2026, 6, 18, 10, 0, 0, TimeSpan.Zero);

    private static ILogErrorSource SourceReturning(params LogError[] rows)
    {
        var src = Substitute.For<ILogErrorSource>();
        src.GetSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(rows);
        return src;
    }

    private static IDbHealthProbe ProbeReturning(params bool[] sequence)
    {
        var probe = Substitute.For<IDbHealthProbe>();
        var queue = new Queue<bool>(sequence);
        probe.CanConnectAsync(Arg.Any<CancellationToken>()).Returns(_ => queue.Count > 0 ? queue.Dequeue() : sequence[^1]);
        return probe;
    }

    [Fact]
    public async Task aggrega_errori_in_un_solo_digest()
    {
        var channel = Substitute.For<IOpsAlertChannel>();
        ILogErrorSource src = SourceReturning(
            new LogError(T0.AddSeconds(1), "Error", "boom A"),
            new LogError(T0.AddSeconds(2), "Fatal", "boom B"));
        var sut = new OpsAlertScanner(src, ProbeReturning(true), channel, Levels, T0);

        await sut.RunOnceAsync();

        await channel.Received(1).SendAsync(
            Arg.Is<OpsAlert>(a => a.Kind == OpsAlertKind.ErrorDigest && a.Title.StartsWith("2 ")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task nessun_errore_nessun_alert()
    {
        var channel = Substitute.For<IOpsAlertChannel>();
        var sut = new OpsAlertScanner(SourceReturning(), ProbeReturning(true), channel, Levels, T0);

        await sut.RunOnceAsync();

        await channel.DidNotReceive().SendAsync(Arg.Any<OpsAlert>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task avanza_il_watermark_oltre_l_ultimo_errore()
    {
        var channel = Substitute.For<IOpsAlertChannel>();
        var src = Substitute.For<ILogErrorSource>();
        src.GetSinceAsync(T0, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([new LogError(T0.AddSeconds(5), "Error", "x")]);
        src.GetSinceAsync(T0.AddSeconds(5), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var sut = new OpsAlertScanner(src, ProbeReturning(true, true), channel, Levels, T0);

        await sut.RunOnceAsync();
        await sut.RunOnceAsync();

        // Alla seconda chiamata il watermark deve essere avanzato all'ultimo timestamp visto (T0+5s).
        await src.Received(1).GetSinceAsync(T0.AddSeconds(5), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task campione_troncato_a_cinque_messaggi_distinti()
    {
        var channel = Substitute.For<IOpsAlertChannel>();
        LogError[] rows = Enumerable.Range(0, 8)
            .Select(i => new LogError(T0.AddSeconds(i), "Error", $"msg {i}"))
            .ToArray();
        OpsAlert? captured = null;
        await channel.SendAsync(Arg.Do<OpsAlert>(a => captured = a), Arg.Any<CancellationToken>());
        var sut = new OpsAlertScanner(SourceReturning(rows), ProbeReturning(true), channel, Levels, T0);

        await sut.RunOnceAsync();

        Assert.NotNull(captured);
        Assert.Equal(5, captured!.Detail.Split('\n').Count(line => line.StartsWith('•')));
    }

    [Fact]
    public async Task db_down_alerta_una_sola_volta_e_non_legge_i_log()
    {
        var channel = Substitute.For<IOpsAlertChannel>();
        ILogErrorSource src = SourceReturning();
        var sut = new OpsAlertScanner(src, ProbeReturning(false, false), channel, Levels, T0);

        await sut.RunOnceAsync();
        await sut.RunOnceAsync();

        await channel.Received(1).SendAsync(Arg.Is<OpsAlert>(a => a.Kind == OpsAlertKind.DbDown), Arg.Any<CancellationToken>());
        await src.DidNotReceive().GetSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task db_recovery_alerta_alla_risalita()
    {
        var channel = Substitute.For<IOpsAlertChannel>();
        var sut = new OpsAlertScanner(SourceReturning(), ProbeReturning(false, true), channel, Levels, T0);

        await sut.RunOnceAsync(); // down
        await sut.RunOnceAsync(); // up → recovered

        await channel.Received(1).SendAsync(Arg.Is<OpsAlert>(a => a.Kind == OpsAlertKind.DbRecovered), Arg.Any<CancellationToken>());
    }
}
