# Design — Osservabilità OPS: rilevazione alert (filone 4.2)

> Data: 2026-06-18 · Branch: `AutoDev` · Filone: VISIONE §4.2 (Osservabilità OPS)
> Stato: **approvato in brainstorming**, in attesa di review utente sullo spec.

## 1. Obiettivo e scope

Costruire il **motore interno di rilevazione anomalie** del backend, come prerequisito di un deploy
Railway sano. In questo giro si realizza il *rilevamento* + l'*astrazione del canale di notifica*; il
*trasporto esterno* (Telegram/email) è **deliberatamente rimandato** e documentato come prossimo passo.

### In scope (questo giro)
- **Alert su errori applicativi**: aggregazione dei log `Error`/`Fatal` dalla tabella `logs` (sink PostgreSQL già esistente).
- **Alert su DB non raggiungibile**: il job esegue un self-check `CanConnectAsync` e segnala la **transizione** down/recovery.
- **Astrazione canale** `IOpsAlertChannel` con un'unica implementazione concreta ora: `LogOnlyAlertChannel`.
- **Configurazione** dedicata `Ops:Alerting` + variabili d'ambiente.
- **Test** unitari (build verde + test verdi prima della chiusura).
- **Documentazione**: `CLAUDE.md` (env, sezione OPS), nota uptime monitor esterno, TODO espliciti per i pezzi rimandati.

### Fuori scope (rimandato, documentato come next)
- **Trasporto Telegram / email** del canale alert (per ora nessuna comunicazione esterna; solo log).
- **Alert su outbox email fallita** (`OutboxEmail.Status == Failed`): l'aggancio è previsto ma non in questo giro.
- Uptime monitor esterno (UptimeRobot): zero codice per scelta, solo documentazione di setup.
- Health check con verifica DB: **già esistente** (`/api/v1/health` fa `CanConnectAsync` → 503), nessun lavoro.

## 2. Nota critica (onestà progettuale)

Senza canale esterno collegato, l'"alert" prodotto è una **riga di log strutturata** emessa via Serilog
sui sink esistenti (**console**, catturata da Railway, + **PostgreSQL** quando il DB è su). Il valore
operativo pieno arriva solo quando si collega un trasporto (Telegram). Si costruisce ora perché:
- il **motore di rilevazione + aggregazione** e l'**astrazione del canale** sono il cuore riutilizzabile;
- collegare in seguito un `TelegramAlertChannel` è un'aggiunta piccola e isolata (una sola classe + config);
- la riga di alert su **console** è comunque visibile su Railway anche quando il DB è giù.

## 3. Architettura

Pattern coerente con i `BackgroundService` esistenti (`EmailOutboxDispatcher`, `LogRetentionJob`,
`DataRetentionJob`) e con la selezione-per-config dei provider (`Email:Provider`).

```
OpsAlertMonitorJob (BackgroundService, singleton)
   │  ogni PollSeconds (default 300s):
   │   1) self-check DB:  db.Database.CanConnectAsync()
   │        ├─ transizione UP→DOWN  → channel.SendAsync(DbDown)
   │        └─ transizione DOWN→UP  → channel.SendAsync(DbRecovered)
   │      (se DB down: salta lo step 2, non interroga logs)
   │   2) query logs WHERE level IN ('Error','Fatal') AND timestamp > watermark
   │        └─ se count > 0 → channel.SendAsync(ErrorDigest{count, sample, since})
   │      avanza watermark
   ▼
IOpsAlertChannel.SendAsync(OpsAlert, ct)
   └─ LogOnlyAlertChannel → Serilog (console + DB) con marcatore "[OPS-ALERT]"
```

### Watermark e anti-flood
- Il **watermark errori** è in-memory, inizializzato all'avvio (`DateTimeOffset.UtcNow`): al riavvio **non** si rialerta lo storico.
- **Aggregazione per tick**: un solo `OpsAlert` di tipo `ErrorDigest` per ciclo, indipendentemente dal numero di errori → niente flood. Il digest riporta `Count`, finestra temporale e un **campione** (max 5 messaggi distinti).
- **DB-down su transizione**: un flag `_dbWasDown` evita di rialertare ad ogni tick mentre il DB resta giù; si emette un alert solo quando lo stato cambia.

### Lettura della tabella `logs`
La tabella `logs` **non è mappata in EF** (la gestisce il sink Serilog). Si legge in SQL grezzo via
`BookingSystemDbContext.Database`, con:
- nome tabella preso da `DatabaseLogSettings` (whitelist già validata, come fa `LogRetentionJob`);
- valore di taglio (watermark) **parametrico** (`{0}`) per evitare injection e l'analyzer EF1002;
- colonne lette: `timestamp`, `level`, `message` (sufficiente per il digest).
Mapping a un piccolo DTO/record interno via `SqlQueryRaw<...>` o reader.

## 4. Componenti (file)

| File | Progetto | Responsabilità |
|---|---|---|
| `OpsAlert.cs` (record) | Core | Modello dell'alert: `Kind` (enum: `ErrorDigest`, `DbDown`, `DbRecovered`), `Title`, `Detail`, `OccurredAt`. |
| `IOpsAlertChannel.cs` | Core | Contratto `Task SendAsync(OpsAlert alert, CancellationToken ct)`. |
| `LogOnlyAlertChannel.cs` | Infrastructure | Unica impl ora: logga l'alert con marcatore `[OPS-ALERT]` a livello `Warning`/`Error` secondo `Kind`. |
| `OpsAlertOptions.cs` | Infrastructure | Binding di `Ops:Alerting` (Enabled, Channel, PollSeconds, MinLevel). |
| `OpsAlertMonitorJob.cs` | Infrastructure (Services) | Il `BackgroundService` con la logica di §3. |
| Registrazione DI | Api `Program.cs` | `AddHostedService<OpsAlertMonitorJob>()` + `IOpsAlertChannel` → `LogOnlyAlertChannel` (selezione per `Ops:Alerting:Channel`, default `LogOnly`). Skippato se `Enabled=false`. |

Ogni file ha intestazione `// [INTENT]` e commenti `// WHY:` sulle parti non ovvie (SQL grezzo, watermark, transizione DB).

## 5. Configurazione

Sezione `appsettings.json` `Ops:Alerting`:

| Chiave | Default | Env override | Significato |
|---|---|---|---|
| `Enabled` | `true` | — | Se `false`, il job non viene avviato. |
| `Channel` | `LogOnly` | `OPS_ALERT_CHANNEL` | `LogOnly` ora; `Telegram` in futuro. Valore non riconosciuto → `LogOnly` con warning. |
| `PollSeconds` | `300` | — | Intervallo di scansione. |
| `MinLevel` | `Error` | — | Livello minimo dei log considerati (`Error` include `Fatal`). |

Disattivato nei test d'integrazione (come il sink DB) per non interferire.

## 6. Testing

Unit test (xUnit, progetto `UnitTests`), con canale fake e dati in-memory:
- **Digest**: dato un set di log Error/Fatal sopra il watermark → un singolo `OpsAlert ErrorDigest` con `Count` corretto e campione troncato a 5.
- **Nessun errore** sopra il watermark → nessun alert.
- **Avanzamento watermark**: errori già "visti" non rigenerano alert al tick successivo.
- **Transizione DB**: UP→DOWN emette `DbDown` una sola volta; ulteriori tick in down non rialertano; DOWN→UP emette `DbRecovered`.
- **LogOnlyAlertChannel**: mappa correttamente `Kind` → livello di log (verifica con un `ILogger` fake / sink di test).

> La lettura SQL grezza della tabella `logs` è dietro una piccola astrazione (`ILogErrorSource`) iniettabile,
> così la logica del job è testabile senza un DB reale. L'implementazione concreta su `DbContext` resta thin.

## 7. Documentazione da aggiornare (nello stesso commit)

- **`CLAUDE.md`**: nuova sottosezione OPS nel blocco *Logging*; tabella env (`OPS_ALERT_CHANNEL`); aggiornare "Prossimo task"/stato.
- **`Claude_Instructions/VISIONE_PRODOTTO_E_ROADMAP.md`** §4.2: spuntare la rilevazione fatta, marcare come "next" il trasporto e l'alert outbox.
- **`Claude_Instructions/DEVELOPMENT_PLAN.md`**: voce changelog + eventuale task.
- **Nota uptime monitor**: UptimeRobot (o equivalente) → `GET /api/v1/health` (readiness, 503 se DB giù) e `GET /api/v1/health/live` (liveness).
- **TODO espliciti** (in `VISIONE` §3 o §4.2): (a) `TelegramAlertChannel` + config bot token/chat id; (b) alert su `OutboxEmail.Status == Failed`.

## 8. Build & verifica

`dotnet build` verde (0 warning, analyzer + warnings-as-errors) e tutti i test verdi prima di dichiarare
completo. Smoke manuale opzionale: forzare un log `Error` e verificare la riga `[OPS-ALERT]` su console.

## 9. Esecuzione (workflow concordato)

Branch `AutoDev` corrente, suddivisione in sotto-task ai subagenti, **review con agente reviewer**, build+test
ad ogni step, commit frequenti. Lo spec passa a `writing-plans` per il piano dettagliato.
