# Development Plan — WebAgency BookingSystem

## Stato: IN PIANIFICAZIONE — CODEBASE VUOTO

> Nessun codice implementato. Tutti i task sono `[ ]`.
> L'implementazione inizia dallo step **1.1** non appena l'utente dà consenso esplicito.
> Regola operativa: **non scrivere codice senza "vai" esplicito dall'utente nella sessione corrente.**

### Prossimo task da eseguire
**→ 3.1 `BookingSystemDbContext`** con Global Query Filter su `tenant_id`.
> Sessione autonoma in corso (senza Docker): si implementa V1 fino allo step 5.8 (endpoint pubblici), gate `dotnet build`.
> Rinviato alla sessione con Docker: `dotnet ef database update`, run API, smoke-test (vedi `DOCKER_SESSION_TODO.md`).

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
- [ ] 2.8 DTOs Request/Response per ogni endpoint admin — **RINVIATO** con endpoint admin (6.x), fuori scope sessione corrente (evita dead code, vedi D-08)
- [x] 2.9 Enums: `BufferPosition`, `BookingStatus`, `UserRole`, `DayOfWeekIndex`

### 3. Infrastructure Layer (`WebAgency_BookingSystem.Infrastructure`)
- [ ] 3.1 `BookingSystemDbContext` con Global Query Filters per `tenant_id`
- [ ] 3.2 Configurazioni EF Core (Fluent API per ogni entità)
- [ ] 3.3 Prima migrazione EF Core (schema completo)
- [ ] 3.4 `TenantRepository`
- [ ] 3.5 `ServiceRepository`
- [ ] 3.6 `StaffRepository`
- [ ] 3.7 `BookingRepository`
- [ ] 3.8 `EmailServiceStub` (implementazione no-op di `IEmailService`)

### 4. Middleware & Cross-Cutting (`WebAgency_BookingSystem.Api`)
- [ ] 4.1 `TenantResolutionMiddleware` (header `X-Api-Key` → `tenant_id` nel context)
- [ ] 4.2 Rate limiting (100 req/min per API key, sliding window)
- [ ] 4.3 Error handling middleware (envelope `{ type, message, errors }`)
- [ ] 4.4 Serilog setup (structured logging, IP anonimizzato a /24)
- [ ] 4.5 Program.cs con DI completo, middleware pipeline, routing

### 5. Endpoint Pubblici
- [ ] 5.1 `GET /api/v1/health` (no auth)
- [ ] 5.2 `GET /api/v1/tenant/config`
- [ ] 5.3 `GET /api/v1/services`
- [ ] 5.4 `GET /api/v1/staff` (con filtro `?serviceId=`)
- [ ] 5.5 `GET /api/v1/availability` (algoritmo completo, granularità 15 min)
- [ ] 5.6 `POST /api/v1/bookings` (con `pg_try_advisory_xact_lock`)
- [ ] 5.7 `GET /api/v1/bookings/{id}?token=...`
- [ ] 5.8 `DELETE /api/v1/bookings/{id}?token=...`

### 6. Admin API
- [ ] 6.1 `POST /api/v1/admin/auth/token` (email + password → JWT)
- [ ] 6.2 JWT middleware (validazione + estrazione `user_id`, `tenant_id`, `role`)
- [ ] 6.3 `GET /api/v1/admin/bookings` (lista con filtri: data, staff, servizio, stato)
- [ ] 6.4 `PATCH /api/v1/admin/bookings/{id}` (aggiorna stato: no-show, ecc.)
- [ ] 6.5 `GET /api/v1/admin/services`
- [ ] 6.6 `POST /api/v1/admin/services`
- [ ] 6.7 `PUT /api/v1/admin/services/{id}`
- [ ] 6.8 `DELETE /api/v1/admin/services/{id}` (soft delete)
- [ ] 6.9 `GET /api/v1/admin/staff`
- [ ] 6.10 `POST /api/v1/admin/staff`
- [ ] 6.11 `PUT /api/v1/admin/staff/{id}`
- [ ] 6.12 `DELETE /api/v1/admin/staff/{id}` (soft delete)
- [ ] 6.13 `PUT /api/v1/admin/business-hours`
- [ ] 6.14 `PUT /api/v1/admin/closures`

### 7. CLI Provisioning (`WebAgency_BookingSystem.TenantProvisioning`)
- [ ] 7.1 Definizione JSON schema per file provisioning
- [ ] 7.2 Validazione input con errori chiari
- [ ] 7.3 Flusso transazionale: tenant → business hours → closures → servizi → staff
- [ ] 7.4 Generazione API key (SHA-256 hash, formato `bk_live_...`)
- [ ] 7.5 Creazione admin user (password hash bcrypt, ruolo `Owner`)
- [ ] 7.6 INSERT `audit_log` al completamento
- [ ] 7.7 File `samples/barbershop-demo.json` (tenant di test completo)

---

## V2 — Completamento

> Prerequisito: Brevo API key attiva.

### 8. Email Transazionale
- [ ] 8.1 `BrevoEmailClient` (HTTP client per REST API Brevo)
- [ ] 8.2 Template HTML: conferma prenotazione cliente
- [ ] 8.3 Template HTML: notifica nuova prenotazione titolare
- [ ] 8.4 Template HTML: conferma disdetta cliente
- [ ] 8.5 Collegamento fire-and-forget post-commit nei servizi booking

### 9. Test Suite
- [ ] 9.1 Unit: `AvailabilityService` — 19 casi (da spec `04-logica-disponibilita.md`)
- [ ] 9.2 Unit: `BookingService` — race condition, cancellation logic
- [ ] 9.3 Unit: `TenantResolutionMiddleware`
- [ ] 9.4 Integration: `POST /api/v1/bookings` — 5 casi (da spec)
- [ ] 9.5 Integration: advisory lock (prenotazione doppia concorrente)
- [ ] 9.6 Integration: pipeline middleware completa

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
