# Code Review — Rilievi e lista fix (V1 endpoint pubblici)

> Review critica della codebase prodotta nella sessione del 2026-06-12 (blocchi 1→5, V1 endpoint pubblici).
> Scopo: lista operativa di fix da affrontare in seguito. **Nessuna fix applicata in questo file** — solo analisi.
> Ambito rivisto: `src/` (Core, Infrastructure, Api), infra (Dockerfile, compose, settings). Fuori ambito: tool provisioning, admin, test (non ancora implementati).

## Legenda severità
- **P0** — blocca l'uso reale del prodotto / bug funzionale grave. Da fare subito.
- **P1** — necessario per andare in produzione seria (sicurezza, osservabilità, deploy, resilienza).
- **P2** — qualità, performance, manutenibilità rilevanti.
- **P3** — minore / cosmetico / debito tecnico lieve.

## Giudizio sintetico
L'architettura è **solida e ben stratificata** (Core puro, DIP rispettato, `Result<T>`, query filter multi-tenant, algoritmo di disponibilità isolato e testabile). Commenti `[INTENT]`/`WHY:`/XML doc presenti e in larga parte allineati. **Build verde.**
I gap principali per un sistema **realmente in produzione con clienti** sono: **(1) osservabilità quasi assente** nella logica di business, **(2) production-readiness del deploy** (CORS, proxy headers, binding porta, resilienza DB), **(3) zero test** sul cuore del sistema. Sotto, il dettaglio prioritizzato.

---

## 🔴 Top priority (fare prima)
- [ ] **R-06 (P0)** — CORS assente: il widget frontend non può chiamare l'API da browser.
- [ ] **R-08 (P1, blocca deploy Railway)** — Dockerfile fissa `ASPNETCORE_URLS` a build time: l'app non ascolta sulla `$PORT` runtime.
- [ ] **R-01 / R-02 / R-04 (P1)** — Logging applicativo e correlazione assenti: impossibile diagnosticare in produzione.
- [ ] **R-07 (P1)** — ForwardedHeaders assente: IP (audit/log) e rate-limit per-IP errati dietro proxy.
- [ ] **R-14 (P1)** — La risoluzione tenant non è rate-limited: brute-force/DoS sulle API key.
- [ ] **R-30 (P1)** — Zero test sul cuore (disponibilità, concorrenza booking).

---

## Copertura & metodo della review
Audit **statico** (lettura codice) di Core, Infrastructure e Api, con focus su: pipeline e middleware, servizi di disponibilità/prenotazione, `AvailabilityCalculator`, configurazioni EF, repository, DTO↔contratto, Dockerfile/compose/settings.
**Approfondito:** logica booking/availability, concorrenza/advisory lock, deploy/production-readiness, coerenza commenti.
**Verificato in seconda passata:** algoritmo `AvailabilityCalculator` (griglia 15 min, buffer, pausa, anticipo — nessun bug bloccante oltre alla semantica buffer già in `DUBBI_SESSIONE.md` D-10), cascade/precisione nelle config EF (corrette), parità DTO↔contratto (allineata) → da cui i rilievi aggiuntivi R-31/R-32/R-33.
**NON coperto (per natura statica / mancanza Docker):** comportamento a runtime (l'advisory lock, le query EF tradotte, la migrazione applicata) — vedi `DOCKER_SESSION_TODO.md`; e la correttezza funzionale end-to-end, che richiede i test (R-30).

---

## 1. Logging & Observability  *(priorità esplicita del committente)*

- [ ] **R-01 (P1) — Nessun logging applicativo nella logica di business.**
  `BookingService`, `AvailabilityService`, i repository e `TenantResolutionMiddleware` non emettono alcun log.
  Non esiste traccia applicativa di: prenotazione creata/disdetta, conflitto slot (409), advisory lock non acquisito, tenant non risolto (401/403), regole violate (422).
  *Impatto:* in produzione, davanti a un problema (“il cliente X non riesce a prenotare”) non c'è modo di capire cosa è successo, chi ha fatto cosa, con quale esito.
  *Fix:* iniettare `ILogger<T>` e loggare gli eventi chiave con **proprietà strutturate** (TenantId, ServiceId, StaffId, BookingId, esito, motivo). Almeno: tenant risolto/negato, booking created/cancelled, conflitto, errori regole. File: [BookingService.cs](src/WebAgency_BookingSystem.Infrastructure/Services/BookingService.cs), [AvailabilityService.cs](src/WebAgency_BookingSystem.Infrastructure/Services/AvailabilityService.cs), [TenantResolutionMiddleware.cs](src/WebAgency_BookingSystem.Api/Middleware/TenantResolutionMiddleware.cs).

- [ ] **R-02 (P1) — Manca correlazione richiesta/tenant nei log e nelle risposte d'errore.**
  `UseSerilogRequestLogging` non è arricchito con TenantId/RequestId; l'errore 500 ([ErrorHandlingMiddleware.cs](src/WebAgency_BookingSystem.Api/Middleware/ErrorHandlingMiddleware.cs)) non restituisce un id correlabile al log.
  *Impatto:* impossibile collegare la segnalazione di un cliente alla riga di log esatta.
  *Fix:* arricchire il `LogContext` con `RequestId` (`HttpContext.TraceIdentifier`) e `TenantId` (in `TenantResolutionMiddleware` via `LogContext.PushProperty`); includere il `traceId` nell'envelope del 500 (campo aggiuntivo o header) così il cliente può comunicarlo al supporto.

- [ ] **R-04 (P1) — Il 409 non distingue “lock non acquisito” da “slot pieno”.**
  In [BookingService.CreateAsync](src/WebAgency_BookingSystem.Infrastructure/Services/BookingService.cs) entrambi i casi restituiscono lo stesso `slot_unavailable` senza log.
  *Impatto:* in debug non si capisce se si tratta di contesa (concorrenza) o di reale capienza esaurita — diagnosi opposte.
  *Fix:* loggare il ramo (lock fallito vs capacità insufficiente) a livello Information/Warning con le proprietà dello slot.

- [ ] **R-03 (P2) — Serilog minimale.** Solo sink Console, nessun enricher (Environment, MachineName, versione app), nessun sink strutturato per ambienti non-Railway.
  *Fix:* aggiungere `Enrich.WithEnvironmentName()`, versione assembly; valutare un sink strutturato (Seq/file) per dev/staging.

- [ ] **R-05 (P2) — Nessuna traccia di sicurezza per accessi falliti.** API key mancante/invalida non viene loggata.
  *Impatto:* impossibile rilevare tentativi di brute-force o chiavi compromesse.
  *Fix:* log Warning (senza loggare la chiave in chiaro: al più il `key_prefix` o l'hash) sui 401/403 di risoluzione tenant.

---

## 2. Production-readiness & Deploy

- [ ] **R-06 (P0) — CORS assente.**
  Il frontend è un widget web che chiama l'API **da browser, cross-origin**. Senza policy CORS le richieste vengono bloccate dal browser → **il prodotto non funziona** per il caso d'uso principale. File: [Program.cs](src/WebAgency_BookingSystem.Api/Program.cs).
  *Fix:* `AddCors` + `UseCors` con policy che autorizzi le origini consentite. Idealmente le origini ammesse derivano dal `site_url` del tenant (multi-tenant CORS) o da configurazione; esporre gli header necessari e `X-Api-Key`.

- [ ] **R-08 (P1 — blocca deploy Railway) — `ASPNETCORE_URLS` fissato a build time nel Dockerfile.**
  [Dockerfile](Dockerfile): `ENV ASPNETCORE_URLS=http://+:${PORT:-8080}` viene valutato **a build time** (PORT non esiste → 8080). Railway inietta `PORT` **a runtime**, ma `ASPNETCORE_URLS` resta `:8080` → Kestrel non ascolta sulla porta assegnata → **app irraggiungibile**.
  *Fix:* non bakare `ASPNETCORE_URLS`; impostarlo a runtime (entrypoint che legge `$PORT`, es. `ASPNETCORE_URLS=http://+:$PORT`) oppure configurare Kestrel a leggere `PORT` nel codice.

- [ ] **R-07 (P1) — ForwardedHeaders assente (dietro proxy Railway).**
  `HttpContext.Connection.RemoteIpAddress` sarà l'IP del proxy, non del cliente. Conseguenze: l'IP anonimizzato salvato in `audit_log` e usato nel fallback del rate limiter è **sbagliato/inutile**. File: [Program.cs](src/WebAgency_BookingSystem.Api/Program.cs), uso in [BookingEndpoints.cs](src/WebAgency_BookingSystem.Api/Endpoints/BookingEndpoints.cs).
  *Fix:* `UseForwardedHeaders` (X-Forwarded-For / X-Forwarded-Proto) con `KnownProxies`/`KnownNetworks` adeguati, **prima** dei middleware che leggono IP/scheme.

- [ ] **R-09 (P1) — `UseHttpsRedirection` dietro TLS-terminating proxy.**
  Railway termina il TLS a monte: il redirect può causare loop o 307 indesiderati. File: [Program.cs](src/WebAgency_BookingSystem.Api/Program.cs).
  *Fix:* rimuovere in produzione o condizionarlo all'ambiente, affidando lo schema a ForwardedHeaders.

- [ ] **R-10 (P1) — `.dockerignore` assente.**
  Il build context include `bin/`, `obj/`, `.git/`, `.vs/` → build lente, immagini più grandi, rischio di copiare artefatti locali. File: [Dockerfile](Dockerfile).
  *Fix:* aggiungere `.dockerignore` (bin, obj, .git, .vs, .env, ecc.).

- [ ] **R-11 (P2) — Immagine finale gira come root.**
  *Fix:* nel final stage usare un utente non-root (`USER app` sull'immagine aspnet, che fornisce l'utente `app`).

- [ ] **R-12 (P1) — Nessuna resilienza ai transient fault del DB.**
  `UseNpgsql` non configura `EnableRetryOnFailure`. Riavvii/failover di Postgres propagano 500. File: [DependencyInjection.cs](src/WebAgency_BookingSystem.Infrastructure/DependencyInjection.cs).
  *Attenzione:* retry + transazioni esplicite e advisory lock richiedono una **execution strategy** gestita manualmente (`CreateExecutionStrategy().ExecuteAsync`).
  *Fix:* abilitare retry e adeguare `BookingService` all'execution strategy.

- [ ] **R-13 (P2) — Scalar/OpenAPI esposti pubblicamente in ogni ambiente.**
  *Fix:* gating per ambiente (solo non-Production) o dietro auth, se non si vuole pubblicare l'intera superficie API.

---

## 3. Sicurezza

- [ ] **R-14 (P1) — La risoluzione tenant non è protetta da rate limiting.**
  Nella pipeline ([Program.cs](src/WebAgency_BookingSystem.Api/Program.cs)) `UseRateLimiter` è **dopo** `TenantResolutionMiddleware`: le richieste con API key invalida (403) **non vengono limitate**, e ogni tentativo fa una query al DB.
  *Impatto:* brute-force/enumerazione di API key e DoS sull'endpoint di risoluzione.
  *Fix:* limiter globale per IP a monte della risoluzione, oppure contare anche i tentativi falliti.

- [ ] **R-15 (P2) — Lookup API key colpisce il DB a ogni richiesta** (nessuna cache). File: [TenantRepository.ResolveActiveByApiKeyHashAsync](src/WebAgency_BookingSystem.Infrastructure/Persistence/Repositories/TenantRepository.cs).
  *Fix:* cache in-memory `hash→tenant` con TTL breve + invalidazione su revoca chiave.

- [ ] **R-16 (P3) — Segreti dev in `appsettings.Development.json`** (JWT secret, password DB). Solo locale, ma incoraggia l'abitudine sbagliata. File: [appsettings.Development.json](src/WebAgency_BookingSystem.Api/appsettings.Development.json).
  *Fix:* usare `dotnet user-secrets` per i valori dev.

---

## 4. Correttezza & Concorrenza

- [ ] **R-17 (P2) — L'advisory lock serializza anche slot con `parallelSlots > 1`.**
  La chiave di lock è `(tenant, service, date, time)`: due prenotazioni **legittime** concorrenti sullo stesso slot multi-capienza vengono serializzate e la seconda può ricevere un **409 spurio** se la prima trattiene il lock oltre i 200 ms (singolo retry). File: [BookingService.cs](src/WebAgency_BookingSystem.Infrastructure/Services/BookingService.cs).
  *Fix:* per capacità > 1 rivedere la strategia (lock per “posto”/contatore, o retry più robusto), e comunque loggare i 409 da contesa.

- [ ] **R-18 (P2) — `DbUpdateException` da race non mappata a 409.**
  Se l'advisory lock fallisse (o per vincoli concorrenti), l'eccezione diventa 500 generico anziché 409.
  *Fix:* catturare le violazioni di concorrenza e mapparle a `slot_unavailable` (difesa in profondità).

- [ ] **R-19 (P3) — Chiusure in `tenant/config` filtrate con `DateTime.UtcNow`** invece dell'ora locale del tenant → possibile off-by-one a cavallo di mezzanotte. File: [TenantConfigEndpoints.cs](src/WebAgency_BookingSystem.Api/Endpoints/TenantConfigEndpoints.cs).
  *Fix:* usare la data locale del tenant.

- [ ] **R-20 (P3) — `CheckBookingRulesAsync` ritorna `Result<CreateBookingResponse>` con valore dummy `default!`** per veicolare solo l'esito → odore di design. File: [BookingService.cs](src/WebAgency_BookingSystem.Infrastructure/Services/BookingService.cs).
  *Fix:* usare `Result` non generico o un tipo esito dedicato (`ValidationOutcome`).

- [ ] **R-31 (P1) — Gli errori di binding/deserializzazione bypassano l'envelope d'errore del contratto.**
  Il contratto (spec 03) impone che **tutti** gli errori abbiano forma `{ type, message, errors? }`. Ma gli errori di binding di Minimal API (Guid malformato, query param obbligatorio mancante, JSON non valido) producono il **400 di default di ASP.NET** (`BadHttpRequestException`/ProblemDetails RFC7807), **non** l'envelope. Es.: `GET /availability` senza `dateFrom`, o `serviceId` non-Guid → 400 fuori formato. File: tutti gli endpoint con parametri tipizzati ([AvailabilityEndpoints.cs](src/WebAgency_BookingSystem.Api/Endpoints/AvailabilityEndpoints.cs), [BookingEndpoints.cs](src/WebAgency_BookingSystem.Api/Endpoints/BookingEndpoints.cs)).
  *Impatto:* il frontend riceve due formati d'errore diversi a seconda del tipo di errore → gestione incoerente lato client.
  *Fix:* gestire `BadHttpRequestException`/binding nel middleware (o `AddProblemDetails` con customizzazione) per emettere l'envelope `{ type: "bad_request", message, ... }` anche per i 400 di binding.

- [ ] **R-32 (P3) — Edge DST nei confronti orari di disponibilità.**
  `AvailabilityCalculator` e `BookingService` confrontano `DateTime` locali “naive” (Kind=Unspecified) ottenuti da `TenantTime`/`DateOnly.ToDateTime`. Nel giorno del cambio ora legale il confronto con l'anticipo minimo può sfasare di un'ora. File: [AvailabilityCalculator.cs](src/WebAgency_BookingSystem.Core/Availability/AvailabilityCalculator.cs), [TenantTime.cs](src/WebAgency_BookingSystem.Infrastructure/Services/TenantTime.cs).
  *Impatto:* marginale (2 giorni/anno, ±1h sull'anticipo minimo).
  *Fix (se rilevante):* ragionare in `DateTimeOffset` o documentare il limite; coprire con un test mirato.

---

## 5. Performance & Scalabilità

- [ ] **R-21 (P2) — Tenant ricaricato dal DB nei servizi** (`AvailabilityService`, `BookingService` via `GetByIdAsync`) benché già caricato dal middleware in `HttpContext.Items`. Query ridondante per ogni richiesta tenant-scoped.
  *Fix:* esporre il tenant corrente (non solo l'Id) tramite `ITenantContext` o un accessor condiviso, popolato in fase di risoluzione.

- [ ] **R-22 (P2) — Nessuna cache su dati quasi-statici** (tenant config, business hours, services) — la spec li indica come candidati cache.
  *Fix:* `IMemoryCache` con TTL breve e invalidazione sugli update admin.

- [ ] **R-23 (P3) — `/health` esegue `CanConnectAsync` (hit DB) a ogni probe.** Con probe frequenti è carico inutile sul DB. File: [HealthEndpoints.cs](src/WebAgency_BookingSystem.Api/Endpoints/HealthEndpoints.cs).
  *Fix:* separare liveness (no DB) da readiness (DB con cache breve), o usare `AddHealthChecks().AddNpgSql()`.

- [ ] **R-24 (P3) — DbContext non poolizzato.** `AddDbContextPool` ridurrebbe le allocazioni, ma confligge con l'iniezione di `ITenantContext` scoped nel DbContext.
  *Fix:* valutare pooling con un pattern compatibile (es. interfaccia tenant impostata post-resolve) solo se il profiling lo giustifica.

---

## 6. Qualità, Manutenibilità, Coerenza commenti/doc

- [ ] **R-25 (P2) — Email `await`-ate post-commit bloccano la response.**
  Accettabile con lo stub no-op, ma con Brevo (V2) bloccherebbe la risposta e, se l'invio fallisse, non c'è retry/persistenza. File: [BookingService.cs](src/WebAgency_BookingSystem.Infrastructure/Services/BookingService.cs).
  *Fix (pianificazione V2):* pattern **outbox** + `BackgroundService` per invio affidabile e non bloccante; aggiornare il contratto/uso di `IEmailService` di conseguenza.

- [ ] **R-26 (P3) — Duplicazione costruzione `ServiceSlotConfig`** in `AvailabilityService` e `BookingService`.
  *Fix:* estrarre una factory `ServiceSlotConfig.From(Service)`.

- [ ] **R-27 (P3) — Timestamp `created_at/updated_at` impostati a mano** nelle entità; nessun meccanismo centrale.
  *Impatto:* arrivando admin/CLF è facile dimenticarne uno → incoerenza.
  *Fix:* `SaveChanges` interceptor che valorizza i timestamp sulle entità che li espongono.

- [ ] **R-28 (P3) — `BookingDetailResponse` usa `?? string.Empty`** per nome servizio/staff soft-deleted → nome vuoto silenzioso. File: [BookingService.cs](src/WebAgency_BookingSystem.Infrastructure/Services/BookingService.cs).
  *Fix:* caricare service/staff con `IgnoreQueryFilters` per il dettaglio storico, o etichetta esplicita.

- [ ] **R-29 (P3) — Coerenza commenti.** Complessivamente buona; due punti da tenere allineati dopo i fix:
  - il commento “GDPR-safe” su Serilog ([Program.cs](src/WebAgency_BookingSystem.Api/Program.cs)) presuppone che l'IP non sia loggato: rivalutare introducendo ForwardedHeaders/logging IP;
  - i riferimenti all'header `X-Api-Key` vs `X-API-Key` (vedi `DUBBI_SESSIONE.md` D-06) vanno uniformati alla decisione finale.

- [ ] **R-33 (P2) — Nessun analyzer / `TreatWarningsAsErrors` / `.editorconfig` condiviso.**
  I `.csproj` non abilitano `EnableNETAnalyzers`/`AnalysisLevel` né trattano i warning come errori; non c'è `.editorconfig` per stile/regole condivise. I 6 warning MSB3277 del tool restano silenziosi (vedi `DUBBI_SESSIONE.md` D-11).
  *Impatto:* per una codebase “di alto livello” la qualità non è imposta automaticamente; i warning si accumulano.
  *Fix:* `Directory.Build.props` con analyzer abilitati e (almeno in CI) warnings-as-errors; `.editorconfig` condiviso. Risolvere prima i warning esistenti.

---

## 7. Testing  *(rischio trasversale)*

- [ ] **R-30 (P1) — Zero test.**
  `AvailabilityCalculator` (algoritmo cuore) e il booking concorrente non hanno alcuna copertura; la spec `04-logica-disponibilita.md` elenca **19 casi unit + 5 integration obbligatori**.
  *Fix:* iniziare da **9.1** (unit su `AvailabilityCalculator`, eseguibili **senza Docker**), poi `BookingService` e integration con Testcontainers. È la singola voce con il miglior rapporto valore/rischio.

---

## Riepilogo per severità (33 rilievi)
| Severità | ID |
|---|---|
| **P0** | R-06 (CORS) |
| **P1** | R-01, R-02, R-04, R-07, R-08, R-09, R-10, R-12, R-14, R-30, R-31 |
| **P2** | R-03, R-05, R-15, R-17, R-18, R-21, R-22, R-25, R-33 |
| **P3** | R-11, R-13, R-16, R-19, R-20, R-23, R-24, R-26, R-27, R-28, R-29, R-32 |

> Nota: i rilievi su CORS, ForwardedHeaders, binding porta, logging e test **non erano nello scope “1.1→5.8”** della sessione autonoma (che mirava al codice degli endpoint pubblici con gate build), ma sono **prerequisiti di produzione**. Vanno schedulati prima del go-live, idealmente insieme alle Sezioni 6/7 e alla Sezione 9 (test).
