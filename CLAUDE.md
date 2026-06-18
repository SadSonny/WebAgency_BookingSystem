# WebAgency BookingSystem — Riferimento per Claude

## Stato Corrente del Progetto

> **V1 VALIDATA A RUNTIME (Sezioni 1→7 + sessione Docker 2026-06-13).**
> Implementati e validati: infra, Core, Infrastructure (DbContext + global query filter, config EF, **migrazione `InitialSchema`
> applicata**, repository, cache, interceptor, email stub), middleware (tenant resolution, JWT admin,
> rate limiting, error handling, correlation, Serilog), **endpoint pubblici 5.1-5.8**, **Admin API 6.1-6.14**
> (JWT + CRUD servizi/staff/orari/chiusure/prenotazioni), **CLI provisioning** (Sezione 7).
> Build `dotnet build` verde (0 warning, analyzer + warnings-as-errors). 41 unit test verdi.
> Tutti gli endpoint validati a runtime con Docker. Nessun bug trovato.

**Fase attuale:** V1 + **V2 email** + **hardening PH-1..PH-5** + **V2.1 salone reale** + **V2.2 hardening perf/sicurezza** + **V2.3 onboarding Owner** (2026-06-16) + **Agency Provisioning API** (2026-06-18). **131 test verdi** (96 unit + 35 integration).

**V2.2 — Hardening (2026-06-15):** Performance — batch query per disponibilità/booking "qualsiasi operatore" (P1/P2), reminder con finestra di scan ristretta + indice (P3), **paginazione** `GET /admin/bookings` (`page`/`pageSize`, `PagedResponse<T>`) (P4). Sicurezza — rate-limit dedicato sulla **creazione** prenotazioni per API key (`RateLimiting:BookingPerMinute`, default 10) (S1); **retention/erasure GDPR** (`DataRetentionJob`: anonimizza PII prenotazioni oltre `Gdpr:RetentionDays`=365, purga outbox inviate oltre `Gdpr:OutboxRetentionDays`=30) (S2); **lockout login admin** (5 tentativi → 15 min, campi `User.FailedAccessCount`/`LockoutEnd`) (S3); **rotazione/revoca API key** via `GET/POST/DELETE /admin/api-keys` (S4); **guard JWT** in produzione (S5). `DbContext` pooling escluso (R-24). 2 migration nuove (`AddReminderScanIndex`, `AddUserLockout`).

**V2.1 — Salone reale (2026-06-15):** assenze per operatore (`StaffTimeOff`, admin `/admin/staff/{id}/time-off`); **"qualsiasi operatore" auto-assegnato** con disponibilità aggregata sulle agende reali (`parallelSlots` solo per servizi senza operatori); **appuntamento multi-servizio** un operatore (`POST /bookings` con `additionalServiceIds`, durata/prezzo sommati, `BookingItem`); cancellazione admin → email cliente; **reschedule** cliente (`PUT /bookings/{id}/reschedule?token=`); **reminder** pre-appuntamento (`Tenant.ReminderHoursBefore`, default 24). 3 migration nuove. Follow-up: disponibilità combinata combo, template email multi-servizio, reschedule admin. Dettagli in `Claude_Instructions/DEVELOPMENT_PLAN.md` §V2.1.

**Email (Sezione 8, AD-10/AD-11 + PH-3):** provider per ambiente via `Email:Provider` — **Mailpit in dev** (SMTP locale, cattura, zero verifica mittente; UI `http://localhost:8025`), **Brevo in prod** (REST). Template HTML inline italiani (`EmailTemplateRenderer`). **Invio via OUTBOX transazionale** (PH-3): l'email è accodata (`OutboxEmail`) nella transazione del booking e inviata da `EmailOutboxDispatcher` con retry/backoff; trasporto `IEmailSender` (Mailpit/Brevo/Null) separato dal contenuto. Admin UI **non** prevista (AD-09); unica UI futura = dashboard interna dev (Sezione 10, rimandata).

**V2.3 — Onboarding credenziali Owner (2026-06-16):** Login per email globale (rimosso `tenantSlug` — breaking change); provisioning crea Owner **senza password** e accoda **email di attivazione** con link (token hash in DB, 72h); CLI non stampa più password. Nuovi endpoint `/api/v1/admin/account/*`: attivazione account, cambio password autenticato, reset password via email (risposta neutra), pagine HTML set-password servite dall'API (**deroga AD-09** circoscritta: solo pagine tecniche, non una dashboard). **SecurityStamp** nel JWT: al cambio/reset/attivazione lo stamp si rigenera, i vecchi token vengono invalidati (validazione cache-first). Policy password configurabile (min 12 char). Rate-limit dedicato per IP su rotte account+login (`AccountSecurity`, default 10/min). Config `PUBLIC_BASE_URL` (base dei link email). 2 migration nuove (`MakeEmailGlobalAndAddSecurityFields`, `AddUserSecurityTokens`). Fix JWT: `MapInboundClaims=false` + `KeyId` stabile (⚠ eseguire smoke test login admin al deploy). 6 nuovi test flusso account.

**Hardening produzione (2026-06-15, PH-1..PH-5):** CORS per-tenant dinamico dai `siteUrl` (catalogo + refresh job); advisory lock **bloccante** con `lock_timeout` (no 409 spurio su `parallelSlots>1`); email outbox durevole; user-secrets dev; confronti **DST-corretti** (`TenantTime.ToInstant`). DbContext pooling NON adottato (motivato nel piano).

**Agency Provisioning API (2026-06-18):** Nuova identità di piattaforma `PlatformAdmin` (separata dai tenant, niente `TenantId`). Login `POST /api/v1/platform/auth/token`. Bootstrap break-glass via `POST /api/v1/platform/setup` (gated da env `PLATFORM_SETUP_TOKEN`). Endpoint completi per creare/elencare/disattivare tenant, gestire API key, resend attivazione Owner — nessuna CLI né accesso DB necessari. Logica provisioning unificata in `ITenantProvisioningService` (CLI + API). `TenantResolutionMiddleware` ora esclude `/api/v1/platform`. 1 migration nuova (`AddPlatformAdmin`). 4 nuovi test `PlatformFlowTests` (integration). Follow-up rimandati: invito multi-admin, reset platform via email, edit tenant, audit completo per-sorgente. Dettagli in `Claude_Instructions/DEVELOPMENT_PLAN.md` §Agency Provisioning.

**Prossimo task:** Railway deploy — applicare le migration incl. `AddPlatformAdmin`; impostare `PLATFORM_SETUP_TOKEN` per il bootstrap admin piattaforma; impostare `EMAIL_PROVIDER=Brevo` + `BREVO_API_KEY`/`BREVO_SENDER_EMAIL` con mittente verificato; impostare `PUBLIC_BASE_URL` (es. `https://<servizio>.railway.app`); eseguire **smoke test** (setup platform → login platform → crea tenant → verifica API key + attivazione Owner). Poi 8.7 (branding template). Dubbi/decisioni aperte in `Claude_Instructions/DUBBI_SESSIONE.md` (D-01…D-15).

**Note runtime (da `Claude_Instructions/DOCKER_SESSION_TODO.md`):**
- API porta **5022** (launchSettings.json profilo `http`)
- DTO `PUT /admin/business-hours`: body `{ "days": [...] }` (non array bare)
- DTO `PUT /admin/closures`: body `{ "closures": [...] }` (non array bare)

---

## Guida per Nuove Sessioni AI

Quando apri questo progetto in una nuova sessione, segui questo ordine **prima di scrivere qualsiasi codice**:

1. **Leggi `CLAUDE.md`** (questo file) — architettura, regole, convenzioni, stato.
2. **Leggi `Claude_Instructions/DEVELOPMENT_PLAN.md`** — trova il primo task con checkbox `[ ]` non completato. Quello è il punto di partenza.
3. **Prima di implementare un feature, leggi la spec corrispondente** in `Claude_Instructions/`:
   - Entità / schema DB → `02-schema-database.md`
   - Endpoint → `03-spec-endpoint.md`
   - Algoritmo disponibilità → `04-logica-disponibilita.md`
   - CLI provisioning → `05-provisioning-e-struttura.md`
   - Architettura generale → `01-architettura-e-stack.md`
4. **Non iniziare a scrivere codice senza consenso esplicito dell'utente** nella sessione corrente. Presenta il piano del task e attendi il "vai".
5. **Dopo ogni task completato**: spunta il checkbox in `Claude_Instructions/DEVELOPMENT_PLAN.md`, aggiungi voce al Changelog, fai commit.

### Domande già risolte — non ripetere
Le seguenti decisioni sono state prese dall'utente e non vanno rinegoziare:

| Argomento | Decisione |
|---|---|
| Versione .NET | .NET 10 (i docs dicono 9, ma i .csproj usano net10.0 — corretto così) |
| Email (Brevo) | Nessuna API key — V1 usa stub no-op; V2 integra Brevo quando la chiave sarà disponibile |
| Staff opzionale | `staff_id` nullable in `bookings`; se non scelto, la disponibilità si basa su `parallelSlots` del servizio |
| Buffer | Per servizio (non per tenant); 3 campi: `bufferEnabled`, `bufferMinutes`, `bufferPosition` |
| Admin layer | JWT auth + CRUD completo da V1 (non solo stub) |
| Test | Scritti dopo l'implementazione funzionale, non in TDD |
| Docker | PostgreSQL + pgAdmin in Docker Compose |
| Errori API | In italiano |
| CLI provisioning | Inclusa in V1 (necessaria per bootstrapping tenant di test) |

---

## Overview

Backend centralizzato multi-tenant per la gestione di prenotazioni di attività locali italiane (barbershop, estetica, medici, ecc.). Ogni tenant è un'attività commerciale con proprie configurazioni, servizi, staff e regole di prenotazione.

> **Posizionamento (importante):** il **cliente del prodotto è un'agenzia web** che realizza siti e li collega a questo backend; **non** l'attività finale. L'architettura **headless/API-only (AD-09) è deliberata**: widget di prenotazione e pannello Owner li costruisce l'agenzia. UI di prodotto, pagamenti interni, billing e sync calendario sono **fuori scope per scelta**. Dettagli, roadmap e voci aperte in [`Claude_Instructions/VISIONE_PRODOTTO_E_ROADMAP.md`](Claude_Instructions/VISIONE_PRODOTTO_E_ROADMAP.md).

## Stack

| Componente | Tecnologia |
|---|---|
| Runtime | .NET 10 / ASP.NET Core Minimal API |
| ORM | EF Core 10 + Npgsql 10 |
| Database | PostgreSQL 16+ |
| Logging | Serilog (structured, GDPR-compliant) |
| Rate Limiting | Microsoft.AspNetCore.RateLimiting |
| Auth pubblica | API Key (header `X-Api-Key`) |
| Auth admin | JWT Bearer |
| Email | Brevo REST API (V2 — stub no-op in V1) |
| Dev infra | Docker Compose (PostgreSQL + pgAdmin) |
| Prod | Railway (EU West) |

## Struttura Progetti

```
WebAgency_BookingSystem/
├── src/
│   ├── WebAgency_BookingSystem.Api/            ← Minimal API, middleware, endpoints, DI
│   ├── WebAgency_BookingSystem.Core/           ← Entità, interfacce, DTOs, Result<T>
│   └── WebAgency_BookingSystem.Infrastructure/ ← DbContext, repository, email
├── tests/
│   ├── WebAgency_BookingSystem.UnitTests/      ← Unit test (V2)
│   └── WebAgency_BookingSystem.IntegrationTests/ ← Integration test Testcontainers (V2)
├── tools/
│   └── WebAgency_BookingSystem.TenantProvisioning/ ← CLI JSON-driven
├── Claude_Instructions/                        ← TUTTA la documentazione (.md) tranne CLAUDE.md:
│   ├── 00-indice.md … 05-provisioning-e-struttura.md ← Spec originali (riferimento immutabile)
│   ├── DEVELOPMENT_PLAN.md                      ← Piano e tracciamento avanzamento
│   ├── GUIDA_INTEGRAZIONE_API.md                ← Guida integrazione siti esterni (API/onboarding/CLI)
│   ├── DUBBI_SESSIONE.md · DOCKER_SESSION_TODO.md · CODE_REVIEW_FINDINGS.md
└── CLAUDE.md                                   ← Questo file (unico .md nella root)
```

## Regole di Comportamento per Agenti AI

Queste regole si applicano a **ogni sessione di sviluppo**, senza eccezioni salvo consenso esplicito dell'utente.

### Qualità e Architettura
- **SOLID sempre**: ogni file, classe e metodo deve rispettare Single Responsibility, Open/Closed, Liskov, Interface Segregation, Dependency Inversion. Qualsiasi deroga richiede consenso esplicito dell'utente con motivazione scritta.
- **Scalabilità e manutenibilità**: ogni scelta implementativa deve essere pensata per crescere. Preferire astrazioni stabili, dipendenze invertite, comportamenti sostituibili via DI.
- **Ottimizzato per AI**: il codice deve essere leggibile e navigabile da agenti AI. Struttura prevedibile, nomi espliciti, nessuna "magia" implicita. Un agente deve capire cosa fa un file leggendo solo il suo `[INTENT]` e le firme pubbliche.

### Commenti Obbligatori

#### `// [INTENT]` — intestazione di ogni file
Ogni file sorgente **deve** iniziare con un commento `// [INTENT]: <descrizione breve>` che spiega **cosa fa questo file** e **perché esiste**. Va aggiornato ogni volta che la responsabilità del file cambia. Serve a minimizzare i token necessari a un agente AI per orientarsi nel codebase.

```csharp
// [INTENT]: Middleware che risolve il tenant corrente dall'header X-Api-Key e lo inietta
// nel contesto HTTP. Blocca la richiesta con 401 se la chiave non è valida o il tenant
// è disattivato. Prerequisito per tutti gli endpoint tenant-scoped.
```

#### Commenti `// WHY:` per logiche non ovvie
Ogni implementazione non immediatamente comprensibile **deve** avere un commento che spiega il **perché**, non il cosa. Il cosa lo dicono già i nomi. Il perché lo sa solo chi ha progettato il sistema.

```csharp
// WHY: usiamo pg_try_advisory_xact_lock invece di un SELECT FOR UPDATE perché
// vogliamo evitare lock escalation su righe non ancora esistenti (slot non ancora prenotato).
// L'hash combina tenant+service+date+time per garantire granularità minima.
await db.Database.ExecuteSqlRawAsync("SELECT pg_try_advisory_xact_lock({0})", lockKey);
```

#### XML summary sui membri pubblici
Tutti i metodi e le proprietà pubbliche di interfacce e servizi devono avere `/// <summary>` che descrive il **contratto** (cosa garantisce, non come lo fa).

### Documentazione OpenAPI — Regola Obbligatoria

La documentazione API è generata **automaticamente a runtime** dal codice (.NET 10 built-in OpenAPI + Scalar UI). È sempre aggiornata perché riflette direttamente gli endpoint registrati. Per mantenerla ricca e utile, ogni endpoint **deve** includere i seguenti metadati:

```csharp
app.MapGet("/api/v1/services", handler)
    .WithName("GetServices")                          // OperationId univoco
    .WithSummary("Lista servizi attivi")              // Titolo breve (max 80 char)
    .WithDescription("Restituisce tutti i servizi...") // Descrizione estesa
    .WithTags("Servizi")                              // Gruppo nel menu laterale
    .Produces<IEnumerable<ServiceResponse>>(200)      // Risposta di successo tipizzata
    .ProducesProblem(401)                             // Risposta di errore
    .RequireAuthorization();                          // Se richiede auth
```

**Regole:**
- `WithName`: sempre presente, camelCase, unico in tutta l'API
- `WithSummary`: sempre presente — è il testo che appare nell'elenco endpoint
- `WithDescription`: obbligatorio per endpoint con logica non ovvia (availability, bookings)
- `WithTags`: raggruppa per area funzionale (`"Sistema"`, `"Servizi"`, `"Staff"`, `"Prenotazioni"`, `"Admin"`)
- `Produces<T>`: sempre tipizzato — mai `IResult` generico; usare `TypedResults.Ok<T>()` nel handler

**Note tecniche:**
- Namespace corretto (v2.0): `using Microsoft.OpenApi;` — non `Microsoft.OpenApi.Models` (rimosso in v2.0)
- `Microsoft.OpenApi 2.0.0` deve essere dipendenza diretta nel `.csproj` (non solo transitiva)

**URL della UI:** `http://localhost:5000/scalar` in sviluppo

### Processo di Sviluppo

- **Scomponi sempre**: ogni task deve essere spezzata in sotto-task atomiche prima di eseguire. Non iniziare a scrivere codice finché la scomposizione non è chiara.
- **Commit frequenti**: commit dopo ogni sotto-task completata e verificata. Mai accumulare modifiche non committate su più file logicamente indipendenti.
- **Build verde prima di completare**: prima di dichiarare una task completata, eseguire `dotnet build`. Se ci sono errori, risolverli e ripetere. Una task è completa solo quando la build è pulita.
- **Documentazione allineata**: ogni modifica che impatta architettura, endpoint, schema o decisioni deve aggiornare `CLAUDE.md` e/o `Claude_Instructions/DEVELOPMENT_PLAN.md` nello stesso commit.
- **Nessuna implementazione parziale**: non lasciare metodi vuoti, `TODO` non tracciati, o stub non documentati come tali. Se qualcosa è intenzionalmente incompleto (es. V2), deve essere una classe/interfaccia esplicita con `[INTENT]` che lo dice.

### Quando Derogare alle Regole
Se una regola deve essere violata (es. performance critica che richiede accoppiamento stretto), l'agente deve:
1. Fermarsi e chiedere consenso esplicito all'utente
2. Spiegare perché la regola standard non si applica in quel caso
3. Documentare la deroga con un commento `// EXCEPTION: <motivazione>` nel codice e in `Claude_Instructions/DEVELOPMENT_PLAN.md`

---

## Convenzioni di Codice

- **DTOs**: `record` immutabili
- **Errori business**: `Result<T>` pattern (nessuna eccezione per flussi attesi)
- **Async**: `async/await` ovunque con `CancellationToken`
- **PK**: `Guid` (UUID v4)
- **Timestamp**: `DateTimeOffset` (TIMESTAMPTZ in PostgreSQL), sempre UTC in storage
- **Orari API**: orari locali del tenant nelle response (mai UTC esposto al cliente)
- **Tenant isolation**: Global Query Filter su `tenant_id` in ogni entità tenant-scoped
- **Soft delete**: dove indicato nello schema (services, staff)
- **Lingua errori**: Italiano nelle response API
- **Commenti**: solo per logiche non ovvie (invarianti, workaround, vincoli nascosti)

## Decisioni Architetturali Chiave

Vedi `Claude_Instructions/DEVELOPMENT_PLAN.md` §Decisioni Architetturali per la lista completa con motivazioni.

Sommario rapido:
- Admin credentials nella tabella `users` (multi-utente per tenant, predisposizione ruoli)
- Buffer configurabile **per servizio** (`bufferEnabled`, `bufferMinutes`, `bufferPosition`)
- Staff opzionale: `staff_id = NULL` in booking se non selezionato/non applicabile
- Disponibilità senza staff regolata da `parallelSlots` del servizio
- `IEmailService` no-op in V1, Brevo in V2 (swap senza impatto su callers)

## Setup Sviluppo Locale

```bash
# 1. Avvia infrastruttura
docker compose up -d

# 2. Applica migrazioni
dotnet ef database update \
  --project src/WebAgency_BookingSystem.Infrastructure \
  --startup-project src/WebAgency_BookingSystem.Api

# 3. Avvia API
dotnet run --project src/WebAgency_BookingSystem.Api

# 4. Provisioning tenant di test
dotnet run --project tools/WebAgency_BookingSystem.TenantProvisioning \
  -- --file samples/barbershop-demo.json
```

- API: `http://localhost:5000`
- pgAdmin: `http://localhost:5050` (email: `admin@admin.com`, password: `admin`)

### Segreti di sviluppo — User Secrets (PH-4)

I valori in `appsettings.Development.json` sono **default locali non sensibili** (DB di docker-compose, JWT
secret di sviluppo marcato `change-me`) e servono solo a far girare il progetto out-of-the-box. Per qualsiasi
**segreto reale in locale** usa gli **User Secrets** (.NET), che non finiscono nel repo e **sovrascrivono**
`appsettings.Development.json` in ambiente Development (il progetto `Api` ha già `UserSecretsId` configurato):

```bash
cd src/WebAgency_BookingSystem.Api
dotnet user-secrets set "Jwt:Secret" "<segreto-random-min-32-char>"
dotnet user-secrets set "ConnectionStrings:Database" "<connection-string>"
dotnet user-secrets set "Email:Brevo:ApiKey" "<xkeysib-...>"
```

In **produzione** non si usano né appsettings né user-secrets: solo **variabili d'ambiente** (vedi tabella sotto).

## Variabili d'Ambiente

| Variabile | Descrizione | Esempio locale |
|---|---|---|
| `DATABASE_URL` | PostgreSQL connection string | `Host=localhost;Port=5432;Database=bookingsystem;Username=postgres;Password=postgres` |
| `JWT_SECRET` | Segreto per firma JWT admin (min 32 char) | `<generato-random>` |
| `JWT_EXPIRY_HOURS` | Validità token JWT (default: 8) | `8` |
| `RATE_LIMIT_PER_MINUTE` | Richieste max/min per API key (default: 100) | `100` |
| `BREVO_API_KEY` | API key Brevo (V2) | `xkeysib-...` |
| `BREVO_SENDER_EMAIL` | From address email (V2) | `noreply@dominio.com` |
| `BREVO_SENDER_NAME` | From name email (V2) | `BookingSystem` |
| `PUBLIC_BASE_URL` | URL base assoluta dell'API, usata per costruire i link nelle email (attivazione, reset password) | `http://localhost:5022` |
| `RATE_LIMIT_ACCOUNT_PER_MINUTE` | Richieste max/min per IP sulle rotte account+login (default: 10) | `10` |
| `DB_AUTO_MIGRATE` | Applica le migration EF all'avvio dell'API (opt-in; default `false`). In Development è già `true` via `appsettings.Development.json`. In produzione lasciare `false` se più istanze girano in parallelo (preferire uno step di migrazione nella pipeline) | `true` |
| `PLATFORM_SETUP_TOKEN` | Token segreto che abilita l'endpoint di bootstrap `POST /api/v1/platform/setup`. Se non configurato, l'endpoint risponde **404** (break-glass: da usare solo al primo avvio per creare o reimpostare l'admin di piattaforma) | `<token-segreto-random>` |
| `Jwt__PlatformAudience` | Audience del JWT emesso per `PlatformAdmin` (default: `WebAgency_BookingSystem.Platform`). Deve differire dall'audience admin tenant per evitare token cross-use | `WebAgency_BookingSystem.Platform` |

## Logging

I log applicativi sono gestiti da **Serilog** con **due sink additivi**, in tutti gli ambienti:
- **Console** (stdout) — catturata dalla piattaforma (Railway). Non si perde mai, anche durante incidenti del DB.
- **PostgreSQL** (`Serilog.Sinks.Postgresql.Alternative`) — persiste i log dal livello `Information` in su nella tabella **`logs`** (auto-creata, **separata** da `audit_log`), interrogabile via SQL. Scritture in **batch** con connessione Npgsql propria del sink (non EF), quindi l'INSERT dei log non si auto-logga. Colonne: `timestamp, level, message, message_template, exception, properties (jsonb), application, environment, request_id`. Config in [DatabaseLogSink.cs](src/WebAgency_BookingSystem.Api/Logging/DatabaseLogSink.cs)/[DatabaseLogSettings.cs](src/WebAgency_BookingSystem.Api/Logging/DatabaseLogSettings.cs).
- **Retention**: `LogRetentionJob` (BackgroundService, giornaliero) purga la tabella `logs` oltre `DatabaseLogging:RetentionDays` (default **90 giorni**).
- **GDPR**: i log sono PII-free (la request logging non logga l'IP; i parametri SQL EF sono mascherati). La tabella `logs` rientra nella retention.
- **`audit_log`** (tabella DB) è una cosa diversa: registro di **eventi di business** (es. `tenant_created`, `booking_created`), non i log applicativi.

Config (sezione `DatabaseLogging` in `appsettings.json`): `Enabled` (default `true`), `MinimumLevel` (`Information`), `RetentionDays` (`90`), `Table` (`logs`, validato whitelist). Disattivato nei test di integrazione. In produzione i log EF restano a `Warning` (vedi `Serilog:MinimumLevel:Override`), evitando flood di SQL nella tabella.

## Endpoint API — Sommario

### Pubblici (autenticazione: `X-Api-Key`)
```
GET    /api/v1/health
GET    /api/v1/tenant/config
GET    /api/v1/services
GET    /api/v1/staff
GET    /api/v1/availability
POST   /api/v1/bookings
GET    /api/v1/bookings/{id}?token=...
DELETE /api/v1/bookings/{id}?token=...
```

### Admin (autenticazione: JWT Bearer)
```
POST   /api/v1/admin/auth/token             body: { email, password }  ← login per email globale (no tenantSlug)
GET    /api/v1/admin/bookings
PATCH  /api/v1/admin/bookings/{id}
GET|POST|PUT|DELETE  /api/v1/admin/services
GET|POST|PUT|DELETE  /api/v1/admin/staff
PUT    /api/v1/admin/business-hours
PUT    /api/v1/admin/closures
GET|POST|DELETE      /api/v1/admin/api-keys

# Gestione account Owner (V2.3)
GET    /api/v1/admin/account/activate?token=      → pagina HTML "imposta password"
POST   /api/v1/admin/account/activate             body: { token, newPassword }  → 204
POST   /api/v1/admin/account/password             body: { currentPassword, newPassword }  → 204  (JWT)
POST   /api/v1/admin/account/password/reset-request  body: { email }  → 202 (sempre, risposta neutra)
GET    /api/v1/admin/account/password/reset?token=   → pagina HTML reset password
POST   /api/v1/admin/account/password/reset       body: { token, newPassword }  → 204
```

### Platform (autenticazione: JWT PlatformAdmin — identità separata dai tenant)

> L'identità `PlatformAdmin` **non appartiene ad alcun tenant** (`TenantId` = null). Il JWT è emesso con audience
> dedicata (`Jwt:PlatformAudience`) e non è accettato sulle rotte admin tenant, e viceversa.
> `TenantResolutionMiddleware` esclude automaticamente tutto `/api/v1/platform` (non usa `X-Api-Key`).

```
# Bootstrap/break-glass (anonimo, 404 se PLATFORM_SETUP_TOKEN non configurato)
POST   /api/v1/platform/setup              body: { setupToken, email, password }  → crea-o-reimposta admin

# Autenticazione (anonima)
POST   /api/v1/platform/auth/token         body: { email, password }  → JWT PlatformAdmin

# Account platform (JWT PlatformAdmin)
POST   /api/v1/platform/account/password   body: { currentPassword, newPassword }  → 204

# Gestione tenant (JWT PlatformAdmin)
POST   /api/v1/platform/tenants            body: ProvisioningInput  → 201 + API key (una sola volta); 409 slug; 422 validazione
GET    /api/v1/platform/tenants            ?page=&pageSize=  → paginato
GET    /api/v1/platform/tenants/{id}
POST   /api/v1/platform/tenants/{id}/deactivate
POST   /api/v1/platform/tenants/{id}/reactivate
GET    /api/v1/platform/tenants/{id}/api-keys
POST   /api/v1/platform/tenants/{id}/api-keys      → 201 + chiave in chiaro (una sola volta)
DELETE /api/v1/platform/tenants/{id}/api-keys/{keyId}   → 204
POST   /api/v1/platform/tenants/{id}/owner/resend-activation  → 202 (re-accoda email attivazione)
```

## Riferimenti

Tutta la documentazione vive in `Claude_Instructions/` (unico `.md` nella root è questo `CLAUDE.md`):
- **Visione di prodotto, scope e roadmap**: `Claude_Instructions/VISIONE_PRODOTTO_E_ROADMAP.md` (chi è il cliente, cosa è dentro/fuori scope e perché)
- Spec originali: `Claude_Instructions/` (00-indice, 01-architettura, 02-schema, 03-endpoint, 04-disponibilità, 05-provisioning)
- Piano e avanzamento: `Claude_Instructions/DEVELOPMENT_PLAN.md`
- Guida integrazione (API, onboarding sito, CLI): `Claude_Instructions/GUIDA_INTEGRAZIONE_API.md`
- Test API con Thunder Client: `Claude_Instructions/TEST_API_THUNDER_CLIENT.md` (file importabili in `thunder-tests/`)
- Note/sessioni: `Claude_Instructions/DUBBI_SESSIONE.md`, `Claude_Instructions/DOCKER_SESSION_TODO.md`, `Claude_Instructions/CODE_REVIEW_FINDINGS.md`
