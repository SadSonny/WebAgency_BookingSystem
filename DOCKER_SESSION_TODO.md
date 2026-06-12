# Checklist sessione con Docker — validazioni runtime rinviate

> Questi passi NON sono stati eseguiti nella sessione autonoma del 2026-06-12 perché Docker non era
> avviabile. Il codice V1 (blocchi 1→5) compila (`dotnet build` verde) e la migrazione è **generata** ma
> **non applicata**. Eseguire questa checklist appena Docker è disponibile, prima di proseguire con la Sezione 6.

## 1. Avvio infrastruttura
- [ ] `docker compose up -d` — verificare che `postgres` diventi healthy e `pgadmin` risponda su http://localhost:5050
- [ ] Verificare connessione DB (es. da pgAdmin o `psql`)

## 2. Applicazione migrazione
- [ ] `dotnet ef database update --project src/WebAgency_BookingSystem.Infrastructure --startup-project src/WebAgency_BookingSystem.Api`
- [ ] Ispezionare lo schema creato in pgAdmin: 11 tabelle (`tenants`, `tenant_api_keys`, `tenant_business_hours`, `tenant_special_closures`, `services`, `staff`, `staff_services`, `staff_business_hours`, `bookings`, `audit_log`, `users`), indici e vincoli univoci attesi
- [ ] Confermare i tipi: `day_of_week` smallint, `status`/`buffer_position`/`role` testo, `metadata` jsonb, timestamp `timestamptz`

> NOTA: la migrazione usa la factory design-time (`DesignTimeBookingSystemDbContextFactory`) che legge
> `DATABASE_URL` o la connection string locale di default. Impostare `DATABASE_URL` se diversa.

## 3. Avvio API e smoke-test
- [ ] `dotnet run --project src/WebAgency_BookingSystem.Api`
- [ ] Aprire `http://localhost:5000/scalar` e verificare che **tutti** gli endpoint pubblici siano elencati con summary/tag corretti
- [ ] `GET /openapi/v1.json` valido
- [ ] `GET /api/v1/health` → 200 `{ "status": "ok" }` (DB raggiungibile)
- [ ] Senza header `X-Api-Key` su un endpoint tenant-scoped → 401 `unauthorized`
- [ ] Con `X-Api-Key` non valida → 403 `forbidden`

## 4. Provisioning di un tenant di test
> Richiede la CLI (Sezione 7), non ancora implementata. In alternativa inserire manualmente da pgAdmin un
> tenant + una API key (ricordando che si salva l'**hash SHA-256** della chiave, non la chiave in chiaro —
> vedi `ApiKeyHasher`). Generare l'hash con lo stesso algoritmo.

## 5. Validazione funzionale endpoint (con tenant valido)
- [ ] `GET /api/v1/tenant/config` → 7 giorni di orari, chiusure future, `bufferMinutes: 0`
- [ ] `GET /api/v1/services` → servizi attivi con `staffIds`
- [ ] `GET /api/v1/staff?serviceId=...` → filtro corretto; serviceId inesistente → 404
- [ ] `GET /api/v1/availability?serviceId=...&dateFrom=...&dateTo=...` → slot 15 min, giorni chiusi esclusi
- [ ] `POST /api/v1/bookings` slot valido → 201 con `cancellationToken`; audit log inserito
- [ ] `POST /api/v1/bookings` stesso slot già pieno → 409 `slot_unavailable`
- [ ] `GET /api/v1/bookings/{id}?token=...` corretto → 200; token errato → 404 neutro
- [ ] `DELETE /api/v1/bookings/{id}?token=...` entro preavviso → 200; fuori preavviso → 403

## 6. Verifiche algoritmo/concorrenza (idealmente come test, Sezione 9)
- [ ] Race condition: due `POST /bookings` simultanee sullo stesso slot → una 201, una 409 (advisory lock)
- [ ] Buffer: slot immediatamente successivo a prenotazione con buffer > 0 → non disponibile (vedi DUBBI D-10)
- [ ] Pausa pranzo, anticipo minimo, bordo chiusura: confrontare con i casi di `04-logica-disponibilita.md`

## 6-bis. Verifica fix di hardening (sessione 2026-06-12)
- [ ] **CORS (R-06):** preflight `OPTIONS` da un'origine consentita → 200/204 con header CORS; da origine non in `Cors:AllowedOrigins` (in non-Development) → bloccato. Verificare che il preflight NON richieda `X-Api-Key`.
- [ ] **Porta runtime (R-08):** con `PORT` impostata, l'app ascolta su quella porta (log Kestrel "Now listening on ...:$PORT").
- [ ] **ForwardedHeaders (R-07):** dietro proxy, l'IP anonimizzato in `audit_log`/log è quello del client (X-Forwarded-For), non del proxy.
- [ ] **Correlation (R-02):** ogni risposta ha header `X-Trace-Id`; i log della richiesta riportano `RequestId` e (dopo auth) `TenantId`.
- [ ] **Binding envelope (R-31):** `GET /availability` senza `dateFrom` → 400 `{ "type": "bad_request" }`; POST con JSON malformato → 400 `bad_request`.
- [ ] **Distinzione 409 (R-04):** sotto contesa concorrente i log distinguono "advisory lock non acquisito" da "slot non disponibile alla ri-verifica".
- [ ] **Rate limit per IP (R-14):** oltre ~300 richieste/min dallo stesso IP verso `/api/v1` → 429 anche con API key mancante/non valida; `/health` non è limitato.

## 7. Punti aperti da confermare con l'utente
Vedi `DUBBI_SESSIONE.md` (D-01 … D-11): versioni NuGet, header API key, `tenant/config.bufferMinutes`,
soft delete, semantica buffer, warning MSB3277 nel tool provisioning.
