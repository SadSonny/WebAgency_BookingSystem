# Design — Osservabilità OPS: rilevazione + alerting (filone 4.2)

> Data: 2026-06-18 · Branch: `AutoDev` · Filone: VISIONE §4.2 (Osservabilità OPS)
> Stato: **approvato in brainstorming** (con scope rivisto: incluso canale Telegram minimale), in attesa di review utente.

## 1. Obiettivo e scope

Costruire il **motore interno di rilevazione anomalie** del backend **e un canale di notifica reale**
(Telegram), come prerequisito di un deploy Railway sano. A differenza della prima bozza, questo giro
**notifica davvero** verso l'esterno: senza un canale concreto il sistema sarebbe stato solo scaffolding
(l'ErrorDigest scriverebbe log dentro la stessa tabella `logs` che legge, e il DB-down sarebbe ridondante
con l'uptime monitor esterno). Il `TelegramAlertChannel` è piccolo ed è ciò che dà valore al resto.

### In scope (questo giro)
- **Alert su errori applicativi**: aggregazione dei log `>= MinLevel` (default `Error`, include `Fatal`) dalla tabella `logs`.
- **Alert su DB non raggiungibile**: il job esegue un self-check `CanConnectAsync` e segnala la **transizione** down/recovery (push proattivo, complementare all'uptime monitor esterno che è pull).
- **Astrazione canale** `IOpsAlertChannel` con due implementazioni: `TelegramAlertChannel` (reale) e `LogOnlyAlertChannel` (fallback/dev).
- **Configurazione** dedicata `Ops:Alerting` + variabili d'ambiente (incl. segreti Telegram).
- **Test** unitari (build verde + test verdi prima della chiusura).
- **Documentazione**: `CLAUDE.md` (env, sezione OPS), nota uptime monitor esterno, TODO espliciti per i pezzi rimandati.

### Fuori scope (rimandato, documentato come next)
- **Alert su outbox email fallita** (`OutboxEmail.Status == Failed`): ora banale da agganciare (basta una sorgente in più verso lo stesso canale), ma escluso da questo giro per scelta.
- **Canale email** come secondo trasporto (Telegram è sufficiente e indipendente dall'infra email).
- Uptime monitor esterno (UptimeRobot): zero codice per scelta, solo documentazione di setup.
- Health check con verifica DB: **già esistente** (`/api/v1/health` fa `CanConnectAsync` → 503), nessun lavoro.

## 2. Architettura

Pattern coerente con i `BackgroundService` esistenti (`EmailOutboxDispatcher`, `LogRetentionJob`,
`DataRetentionJob`) e con la selezione-per-config dei provider (`Email:Provider`).

```
OpsAlertMonitorJob (BackgroundService, singleton)
   │  ogni PollSeconds (default 300s):
   │   1) self-check DB:  db.Database.CanConnectAsync()
   │        ├─ transizione UP→DOWN  → channel.SendAsync(DbDown)
   │        └─ transizione DOWN→UP  → channel.SendAsync(DbRecovered)
   │      (se DB down: salta lo step 2, non interroga logs)
   │   2) ILogErrorSource.GetSince(watermark, MinLevel)  →  errori nuovi
   │        └─ se count > 0 → channel.SendAsync(ErrorDigest{count, sample, since})
   │      avanza watermark
   ▼
IOpsAlertChannel.SendAsync(OpsAlert, ct)
   ├─ TelegramAlertChannel → POST api.telegram.org/bot<token>/sendMessage
   └─ LogOnlyAlertChannel  → Serilog (console + DB) con marcatore "[OPS-ALERT]"
```

### Watermark e anti-flood
- Il **watermark errori** è in-memory, inizializzato all'avvio (`DateTimeOffset.UtcNow`): al riavvio **non** si rialerta lo storico.
- **Aggregazione per tick**: un solo `OpsAlert ErrorDigest` per ciclo, indipendentemente dal numero di errori → niente flood. Il digest riporta `Count`, finestra temporale e un **campione** (max 5 messaggi distinti).
- **DB-down su transizione**: un flag `_dbWasDown` evita di rialertare ad ogni tick mentre il DB resta giù; alert solo al cambio di stato.

### Assunzione esplicita — singola istanza
Il watermark e i flag di stato sono **in-memory per processo**. Con **più istanze in parallelo** (scenario citato in `CLAUDE.md` per le migration) ogni istanza alerterebbe in modo indipendente → **alert duplicati**. Per il deploy Railway **a istanza singola** va bene; uno stato condiviso (DB/cache) è un follow-up esplicito se si scalerà orizzontalmente.

### Lettura della tabella `logs`
La tabella `logs` **non è mappata in EF** (la gestisce il sink Serilog). La lettura è dietro
l'astrazione `ILogErrorSource` (testabile in isolamento); l'implementazione concreta usa
`BookingSystemDbContext.Database` in SQL grezzo con:
- nome tabella da `DatabaseLogSettings` (whitelist già validata, come `LogRetentionJob`);
- watermark **parametrico** (`{0}`) per evitare injection e l'analyzer EF1002;
- filtro livello da `MinLevel` (config, non hardcoded);
- colonne lette: `timestamp`, `level`, `message`.

## 3. Componenti (file)

| File | Progetto | Responsabilità |
|---|---|---|
| `OpsAlert.cs` (record) | Core | Modello alert: `Kind` (enum `ErrorDigest`/`DbDown`/`DbRecovered`), `Title`, `Detail`, `OccurredAt`. |
| `IOpsAlertChannel.cs` | Core | Contratto `Task SendAsync(OpsAlert alert, CancellationToken ct)`. |
| `ILogErrorSource.cs` | Core | Contratto `Task<IReadOnlyList<LogError>> GetSinceAsync(DateTimeOffset watermark, string minLevel, CancellationToken ct)`. |
| `DbLogErrorSource.cs` | Infrastructure | Impl di `ILogErrorSource` su `DbContext` (SQL grezzo, vedi §2). |
| `TelegramAlertChannel.cs` | Infrastructure | POST a Bot API via `HttpClient` (IHttpClientFactory). Errori di rete loggati, mai propagati. |
| `LogOnlyAlertChannel.cs` | Infrastructure | Fallback: logga l'alert con marcatore `[OPS-ALERT]`, livello secondo `Kind`. |
| `OpsAlertOptions.cs` | Infrastructure | Binding di `Ops:Alerting` (Enabled, Channel, PollSeconds, MinLevel, Telegram.BotToken, Telegram.ChatId). |
| `OpsAlertMonitorJob.cs` | Infrastructure (Services) | Il `BackgroundService` con la logica di §2. |
| Registrazione DI | Api `Program.cs` | `AddHostedService<OpsAlertMonitorJob>()`; `IOpsAlertChannel` selezionato per `Channel`; se `Telegram` ma token/chatId mancanti → fallback `LogOnly` con warning all'avvio. Skippato se `Enabled=false`. |

Ogni file ha intestazione `// [INTENT]` e commenti `// WHY:` sulle parti non ovvie (SQL grezzo, watermark, transizione DB, fallback canale).

## 4. Configurazione

Sezione `appsettings.json` `Ops:Alerting`:

| Chiave | Default | Env override | Significato |
|---|---|---|---|
| `Enabled` | `true` | — | Se `false`, il job non viene avviato. |
| `Channel` | `LogOnly` | `OPS_ALERT_CHANNEL` | `Telegram` o `LogOnly`. Valore non riconosciuto, o `Telegram` senza credenziali → `LogOnly` con warning. |
| `PollSeconds` | `300` | — | Intervallo di scansione. |
| `MinLevel` | `Error` | — | Livello minimo dei log considerati (guida il filtro SQL; `Error` include `Fatal`). |
| `Telegram:BotToken` | — | `OPS_ALERT_TELEGRAM_BOT_TOKEN` | **Segreto.** Dev: user-secrets; prod: env. |
| `Telegram:ChatId` | — | `OPS_ALERT_TELEGRAM_CHAT_ID` | Id chat/canale destinatario. |

Default `Channel=LogOnly` così out-of-the-box (e nei test) non si tenta nessuna chiamata di rete.
Disattivato nei test d'integrazione.

## 5. Testing

Unit test (xUnit, progetto `UnitTests`), con canale/sorgente fake e dati in-memory:
- **Digest**: log `>= MinLevel` sopra il watermark → un singolo `ErrorDigest` con `Count` corretto e campione troncato a 5.
- **Filtro MinLevel**: log sotto `MinLevel` ignorati.
- **Nessun errore** sopra il watermark → nessun alert.
- **Avanzamento watermark**: errori già "visti" non rigenerano alert al tick successivo.
- **Transizione DB**: UP→DOWN emette `DbDown` una sola volta; tick successivi in down non rialertano; DOWN→UP emette `DbRecovered`.
- **LogOnlyAlertChannel**: mappa `Kind` → livello di log corretto (ILogger/sink di test).
- **TelegramAlertChannel**: con `HttpMessageHandler` mockato, verifica URL/payload corretti e che un errore HTTP **non** propaghi (loggato, swallowed).
- **Selezione canale**: `Channel=Telegram` senza credenziali → risolve `LogOnly` con warning.

## 6. Documentazione da aggiornare (nello stesso commit)

- **`CLAUDE.md`**: sottosezione OPS nel blocco *Logging*; righe env (`OPS_ALERT_CHANNEL`, `OPS_ALERT_TELEGRAM_BOT_TOKEN`, `OPS_ALERT_TELEGRAM_CHAT_ID`); nota user-secrets per il token in dev; aggiornare "Prossimo task"/stato.
- **`Claude_Instructions/VISIONE_PRODOTTO_E_ROADMAP.md`** §4.2: spuntare rilevazione + canale Telegram; marcare come "next" l'alert outbox e l'eventuale canale email.
- **`Claude_Instructions/DEVELOPMENT_PLAN.md`**: voce changelog + task.
- **Nota uptime monitor**: UptimeRobot → `GET /api/v1/health` (readiness, 503 se DB giù) e `GET /api/v1/health/live` (liveness).
- **TODO esplicito**: alert su `OutboxEmail.Status == Failed` (aggancio banale ora che il canale esiste).

## 7. Build & verifica

`dotnet build` verde (0 warning, analyzer + warnings-as-errors) e tutti i test verdi prima di dichiarare
completo. Smoke manuale opzionale: con un bot Telegram di test, forzare un log `Error` e verificare la
ricezione del messaggio; in alternativa `Channel=LogOnly` e verificare la riga `[OPS-ALERT]` su console.

## 8. Esecuzione (workflow concordato)

Branch `AutoDev` corrente, suddivisione in sotto-task ai subagenti, **review con agente reviewer**, build+test
ad ogni step, commit frequenti. Lo spec passa a `writing-plans` per il piano dettagliato.
