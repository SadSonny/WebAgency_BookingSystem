# Checklist sessione con Docker — validazioni runtime rinviate

> Questi passi NON sono stati eseguiti nella sessione autonoma del 2026-06-12 perché Docker non era
> avviabile. Il codice V1 (blocchi 1→5) compila (`dotnet build` verde) e la migrazione è **generata** ma
> **non applicata**. Eseguire questa checklist appena Docker è disponibile, prima di proseguire con la Sezione 6.

> **COMPLETATO 2026-06-13**: tutti i passi eseguiti e verificati. Nessun bug runtime trovato.
> Nota: API disponibile su porta **5022** (launchSettings.json, profilo `http`).
> Nota: DTO `PUT /admin/business-hours` e `PUT /admin/closures` wrappano la lista in `{ days: [...] }` e `{ closures: [...] }` (come da spec).

## 1. Avvio infrastruttura
- [x] `docker compose up -d` — verificare che `postgres` diventi healthy e `pgadmin` risponda su http://localhost:5050
- [x] Verificare connessione DB (es. da pgAdmin o `psql`)

## 2. Applicazione migrazione
- [x] `dotnet ef database update --project src/WebAgency_BookingSystem.Infrastructure --startup-project src/WebAgency_BookingSystem.Api`
- [x] Ispezionare lo schema creato in pgAdmin: 11 tabelle (`tenants`, `tenant_api_keys`, `tenant_business_hours`, `tenant_special_closures`, `services`, `staff`, `staff_services`, `staff_business_hours`, `bookings`, `audit_log`, `users`), indici e vincoli univoci attesi
- [x] Confermare i tipi: `day_of_week` smallint, `status`/`buffer_position`/`role` testo, `metadata` jsonb, timestamp `timestamptz`

> NOTA: la migrazione usa la factory design-time (`DesignTimeBookingSystemDbContextFactory`) che legge
> `DATABASE_URL` o la connection string locale di default. Impostare `DATABASE_URL` se diversa.

## 3. Avvio API e smoke-test
- [x] `dotnet run --project src/WebAgency_BookingSystem.Api`
- [x] Aprire `http://localhost:5022/scalar` e verificare che **tutti** gli endpoint pubblici siano elencati con summary/tag corretti
- [x] `GET /openapi/v1.json` valido (60503 chars)
- [x] `GET /api/v1/health` → 200 `{ "status": "ok" }` (DB raggiungibile)
- [x] Senza header `X-Api-Key` su un endpoint tenant-scoped → 401 `unauthorized`
- [x] Con `X-Api-Key` non valida → 403 `forbidden`

## 4. Provisioning di un tenant di test (CLI — Sezione 7, implementata)
- [x] Applicato lo schema (passo 2), eseguire:
  ```bash
  dotnet run --project tools/WebAgency_BookingSystem.TenantProvisioning -- \
    --input samples/barbershop-demo.json \
    --connection "Host=localhost;Port=5432;Database=bookingsystem;Username=postgres;Password=postgres"
  ```
- [x] Verificare l'output: API key `bk_live_...` e credenziali admin (mostrate una sola volta) → salvarle.
- [x] Ispezionare in pgAdmin: tenant (1), orari (7), chiusure (2), servizi (2), staff (2), staff_services (3), staff_hours (6), api_key (1), user Owner (1), audit (1 `tenant_created`).
- [x] Usare l'API key restituita come header `X-Api-Key` negli smoke-test del passo 5.

## 5. Validazione funzionale endpoint (con tenant valido)
- [x] `GET /api/v1/tenant/config` → 7 giorni di orari, chiusure future, `bufferMinutes: 0`
- [x] `GET /api/v1/services` → 2 servizi attivi con `staffIds`
- [x] `GET /api/v1/staff?serviceId=taglio-uomo` → 2 staff; `serviceId=taglio-barba` → 1 staff; serviceId inesistente → 404
- [x] `GET /api/v1/availability?serviceId=...&dateFrom=...&dateTo=...` → slot 15 min (34/giorno), giorni chiusi esclusi (domenica)
- [x] `POST /api/v1/bookings` slot valido → 201 con `cancellationToken`; audit log inserito
- [x] `POST /api/v1/bookings` stesso slot su servizio con `parallelSlots=2` → 201 (secondo posto); terzo → 409 `slot_unavailable`
- [x] `GET /api/v1/bookings/{id}?token=valid` → 200; token GUID valido ma errato → 404 neutro
- [x] `DELETE /api/v1/bookings/{id}?token=...` entro preavviso (>24h) → 200; fuori preavviso (<24h) → 403 `cancellation_deadline_exceeded`

## 6. Verifiche algoritmo/concorrenza (idealmente come test, Sezione 9)
- [ ] Race condition: due `POST /bookings` simultanee sullo stesso slot → una 201, una 409 (advisory lock) — richiede integration test Testcontainers (9.5)
- [ ] Buffer: slot immediatamente successivo a prenotazione con buffer > 0 → non disponibile (vedi DUBBI D-10) — richiede integration test
- [x] Pausa pranzo: slot 13:00-13:45 assenti dalla risposta availability ✓
- [x] Anticipo minimo: slot oggi (past) e slot < 1h esclusi ✓ (minAdvanceHours=1)
- [x] Chiusura domenica: 2026-06-14 (dom) assente da availability ✓

## 5-bis. Smoke-test Admin API (Sezione 6)
- [x] `POST /api/v1/admin/auth/token` con `{ tenantSlug, email, password }` → 200 con JWT; credenziali errate → 401 neutro.
- [x] `GET /admin/services`, `POST/PUT/DELETE /admin/services` (CRUD completo)
- [x] `GET /admin/staff` + visualizzazione con servizi/orari
- [x] `PUT /admin/business-hours` → 204 (**nota**: body = `{ "days": [...] }`)
- [x] `PUT /admin/closures` → 204 (**nota**: body = `{ "closures": [...] }`)
- [x] `GET /admin/bookings` (con filtri dateFrom/dateTo) + `PATCH /admin/bookings/{id}` (stato `no_show`)
- [x] Senza JWT → 401; con JWT → accesso consentito.
- [x] Dopo soft-delete servizio admin, lista pubblica mostra ancora solo i 2 originali (cache invalidata ✓)

## 6-bis. Verifica fix di hardening (sessione 2026-06-12)
- [x] **CORS (R-06):** preflight `OPTIONS` → 204 con `Access-Control-Allow-Origin` (Development: open); NON richiede `X-Api-Key` ✓
- [x] **Correlation (R-02):** ogni risposta ha header `X-Trace-Id` ✓
- [x] **Binding envelope (R-31):** `GET /availability` senza `serviceId` → 400 `bad_request` con messaggio; POST con JSON malformato → 400 `bad_request` ✓
- [x] **Rate limit per API key:** ~97 richieste → 429 (limite 100/min per key) ✓
- [ ] **Porta runtime (R-08):** da verificare con `PORT` env var in Railway/prod
- [ ] **ForwardedHeaders (R-07):** da verificare dietro proxy reale

## 7. Punti aperti da confermare con l'utente
- Race condition advisory lock → integration test con Testcontainers (9.4/9.5)
- DTO `PUT /admin/business-hours`: body `{ "days": [...] }` (non array bare) — da documentare in Scalar
- DTO `PUT /admin/closures`: body `{ "closures": [...] }` (non array bare) — da documentare in Scalar
Vedi `DUBBI_SESSIONE.md` (D-01 … D-11) per dubbi aperti.
