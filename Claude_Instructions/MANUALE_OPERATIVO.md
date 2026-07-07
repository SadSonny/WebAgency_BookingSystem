<!-- [INTENT]: Manuale operativo e di manutenzione del backend BookingSystem. Documento autosufficiente per
chi prende in mano lo strumento per la prima volta: architettura essenziale, setup locale, deploy, variabili
d'ambiente (cosa e perché), creazione utenti/tenant, uso dell'admin di piattaforma, gestione segreti, job in
background, dove trovare i log, monitoraggio, backup/GDPR e troubleshooting. NON documenta le API applicative
(solo health) — per quelle vedere GUIDA_INTEGRAZIONE_API.md. Ultimo allineamento al codice: 2026-07-07. -->

# Manuale Operativo e di Manutenzione — WebAgency BookingSystem

> Documento per chi **gestisce e mantiene** il backend, non per chi integra un sito.
> Copre: come funziona il sistema a grandi linee, come farlo girare in locale, come si fa il deploy,
> quali variabili impostare (e **perché**), come si creano utenti e tenant, come si usa l'admin di sistema,
> dove si trovano i log e cosa monitorare.
>
> **Fuori da questo documento:** le API applicative (prenotazioni, servizi, staff, admin CRUD, platform…).
> Qui trovi **solo** gli endpoint di health. Per il contratto API completo → `GUIDA_INTEGRAZIONE_API.md`.

---

## 0. In 60 secondi: cos'è e chi lo usa

- È un **backend multi-tenant API-only** (headless) per prenotazioni di attività locali (barbieri, estetica, ecc.).
- **Non ha interfaccia grafica di prodotto.** Il cliente del prodotto è **un'agenzia web**: l'agenzia crea i siti
  dei clienti finali e li collega a questo backend. Widget di prenotazione e pannello del titolare li costruisce l'agenzia.
- Ci sono **tre identità** nel sistema:
  1. **Cliente finale / widget** → si autentica con una **API key** (`X-Api-Key`). È il sito pubblico.
  2. **Owner (titolare dell'attività)** → si autentica con **JWT admin**. Gestisce il proprio tenant.
  3. **PlatformAdmin (l'agenzia)** → si autentica con **JWT platform**. Crea e gestisce i tenant. È l'"admin di sistema".
- Stack: **.NET 10 / ASP.NET Core Minimal API + EF Core 10 + PostgreSQL 16**. Deploy su **Railway**.

Se stai prendendo in mano il progetto: leggi in ordine §1 (struttura), §2 (locale), poi §5 (deploy) e §6 (utenti).

---

## 1. Struttura del codice (mappa minima)

```
WebAgency_BookingSystem/
├── src/
│   ├── ...Api/              ← Minimal API: endpoint, middleware, DI, logging, Program.cs (entry point)
│   ├── ...Core/             ← Entità, interfacce, DTO, Result<T>, logica di dominio (availability/booking)
│   └── ...Infrastructure/   ← DbContext + migration, repository, email, job in background
├── tests/
│   ├── ...UnitTests/        ← test unità
│   └── ...IntegrationTests/ ← test su Postgres reale (Testcontainers)
├── tools/
│   └── ...TenantProvisioning/ ← CLI per creare tenant da file JSON (alternativa alla Platform API)
├── Claude_Instructions/    ← TUTTA la documentazione .md (incluso questo file)
├── docker-compose.yml      ← Infra locale: Postgres + pgAdmin + Mailpit
├── Dockerfile              ← Immagine di produzione (usata da Railway)
└── CLAUDE.md               ← Istruzioni per gli agenti AI + stato del progetto
```

**Convenzione utile:** ogni file sorgente inizia con un commento `// [INTENT]: …` che spiega cosa fa. Per capire
un file senza leggerlo tutto, leggi il suo `[INTENT]` e le firme pubbliche.

---

## 2. Far girare il sistema in locale

### Prerequisiti
- **.NET 10 SDK**
- **Docker Desktop** (per Postgres/pgAdmin/Mailpit)

### Passi

```bash
# 1. Avvia l'infrastruttura locale (Postgres + pgAdmin + Mailpit)
docker compose up -d

# 2. Applica le migration al database
dotnet ef database update \
  --project src/WebAgency_BookingSystem.Infrastructure \
  --startup-project src/WebAgency_BookingSystem.Api

# 3. Avvia l'API
dotnet run --project src/WebAgency_BookingSystem.Api

# 4. (opzionale) Crea un tenant di prova
dotnet run --project tools/WebAgency_BookingSystem.TenantProvisioning \
  -- --file samples/barbershop-demo.json
```

> In Development, `DB_AUTO_MIGRATE` è già `true` (via `appsettings.Development.json`): le migration si applicano
> anche da sole all'avvio dell'API. Lo step 2 serve solo se vuoi applicarle prima o dal CLI.

### Indirizzi utili in locale
| Servizio | URL | Note |
|---|---|---|
| API | `http://localhost:5022` | Porta del profilo `http` in `launchSettings.json` |
| Documentazione API (Scalar) | `http://localhost:5022/scalar` | Solo in non-produzione |
| pgAdmin | `http://localhost:5050` | login: `admin@admin.com` / `admin` |
| Mailpit (email catturate) | `http://localhost:8025` | Tutte le email inviate in dev finiscono qui, non a destinatari reali |
| Postgres | `localhost:5432` | db `bookingsystem`, user/pass `postgres`/`postgres` |

### Health check (gli unici endpoint qui documentati)
| Endpoint | Cosa verifica | Risposte |
|---|---|---|
| `GET /api/v1/health/live` | **Liveness**: il processo è vivo. **Non tocca il DB** → adatto a probe frequenti. | `200` sempre se il processo risponde |
| `GET /api/v1/health` | **Readiness**: il DB è raggiungibile. | `200` se pronto, `503` se il DB non risponde |

Regola pratica: **uptime monitor esterno → `/health/live`**; **orchestratore/probe di readiness → `/health`**.

---

## 3. Configurazione: da dove arriva e con quale priorità

La configurazione segue la pipeline standard .NET. Ordine di **precedenza crescente** (l'ultimo vince):

1. `appsettings.json` — default non sensibili, validi in tutti gli ambienti.
2. `appsettings.{Environment}.json` — override per ambiente (es. `appsettings.Development.json`).
3. **User Secrets** — solo in Development, per i **segreti reali in locale** (non finiscono nel repo).
4. **Variabili d'ambiente** — vincono su tutto. **In produzione si usano SOLO queste.**

**Regole d'oro:**
- In **produzione**: niente `appsettings` con segreti, niente user-secrets. **Solo variabili d'ambiente.**
- In **sviluppo**: i valori in `appsettings.Development.json` sono default locali innocui (DB di docker-compose,
  `Jwt:Secret` marcato `change-me`). Per un segreto reale in locale usa gli **User Secrets**:

```bash
cd src/WebAgency_BookingSystem.Api
dotnet user-secrets set "Jwt:Secret" "<segreto-random-min-32-char>"
dotnet user-secrets set "ConnectionStrings:Database" "<connection-string>"
dotnet user-secrets set "Email:Brevo:ApiKey" "<xkeysib-...>"
```

> **Mapping env → config**: il doppio underscore `__` mappa la gerarchia JSON. Es. `Jwt__PlatformAudience`
> imposta `Jwt:PlatformAudience`. Alcune variabili "amichevoli" (`JWT_SECRET`, `DATABASE_URL`, `EMAIL_PROVIDER`,
> `PLATFORM_SETUP_TOKEN`, `OPS_ALERT_*`…) sono lette esplicitamente dal codice: le trovi in §4.

---

## 4. Variabili d'ambiente — riferimento completo

Legenda: **[R]** = richiesta in produzione · **[S]** = segreto (non committare, non loggare) · **[O]** = opzionale.

### 4.1 Core (sempre)
| Variabile | Tipo | Descrizione / perché serve | Esempio |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | [R] | Attiva i **guard di produzione** (es. rifiuta il JWT secret `change-me`, disattiva Scalar/OpenAPI). Senza `Production` il sistema gira in modalità dev-permissiva. | `Production` |
| `DATABASE_URL` | [R][S] | Connection string Postgres. Accetta sia il **formato URI** di Railway (`postgresql://…`) sia il **formato keyword** Npgsql: l'app converte l'URI automaticamente (`DatabaseConnectionString.Normalize`). Una stringa **vuota** è trattata come *mancante* → errore chiaro all'avvio. | `Host=…;Database=…;Username=…;Password=…` |
| `JWT_SECRET` | [R][S] | Segreto per firmare i JWT admin **e** platform. **≥ 32 caratteri random reali.** In `Production` l'avvio **fallisce** se contiene `change-me`. | `<random ≥32 char>` |
| `JWT_EXPIRY_HOURS` | [O] | Validità dei token JWT. Default `8`. | `8` |
| `PORT` | [R su Railway] | Porta di ascolto. **La inietta la piattaforma** (Railway); l'app la legge in `Program.cs`. In locale non serve (usa `launchSettings.json`). | `8080` |
| `PUBLIC_BASE_URL` | [R] | Base URL assoluta dell'API, usata per costruire i **link nelle email** (attivazione account, reset password). Se sbagliata, i link nelle email puntano nel vuoto. Impostala dopo aver ottenuto il dominio. | `https://<servizio>.up.railway.app` |
| `DB_AUTO_MIGRATE` | [O] | Se `true`, applica le migration EF **all'avvio** dell'API. Comodo con **una sola istanza**. Con più istanze in parallelo lascia `false` e migra in pipeline (vedi §5.4). In Development è già `true`. | `true` |

### 4.2 Email
| Variabile | Tipo | Descrizione | Esempio |
|---|---|---|---|
| `EMAIL_PROVIDER` (`Email:Provider`) | [R] | `Brevo` in produzione, `Smtp`/Mailpit in dev, `None` per disattivare. Determina il trasporto email. | `Brevo` |
| `BREVO_API_KEY` | [S] | API key Brevo (REST). Richiesta se provider = `Brevo`. | `xkeysib-…` |
| `BREVO_SENDER_EMAIL` | [R con Brevo] | Indirizzo mittente. **Deve essere verificato su Brevo**, altrimenti le email non partono. | `noreply@dominio.it` |
| `BREVO_SENDER_NAME` | [O] | Nome mittente mostrato. | `BookingSystem` |

> **Come funziona l'invio:** le email non partono in linea con la richiesta. Vengono **accodate in una outbox
> transazionale** (`OutboxEmail`, nella stessa transazione del booking) e inviate da un job in background
> (`EmailOutboxDispatcher`) con retry/backoff. Vantaggio: se Brevo è momentaneamente giù, l'email non si perde
> e la risposta HTTP resta veloce.

### 4.3 Platform admin (l'agenzia)
| Variabile | Tipo | Descrizione | Esempio |
|---|---|---|---|
| `PLATFORM_SETUP_TOKEN` | [S] | **Break-glass.** Abilita l'endpoint di bootstrap `POST /api/v1/platform/setup` per creare/reimpostare l'admin di piattaforma. Se **non** configurato, l'endpoint risponde **404**. Impostalo al primo avvio, poi **rimuovilo** (vedi §6.1). | `<token random>` |
| `Jwt__PlatformAudience` | [O] | Audience del JWT PlatformAdmin. Deve **differire** dall'audience admin tenant (default `WebAgency_BookingSystem.Admin`), così un token platform non è accettato sulle rotte tenant e viceversa. | `WebAgency_BookingSystem.Platform` |

### 4.4 Rate limiting
| Variabile | Default | Cosa limita |
|---|---|---|
| `RATE_LIMIT_PER_MINUTE` | 100 | Richieste per API key (sliding window). |
| `RATE_LIMIT_IP_PER_MINUTE` | 300 | Richieste per IP, prima dell'auth (anti brute-force). |
| `RATE_LIMIT_BOOKING_PER_MINUTE` | 10 | Creazione prenotazioni per API key (anti-spam con chiave pubblica). |
| `RATE_LIMIT_ACCOUNT_PER_MINUTE` | 10 | Rotte login/account admin per IP (policy `AccountSecurity`). |

### 4.5 Monitoraggio / alert OPS
| Variabile | Default | Descrizione |
|---|---|---|
| `OPS_ALERT_CHANNEL` (`Ops:Alerting:Channel`) | `LogOnly` | `LogOnly` scrive una riga `[OPS-ALERT]` su console/DB. `Telegram` recapita l'alert su un bot. |
| `OPS_ALERT_TELEGRAM_BOT_TOKEN` | — | [S] Token bot Telegram. Richiesto solo se `Channel=Telegram`. |
| `OPS_ALERT_TELEGRAM_CHAT_ID` | — | Id chat/canale destinatario. Richiesto solo se `Channel=Telegram`. |

### 4.6 CORS
| Variabile | Descrizione |
|---|---|
| `Cors__AllowedOrigins__0`, `__1`, … | Allowlist **statica aggiuntiva** di origini (es. staging, tool interni). **Nella normalità non serve toccarla:** le origini dei siti clienti sono autorizzate automaticamente dai loro `siteUrl` (vedi §7). |

### 4.7 Retention / GDPR (raramente da toccare — valori in `appsettings.json`)
| Chiave | Default | Descrizione |
|---|---|---|
| `Gdpr:RetentionDays` | 365 | Oltre questa età le PII delle prenotazioni vengono **anonimizzate**. |
| `Gdpr:OutboxRetentionDays` | 30 | Le email outbox già inviate vengono **purgate** dopo questi giorni. |
| `DatabaseLogging:RetentionDays` | 90 | La tabella `logs` viene purgata oltre questa età. |

---

## 5. Deploy su Railway

Il deploy è su **Railway (EU West)**, istanza continuativa (non "a consumo che dorme": ci sono job sempre attivi).
Riferimento operativo dettagliato con checklist: `DEPLOY_RAILWAY.md`.

### 5.1 Cosa è già pronto nel repo
- **`Dockerfile`** multi-stage (`sdk:10.0` → `aspnet:10.0`), utente non-root. Railway lo rileva e builda da solo.
- L'app legge la porta da **`$PORT`** (iniettata da Railway).
- `DATABASE_URL` può puntare direttamente alla variabile di riferimento del Postgres managed (l'app converte l'URI).

### 5.2 Passi
1. Crea un progetto su Railway, aggiungi il plugin **PostgreSQL** managed (espone `DATABASE_URL` e `DATABASE_PRIVATE_URL`).
2. Collega il repo GitHub (branch **`main`**) — Railway builda il `Dockerfile`.
3. Imposta le **variabili d'ambiente** del servizio API (vedi §5.3).
4. Attendi la build e verifica i log di avvio (migrazioni, connessione DB).
5. Esegui lo **smoke test** (§5.5).

### 5.3 Set minimo di variabili in produzione
```
ASPNETCORE_ENVIRONMENT = Production
DATABASE_URL           = ${{Postgres.DATABASE_PRIVATE_URL}}   # rete interna: egress gratis, più veloce
JWT_SECRET             = <random reale ≥32 char>
EMAIL_PROVIDER         = Brevo
BREVO_API_KEY          = <xkeysib-…>
BREVO_SENDER_EMAIL     = <mittente verificato su Brevo>
BREVO_SENDER_NAME      = BookingSystem
PUBLIC_BASE_URL        = https://<servizio>.up.railway.app
DB_AUTO_MIGRATE        = true                                  # ok con istanza singola
PLATFORM_SETUP_TOKEN   = <token random>                        # solo per il bootstrap, poi RIMUOVILO
# Opzionali:
JWT_EXPIRY_HOURS       = 8
OPS_ALERT_CHANNEL      = Telegram
OPS_ALERT_TELEGRAM_BOT_TOKEN = <token bot>
OPS_ALERT_TELEGRAM_CHAT_ID   = <chat id>
```

### 5.4 Migration
Con `DB_AUTO_MIGRATE=true` (istanza singola) le migration EF si applicano all'avvio. **Verifica nei log** che siano
andate a buon fine.

Se un giorno scali a **più istanze**, disattiva `DB_AUTO_MIGRATE` (per evitare che due istanze migrino insieme) e
applica le migration come **step di pipeline** prima del rollout:
```bash
dotnet ef database update \
  --project src/WebAgency_BookingSystem.Infrastructure \
  --startup-project src/WebAgency_BookingSystem.Api \
  --connection "<DATABASE_URL di produzione>"
```

### 5.5 Smoke test post-deploy (l'ordine conta)
1. `GET /api/v1/health/live` → **200** (processo vivo).
2. `GET /api/v1/health` → **200** (DB raggiungibile).
3. Bootstrap admin di piattaforma → login platform → crea un tenant di prova → verifica che arrivi l'**API key**
   e che **Brevo consegni** l'email di attivazione all'Owner (vedi §6).
4. Attiva l'Owner e fai un **login admin** di prova (valida i fix JWT).
5. **Rimuovi `PLATFORM_SETUP_TOKEN`** dalle env → l'endpoint di setup torna a `404`.

### 5.6 Rollback
Railway mantiene lo storico dei deploy: in caso di regressione, **redeploy della build precedente** dal cruscotto.
⚠ Attenzione alle migration: un rollback del codice **non** annulla una migration già applicata al DB. Se una
migration è distruttiva, valuta prima un backup del DB (§9).

---

## 6. Creare e gestire gli utenti

Ci sono due tipi di "utenti" che si creano/gestiscono: l'**admin di piattaforma** (l'agenzia) e gli **Owner** dei tenant.
I clienti finali **non** sono utenti: usano solo la API key del loro tenant.

### 6.1 Admin di piattaforma (l'admin di sistema)

È l'identità dell'agenzia. Non appartiene ad alcun tenant. Serve a creare/gestire i tenant senza toccare il DB.

**Primo admin — bootstrap break-glass (una tantum):**
1. Imposta `PLATFORM_SETUP_TOKEN` nelle env (un token segreto random).
2. Chiama `POST /api/v1/platform/setup` con `{ setupToken, email, password }` → crea (o reimposta) l'admin per quell'email.
3. **Rimuovi `PLATFORM_SETUP_TOKEN`** dalle env → l'endpoint torna a rispondere `404`.

> Perché break-glass: senza il token l'endpoint di setup è invisibile (404). Il token esiste solo per creare il
> primissimo admin (o recuperare l'accesso se si perdono le credenziali). Non lasciarlo impostato stabilmente.

**Login e uso:** `POST /api/v1/platform/auth/token` → ottieni il JWT PlatformAdmin, da usare come
`Authorization: Bearer <JWT>` sulle rotte `/api/v1/platform/*`. Con quel JWT puoi creare tenant, elencarli,
disattivarli/riattivarli, gestire le loro API key e re-inviare l'email di attivazione all'Owner.

**Cambio password admin di piattaforma:** `POST /api/v1/platform/account/password` (autenticato).

> Follow-up non ancora implementati: invito multi-admin e reset password platform via email. Oggi il recupero
> dell'accesso platform passa dal break-glass (`PLATFORM_SETUP_TOKEN`).

### 6.2 Owner di un tenant

L'Owner è il titolare dell'attività. Viene creato **insieme al tenant** (durante il provisioning) e **senza password**:
riceve un'**email con link di attivazione** (token in DB, valido 72h) e imposta la password da sé.

Flusso di attivazione:
1. Il provisioning crea l'Owner senza password e **accoda l'email di attivazione**.
2. L'Owner apre il link → pagina HTML servita dall'API → imposta la password.
3. Da lì fa login con email globale + password (`POST /api/v1/admin/auth/token`).

> **Login per email globale:** l'email dell'Owner è univoca a livello di intero sistema; il tenant si ricava
> dall'account. Non serve indicare lo slug del tenant.

Cosa può fare l'agenzia se qualcosa va storto:
- **Owner non ha ricevuto l'email / link scaduto** → `POST /api/v1/platform/tenants/{id}/owner/resend-activation`
  (rigenera il token e re-accoda l'email).
- **Owner ha dimenticato la password** → l'Owner usa il flusso self-service di reset via email
  (`/api/v1/admin/account/password/reset-request`), risposta sempre neutra.

**Sicurezza login Owner:** 5 tentativi falliti consecutivi → **blocco 15 minuti** dell'utente. Al cambio/reset/
attivazione password si **rigenera il SecurityStamp**: i JWT emessi prima diventano invalidi (il frontend deve
gestire il 401 rifacendo il login).

### 6.3 Creare un tenant (nuovo cliente dell'agenzia)

Due strade equivalenti — usano lo **stesso** `ITenantProvisioningService` internamente:

**A) Via Platform API (consigliata):** `POST /api/v1/platform/tenants` con il JSON di provisioning
(schema `ProvisioningInput`). Restituisce l'**API key in chiaro una sola volta** e accoda l'email di attivazione Owner.
Nessun accesso al DB necessario.

**B) Via CLI (fallback / automazione locale):**
```bash
dotnet run --project tools/WebAgency_BookingSystem.TenantProvisioning -- \
  --file clienti/nome-cliente.json \
  --connection "<DATABASE_URL>"     # oppure imposta la env DATABASE_URL
```
La CLI valida il JSON, inserisce tutto in **una singola transazione atomica** (tenant → orari → chiusure →
servizi → staff → associazioni), genera l'**API key** (salvata come hash SHA-256) e l'**Owner** senza password,
scrive l'`audit_log` e stampa i segreti **una sola volta**. Codici di uscita: `0` ok, `1` errore runtime
(DB/slug già esistente), `2` errore di input (argomenti/file/JSON/validazione).

> ⚠ La modalità `--update` **non è supportata** in V1: ri-eseguire il provisioning sullo stesso slug viene
> rifiutato. Le modifiche a un tenant esistente (servizi, staff, orari) si fanno via **Admin API**.

Lo schema del file JSON di provisioning è documentato in dettaglio in `05-provisioning-e-struttura.md` (Parte 2)
e in `GUIDA_INTEGRAZIONE_API.md` §4; un modello pronto è `samples/barbershop-demo.json`.

### 6.4 API key dei tenant
- Ogni API key appartiene a **un solo tenant**; tutte le query sono filtrate per `tenant_id` (isolamento).
- In DB è salvato **solo l'hash** (SHA-256): la chiave in chiaro si vede **una sola volta** alla creazione.
- **Rotazione/revoca:** l'Owner via Admin API (`/admin/api-keys`) o l'agenzia via Platform API
  (`/platform/tenants/{id}/api-keys`). Rotazione consigliata: crea la nuova, aggiorna il frontend, poi revoca la
  vecchia. La revoca ha **effetto immediato** (rimozione dalla cache).
- **Disattivare un tenant** (`/platform/tenants/{id}/deactivate`) fa smettere di funzionare **tutte** le sue API key.

### 6.5 Flusso completo: collegare un nuovo sito/cliente da zero

Questa è la procedura **end-to-end** da seguire quando un'agenzia deve mettere online un nuovo cliente. Se non
conosci il sistema, segui questi passi in ordine: al termine il sito del cliente sarà collegato e prenotabile.

**Chi fa cosa** in questo flusso:
- **Agenzia (tu)** — raccoglie i dati, crea il tenant, consegna le credenziali, verifica.
- **Sistema (backend)** — genera API key + Owner, invia l'email di attivazione, autorizza il CORS.
- **Owner (titolare)** — attiva il proprio account impostando la password.
- **Sviluppatore frontend** — configura il sito con API key + base URL.

Schema del flusso:
```
[1] Raccogli dati   →  [2] Componi JSON   →  [3] Crea tenant (Platform API o CLI)
                                                    │
                          ┌─────────────────────────┼─────────────────────────┐
                          ▼                          ▼                         ▼
            [4] API key (una volta)   [5] Email attivazione Owner   CORS autorizzato (auto)
                          │                          │
                          ▼                          ▼
       [6] Configura il frontend        [7] Owner imposta password
                          │                          │
                          └─────────────┬────────────┘
                                        ▼
                          [8] Verifica end-to-end  →  🎉 sito online
```

**Passo 1 — Raccogli i dati dell'attività.** Ti servono:
- Identità: **nome**, **slug** univoco (minuscolo, es. `barbershop-mario`), **URL del sito** (`siteUrl`),
  **email del titolare** (`ownerEmail`, sarà la sua login), **timezone** (es. `Europe/Rome`).
- Regole di prenotazione: anticipo minimo, preavviso minimo di disdetta, giorni visibili, se il cliente può
  scegliere l'operatore, metodo di notifica.
- **Orari settimanali** (7 giorni, 0=Domenica … 6=Sabato; pausa opzionale).
- Eventuali **chiusure straordinarie** (ferie, festività).
- **Servizi**: nome, durata, prezzo, quante prenotazioni simultanee accetta (`parallelSlots` = postazioni),
  eventuale buffer.
- **Staff**: nome, ruolo, orari, quali servizi esegue (con eventuale prezzo diverso).

**Passo 2 — Componi il file JSON di provisioning.** Parti dal modello reale `samples/barbershop-demo.json` e
adattalo. Punti su cui sbagliano i principianti:
- `localId` / `serviceLocalId` sono **identificatori interni al file**, servono solo a collegare staff↔servizi;
  **non** sono gli UUID del database (li genera il sistema).
- `businessHours` dello staff: array **vuoto** `[]` → usa gli orari del tenant. Se lo compili, deve coprire tutti
  i giorni in cui quello staff lavora.
- `priceOverride: null` → usa il `basePrice` del servizio.
- `parallelSlots` = quante persone possono essere servite in contemporanea per quel servizio (postazioni fisiche).
- Lo schema completo è in `05-provisioning-e-struttura.md` (Parte 2) e in `GUIDA_INTEGRAZIONE_API.md` §4.

**Passo 3 — Crea il tenant.** Due strade equivalenti (stessa logica dietro, vedi §6.3):
- **Platform API** (consigliata, nessun accesso al DB): autenticati come admin di piattaforma (§6.1) e chiama
  `POST /api/v1/platform/tenants` con il JSON del Passo 2.
- **CLI** (fallback/locale):
  ```bash
  dotnet run --project tools/WebAgency_BookingSystem.TenantProvisioning -- \
    --file clienti/nome-cliente.json \
    --connection "<DATABASE_URL>"
  ```
  Il provisioning è **atomico**: o va a buon fine tutto, o non viene creato nulla.

**Passo 4 — Salva subito la API key.** Alla creazione ottieni l'**API key in chiaro, mostrata UNA SOLA VOLTA**
(in DB c'è solo l'hash: non è più recuperabile). Salvala immediatamente in un gestore di segreti. Se la perdi, non
è un dramma: puoi generarne una nuova e revocare la vecchia (§6.4).

**Passo 5 — Il sistema invia l'email di attivazione all'Owner** (automatico). L'Owner viene creato **senza
password** e riceve un'email con un link (valido 72h) per impostarla. In **dev** l'email non parte davvero: la
trovi su Mailpit (`http://localhost:8025`). In **prod** verifica che Brevo la consegni (vedi §10).

**Passo 6 — Configura il frontend del cliente** con la API key e la base URL del backend:
```
VITE_BOOKING_API_KEY=<api_key_del_passo_4>
VITE_BOOKING_API_URL=https://<backend>/api/v1
```
Il **CORS non richiede azioni manuali** (§7): l'origine del `siteUrl` viene autorizzata automaticamente entro
~60 secondi. Solo per domini extra (es. staging) aggiungi una voce a `Cors__AllowedOrigins__N`.

**Passo 7 — L'Owner attiva il suo account.** Apre il link dell'email → pagina "imposta password" → sceglie la
password (min 12 caratteri). Da quel momento fa login con **email globale + password**. Se non ha ricevuto l'email
o il link è scaduto, l'agenzia lo re-invia con `POST /api/v1/platform/tenants/{id}/owner/resend-activation`.

**Passo 8 — Verifica end-to-end** che tutto sia collegato:
1. Con la API key, il widget legge la configurazione del tenant e i servizi (le rotte pubbliche → vedi
   `GUIDA_INTEGRAZIONE_API.md`).
2. Fai una **prenotazione di prova** dal sito.
3. Controlla che arrivi l'**email di conferma** (dev: Mailpit; prod: casella reale).
4. Verifica che l'**Owner riesca a fare login** e a vedere la prenotazione dal proprio pannello/script.

**Passo 9 — Archivia** il file JSON del cliente (in una cartella privata) per riferimento futuro, e consegna
all'agenzia/cliente le credenziali e le note operative.

> ⚠ **Modificare un tenant già creato:** la ri-esecuzione del provisioning sullo stesso slug **non è supportata**
> in V1 (`--update` rifiutato). Le modifiche successive (aggiungere un servizio, cambiare orari, ecc.) si fanno
> via **Admin API** con il JWT dell'Owner, non ricreando il tenant.

---

## 7. CORS (perché in genere non devi toccarlo)

Le origini ammesse dal browser derivano **automaticamente** dai `siteUrl` dei tenant attivi: un job in background
(`TenantOriginRefreshJob`) aggiorna l'allowlist ogni `Cors:OriginRefreshSeconds` (default 60s). Quindi **onboardare
un nuovo sito non richiede modifiche di configurazione** — basta che il tenant abbia il `siteUrl` corretto.

Aggiungi voci a `Cors__AllowedOrigins__N` **solo** per origini extra che non sono `siteUrl` di un tenant
(es. un dominio di staging, un tool interno).

---

## 8. Job in background (i "demoni" che devono restare attivi)

Il servizio non è solo request/response: ci sono `BackgroundService` sempre attivi. **Per questo l'istanza non
deve dormire.** Elenco e scopo:

| Job | File | Cosa fa | Config |
|---|---|---|---|
| `EmailOutboxDispatcher` | Infrastructure/Email | Invia le email accodate nell'outbox con retry/backoff | `Email:Outbox:PollSeconds` (30) |
| `ReminderJob` | Infrastructure/Services | Invia i promemoria pre-appuntamento | `Reminder:PollMinutes` (15); `Tenant.ReminderHoursBefore` (24, 0=off) |
| `DataRetentionJob` | Infrastructure/Services | Anonimizza PII vecchie e purga outbox inviate (GDPR) | `Gdpr:RetentionDays` (365), `Gdpr:OutboxRetentionDays` (30), `Gdpr:PollHours` (24) |
| `ExpiredBookingCleanupJob` | Infrastructure/Services | Pulisce prenotazioni scadute/incomplete | `CleanupJob:IntervalMinutes` (60) |
| `TenantOriginRefreshJob` | Infrastructure/Cors | Ricostruisce l'allowlist CORS dai `siteUrl` dei tenant | `Cors:OriginRefreshSeconds` (60) |
| `LogRetentionJob` | Api/Logging | Purga la tabella `logs` oltre la retention | `DatabaseLogging:RetentionDays` (90) |
| `OpsAlertMonitorJob` | Infrastructure/Observability | Scansiona i log per errori e rileva il DB-down, invia alert | `Ops:Alerting:PollSeconds` (60), `MinLevel` (Error) |

> ⚠ **Assunzione singola istanza** per i job basati su stato in-memory (in particolare `OpsAlertMonitorJob`, che
> tiene watermark/stato in memoria). Se scali orizzontalmente, questi job vanno ripensati (deduplica/leader election).

---

## 9. Dove trovare i log

Serilog scrive su **due sink additivi**, in tutti gli ambienti:

### 9.1 Console (stdout)
Catturata dalla piattaforma (Railway → tab **Logs** / Deploy logs). **Non si perde mai**, nemmeno durante un
incidente del DB. È il primo posto dove guardare per errori di avvio e crash.

### 9.2 Tabella `logs` nel database
Il sink Postgres persiste i log dal livello `Information` in su nella tabella **`logs`** (auto-creata, con una
connessione Npgsql propria del sink — non EF — così l'INSERT dei log non si auto-logga). Interrogabile via SQL:

```sql
-- Ultimi errori
SELECT timestamp, level, message, exception, request_id
FROM logs
WHERE level IN ('Error','Fatal')
ORDER BY timestamp DESC
LIMIT 100;

-- Tutti i log di una richiesta specifica (usa l'header X-Trace-Id restituito al client)
SELECT * FROM logs WHERE request_id = '<trace-id>' ORDER BY timestamp;
```
Colonne principali: `timestamp, level, message, message_template, exception, properties (jsonb), application,
environment, request_id`. Retention: `LogRetentionJob` purga oltre `DatabaseLogging:RetentionDays` (90 giorni).

> **Correlazione:** ogni response HTTP porta l'header **`X-Trace-Id`**. È la chiave per ritrovare in `logs` tutto
> ciò che riguarda una singola richiesta — chiedilo sempre in una segnalazione di bug.

### 9.3 Cosa NON troverai nei log (per scelta GDPR)
I log sono **PII-free**: la request logging **non** logga l'IP e i parametri SQL di EF sono mascherati. In
produzione i log EF stanno a `Warning` (evita il flood di SQL nella tabella `logs`).

### 9.4 `audit_log` è un'altra cosa
La tabella **`audit_log`** registra **eventi di business** (`tenant_created`, `booking_created`, cambi di stato…),
non i log applicativi. Serve per tracciabilità/audit, non per il debug tecnico. Gli audit sono PII-free
(es. `subjectRef` HMAC per i soggetti GDPR).

### 9.5 Alert proattivi (OPS)
`OpsAlertMonitorJob` scansiona la tabella `logs` per errori `>= MinLevel` e rileva la transizione DB-down,
recapitando un `OpsAlert` sul canale configurato:
- **`LogOnly`** (default): scrive una riga `[OPS-ALERT]` su console/DB. Nessuna dipendenza esterna.
- **`Telegram`**: manda il messaggio a un bot/chat (richiede `OPS_ALERT_TELEGRAM_BOT_TOKEN` + `_CHAT_ID`).

Per l'uptime dall'esterno, punta un monitor (UptimeRobot o simile) su `GET /api/v1/health/live`.

---

## 10. Email: dove verificare e come si comportano

- **Sviluppo:** provider `Smtp` verso **Mailpit** (docker-compose). Tutte le email vengono **catturate** e mostrate
  su `http://localhost:8025` — non raggiungono destinatari reali, nessun mittente da verificare.
- **Produzione:** provider `Brevo` (REST). Il **mittente deve essere verificato** su Brevo o le email non partono.
- **Meccanica:** invio via **outbox transazionale** + dispatcher in background con retry. Se un'email non arriva:
  1. Controlla la tabella outbox (`OutboxEmail`) — stato `Failed`/`Pending` e ultimo errore.
  2. Controlla i log per errori del dispatcher.
  3. Verifica `EMAIL_PROVIDER`, `BREVO_API_KEY`, `BREVO_SENDER_EMAIL` (verificato), `PUBLIC_BASE_URL` (per i link).

---

## 11. Backup e GDPR

- **Backup DB:** usa gli **snapshot/backup del Postgres managed di Railway**. Prima di migration distruttive o
  operazioni di massa, fai uno snapshot manuale.
- **Retention automatica (GDPR):** `DataRetentionJob` anonimizza le PII delle prenotazioni oltre `Gdpr:RetentionDays`
  e purga le outbox inviate oltre `Gdpr:OutboxRetentionDays`.
- **DSAR on-demand:** esistono endpoint admin per **export** (diritto d'accesso) e **cancellazione** dei dati di un
  cliente per email (l'erasure = anonimizzazione delle prenotazioni + eliminazione outbox del cliente, atomica).
  Dettagli e basi legali in `GDPR_COMPLIANCE.md`.

---

## 12. Manutenzione del codice

- **Build pulita prima di completare qualsiasi cosa:** `dotnet build` deve essere **verde** (0 warning; il progetto
  ha analyzer + warnings-as-errors).
- **Test:** `dotnet test`. Suite unit + integration (queste ultime su Postgres reale via Testcontainers → serve Docker).
- **Nuova migration** (dopo aver cambiato un'entità/DbContext):
  ```bash
  dotnet ef migrations add <NomeMigrazione> \
    --project src/WebAgency_BookingSystem.Infrastructure \
    --startup-project src/WebAgency_BookingSystem.Api
  ```
  Poi applicala (`database update`) e committала insieme al codice che la richiede.
- **Convenzioni** (rispettarle, sono verificate in review): `[INTENT]` in testa a ogni file; `// WHY:` sulle
  logiche non ovvie; XML `/// <summary>` sui membri pubblici; DTO come `record` immutabili; errori di business con
  `Result<T>` (niente eccezioni per flussi attesi); `async/await` + `CancellationToken`; PK `Guid`; timestamp
  `DateTimeOffset` UTC in storage ma **orari locali del tenant** nelle response; isolamento tenant via global
  query filter; **messaggi di errore in italiano**.
- **Documentazione allineata:** ogni modifica che tocca architettura/endpoint/schema/decisioni aggiorna `CLAUDE.md`
  e/o `DEVELOPMENT_PLAN.md` nello **stesso commit**.

---

## 13. Troubleshooting rapido

| Sintomo | Causa probabile / dove guardare |
|---|---|
| L'app non parte in produzione, errore su JWT | `JWT_SECRET` mancante o contiene `change-me` (guard S5 in `Production`). Imposta un segreto reale ≥32 char. |
| L'app non parte, errore su DATABASE_URL | `DATABASE_URL` vuota o mancante (trattata come errore chiaro all'avvio). Verifica la variabile di riferimento del Postgres. |
| `/health` → 503 ma `/health/live` → 200 | Il processo è vivo ma **il DB non risponde**. Controlla il Postgres managed e la connection string. |
| Le email non arrivano (prod) | Mittente **non verificato** su Brevo, `BREVO_API_KEY` errata, o outbox in stato `Failed`. Vedi §10. |
| I link nelle email portano nel vuoto | `PUBLIC_BASE_URL` errata o non impostata. |
| `POST /platform/setup` → 404 | `PLATFORM_SETUP_TOKEN` non impostato (comportamento voluto). Impostalo per il bootstrap, poi rimuovilo. |
| Login admin fallisce dopo un deploy | Cambi ai fix JWT (`MapInboundClaims=false`, `KeyId`): rifai lo **smoke test login** (§5.5). Ricorda che cambio/reset password invalidano i vecchi token (SecurityStamp). |
| Il widget del sito riceve errori CORS | Il `siteUrl` del tenant non combacia con l'origine reale del sito, oppure attendi il refresh (fino a 60s). Per domini extra usa `Cors__AllowedOrigins__N`. |
| Un cliente segnala un bug su una richiesta | Fatti dare l'header **`X-Trace-Id`** e cerca in `logs` per `request_id` (§9.2). |
| Alert OPS non arrivano su Telegram | `OPS_ALERT_CHANNEL=Telegram` ma manca `OPS_ALERT_TELEGRAM_BOT_TOKEN`/`_CHAT_ID`, oppure sei su più istanze (job single-instance). |

---

## 14. Rimandi

| Argomento | Documento |
|---|---|
| Contratto API completo (pubbliche, admin, platform) | `GUIDA_INTEGRAZIONE_API.md` |
| Checklist deploy Railway passo-passo | `DEPLOY_RAILWAY.md` |
| Schema del file di provisioning tenant | `05-provisioning-e-struttura.md` |
| Compliance GDPR (ruoli, data-flow, DSAR, retention) | `GDPR_COMPLIANCE.md` |
| Architettura e stack | `01-architettura-e-stack.md` |
| Stato del progetto e decisioni | `CLAUDE.md`, `DEVELOPMENT_PLAN.md` |
| Visione di prodotto e scope | `VISIONE_PRODOTTO_E_ROADMAP.md` |
