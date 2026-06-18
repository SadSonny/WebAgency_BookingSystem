# OPS Observability — Rilevazione + Alerting — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Aggiungere al backend un motore interno che rileva errori applicativi (aggregati dalla tabella `logs`) e il DB irraggiungibile (su transizione), e li notifica via un canale Telegram reale (con fallback LogOnly).

**Architecture:** Un `BackgroundService` (`OpsAlertMonitorJob`) chiama ogni `PollSeconds` un `OpsAlertScanner` stateful (singleton) che: (1) sonda il DB via `IDbHealthProbe`; (2) se su, legge i nuovi errori via `ILogErrorSource` oltre un watermark in-memory, li aggrega in un digest, e invia un `OpsAlert` via `IOpsAlertChannel`. Le tre dipendenze del loop sono astratte → la logica è unit-testabile senza DB né rete. Le implementazioni concrete su DB (`DbLogErrorSource`, `DbHealthProbe`) sono singleton thin che creano uno scope per chiamata.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core 10 + Npgsql (SQL grezzo su tabella `logs` non mappata), `IHttpClientFactory` per Telegram, xUnit + NSubstitute.

## Global Constraints

- .NET target `net10.0`; build con analyzer + **warnings-as-errors** (0 warning).
- Ogni file sorgente inizia con `// [INTENT]: ...`; commenti `// WHY:` sulle logiche non ovvie; XML `/// <summary>` sui membri pubblici.
- DTO/modelli = `record` immutabili; `Guid` PK; `DateTimeOffset` UTC in storage; `async/await` con `CancellationToken` ovunque.
- Errori/log applicativi: messaggi in italiano.
- Contratti condivisi in `WebAgency_BookingSystem.Core` (**public**); implementazioni in `WebAgency_BookingSystem.Infrastructure` (**internal** — `InternalsVisibleTo` verso `UnitTests` già presente, come per `EmailSettings`).
- Nessun segreto in `appsettings*.json`: il bot token Telegram va in env/user-secrets.
- SQL grezzo: nome tabella dalla whitelist `DatabaseLogSettings.Table` (concatenato come fa `LogRetentionJob`), valori sempre **parametrici** (`{0}`,`{1}`) per evitare l'analyzer EF1002 e injection.
- Comandi: build = `dotnet build`; test = `dotnet test tests/WebAgency_BookingSystem.UnitTests/WebAgency_BookingSystem.UnitTests.csproj`. Singolo test: aggiungere `--filter "FullyQualifiedName~<NomeClasse>"`.

---

## File Structure

| File | Progetto | Responsabilità |
|---|---|---|
| `Observability/OpsAlert.cs` | Core | `record OpsAlert` + `enum OpsAlertKind`. |
| `Observability/IOpsAlertChannel.cs` | Core | Contratto canale di notifica. |
| `Observability/ILogErrorSource.cs` | Core | Contratto sorgente errori + `record LogError`. |
| `Observability/IDbHealthProbe.cs` | Core | Contratto self-check connettività DB. |
| `Observability/OpsAlertOptions.cs` | Infrastructure | Binding config `Ops:Alerting` + risoluzione canale effettivo. |
| `Observability/LogOnlyAlertChannel.cs` | Infrastructure | Canale fallback: logga `[OPS-ALERT]`. |
| `Observability/TelegramAlertChannel.cs` | Infrastructure | Canale reale: POST a Bot API; errori swallowed. |
| `Observability/DbLogErrorSource.cs` | Infrastructure | Lettura SQL grezza della tabella `logs`. |
| `Observability/DbHealthProbe.cs` | Infrastructure | `CanConnectAsync` su DbContext via scope. |
| `Observability/OpsAlertScanner.cs` | Infrastructure | Logica per-tick stateful (watermark, transizione DB, digest). |
| `Observability/OpsAlertMonitorJob.cs` | Infrastructure | `BackgroundService` che chiama lo scanner sul timer. |
| `DependencyInjection.cs` | Infrastructure | Nuovo `AddOpsAlerting(...)` invocato da `AddInfrastructure`. |
| `appsettings.json` | Api | Sezione `Ops:Alerting`. |
| `tests/.../Observability/*.cs` | UnitTests | Test di options, canali, scanner. |

---

### Task 1: Contratti Core (modello alert + interfacce)

**Files:**
- Create: `src/WebAgency_BookingSystem.Core/Observability/OpsAlert.cs`
- Create: `src/WebAgency_BookingSystem.Core/Observability/IOpsAlertChannel.cs`
- Create: `src/WebAgency_BookingSystem.Core/Observability/ILogErrorSource.cs`
- Create: `src/WebAgency_BookingSystem.Core/Observability/IDbHealthProbe.cs`

**Interfaces:**
- Produces: `OpsAlert(OpsAlertKind Kind, string Title, string Detail, DateTimeOffset OccurredAt)`; `enum OpsAlertKind { ErrorDigest, DbDown, DbRecovered }`; `IOpsAlertChannel.SendAsync(OpsAlert, CancellationToken)`; `LogError(DateTimeOffset Timestamp, string Level, string Message)`; `ILogErrorSource.GetSinceAsync(DateTimeOffset since, IReadOnlyList<string> levels, CancellationToken)` → `Task<IReadOnlyList<LogError>>`; `IDbHealthProbe.CanConnectAsync(CancellationToken)` → `Task<bool>`.

- [ ] **Step 1: Crea il modello alert**

`src/WebAgency_BookingSystem.Core/Observability/OpsAlert.cs`:
```csharp
// [INTENT]: Modello immutabile di un alert operativo (OPS) prodotto dal monitor interno e recapitato da un
// IOpsAlertChannel. Kind distingue il tipo di evento; Title/Detail sono testo già pronto per la notifica.

namespace WebAgency_BookingSystem.Core.Observability;

/// <summary>Tipo di evento operativo segnalato.</summary>
public enum OpsAlertKind
{
    /// <summary>Riepilogo aggregato di errori applicativi nella finestra di scansione.</summary>
    ErrorDigest,

    /// <summary>Transizione: il database è diventato irraggiungibile.</summary>
    DbDown,

    /// <summary>Transizione: il database è tornato raggiungibile.</summary>
    DbRecovered,
}

/// <summary>Alert operativo pronto per la notifica.</summary>
public sealed record OpsAlert(OpsAlertKind Kind, string Title, string Detail, DateTimeOffset OccurredAt);
```

- [ ] **Step 2: Crea il contratto del canale**

`src/WebAgency_BookingSystem.Core/Observability/IOpsAlertChannel.cs`:
```csharp
// [INTENT]: Astrazione del canale di recapito degli alert operativi. Implementazioni: LogOnly (fallback) e
// Telegram (reale). Disaccoppia il rilevamento (scanner) dal trasporto, sostituibile via DI.

namespace WebAgency_BookingSystem.Core.Observability;

/// <summary>Recapita un alert operativo. Le implementazioni NON devono propagare eccezioni di trasporto.</summary>
public interface IOpsAlertChannel
{
    /// <summary>Invia l'alert sul canale configurato. Un fallimento di trasporto è loggato, non propagato.</summary>
    Task SendAsync(OpsAlert alert, CancellationToken ct = default);
}
```

- [ ] **Step 3: Crea il contratto della sorgente errori**

`src/WebAgency_BookingSystem.Core/Observability/ILogErrorSource.cs`:
```csharp
// [INTENT]: Astrazione della lettura degli errori applicativi dalla tabella dei log. Tenere il job disaccoppiato
// dal DB rende la logica di rilevamento unit-testabile con una sorgente fake.

namespace WebAgency_BookingSystem.Core.Observability;

/// <summary>Una riga di log applicativo rilevante per gli alert.</summary>
public sealed record LogError(DateTimeOffset Timestamp, string Level, string Message);

/// <summary>Fornisce gli errori applicativi registrati dopo un certo istante.</summary>
public interface ILogErrorSource
{
    /// <summary>Restituisce i log con timestamp &gt; <paramref name="since"/> e livello incluso in
    /// <paramref name="levels"/>, ordinati per timestamp crescente.</summary>
    Task<IReadOnlyList<LogError>> GetSinceAsync(DateTimeOffset since, IReadOnlyList<string> levels, CancellationToken ct = default);
}
```

- [ ] **Step 4: Crea il contratto del probe DB**

`src/WebAgency_BookingSystem.Core/Observability/IDbHealthProbe.cs`:
```csharp
// [INTENT]: Astrazione del self-check di connettività al database, usata dal monitor OPS per rilevare le
// transizioni down/recovery. Separata dalla sorgente log per testabilità.

namespace WebAgency_BookingSystem.Core.Observability;

/// <summary>Verifica se il database è raggiungibile in questo momento.</summary>
public interface IDbHealthProbe
{
    /// <summary>True se la connessione al DB riesce; false altrimenti (non lancia).</summary>
    Task<bool> CanConnectAsync(CancellationToken ct = default);
}
```

- [ ] **Step 5: Build**

Run: `dotnet build`
Expected: build verde, 0 warning.

- [ ] **Step 6: Commit**

```bash
git add src/WebAgency_BookingSystem.Core/Observability/
git commit -m "feat(ops): contratti Core per alerting (OpsAlert, canale, sorgente log, probe DB)"
```

---

### Task 2: OpsAlertOptions (configurazione)

**Files:**
- Create: `src/WebAgency_BookingSystem.Infrastructure/Observability/OpsAlertOptions.cs`
- Test: `tests/WebAgency_BookingSystem.UnitTests/Observability/OpsAlertOptionsTests.cs`

**Interfaces:**
- Produces: `enum OpsAlertChannelKind { LogOnly, Telegram }`; `OpsAlertOptions` con `bool Enabled`, `OpsAlertChannelKind Channel` (effettivo, già risolto), `bool FellBackToLogOnly`, `int PollSeconds`, `string[] Levels`, `string TelegramBotToken`, `string TelegramChatId`; static `OpsAlertOptions.FromConfiguration(IConfiguration)`.
- Consumes: nulla.

- [ ] **Step 1: Scrivi i test (falliscono)**

`tests/WebAgency_BookingSystem.UnitTests/Observability/OpsAlertOptionsTests.cs`:
```csharp
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
```

- [ ] **Step 2: Esegui i test (falliscono per tipo mancante)**

Run: `dotnet test tests/WebAgency_BookingSystem.UnitTests/WebAgency_BookingSystem.UnitTests.csproj --filter "FullyQualifiedName~OpsAlertOptionsTests"`
Expected: errore di compilazione (`OpsAlertOptions` non esiste).

- [ ] **Step 3: Implementa OpsAlertOptions**

`src/WebAgency_BookingSystem.Infrastructure/Observability/OpsAlertOptions.cs`:
```csharp
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
```

- [ ] **Step 4: Esegui i test (passano)**

Run: `dotnet test tests/WebAgency_BookingSystem.UnitTests/WebAgency_BookingSystem.UnitTests.csproj --filter "FullyQualifiedName~OpsAlertOptionsTests"`
Expected: PASS (6 test).

- [ ] **Step 5: Commit**

```bash
git add src/WebAgency_BookingSystem.Infrastructure/Observability/OpsAlertOptions.cs tests/WebAgency_BookingSystem.UnitTests/Observability/OpsAlertOptionsTests.cs
git commit -m "feat(ops): OpsAlertOptions con risoluzione canale e fallback LogOnly (+test)"
```

---

### Task 3: LogOnlyAlertChannel

**Files:**
- Create: `src/WebAgency_BookingSystem.Infrastructure/Observability/LogOnlyAlertChannel.cs`
- Test: `tests/WebAgency_BookingSystem.UnitTests/Observability/LogOnlyAlertChannelTests.cs`

**Interfaces:**
- Consumes: `IOpsAlertChannel`, `OpsAlert`, `OpsAlertKind` (Task 1).
- Produces: `internal sealed class LogOnlyAlertChannel : IOpsAlertChannel`.

- [ ] **Step 1: Scrivi il test (fallisce)**

`tests/WebAgency_BookingSystem.UnitTests/Observability/LogOnlyAlertChannelTests.cs`:
```csharp
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
```

- [ ] **Step 2: Esegui il test (fallisce)**

Run: `dotnet test tests/WebAgency_BookingSystem.UnitTests/WebAgency_BookingSystem.UnitTests.csproj --filter "FullyQualifiedName~LogOnlyAlertChannelTests"`
Expected: errore di compilazione (`LogOnlyAlertChannel` non esiste).

- [ ] **Step 3: Implementa LogOnlyAlertChannel**

`src/WebAgency_BookingSystem.Infrastructure/Observability/LogOnlyAlertChannel.cs`:
```csharp
// [INTENT]: Canale di alert di fallback (e default in dev/test): non notifica nessun servizio esterno, ma scrive
// l'alert con il marcatore [OPS-ALERT] sui sink Serilog (console + DB). Resta visibile su Railway anche quando il
// DB è giù (il sink console non dipende dal DB). È il punto di aggancio per un futuro canale reale.

using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Observability;

namespace WebAgency_BookingSystem.Infrastructure.Observability;

internal sealed class LogOnlyAlertChannel : IOpsAlertChannel
{
    private readonly ILogger<LogOnlyAlertChannel> _logger;

    public LogOnlyAlertChannel(ILogger<LogOnlyAlertChannel> logger) => _logger = logger;

    public Task SendAsync(OpsAlert alert, CancellationToken ct = default)
    {
        // WHY: DbRecovered è una buona notizia (Warning); ErrorDigest/DbDown sono problemi attivi (Error).
        LogLevel level = alert.Kind == OpsAlertKind.DbRecovered ? LogLevel.Warning : LogLevel.Error;
        _logger.Log(level, "[OPS-ALERT] {Kind}: {Title} — {Detail}", alert.Kind, alert.Title, alert.Detail);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Esegui il test (passa)**

Run: `dotnet test tests/WebAgency_BookingSystem.UnitTests/WebAgency_BookingSystem.UnitTests.csproj --filter "FullyQualifiedName~LogOnlyAlertChannelTests"`
Expected: PASS (2 test).

- [ ] **Step 5: Commit**

```bash
git add src/WebAgency_BookingSystem.Infrastructure/Observability/LogOnlyAlertChannel.cs tests/WebAgency_BookingSystem.UnitTests/Observability/LogOnlyAlertChannelTests.cs
git commit -m "feat(ops): LogOnlyAlertChannel (+test livelli di log)"
```

---

### Task 4: TelegramAlertChannel

**Files:**
- Create: `src/WebAgency_BookingSystem.Infrastructure/Observability/TelegramAlertChannel.cs`
- Test: `tests/WebAgency_BookingSystem.UnitTests/Observability/TelegramAlertChannelTests.cs`

**Interfaces:**
- Consumes: `IOpsAlertChannel`, `OpsAlert` (Task 1). Usa `IHttpClientFactory` con client denominato `"ops-telegram"` (BaseAddress configurata in DI, Task 7).
- Produces: `internal sealed class TelegramAlertChannel : IOpsAlertChannel` con ctor `(IHttpClientFactory factory, string chatId, ILogger<TelegramAlertChannel> logger)`. Costante pubblica interna `TelegramAlertChannel.HttpClientName = "ops-telegram"`.

- [ ] **Step 1: Scrivi i test (falliscono)**

`tests/WebAgency_BookingSystem.UnitTests/Observability/TelegramAlertChannelTests.cs`:
```csharp
// [INTENT]: Unit test di TelegramAlertChannel con un HttpMessageHandler stub: verifica che la richiesta vada su
// "sendMessage" col chat_id corretto, e che un errore HTTP o un'eccezione di rete NON propaghino (swallowed).

using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WebAgency_BookingSystem.Core.Observability;
using WebAgency_BookingSystem.Infrastructure.Observability;

namespace WebAgency_BookingSystem.UnitTests.Observability;

public class TelegramAlertChannelTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }

    private static IHttpClientFactory FactoryFor(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org/bot123:abc/") };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(TelegramAlertChannel.HttpClientName).Returns(http);
        return factory;
    }

    private static OpsAlert SampleAlert() =>
        new(OpsAlertKind.ErrorDigest, "2 errori", "dettaglio", DateTimeOffset.UtcNow);

    [Fact]
    public async Task invia_su_sendMessage_con_chat_id()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = new TelegramAlertChannel(FactoryFor(handler), "999", NullLogger<TelegramAlertChannel>.Instance);

        await sut.SendAsync(SampleAlert());

        Assert.EndsWith("sendMessage", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"chat_id\":\"999\"", handler.LastBody);
        Assert.Contains("2 errori", handler.LastBody);
    }

    [Fact]
    public async Task errore_http_non_propaga()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = new TelegramAlertChannel(FactoryFor(handler), "999", NullLogger<TelegramAlertChannel>.Instance);

        // Non deve lanciare.
        await sut.SendAsync(SampleAlert());
    }

    [Fact]
    public async Task eccezione_di_rete_non_propaga()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("network down"));
        var sut = new TelegramAlertChannel(FactoryFor(handler), "999", NullLogger<TelegramAlertChannel>.Instance);

        await sut.SendAsync(SampleAlert());
    }
}
```

- [ ] **Step 2: Esegui i test (falliscono)**

Run: `dotnet test tests/WebAgency_BookingSystem.UnitTests/WebAgency_BookingSystem.UnitTests.csproj --filter "FullyQualifiedName~TelegramAlertChannelTests"`
Expected: errore di compilazione (`TelegramAlertChannel` non esiste).

- [ ] **Step 3: Implementa TelegramAlertChannel**

`src/WebAgency_BookingSystem.Infrastructure/Observability/TelegramAlertChannel.cs`:
```csharp
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
```

- [ ] **Step 4: Esegui i test (passano)**

Run: `dotnet test tests/WebAgency_BookingSystem.UnitTests/WebAgency_BookingSystem.UnitTests.csproj --filter "FullyQualifiedName~TelegramAlertChannelTests"`
Expected: PASS (3 test).

- [ ] **Step 5: Commit**

```bash
git add src/WebAgency_BookingSystem.Infrastructure/Observability/TelegramAlertChannel.cs tests/WebAgency_BookingSystem.UnitTests/Observability/TelegramAlertChannelTests.cs
git commit -m "feat(ops): TelegramAlertChannel via IHttpClientFactory (+test, errori swallowed)"
```

---

### Task 5: OpsAlertScanner (logica per-tick) — il cuore

**Files:**
- Create: `src/WebAgency_BookingSystem.Infrastructure/Observability/OpsAlertScanner.cs`
- Test: `tests/WebAgency_BookingSystem.UnitTests/Observability/OpsAlertScannerTests.cs`

**Interfaces:**
- Consumes: `ILogErrorSource`, `IDbHealthProbe`, `IOpsAlertChannel`, `OpsAlert`, `OpsAlertKind`, `LogError` (Task 1).
- Produces: `internal sealed class OpsAlertScanner` con ctor `(ILogErrorSource logErrors, IDbHealthProbe dbHealth, IOpsAlertChannel channel, string[] levels, DateTimeOffset startedAt)` e metodo `Task RunOnceAsync(CancellationToken ct = default)`. Stateful (watermark + flag DB) → registrato singleton.

- [ ] **Step 1: Scrivi i test (falliscono)**

`tests/WebAgency_BookingSystem.UnitTests/Observability/OpsAlertScannerTests.cs`:
```csharp
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
        src.GetSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([new LogError(T0.AddSeconds(5), "Error", "x")]);
        var sut = new OpsAlertScanner(src, ProbeReturning(true), channel, Levels, T0);

        await sut.RunOnceAsync();

        // Alla seconda chiamata il watermark deve essere avanzato all'ultimo timestamp visto (T0+5s).
        await src.Received().GetSinceAsync(T0.AddSeconds(5), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
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
        Assert.Equal(5, captured!.Detail.Split('\n').Count(line => line.StartsWith("•")));
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
```

- [ ] **Step 2: Esegui i test (falliscono)**

Run: `dotnet test tests/WebAgency_BookingSystem.UnitTests/WebAgency_BookingSystem.UnitTests.csproj --filter "FullyQualifiedName~OpsAlertScannerTests"`
Expected: errore di compilazione (`OpsAlertScanner` non esiste).

- [ ] **Step 3: Implementa OpsAlertScanner**

`src/WebAgency_BookingSystem.Infrastructure/Observability/OpsAlertScanner.cs`:
```csharp
// [INTENT]: Cuore del monitor OPS: una singola scansione (RunOnceAsync) stateful. Sonda il DB; se giù segnala la
// transizione (una sola volta) e si ferma (non può leggere la tabella logs); se su, ripristina lo stato e legge i
// nuovi errori oltre il watermark, li aggrega in un unico ErrorDigest e li invia. È singleton (mantiene watermark
// e flag DB tra i tick); il BackgroundService si limita a chiamarlo sul timer.

using WebAgency_BookingSystem.Core.Observability;

namespace WebAgency_BookingSystem.Infrastructure.Observability;

internal sealed class OpsAlertScanner
{
    private const int SampleSize = 5;
    private const int MaxMessageLength = 200;

    private readonly ILogErrorSource _logErrors;
    private readonly IDbHealthProbe _dbHealth;
    private readonly IOpsAlertChannel _channel;
    private readonly string[] _levels;

    private DateTimeOffset _watermark;
    private bool _dbWasDown;

    public OpsAlertScanner(
        ILogErrorSource logErrors,
        IDbHealthProbe dbHealth,
        IOpsAlertChannel channel,
        string[] levels,
        DateTimeOffset startedAt)
    {
        _logErrors = logErrors;
        _dbHealth = dbHealth;
        _channel = channel;
        _levels = levels;
        _watermark = startedAt; // WHY: si parte da "ora": al riavvio non si rialerta lo storico.
    }

    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        bool up = await _dbHealth.CanConnectAsync(ct);
        if (!up)
        {
            if (!_dbWasDown)
            {
                _dbWasDown = true;
                await _channel.SendAsync(new OpsAlert(
                    OpsAlertKind.DbDown,
                    "Database irraggiungibile",
                    "Il self-check di connettività al database è fallito.",
                    DateTimeOffset.UtcNow), ct);
            }

            return; // WHY: con il DB giù non si può leggere la tabella logs; ricontrolleremo al prossimo tick.
        }

        if (_dbWasDown)
        {
            _dbWasDown = false;
            await _channel.SendAsync(new OpsAlert(
                OpsAlertKind.DbRecovered,
                "Database di nuovo raggiungibile",
                "La connessione al database è stata ripristinata.",
                DateTimeOffset.UtcNow), ct);
        }

        IReadOnlyList<LogError> errors = await _logErrors.GetSinceAsync(_watermark, _levels, ct);
        if (errors.Count == 0)
        {
            return;
        }

        DateTimeOffset previous = _watermark;
        _watermark = errors.Max(e => e.Timestamp);

        string sample = string.Join("\n", errors
            .Select(e => e.Message)
            .Distinct()
            .Take(SampleSize)
            .Select(m => "• " + Truncate(m, MaxMessageLength)));

        await _channel.SendAsync(new OpsAlert(
            OpsAlertKind.ErrorDigest,
            $"{errors.Count} errori applicativi",
            $"Dal {previous:o}:\n{sample}",
            DateTimeOffset.UtcNow), ct);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : string.Concat(value.AsSpan(0, max), "…");
}
```

- [ ] **Step 4: Esegui i test (passano)**

Run: `dotnet test tests/WebAgency_BookingSystem.UnitTests/WebAgency_BookingSystem.UnitTests.csproj --filter "FullyQualifiedName~OpsAlertScannerTests"`
Expected: PASS (6 test).

- [ ] **Step 5: Commit**

```bash
git add src/WebAgency_BookingSystem.Infrastructure/Observability/OpsAlertScanner.cs tests/WebAgency_BookingSystem.UnitTests/Observability/OpsAlertScannerTests.cs
git commit -m "feat(ops): OpsAlertScanner — digest, watermark, transizioni DB (+test)"
```

---

### Task 6: Implementazioni DB (DbLogErrorSource + DbHealthProbe)

**Files:**
- Create: `src/WebAgency_BookingSystem.Infrastructure/Observability/DbLogErrorSource.cs`
- Create: `src/WebAgency_BookingSystem.Infrastructure/Observability/DbHealthProbe.cs`

**Interfaces:**
- Consumes: `ILogErrorSource`, `LogError`, `IDbHealthProbe` (Task 1), `BookingSystemDbContext`, `DatabaseLogSettings` (esistente in `WebAgency_BookingSystem.Api.Logging` — NB: vive nel progetto Api). Vedi nota sotto.
- Produces: `internal sealed class DbLogErrorSource : ILogErrorSource`, `internal sealed class DbHealthProbe : IDbHealthProbe`. Entrambi singleton che creano uno scope per chiamata via `IServiceScopeFactory`.

> **Nota dipendenza (importante):** `DatabaseLogSettings` è in `WebAgency_BookingSystem.Api.Logging`, ma `DbLogErrorSource` sta in Infrastructure (che NON referenzia Api). Per evitare un riferimento circolare, `DbLogErrorSource` NON usa `DatabaseLogSettings`: riceve il **nome tabella già validato** come parametro (`string logTable`) passato in fase di DI (Task 7), dove `DatabaseLogSettings.FromConfiguration(...)` è disponibile nel progetto Api. Il nome resta una whitelist validata a monte.
>
> **Verifica:** questo task non ha un test automatico (lettura SQL grezza su tabella creata a runtime dal sink Serilog, non da migration). È coperto da: (a) build verde; (b) smoke test runtime nel Task 7. Questa scelta è coerente con lo spec (§5), che mette la logica testabile dietro `ILogErrorSource`.

- [ ] **Step 1: Implementa DbHealthProbe**

`src/WebAgency_BookingSystem.Infrastructure/Observability/DbHealthProbe.cs`:
```csharp
// [INTENT]: Implementazione di IDbHealthProbe: verifica la connettività al database con CanConnectAsync. Singleton
// che apre uno scope per chiamata (il DbContext è scoped). Non lancia: in caso di errore restituisce false.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebAgency_BookingSystem.Core.Observability;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Observability;

internal sealed class DbHealthProbe : IDbHealthProbe
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DbHealthProbe(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task<bool> CanConnectAsync(CancellationToken ct = default)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            BookingSystemDbContext db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();
            return await db.Database.CanConnectAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // WHY: un'eccezione di connessione significa "DB non raggiungibile", non un errore da propagare.
            return false;
        }
    }
}
```

- [ ] **Step 2: Implementa DbLogErrorSource**

`src/WebAgency_BookingSystem.Infrastructure/Observability/DbLogErrorSource.cs`:
```csharp
// [INTENT]: Implementazione di ILogErrorSource: legge gli errori dalla tabella dei log applicativi (sink Serilog,
// NON mappata in EF) via SQL grezzo. Singleton che apre uno scope per chiamata. Il nome tabella è una whitelist
// validata, iniettata in DI (vedi DependencyInjection); i valori (watermark, livelli) sono sempre parametrici.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebAgency_BookingSystem.Core.Observability;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Observability;

internal sealed class DbLogErrorSource : ILogErrorSource
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _logTable;

    public DbLogErrorSource(IServiceScopeFactory scopeFactory, string logTable)
    {
        _scopeFactory = scopeFactory;
        _logTable = logTable;
    }

    public async Task<IReadOnlyList<LogError>> GetSinceAsync(
        DateTimeOffset since, IReadOnlyList<string> levels, CancellationToken ct = default)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        BookingSystemDbContext db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();

        // WHY: il nome tabella è una whitelist validata (DatabaseLogSettings) → concatenarlo come identificatore è
        // sicuro; i valori restano parametrici ({0} watermark, {1} array di livelli con Postgres ANY). Stringa non
        // interpolata per non incorrere nell'analyzer EF1002.
        string sql =
            "SELECT \"timestamp\" AS \"Timestamp\", \"level\" AS \"Level\", \"message\" AS \"Message\" FROM "
            + _logTable
            + " WHERE \"timestamp\" > {0} AND \"level\" = ANY({1}) ORDER BY \"timestamp\"";

        List<LogError> rows = await db.Database
            .SqlQueryRaw<LogError>(sql, since, levels.ToArray())
            .ToListAsync(ct);

        return rows;
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: build verde, 0 warning. (Se `SqlQueryRaw<LogError>` non mappa per nome, verificare che gli alias di colonna `"Timestamp"/"Level"/"Message"` corrispondano esattamente ai nomi del record `LogError`.)

- [ ] **Step 4: Commit**

```bash
git add src/WebAgency_BookingSystem.Infrastructure/Observability/DbHealthProbe.cs src/WebAgency_BookingSystem.Infrastructure/Observability/DbLogErrorSource.cs
git commit -m "feat(ops): DbHealthProbe + DbLogErrorSource (lettura SQL grezza tabella logs)"
```

---

### Task 7: BackgroundService + wiring DI + config + documentazione

**Files:**
- Create: `src/WebAgency_BookingSystem.Infrastructure/Observability/OpsAlertMonitorJob.cs`
- Modify: `src/WebAgency_BookingSystem.Infrastructure/DependencyInjection.cs` (aggiungi `AddOpsAlerting` + chiamata in `AddInfrastructure`)
- Modify: `src/WebAgency_BookingSystem.Api/appsettings.json` (sezione `Ops:Alerting`)
- Modify: `CLAUDE.md`, `Claude_Instructions/VISIONE_PRODOTTO_E_ROADMAP.md`, `Claude_Instructions/DEVELOPMENT_PLAN.md`

**Interfaces:**
- Consumes: `OpsAlertScanner` (Task 5), `OpsAlertOptions` (Task 2), tutti i canali/sorgenti (Task 3-6), `DatabaseLogSettings` (Api, per il nome tabella). `EmailOutboxDispatcher` registration pattern (riferimento `DependencyInjection.cs:147-152`).
- Produces: `internal sealed class OpsAlertMonitorJob : BackgroundService`; `AddOpsAlerting(IServiceCollection, IConfiguration)`.

- [ ] **Step 1: Implementa OpsAlertMonitorJob**

`src/WebAgency_BookingSystem.Infrastructure/Observability/OpsAlertMonitorJob.cs`:
```csharp
// [INTENT]: BackgroundService che, ogni PollSeconds, invoca OpsAlertScanner.RunOnceAsync. Si occupa solo dello
// scheduling: la logica (watermark, transizioni, digest) è tutta nello scanner (testabile). Un errore di un ciclo
// non interrompe il job: si logga e si ritenta al tick successivo. All'avvio, se il canale Telegram è stato
// richiesto ma è ripiegato su LogOnly per credenziali mancanti, emette un warning una sola volta.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WebAgency_BookingSystem.Infrastructure.Observability;

internal sealed class OpsAlertMonitorJob : BackgroundService
{
    private readonly OpsAlertScanner _scanner;
    private readonly ILogger<OpsAlertMonitorJob> _logger;
    private readonly TimeSpan _interval;
    private readonly bool _fellBackToLogOnly;

    public OpsAlertMonitorJob(OpsAlertScanner scanner, ILogger<OpsAlertMonitorJob> logger, TimeSpan interval, bool fellBackToLogOnly)
    {
        _scanner = scanner;
        _logger = logger;
        _interval = interval;
        _fellBackToLogOnly = fellBackToLogOnly;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_fellBackToLogOnly)
        {
            _logger.LogWarning(
                "Canale alert Telegram richiesto ma credenziali mancanti (token/chat id): uso il canale LogOnly.");
        }

        using var timer = new PeriodicTimer(_interval);
        do
        {
            try
            {
                await _scanner.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nel ciclo del monitor OPS");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
```

- [ ] **Step 2: Aggiungi AddOpsAlerting in DependencyInjection.cs**

In `src/WebAgency_BookingSystem.Infrastructure/DependencyInjection.cs`, dentro `AddInfrastructure` (dopo `AddEmail(services, configuration);`), aggiungi la chiamata:
```csharp
        AddOpsAlerting(services, configuration);
```

In fondo alla classe (dopo il metodo `AddEmail`), aggiungi:
```csharp
    // WHY (4.2): monitor OPS. Lo scanner è singleton (mantiene watermark/flag tra i tick); sorgente log e probe DB
    // sono singleton che aprono uno scope per chiamata. Il canale è scelto una sola volta da OpsAlertOptions
    // (Telegram se configurato, altrimenti LogOnly). Se Enabled=false non si registra nulla.
    private static void AddOpsAlerting(IServiceCollection services, IConfiguration configuration)
    {
        OpsAlertOptions options = OpsAlertOptions.FromConfiguration(configuration);
        if (!options.Enabled)
        {
            return;
        }

        // Nome tabella log validato: vive nel progetto Api (DatabaseLogSettings). Lo leggiamo qui dalla config con
        // lo stesso default/whitelist per non creare un riferimento Infrastructure → Api.
        string logTable = configuration["DatabaseLogging:Table"] is { Length: > 0 } t && IsSafeIdentifier(t)
            ? t
            : "logs";

        services.AddSingleton<ILogErrorSource>(sp => new DbLogErrorSource(
            sp.GetRequiredService<IServiceScopeFactory>(), logTable));
        services.AddSingleton<IDbHealthProbe, DbHealthProbe>();

        if (options.Channel == OpsAlertChannelKind.Telegram)
        {
            services.AddHttpClient(TelegramAlertChannel.HttpClientName, client =>
                client.BaseAddress = new Uri($"https://api.telegram.org/bot{options.TelegramBotToken}/"));
            services.AddSingleton<IOpsAlertChannel>(sp => new TelegramAlertChannel(
                sp.GetRequiredService<IHttpClientFactory>(),
                options.TelegramChatId,
                sp.GetRequiredService<ILogger<TelegramAlertChannel>>()));
        }
        else
        {
            services.AddSingleton<IOpsAlertChannel, LogOnlyAlertChannel>();
        }

        services.AddSingleton(sp => new OpsAlertScanner(
            sp.GetRequiredService<ILogErrorSource>(),
            sp.GetRequiredService<IDbHealthProbe>(),
            sp.GetRequiredService<IOpsAlertChannel>(),
            options.Levels,
            DateTimeOffset.UtcNow));

        TimeSpan interval = TimeSpan.FromSeconds(options.PollSeconds);
        services.AddHostedService(sp => new OpsAlertMonitorJob(
            sp.GetRequiredService<OpsAlertScanner>(),
            sp.GetRequiredService<ILogger<OpsAlertMonitorJob>>(),
            interval,
            options.FellBackToLogOnly));
    }

    // WHY: difesa in profondità sul nome tabella (già whitelist altrove). Identificatore Postgres semplice.
    private static bool IsSafeIdentifier(string value) =>
        value.All(c => char.IsLetterOrDigit(c) || c == '_');
```

> Verifica gli `using` in testa al file `DependencyInjection.cs`: servono `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, e `using WebAgency_BookingSystem.Core.Observability;` + `using WebAgency_BookingSystem.Infrastructure.Observability;`. Aggiungi quelli mancanti.

- [ ] **Step 3: Aggiungi la sezione di config in appsettings.json**

In `src/WebAgency_BookingSystem.Api/appsettings.json`, aggiungi al livello radice (accanto a `DatabaseLogging`):
```json
  "Ops": {
    "Alerting": {
      "Enabled": true,
      "Channel": "LogOnly",
      "PollSeconds": 60,
      "MinLevel": "Error"
    }
  },
```

- [ ] **Step 4: Build + intera suite di test**

Run: `dotnet build`
Expected: build verde, 0 warning.

Run: `dotnet test tests/WebAgency_BookingSystem.UnitTests/WebAgency_BookingSystem.UnitTests.csproj`
Expected: tutti verdi (i 17 nuovi test + i preesistenti).

- [ ] **Step 5: Smoke test runtime (manuale, opzionale ma raccomandato)**

Con Docker su e `Channel=LogOnly` (default): avvia l'API, provoca un log `Error` (es. una richiesta che fa fallire qualcosa, o abbassa temporaneamente il DB), e verifica sui log console la riga `[OPS-ALERT] ErrorDigest...` entro ~60s. Per il DB-down: ferma il container Postgres e verifica `[OPS-ALERT] DbDown` su console; riavvialo e verifica `DbRecovered`.

- [ ] **Step 6: Aggiorna la documentazione**

In `CLAUDE.md`:
- Nella tabella **Variabili d'Ambiente**, aggiungi:
  - `OPS_ALERT_CHANNEL` — canale alert OPS (`LogOnly` default, `Telegram`).
  - `OPS_ALERT_TELEGRAM_BOT_TOKEN` — **segreto** bot Telegram (dev: user-secrets; prod: env).
  - `OPS_ALERT_TELEGRAM_CHAT_ID` — id chat/canale destinatario.
- Nella sezione **Logging**, aggiungi un paragrafo: *Monitor OPS (4.2): `OpsAlertMonitorJob` scansiona ogni `PollSeconds` (default 60) la tabella `logs` per errori `>= MinLevel` e rileva il DB-down su transizione, recapitando un `OpsAlert` via `IOpsAlertChannel` (Telegram se configurato, altrimenti LogOnly = riga `[OPS-ALERT]` su console/DB). Assunzione: singola istanza (watermark/stato in-memory). Uptime monitor esterno → `GET /api/v1/health`.*
- Aggiorna lo **stato del progetto** e "Prossimo task".

In `Claude_Instructions/VISIONE_PRODOTTO_E_ROADMAP.md` §4.2: spunta "Health check reale" (già esistente) + "alerting" (rilevazione + canale Telegram); marca come **next** l'alert su `OutboxEmail.Status == Failed` e l'eventuale canale email.

In `Claude_Instructions/DEVELOPMENT_PLAN.md`: aggiungi una voce al Changelog (data 2026-06-18) descrivendo il monitor OPS e i test.

- [ ] **Step 7: Commit finale**

```bash
git add src/WebAgency_BookingSystem.Infrastructure/Observability/OpsAlertMonitorJob.cs src/WebAgency_BookingSystem.Infrastructure/DependencyInjection.cs src/WebAgency_BookingSystem.Api/appsettings.json CLAUDE.md Claude_Instructions/VISIONE_PRODOTTO_E_ROADMAP.md Claude_Instructions/DEVELOPMENT_PLAN.md
git commit -m "feat(ops): monitor OPS (BackgroundService + DI + config) e documentazione 4.2"
```

---

## Self-Review (compilato in fase di stesura)

- **Spec coverage:** errori applicativi aggregati (Task 5/6), DB-down su transizione (Task 5/6), astrazione canale + LogOnly (Task 1/3), Telegram (Task 4), config `Ops:Alerting` + env (Task 2/7), test (Task 2-5), doc + uptime monitor + TODO outbox (Task 7), health check già esistente (annotato). ✔
- **Type consistency:** `ILogErrorSource.GetSinceAsync(DateTimeOffset, IReadOnlyList<string>, CancellationToken)` usato identico in Task 1/5/6; `OpsAlert(Kind,Title,Detail,OccurredAt)` coerente; `TelegramAlertChannel.HttpClientName` usato in Task 4 (test) e Task 7 (DI). ✔
- **Placeholder scan:** nessun TODO/TBD nel codice; ogni step di codice mostra il codice completo. ✔
- **Rischio noto annotato:** Task 6 senza test automatico (lettura SQL grezza), coperto da build + smoke — scelta coerente con lo spec. Task 7 risolve il nome tabella da config per evitare il riferimento circolare Infrastructure→Api. ✔
