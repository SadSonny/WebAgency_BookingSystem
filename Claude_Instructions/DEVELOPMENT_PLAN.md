# Development Plan — WebAgency BookingSystem

## Stato: V2.1 SALONE REALE (Tier 1+2) COMPLETATO (2026-06-15)

> **V1 + V2 email + hardening PH-1..PH-5 + V2.1 salone reale**: infra, Core, Infrastructure, middleware, endpoint
> pubblici (5.1–5.8), Admin API (6.1–6.14), CLI provisioning (7.x), email transazionale (Sez. 8), hardening
> PH-1..PH-5, **funzionalità salone reale Tier 1+2**.
> Build verde (0 warning), **119 test verdi** (95 unit + 24 integration con Testcontainers).
> V2.1 (2026-06-15): **assenze operatore** (T1.1), **"qualsiasi operatore" auto-assegnato** con disponibilità
> aggregata (T1.2), **appuntamento multi-servizio** un operatore (T1.3), **cancellazione admin → email cliente**
> (T2.1), **reschedule cliente** (T2.2), **reminder pre-appuntamento** configurabile (T2.3).
> Email via outbox transazionale; provider Mailpit dev / Brevo prod (AD-10/AD-11).
> Regola operativa: **non scrivere codice senza "vai" esplicito dall'utente nella sessione corrente.**

### Prossimo task da eseguire
**V2.1 Tier 1+2 COMPLETATO** (2026-06-15): vedi sezione "V2.1 — Salone reale". 119 test verdi.
**→ Prossimo:** Railway deploy (`EMAIL_PROVIDER=Brevo` + `BREVO_*` con mittente verificato; applicare le migration
incl. AddStaffTimeOff/AddBookingItems/AddReminderFields). Follow-up V2.1: disponibilità combinata combo, template
email multi-servizio, reschedule admin. Poi 8.7 (branding). **NON** è prevista una Admin UI (vedi nota sotto).

> **Direzione prodotto (2026-06-14):** il sistema è un **backend API-only** pensato perché siti web esterni
> dei tenant si colleghino via API. **Non si sviluppa una Admin UI**: la gestione resta via API admin + CLI.
> L'unica UI prevista in futuro è una **dashboard interna per i dev** (observability cross-tenant: logging,
> volumi prenotazioni per cliente, ecc.) — pianificata nella **Sezione 10**, ma **rimandata**.
> **Nota Docker session 2026-06-13**: DTO `PUT /admin/business-hours` e `PUT /admin/closures` wrappano la lista in `{ "days": [...] }` / `{ "closures": [...] }`. API porta **5022** (launchSettings.json).

### Come aggiornare questo file
- Spunta `[x]` il task completato immediatamente dopo averlo finito e verificato con `dotnet build`.
- Aggiungi riga al Changelog in fondo.
- Aggiorna la sezione "Prossimo task da eseguire" con il task successivo.
- Se una decisione architetturale emerge durante l'implementazione, aggiungila alla tabella §Decisioni Architetturali.

---

## V1 — API Completa (senza email)

> Obiettivo: sistema prenotazioni funzionante end-to-end, provisionabile via CLI, senza notifiche email.

### 1. Infrastruttura & Setup
- [x] 1.0 OpenAPI + Scalar UI (`Scalar.AspNetCore 2.16.3`, `Microsoft.AspNetCore.OpenApi 10.0.8`) — UI: `/scalar`, doc JSON: `/openapi/v1.json`
- [x] 1.1 `docker-compose.yml` (PostgreSQL 16 + pgAdmin)
- [x] 1.2 `Dockerfile` multi-stage per `WebAgency_BookingSystem.Api`
- [x] 1.3 `appsettings.json` + `appsettings.Development.json` con tutte le sezioni
- [x] 1.4 `.env.example` con tutte le variabili d'ambiente documentate
- [x] 1.5 NuGet packages aggiunti (Infra: `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.2`, `Microsoft.EntityFrameworkCore.Design 10.0.9`; Api: stessi `Design` + `Serilog.AspNetCore`, `Serilog.Sinks.Console`, `FluentValidation 12.1.1` + DI ext). Core resta senza dipendenze esterne.

### 2. Core Layer (`WebAgency_BookingSystem.Core`)
- [x] 2.1 Entità: `Tenant`, `TenantApiKey`, `TenantBusinessHours`, `TenantSpecialClosure`
- [x] 2.2 Entità: `Service`, `Staff`, `StaffService`, `StaffBusinessHours`
- [x] 2.3 Entità: `Booking`, `AuditLog`, `User`
- [x] 2.4 `Result<T>` pattern + `Error` type (+ `ErrorType` per mapping HTTP)
- [x] 2.5 Interfacce: `IAvailabilityService`, `IBookingService`, `IEmailService` (+ `ITenantContext`)
- [x] 2.6 Interfacce: `ITenantRepository`, `IServiceRepository`, `IStaffRepository`, `IBookingRepository`
- [x] 2.7 DTOs Request/Response per ogni endpoint pubblico (+ `ErrorResponse`)
- [x] 2.8 DTOs Request/Response per ogni endpoint admin — implementati con la Sezione 6 (auth, servizi, staff, orari/chiusure, prenotazioni)
- [x] 2.9 Enums: `BufferPosition`, `BookingStatus`, `UserRole`, `DayOfWeekIndex`

### 3. Infrastructure Layer (`WebAgency_BookingSystem.Infrastructure`)
- [x] 3.1 `BookingSystemDbContext` con Global Query Filters per `tenant_id` (+ soft delete service/staff)
- [x] 3.2 Configurazioni EF Core (Fluent API per ogni entità) — snake_case, indici, vincoli, enum→string
- [x] 3.3 Prima migrazione EF Core (`InitialSchema`) — **generata, NON applicata** (manca Docker; vedi `DOCKER_SESSION_TODO.md`)
- [x] 3.4 `TenantRepository` (+ risoluzione API key con `IgnoreQueryFilters`)
- [x] 3.5 `ServiceRepository`
- [x] 3.6 `StaffRepository`
- [x] 3.7 `BookingRepository`
- [x] 3.8 `EmailServiceStub` (implementazione no-op di `IEmailService`) + `AddInfrastructure` DI

### 4. Middleware & Cross-Cutting (`WebAgency_BookingSystem.Api`)
- [x] 4.1 `TenantResolutionMiddleware` (header `X-Api-Key` → `tenant_id` nel context) + `ApiKeyHasher` (Core)
- [x] 4.2 Rate limiting (100 req/min per API key, sliding window) — policy `PublicApi`, 429 + Retry-After
- [x] 4.3 Error handling middleware (envelope `{ type, message }`) + `ResultMapping` (Result→HTTP) + `HttpErrorWriter`
- [x] 4.4 Serilog setup (Console, request logging GDPR-safe) + `IpAnonymizer` (Core, /24)
- [x] 4.5 Program.cs con DI completo, middleware pipeline, routing (endpoint mapping attivato in 5.x)

### 5. Endpoint Pubblici
- [x] 5.1 `GET /api/v1/health` (no auth) — verifica raggiungibilità DB (200/503)
- [x] 5.2 `GET /api/v1/tenant/config` — 7 giorni, chiusure future, bufferMinutes=0
- [x] 5.3 `GET /api/v1/services` — con `staffIds`
- [x] 5.4 `GET /api/v1/staff` (con filtro `?serviceId=`)
- [x] 5.5 `GET /api/v1/availability` (algoritmo completo, granularità 15 min) — `AvailabilityCalculator` puro
- [x] 5.6 `POST /api/v1/bookings` (con `pg_try_advisory_xact_lock` + retry) — FluentValidation 422
- [x] 5.7 `GET /api/v1/bookings/{id}?token=...` — 404 neutro
- [x] 5.8 `DELETE /api/v1/bookings/{id}?token=...` — 403 oltre preavviso

### 6. Admin API
- [x] 6.1 `POST /api/v1/admin/auth/token` (tenantSlug + email + password → JWT) — login per slug (D-15)
- [x] 6.2 JWT bearer (validazione firma/issuer/audience/lifetime) + `AdminContextMiddleware` (tenant dal claim `tenant_id`)
- [x] 6.3 `GET /api/v1/admin/bookings` (filtri: dateFrom/dateTo, staff, servizio, stato)
- [x] 6.4 `PATCH /api/v1/admin/bookings/{id}` (aggiorna stato: no_show/completed/cancelled, + audit)
- [x] 6.5 `GET /api/v1/admin/services` (inclusi inattivi)
- [x] 6.6 `POST /api/v1/admin/services`
- [x] 6.7 `PUT /api/v1/admin/services/{id}`
- [x] 6.8 `DELETE /api/v1/admin/services/{id}` (soft delete) + invalidazione cache
- [x] 6.9 `GET /api/v1/admin/staff` (con servizi/orari)
- [x] 6.10 `POST /api/v1/admin/staff` (con assegnazione servizi + orari)
- [x] 6.11 `PUT /api/v1/admin/staff/{id}`
- [x] 6.12 `DELETE /api/v1/admin/staff/{id}` (soft delete) + invalidazione cache
- [x] 6.13 `PUT /api/v1/admin/business-hours` (sostituzione in blocco)
- [x] 6.14 `PUT /api/v1/admin/closures` (sostituzione in blocco)

### 7. CLI Provisioning (`WebAgency_BookingSystem.TenantProvisioning`)
- [x] 7.1 JSON schema per file provisioning (`ProvisioningModels`)
- [x] 7.2 Validazione input con errori chiari (`ProvisioningValidator`, raccoglie tutti gli errori)
- [x] 7.3 Flusso transazionale: tenant → business hours → closures → servizi → staff (un solo `SaveChanges`)
- [x] 7.4 Generazione API key (SHA-256 hash via `ApiKeyHasher`, formato `bk_live_...`)
- [x] 7.5 Creazione admin user (password bcrypt generata, ruolo `Owner`) — mostrata una sola volta
- [x] 7.6 INSERT `audit_log` (`tenant_created`, actor `provisioning`) al completamento
- [x] 7.7 File `samples/barbershop-demo.json` + `README.md`
- **Nota:** solo modalità CREATE in V1; `--update` rinviato. Esecuzione reale richiede DB (vedi `DOCKER_SESSION_TODO.md`).

---

## V2 — Completamento

> Prerequisito: Brevo API key attiva.

### 8. Email Transazionale

> **Strategia provider (AD-10):** `IEmailService` selezionato per ambiente via `Email:Provider`.
> **Dev → Mailpit** (SMTP locale, zero verifica, cattura in UI); **Prod → Brevo** (REST, dominio verificato).
> I template sono **HTML inline renderizzati nel codice** (AD-11): unico approccio che dà parità dev/prod
> (i template gestiti da Brevo non funzionerebbero con Mailpit). Il rendering vive in un componente condiviso
> riusato da entrambi i provider (SRP: provider = trasporto, renderer = contenuto).

- [x] 8.0 Config `Email:Provider` (`Mailpit`|`Brevo`|`None`) + `EmailSettings`; switch in DI (`AddInfrastructure.AddEmail`)
- [x] 8.1a `IEmailTemplateRenderer` + `EmailMessage` (subject + HTML + testo), rendering inline italiano
- [x] 8.1b `MailpitEmailService` (SMTP via MailKit 4.17.0) — dev, nessuna verifica mittente
- [x] 8.1c `BrevoEmailClient` (HttpClient tipizzato, `POST /v3/smtp/email`) — prod
- [x] 8.2 Template: conferma prenotazione cliente
- [x] 8.3 Template: notifica nuova prenotazione titolare (gated su `Tenant.NotificationMethod == "email"`)
- [x] 8.4 Template: conferma disdetta cliente
- [x] 8.5 Invio fire-and-forget post-commit nei servizi booking (era `await` inline in `BookingService`)
- [x] 8.6 `docker-compose.yml`: servizio `mailpit` (porte 1025 SMTP / 8025 UI)
- [ ] 8.7 **(RIMANDATO)** Revisione branding template: layout V1 è sobrio/neutro; rivedere con logo,
  colori e footer GDPR definitivi (eventualmente brandizzabile per-tenant) prima del go-live commerciale.

### 9. Test Suite
- [x] 9.1 Unit: disponibilità — **`AvailabilityCalculator` (23 test) + `HoursResolver` (9 test)** verdi. Coprono granularità, bordi chiusura, pausa, anticipo/passato, capienza parallelSlots/staff, buffer (D-10), `IsSlotAvailable`, chiusure, giorno chiuso, precedenza orari staff/tenant.
- [~] 9.2 Unit: `BookingService` — **consultazione e disdetta coperte (9 test)**: 404 neutro, stato non disdicibile (422), preavviso (403), canCancel, audit+email. La **creazione** (`CreateAsync`, advisory lock/transazione/SQL raw) resta per l'**integration** con Docker (9.4/9.5).
- [x] 9.3 Unit: `TenantResolutionMiddleware` — **7 test** (path exclusion ×4, 401/403/tenant risolto)
- [x] 9.4 Integration: `POST /api/v1/bookings` — **5 casi** (201+token, 409 slot pieno, 422 giorno chiuso, 422 passato, 400 JSON malformato)
- [x] 9.5 Integration: advisory lock — **Task.WhenAll** due client concorrenti → 1×201 + 1×409 garantito
- [x] 9.6 Integration: pipeline middleware — **4 test** (401, 403, X-Trace-Id, 400 malformed JSON)
- [x] 9.7 Integration: buffer per servizio (D-10) — `BufferPosition=After, BufferMinutes=15`: 10:00→201, 10:30→409, 10:45→201
- [x] 9.8 Integration: `IExpiredBookingCleaner` — prenotazione ieri → NoShow; prenotazione futura → invariata
- [x] 9.9 Unit: email — `EmailTemplateRendererTests` (8: destinatari, dati chiave, gating titolare, staff/prezzo condizionali, HTML-encoding) + `EmailSettingsTests` (6: provider, precedenza env>section, default SMTP, validazione Brevo)
- [x] 9.10 Integration: smoke Mailpit — `POST /bookings` → email di conferma catturata via HTTP API del container (`MailpitEmailTests`)

---

## Hardening Produzione (PH-1..PH-5)

> Rilievi emersi dall'analisi di production-readiness (2026-06-14), **implementati 2026-06-15**.
> Riferimenti a `CODE_REVIEW_FINDINGS.md`.

- [x] **PH-1 — CORS per-tenant.** Lista globale statica sostituita da `TenantOriginCatalog` (snapshot in-memory
  thread-safe) aggiornato da `TenantOriginRefreshJob` (BackgroundService) dai `siteUrl` dei tenant attivi
  (`Cors:OriginRefreshSeconds`, default 60). Onboardare un nuovo sito non richiede più modifiche di config. La
  callback CORS resta sincrona (legge solo memoria). `Cors:AllowedOrigins` resta come allowlist statica aggiuntiva.
  `OriginNormalizer` + catalogo: +17 unit test.
- [x] **PH-2 — Advisory lock con `parallelSlots > 1` (R-17).** `pg_try_advisory_xact_lock` + singolo retry
  sostituito da `pg_advisory_xact_lock` BLOCCANTE con `SET LOCAL lock_timeout` (5s → 55P03 → 409 conteso):
  niente 409 spurio, le prenotazioni legittime si accodano e ognuna riconta i posti sotto lock. Lock di SLOT
  (non per-posto: sarebbe NON sicuro per la capacità). +2 integration test (2 conc.→2×201; 3 conc.→2×201+409).
- [x] **PH-3 — Email outbox durevole (R-25).** Outbox transazionale: l'email è accodata (`OutboxEmail`) nella
  stessa transazione del booking (consegna garantita) e inviata da `EmailOutboxDispatcher` con retry/backoff
  (MaxAttempts=5). Refactor trasporto `IEmailSender` (Mailpit/Brevo/Null) separato dal contenuto. Migration
  `AddEmailOutbox`. +4 unit (processor) + integration Mailpit via outbox.
- [x] **PH-4 — Segreti dev su user-secrets (R-16).** `UserSecretsId` sul progetto Api: in Development gli User
  Secrets sovrascrivono `appsettings.Development.json` (segreti reali fuori dal repo). Documentato in CLAUDE.md.
- [x] **PH-5 — Edge DST (R-32).** `TenantTime.ToInstant` (orario locale → istante DST-corretto); le decisioni di
  anticipo minimo e preavviso disdetta confrontano istanti assoluti (UtcNow), non DateTime locali "naive". +4
  unit test. **`DbContext` pooling (R-24): NON implementato di proposito** — confligge con l'iniezione scoped di
  `ITenantContext` (da cui dipende il global query filter) e non ha beneficio dimostrato senza profiling.

---

## V2.1 — Salone reale (Tier 1 + Tier 2)

> Funzionalità necessarie per un'attività reale con N operatori (es. parrucchieri), da analisi 2026-06-15.
> Decisioni utente: **un operatore per appuntamento** (servizi consecutivi, durata sommata); **"qualsiasi
> operatore" auto-assegnato** alla conferma con disponibilità aggregata sulle agende reali; **assenze staff a
> giorni interi + fasce orarie**; **reminder 24h prima, configurabile per tenant**. Reschedule: cliente (via
> token, entro preavviso) + admin, coerente con la disdetta. Tier 3 (anagrafica cliente, pagamenti, report)
> escluso per ora.

### Tier 1 — abilitanti per il salone reale
- [x] **T1.1 Assenze per operatore** (`StaffTimeOff`): entità + migration + repo + Admin CRUD
  (`/admin/staff/{id}/time-off`). Giorno intero → niente slot per quell'operatore; fascia parziale → intervallo
  bloccante (`AvailabilityCalculator.staffBlocks`). +6 unit + 2 integration.
- [x] **T1.2 "Qualsiasi operatore"**: senza `staffId` la disponibilità aggrega le agende dei soli operatori
  qualificati (`AvailabilityService.AccumulateStaffAsync`); alla creazione `ResolveStaffAsync` **auto-assegna**
  il primo operatore libero. Fallback `parallelSlots` per servizi senza operatori. +2 integration.
- [x] **T1.3 Appuntamento multi-servizio** (un operatore): `BookingItem` + migration; `POST /bookings` accetta
  `additionalServiceIds` (additivo), durata/prezzo = somma, blocco continuo, operatore che esegue tutti i servizi
  (`ExecutesAllAsync`); dettaglio elenca i servizi. +2 integration.
  **Follow-up (non bloccanti):** disponibilità combinata per combo via `GET /availability` (oggi singolo
  servizio); template email che elenca tutti i servizi (oggi mostra il principale + durata totale).

### Tier 2 — qualità servizio / anti no-show
- [x] **T2.1 Cancellazione da admin notifica il cliente**: `PATCH /admin/bookings/{id}` → `cancelled` accoda
  l'email di disdetta (solo Confirmed→Cancelled).
- [x] **T2.2 Reschedule / modifica appuntamento**: `PUT /bookings/{id}/reschedule?token=` (cliente) — ri-verifica
  disponibilità sotto lock escludendo se stessa, rispetta anticipo/preavviso, notifica con dati aggiornati.
  +2 integration. **Follow-up:** endpoint reschedule lato admin (oggi l'admin può disdire+riprenotare).
- [x] **T2.3 Reminder pre-appuntamento**: `Tenant.ReminderHoursBefore` (default 24, 0=off, con notifiche email),
  `Booking.ReminderSentAt`, `EmailKind.Reminder` + template, `ReminderJob` (BackgroundService) → `IReminderEnqueuer`
  accoda i promemoria dovuti nella outbox. +4 unit.

> **Escluso (Tier 3, eventuale V2.2+):** anagrafica cliente + storico, acconti/pagamenti, reportistica titolare,
> SMS/WhatsApp, waitlist, ricorrenti, multi-sede.

---

## V2.2 — Hardening pre-produzione (performance + sicurezza)

> Dall'analisi di production-readiness (2026-06-15). Obiettivo: nessun collo di bottiglia su scala (più
> operatori/prenotazioni) e copertura sicurezza per un servizio reale multi-tenant UE. `DbContext` pooling
> ESCLUSO di proposito (vedi PH-5/R-24). i18n non incluso in questa fase.

### Performance
- [ ] **P1 Batch disponibilità "qualsiasi operatore"**: `AvailabilityService.AccumulateStaffAsync` fa 3 query per
  operatore (orari/assenze/prenotazioni). Caricarle in **3 query totali** per tutti gli operatori (`WHERE
  staff_id IN (...)`) e raggruppare in memoria.
- [ ] **P2 Batch booking "qualsiasi"**: `ResolveStaffAsync` cicla N operatori con `ExecutesAllAsync` (M query) +
  3 query slot. Ridurre a ~poche query: executes-all con un solo `IN`, batch di orari/assenze/prenotazioni.
- [ ] **P3 Reminder: finestra + indice**: `ReminderEnqueuer` scandisce tutte le prenotazioni future. Restringere
  alla finestra max di anticipo e aggiungere indice `(status, reminder_sent_at, booking_date)`.
- [ ] **P4 Paginazione `GET /admin/bookings`**: oggi ritorna tutto. Aggiungere `page`/`pageSize` (default 50,
  max 200) e risposta paginata.

### Sicurezza
- [ ] **S1 Rate limit creazione prenotazioni per API key**: policy dedicata su `POST /bookings` (limite più
  basso, per chiave) contro lo spam con chiave pubblica esposta.
- [ ] **S2 GDPR retention/erasure**: job che **anonimizza** i dati personali (nome/telefono/email/note) delle
  prenotazioni oltre la retention (`Gdpr:RetentionDays`, default 365) e **purga** gli `OutboxEmail` inviati
  oltre N giorni (contengono PII nell'HTML).
- [ ] **S3 Lockout login admin**: tentativi falliti + blocco temporaneo (campi `User.FailedAccessCount`/
  `LockoutEnd`), oltre al rate-limit per IP; risposta invariata per non rivelare gli account esistenti.
- [ ] **S4 Rotazione/revoca API key**: Admin API `GET/POST/DELETE /admin/api-keys` (genera nuova chiave mostrata
  una volta, lista per prefisso, revoca). La revoca è effettiva entro la TTL cache (60s).
- [ ] **S5 Guard JWT secret in produzione**: all'avvio in Production fallire se il secret è il placeholder dev
  (`change-me`) o troppo corto — evita deploy con segreto debole.

> **Stato: pianificazione differita.** Non si parte finché V2 (email) e deploy non sono stabili.
> NON è una Admin UI per i tenant (quella non esiste, vedi AD-09). È uno strumento **interno** per noi dev,
> a sola lettura, per osservare il sistema cross-tenant. Da definire meglio prima di stimare/implementare.

### 10. Dashboard Observability Interna
- [ ] 10.0 **Definizione** (design doc): scopo, utenti (solo dev), dati esposti, modello di accesso/segretezza,
  hosting (stessa app vs progetto separato), stack frontend. **Prerequisito a ogni 10.x.**
- [ ] 10.1 Endpoint/area protetta separata dalle API tenant (auth dedicata dev, non il JWT admin tenant)
- [ ] 10.2 Metriche cross-tenant: volumi prenotazioni per tenant/cliente, stati (Confirmed/Cancelled/NoShow)
- [ ] 10.3 Vista logging/observability (correlazione `X-Trace-Id`, errori, rate-limit)
- [ ] 10.4 UI minima read-only (stack da decidere in 10.0)

> Decisioni aperte per la 10.0: dove vivono i dati di analytics (query dirette vs proiezione/aggregati),
> isolamento dalla superficie API pubblica, e se la UI sia server-rendered minimale o SPA separata.

---

## Decisioni Architetturali

| ID | Decisione | Motivazione | Data |
|---|---|---|---|
| AD-01 | .NET 10 (docs indicavano .NET 9) | Progetto scaffolded in net10.0, API compatibili | 2026-06-11 |
| AD-02 | Admin credentials in tabella `users` | Scalabilità: più admin per tenant, predisposizione ruoli (Owner/Manager) | 2026-06-11 |
| AD-03 | Buffer configurabile **per servizio** | Ogni servizio ha esigenze diverse; 3 campi: `bufferEnabled`, `bufferMinutes`, `bufferPosition` (Before/After/Both) | 2026-06-11 |
| AD-04 | `staff_id` nullable in `bookings` | Non tutti i servizi richiedono staff; disponibilità regolata da `parallelSlots` | 2026-06-11 |
| AD-05 | Errori API in italiano | Richiesto esplicitamente | 2026-06-11 |
| AD-06 | `IEmailService` no-op in V1 | Email Brevo in V2; interfaccia stabile permette swap senza modifiche ai caller | 2026-06-11 |
| AD-07 | pgAdmin incluso in Docker Compose | Ispezione visiva DB durante sviluppo, zero installazioni extra | 2026-06-11 |
| AD-08 | JWT admin con `user_id` + `tenant_id` + `role` nel payload | Permette autorizzazione scoped per tenant senza query DB ad ogni richiesta | 2026-06-11 |
| AD-09 | **Nessuna Admin UI**; prodotto API-only per integrazione di siti esterni | La gestione tenant avviene via Admin API + CLI. L'unica UI prevista è una dashboard interna dev (Sez. 10), rimandata | 2026-06-14 |
| AD-10 | Email per ambiente: **Mailpit (dev) + Brevo (prod)** via `Email:Provider` | Mailpit elimina la verifica mittente in sviluppo (cattura, non consegna); Brevo per la consegna reale in prod. Swap via DI, `IEmailService` invariato | 2026-06-14 |
| AD-11 | Template email **HTML inline nel codice** (no template gestiti da Brevo) | Unico approccio con parità dev/prod: i template Brevo non sarebbero renderizzabili da Mailpit. Versionati nel repo, testabili | 2026-06-14 |

---

## Schema — Modifiche rispetto a spec originale

Le seguenti modifiche allo schema rispetto ai documenti `Claude_Instructions/02-schema-database.md` sono necessarie:

| Tabella | Modifica | Motivo |
|---|---|---|
| `services` | Aggiunti: `buffer_enabled BOOL`, `buffer_minutes INT`, `buffer_position VARCHAR(10)` | AD-03 |
| `tenants` | **Rimosso** `buffer_minutes` | AD-03 (buffer è per-servizio, non per-tenant) |
| `services` | Aggiunto `deleted_at TIMESTAMPTZ NULL` | Soft delete da convenzione (D-09) |
| `staff` | Aggiunto `deleted_at TIMESTAMPTZ NULL` | Soft delete da convenzione (D-09) |
| `users` | Attivata (era "predisposizione futura"): `id`, `tenant_id`, `email`, `password_hash`, `role`, `active`, `last_login_at`, `created_at`, `updated_at` | AD-02 |
| `bookings` | `staff_id` confermato nullable | AD-04 |

---

## Changelog

| Data | Tipo | Descrizione |
|---|---|---|
| 2026-06-11 | Pianificazione | Piano V1/V2 creato; decisioni architetturali definite |
| 2026-06-11 | Documentazione | CLAUDE.md e DEVELOPMENT_PLAN.md aggiornati con guida sessione AI, stato codebase, decisioni già prese |
| 2026-06-11 | Feature | Step 1.0 completato: OpenAPI + Scalar.AspNetCore 2.16.3 — UI su `/scalar`, doc su `/openapi/v1.json` |
| 2026-06-12 | Infra | Step 1.1–1.5 completati: docker-compose (Postgres 16 + pgAdmin), Dockerfile multi-stage, appsettings completi, `.env.example`, pacchetti NuGet (EF Core/Npgsql, Serilog, FluentValidation). Build verde. |
| 2026-06-12 | Core | Step 2.1–2.7, 2.9 completati: 11 entità, enum, `Result<T>`/`Error`/`ErrorType`, DTO pubblici + `ErrorResponse`, interfacce repository/servizi + `ITenantContext`. Deviazioni schema: buffer per-servizio (AD-03), `deleted_at` su services/staff. Step 2.8 (DTO admin) rinviato con 6.x. Build Core verde. |
| 2026-06-12 | Infra | Step 3.1–3.8 completati: DbContext + global query filter, 11 config Fluent API (snake_case via EFCore.NamingConventions), migrazione `InitialSchema` **generata** con factory design-time (no Docker), 4 repository, `EmailServiceStub`, DI `AddInfrastructure`. Build verde. |
| 2026-06-12 | Middleware | Step 4.1–4.5 completati: `TenantResolutionMiddleware` (X-Api-Key→tenant, 401/403), rate limiting sliding window per API key, error handling middleware + mapping Result→HTTP, Serilog Console, helper Core `ApiKeyHasher`/`IpAnonymizer`, Program.cs pipeline. Build verde. |
| 2026-06-12 | Endpoint | Step 5.1–5.8 completati: health, tenant/config, services, staff, availability (`AvailabilityCalculator` puro + `AvailabilityService`), bookings POST/GET/DELETE (`BookingService` con advisory lock). FluentValidation, metadati OpenAPI su tutti gli endpoint. Build solution verde. **V1 endpoint pubblici completi.** Validazione runtime rinviata a `DOCKER_SESSION_TODO.md`. |
| 2026-06-12 | Review | Prodotto `CODE_REVIEW_FINDINGS.md`: review statica critica, 33 rilievi P0–P3 (logging, CORS, deploy/proxy, sicurezza, concorrenza, performance, test). |
| 2026-06-12 | Test | Step 9.1 (parziale): suite unit `AvailabilityCalculatorTests` — 23 test verdi sul cuore puro dell'algoritmo, senza Docker. Rimosso placeholder `UnitTest1.cs`. |
| 2026-06-12 | Refactor+Test | `HoursResolver` spostato in `Core.Availability` (logica pura) + 9 unit test. Step 9.1 completato. |
| 2026-06-12 | Test | Step 9.2 (parziale): `BookingServiceTests` — 9 test su consultazione/disdetta (NSubstitute + EF InMemory). `CreateAsync` rinviato a integration (Docker). Suite totale: **41 test verdi**. |
| 2026-06-12 | Hardening | Risolti rilievi production-readiness della review: CORS (R-06), ForwardedHeaders (R-07), Dockerfile `$PORT` runtime + utente non-root + `.dockerignore` (R-08/R-10/R-11), HttpsRedirection mitigata (R-09), errori di binding nell'envelope (R-31). |
| 2026-06-12 | Observability | Logging strutturato in middleware/servizi (R-01), correlation id `X-Trace-Id` + `RequestId`/`TenantId` in LogContext (R-02), distinzione 409 contesa/pieno (R-04). Build + 41 test verdi. |
| 2026-06-13 | Admin API | Sezione 6 completata: auth JWT (login per slug, AD-08) + AdminContextMiddleware; CRUD servizi (6.5-6.8), staff con servizi/orari (6.9-6.12), orari/chiusure (6.13-6.14), prenotazioni lista+filtri e PATCH stato (6.3-6.4). DTO admin (2.8). Invalidazione cache su mutazioni. Build 0 warning, 41 test verdi. **V1 funzionalmente completa** (manca solo validazione runtime + test integrazione, richiedono Docker). |
| 2026-06-12 | CLI | Sezione 7 completata: CLI `TenantProvisioning` (modelli JSON, validazione, flusso transazionale, API key `bk_live_` + hash, admin Owner bcrypt, audit). Sample `barbershop-demo.json` + README. Solo CREATE (`--update` rinviato). Build 0 warning, 41 test verdi. |
| 2026-06-12 | Hardening V2 | Chiusi TUTTI i rilievi review fattibili senza Docker: rate-limit per IP (R-14), tenant in ITenantContext (R-21), retry DB + execution strategy (R-12), 409 su DbUpdate (R-18), Result/factory cleanup (R-20/R-26), enrichers log (R-03), Scalar solo non-prod (R-13), closures tenant-local (R-19), liveness/readiness (R-23), cache API key + dati tenant (R-15/R-22), interceptor timestamp (R-27), dettaglio soft-deleted (R-28), analyzer+editorconfig+warnings-as-errors+MSB3277 (R-33). Deferiti (Docker/V2/processo): R-16, R-17, R-24, R-25, R-30, R-32. Build 0 warning, 41 test verdi. |
| 2026-06-13 | Docker session | Validazione runtime V1 completa: migrazione applicata (11 tabelle, tipi verificati), provisioning `barbershop-demo` (API key + admin), tutti gli endpoint pubblici e admin validati a runtime. Algoritmo disponibilità: 34 slot/giorno, pausa/chiusure corrette. parallelSlots, advisory lock (409), cancellazione 403 verificati. Hardening: X-Trace-Id, CORS preflight 204, rate-limit 429, envelope 400. Nessun bug runtime. `DOCKER_SESSION_TODO.md` completato. |
| 2026-06-13 | Test | Sezione 9.3–9.6 completata: +7 unit test `TenantResolutionMiddleware`, +10 integration test Testcontainers (5 booking, 1 advisory lock concorrente, 4 pipeline). Suite totale: **58 test verdi** (48 unit + 10 integration). Infrastruttura: `BookingSystemFixture` (PostgreSqlContainer), `BookingSystemFactory` (WebApplicationFactory), `TestData.SeedAsync` idempotente, `IntegrationTestBase` con cleanup. |
| 2026-06-13 | Test | 9.7 (D-10): `BufferTests` integration — ServizioBuffer (30min, After, 15min): 10:00→201, 10:30→409 (dentro buffer), 10:45→201 (fuori buffer). `SeedAsync` esteso con `EnsureLaterSeedAsync` per container reuse. Suite totale: **59 test verdi**. |
| 2026-06-13 | Feature | `ExpiredBookingCleanupJob`: BackgroundService che ogni 60 min (configurabile `CleanupJob:IntervalMinutes`) segna NoShow le prenotazioni Confirmed scadute nel timezone del tenant. `IgnoreQueryFilters()` per operazione cross-tenant. Aggiunto `Microsoft.Extensions.Hosting.Abstractions 10.0.0`. Build 0 warning. |
| 2026-06-14 | Email V2 | **Sezione 8 completata.** Sottosistema email multi-provider (AD-10/AD-11): renderer template HTML inline italiani (conferma/notifica titolare/disdetta), `MailpitEmailService` (SMTP/MailKit, dev) + `BrevoEmailClient` (REST, prod) dietro `RenderedEmailService`, switch per ambiente via `Email:Provider`. Invio fire-and-forget post-commit in `BookingService`. Mailpit nel docker-compose. Test: +14 unit (renderer+settings) e +1 integration smoke Mailpit. **76 test verdi** (62 unit + 14 integration), build 0 warning. Branding template rimandato (8.7). |
| 2026-06-15 | Salone reale | **V2.1 Tier 1+2 completati**: assenze operatore StaffTimeOff (T1.1), "qualsiasi operatore" con disponibilità aggregata + auto-assegnazione (T1.2), appuntamento multi-servizio un operatore BookingItem (T1.3), cancellazione admin notifica cliente (T2.1), reschedule cliente via token (T2.2), reminder pre-appuntamento configurabile (T2.3). 3 migration (StaffTimeOff, BookingItems, ReminderFields). Build 0 warning, **119 test verdi** (95 unit + 24 integration). Follow-up: disponibilità combinata combo, template email multi-servizio, reschedule admin. |
| 2026-06-15 | Hardening | **PH-1..PH-5 completati**: CORS per-tenant dinamico dai siteUrl (catalogo + refresh job); advisory lock bloccante con lock_timeout anti-409-spurio su parallelSlots>1 (R-17); email outbox transazionale con dispatcher/retry, refactor trasporto IEmailSender + migration AddEmailOutbox (R-25); user-secrets dev (R-16); confronti DST-corretti via TenantTime.ToInstant (R-32); pooling (R-24) non implementato di proposito con motivazione. Build 0 warning, **103 test verdi** (87 unit + 16 integration). |
| 2026-06-14 | Direzione | Confermato prodotto **API-only senza Admin UI** (AD-09). Aggiunta **Sezione 10** (dashboard interna dev per observability cross-tenant), rimandata. Avvio V2 email Brevo. |
| 2026-06-13 | Test | 9.8: logica estratta in `IExpiredBookingCleaner` (scoped, testabile in isolation). `CleanupJobTests`: prenotazione ieri → NoShow, prenotazione futura → invariata. `InternalsVisibleTo` per IntegrationTests. Fix `EnsureLaterSeedAsync` con `IgnoreQueryFilters()`. Suite: **61 test verdi** (48 unit + 13 integration). |
