<!-- [INTENT]: Guida operativa per integrare un sito esterno con il BookingSystem. Tre parti: (1) API esposte
con contratto completo, (2) onboarding di un nuovo sito tenant, (3) uso della CLI di provisioning. Documento
rivolto agli sviluppatori che collegano i siti dei clienti; allineato al codice al 2026-06-14. -->

# Guida Integrazione API — WebAgency BookingSystem

> Backend multi-tenant **API-only** per prenotazioni di attività locali. I siti esterni dei clienti
> (tenant) si collegano via **HTTPS** (in produzione; in sviluppo locale si usa HTTP — vedi §1).
> **Non esiste una Admin UI** (AD-09): la gestione avviene via Admin API + CLI di provisioning.
> Questa guida copre: API esposte, onboarding di un nuovo sito, uso della CLI.

Ultimo allineamento al codice: **2026-06-15** (include hardening PH-1..PH-5).

---

## 1. Concetti fondamentali

### Base URL
| Ambiente | URL |
|---|---|
| Sviluppo locale | `http://localhost:5022` (profilo `http` in `launchSettings.json`) |
| Produzione | `https://<servizio>.railway.app` (HTTPS, EU West) |

Tutte le rotte sono versionate sotto `/api/v1`.

> **HTTP vs HTTPS.** In **produzione** il backend è raggiungibile **solo in HTTPS**: il TLS è terminato dal
> proxy della piattaforma (Railway), che inoltra la richiesta al container in HTTP sulla rete interna. L'app
> usa `UseForwardedHeaders` per ricostruire scheme/IP reali del client e `UseHttpsRedirection`. In **sviluppo
> locale** l'API gira in chiaro su HTTP per comodità (nessun certificato da gestire). Quindi: i siti client
> chiamano sempre `https://…` in produzione — l'HTTP visto in questa guida riguarda solo l'ambiente locale.

### Autenticazione — due livelli distinti
| Livello | Header | Chi lo usa | Scope |
|---|---|---|---|
| **Pubblica** | `X-Api-Key: <chiave>` | Il widget/sito del cliente finale | Tutte le rotte `/api/v1/*` tranne `/health*` e `/admin/*` |
| **Admin** | `Authorization: Bearer <JWT>` | Backoffice/script del titolare | Rotte `/api/v1/admin/*` (tranne il login) |

Ogni **API key appartiene a un solo tenant**. Tutte le risorse restituite sono automaticamente filtrate per
quel tenant (isolamento via global query filter su `tenant_id`): un sito non può mai vedere i dati di un altro.

### Formati e convenzioni
- **Date**: `yyyy-MM-dd` · **Orari**: `HH:mm` · **Timestamp**: ISO 8601.
- **Orari sempre LOCALI del tenant** nelle response (mai UTC esposto al cliente).
- Body e response JSON in **camelCase**.
- Errori in **italiano**.

### Envelope di errore (uniforme su tutte le rotte)
```json
{
  "type": "validation_error",
  "message": "Messaggio leggibile in italiano.",
  "errors": { "campo": ["dettaglio 1", "dettaglio 2"] }
}
```
`errors` è presente **solo** per i `validation_error` (422). `type` è un codice stabile snake_case
(`unauthorized`, `forbidden`, `not_found`, `slot_unavailable`, `validation_error`, `bad_request`,
`rate_limit_exceeded`, `internal_error`).

### Codici di stato comuni
| Codice | Significato |
|---|---|
| 400 `bad_request` | Parametri query mancanti/malformati o JSON non valido |
| 401 `unauthorized` | API key mancante (pubblico) o JWT assente/non valido (admin) |
| 403 `forbidden` | API key non valida/tenant disattivato, o azione non consentita (es. disdetta oltre preavviso) |
| 404 `not_found` | Risorsa inesistente (404 **neutro** su prenotazioni: non rivela se l'id esiste) |
| 409 `slot_unavailable` | Slot non più disponibile (pieno o conteso sotto lock) |
| 422 `validation_error` | Dati o regole di business non soddisfatte |
| 429 `rate_limit_exceeded` | Troppe richieste (header `Retry-After: 60`) |

### Rate limiting
- **Per API key**: 100 richieste/minuto (sliding window), configurabile (`RATE_LIMIT_PER_MINUTE`).
- **Per IP**: 300/minuto, applicato a monte dell'auth (anti brute-force), configurabile (`RATE_LIMIT_IP_PER_MINUTE`).

### Correlazione e supporto
Ogni response include l'header **`X-Trace-Id`**: comunicalo nelle segnalazioni, è la chiave per correlare i log.

### Documentazione interattiva
In ambiente non-produzione: **Scalar UI** su `/scalar`, OpenAPI JSON su `/openapi/v1.json` (disattivati in produzione).

---

## 2. API esposte

### 2.1 Sistema (no auth)

#### `GET /api/v1/health/live` — Liveness
Indica che il processo è vivo, **senza** toccare il DB (per probe frequenti).
→ `200 { "status": "ok", "timestamp": "..." }`

#### `GET /api/v1/health` — Readiness
Verifica la raggiungibilità del database.
→ `200` se pronto · `503` se il DB non risponde.

---

### 2.2 Pubbliche (auth: `X-Api-Key`)

#### `GET /api/v1/tenant/config` — Configurazione del tenant
Regole di prenotazione, orari settimanali (sempre 7 giorni, 0=Dom..6=Sab) e chiusure straordinarie future.
Usato dal widget per la validazione lato client.
```json
{
  "tenantId": "…", "name": "Barbershop Mario", "timezone": "Europe/Rome",
  "staffChoiceEnabled": true, "minAdvanceHours": 1, "minCancellationHours": 24,
  "visibleDaysAhead": 30, "bufferMinutes": 0,
  "businessHours": [ { "dayOfWeek": 1, "isOpen": true, "openTime": "09:00", "closeTime": "19:00", "breakStart": "13:00", "breakEnd": "14:00" } ],
  "specialClosures": [ { "dateFrom": "2026-08-15", "dateTo": "2026-08-20", "reason": "Ferie" } ]
}
```
> Nota: `bufferMinutes` è sempre 0 a questo livello — il buffer è **per servizio** (AD-03) e già applicato lato server nel calcolo della disponibilità.

#### `GET /api/v1/services` — Lista servizi attivi
```json
[ { "id": "…", "name": "Taglio Uomo", "category": "Capelli", "durationMinutes": 30,
    "basePrice": 18.0, "description": "…", "staffIds": ["…"], "active": true } ]
```

#### `GET /api/v1/staff?serviceId={guid}` — Lista staff attivo
`serviceId` opzionale: se presente filtra chi esegue quel servizio (404 se il servizio non esiste/non è attivo).
```json
[ { "id": "…", "name": "Marco", "role": "Barbiere", "specialization": "…", "photoUrl": "…", "active": true } ]
```

#### `GET /api/v1/availability` — Slot prenotabili
Query: `serviceId` (obbligatorio), `staffId` (opzionale), `dateFrom`, `dateTo` (obbligatori, `yyyy-MM-dd`, max 31 giorni).
- Con `staffId`: disponibilità individuale dello staff.
- Senza `staffId`: aggregata sui `parallelSlots` del servizio.
- Granularità **15 min**. I giorni chiusi non sono inclusi; i giorni pieni sono inclusi con slot `available: false`.
```json
[ { "date": "2026-06-22",
    "slots": [ { "time": "09:00", "staffId": null, "available": true },
               { "time": "09:15", "staffId": null, "available": false } ] } ]
```

#### `POST /api/v1/bookings` — Crea prenotazione
Creazione **atomica** con advisory lock PostgreSQL **bloccante** (no doppie prenotazioni sullo stesso slot;
nessun 409 spurio sui servizi multi-posto `parallelSlots>1` — le richieste legittime concorrenti si accodano).
```json
// Richiesta
{ "serviceId": "…", "staffId": null, "date": "2026-06-22", "time": "10:00",
  "customer": { "name": "Luca Bianchi", "phone": "+39 333 1234567", "email": "luca@example.it", "notes": "…" },
  "gdprConsent": true }
```
```json
// 201 Created
{ "bookingId": "…", "status": "confirmed", "cancellationToken": "…" }
```
→ Esiti: `201` ok · `409 slot_unavailable` slot pieno/conteso · `422 validation_error` dati o regole (anticipo minimo, finestra prenotabile, giorno chiuso) · `400 bad_request` JSON malformato.
> ⚠ **Conserva il `cancellationToken`**: è l'unico modo per consultare/disdire la prenotazione senza login.
> Alla creazione le email (conferma al cliente + notifica al titolare) sono **accodate in una outbox
> transazionale** e inviate da un dispatcher in background con retry: la consegna è garantita anche se il
> provider email è momentaneamente irraggiungibile, e non aggiunge latenza alla risposta.

#### `GET /api/v1/bookings/{id}?token={guid}` — Dettaglio prenotazione
Richiede `id` + `token`. **404 neutro** se non combaciano (non rivela l'esistenza dell'id).
```json
{ "bookingId": "…", "status": "confirmed", "date": "2026-06-22", "time": "10:00", "durationMin": 30,
  "service": { "id": "…", "name": "Taglio Uomo" }, "staff": { "id": "…", "name": "Marco" },
  "customer": { "name": "Luca Bianchi", "email": "luca@example.it" },
  "canCancel": true, "cancellationDeadline": "2026-06-21T10:00:00" }
```

#### `DELETE /api/v1/bookings/{id}?token={guid}` — Disdici prenotazione
Disdetta del cliente via `id` + `token`. `403` se oltre il preavviso minimo (`minCancellationHours`), `404` neutro se id/token non combaciano.
```json
{ "bookingId": "…", "status": "cancelled", "message": "Prenotazione disdetta con successo." }
```

---

### 2.3 Admin (auth: `Authorization: Bearer <JWT>`)

#### `POST /api/v1/admin/auth/token` — Login admin (anonimo)
Il tenant è identificato dallo **slug** (l'email è unica solo all'interno del tenant).
```json
// Richiesta
{ "tenantSlug": "barbershop-mario", "email": "owner@barbershop.it", "password": "…" }
// 200
{ "token": "<JWT>", "tokenType": "Bearer", "expiresAt": "2026-06-14T20:00:00Z" }
```
Il JWT (validità default 8h) porta `user_id`, `tenant_id`, `role`. Usalo come header su tutte le rotte admin.

#### Prenotazioni
| Metodo | Path | Azione |
|---|---|---|
| `GET` | `/api/v1/admin/bookings` | Lista con filtri query: `dateFrom`, `dateTo`, `staffId`, `serviceId`, `status` (`confirmed`/`cancelled`/`no_show`/`completed`) |
| `PATCH` | `/api/v1/admin/bookings/{id}` | Aggiorna stato (es. `no_show`, `completed`, `cancelled`); registra audit |

#### Servizi (CRUD)
| Metodo | Path | Azione |
|---|---|---|
| `GET` | `/api/v1/admin/services` | Lista (inclusi inattivi, esclusi soft-deleted) |
| `POST` | `/api/v1/admin/services` | Crea → `201` |
| `PUT` | `/api/v1/admin/services/{id}` | Aggiorna |
| `DELETE` | `/api/v1/admin/services/{id}` | Soft delete → `204` (+ invalidazione cache) |

#### Staff (CRUD)
| Metodo | Path | Azione |
|---|---|---|
| `GET` | `/api/v1/admin/staff` | Lista (inclusi inattivi) con servizi erogati e orari |
| `POST` | `/api/v1/admin/staff` | Crea (con assegnazione servizi + orari) → `201` |
| `PUT` | `/api/v1/admin/staff/{id}` | Aggiorna |
| `DELETE` | `/api/v1/admin/staff/{id}` | Soft delete → `204` (+ invalidazione cache) |

#### Orari e chiusure (sostituzione in blocco)
| Metodo | Path | Body | Azione |
|---|---|---|---|
| `PUT` | `/api/v1/admin/business-hours` | `{ "days": [ … ] }` | Sostituisce tutti gli orari settimanali → `204` |
| `PUT` | `/api/v1/admin/closures` | `{ "closures": [ … ] }` | Sostituisce tutte le chiusure straordinarie → `204` |
> ⚠ Entrambi richiedono il body **wrappato** (`days` / `closures`), non un array nudo.

---

## 3. Onboarding di un nuovo sito (tenant)

Quando un nuovo sito cliente deve collegarsi:

1. **Raccogli i dati dell'attività**: nome, slug univoco, URL del sito, email titolare, timezone, regole
   (anticipo/preavviso/giorni visibili), orari settimanali, eventuali chiusure, servizi (durata, prezzo,
   `parallelSlots`, buffer), staff (orari, servizi erogati).
2. **Componi il file JSON di provisioning** (vedi `samples/barbershop-demo.json` come modello).
3. **Esegui la CLI di provisioning** (sezione 4). Restituisce — **una sola volta** —:
   - la **API key** (`bk_live_…`) del tenant,
   - le **credenziali admin** (email titolare + password generata).
   👉 Salvale subito in un gestore di segreti: non sono più recuperabili (in DB c'è solo l'hash).
4. **Configura il frontend del cliente** con la API key e la base URL del backend:
   ```
   VITE_BOOKING_API_KEY=bk_live_xxxxxxxx
   VITE_BOOKING_API_URL=https://<backend>/api/v1
   ```
5. **CORS — nessuna azione manuale** (PH-1): l'origine del sito cliente è autorizzata **automaticamente** dal
   suo `siteUrl` (campo del provisioning). Il backend ricostruisce in background l'elenco delle origini ammesse
   dai tenant attivi. Solo per domini extra (es. staging) si aggiunge una voce a `Cors:AllowedOrigins`.
6. **Verifica end-to-end**: `GET /tenant/config` e `GET /services` con la nuova API key, poi una prenotazione
   di prova; controlla l'arrivo dell'email (in dev: UI Mailpit `http://localhost:8025`).
7. **Consegna al cliente** le credenziali admin per la gestione via Admin API.

> Aggiornare un tenant esistente (modalità `--update`) **non è ancora supportato**: in V1 si possono modificare
> servizi/staff/orari **via Admin API**; la ri-esecuzione della CLI sullo stesso slug viene rifiutata.

---

## 4. CLI di provisioning — come, quando, perché

**Progetto:** `tools/WebAgency_BookingSystem.TenantProvisioning`

### Perché esiste
Non c'è una Admin UI per creare un tenant da zero, e la creazione iniziale richiede operazioni che **non**
sono esposte via API per sicurezza: generazione della API key, creazione dell'utente Owner con password,
inserimento atomico di tutta la configurazione. La CLI è lo strumento di **bootstrap** di un nuovo tenant.

### Quando usarla
- **Sempre** alla creazione di un nuovo tenant (onboarding di un nuovo sito).
- **Solo** per il bootstrap: le modifiche correnti (aggiungere un servizio, cambiare orari…) si fanno via Admin API.
- Esecuzione manuale e controllata da un operatore (non è un endpoint, non è automatizzabile da remoto).

### Come si usa
```bash
dotnet run --project tools/WebAgency_BookingSystem.TenantProvisioning -- \
  --file samples/barbershop-demo.json \
  --connection "Host=localhost;Port=5432;Database=bookingsystem;Username=postgres;Password=postgres"
```
- `--file` (o `--input`): percorso del JSON di provisioning **(obbligatorio)**.
- `--connection`: connection string PostgreSQL; in alternativa imposta la variabile **`DATABASE_URL`**.

### Cosa fa (transazione unica e atomica)
1. Valida il JSON (raccoglie **tutti** gli errori con messaggi chiari).
2. Verifica che lo slug non esista già (altrimenti si ferma: `--update` non supportato).
3. Inserisce in un **solo `SaveChanges`**: tenant → orari → chiusure → servizi → staff (+ associazioni staff↔servizi e orari staff).
4. Genera la **API key** (`bk_live_…`, salvata come hash SHA-256) e l'**utente Owner** (password bcrypt).
5. Registra l'`audit_log` (`tenant_created`).
6. Stampa i segreti generati — **da mostrare una sola volta**.

### Codici di uscita
| Codice | Significato |
|---|---|
| `0` | Successo |
| `1` | Errore di runtime (DB irraggiungibile, provisioning interrotto, slug già esistente) |
| `2` | Errore di input (argomenti mancanti, file non trovato, JSON non valido, validazione fallita) |

### Struttura del file JSON (sintesi)
```jsonc
{
  "slug": "barbershop-mario", "name": "Barbershop Mario",
  "siteUrl": "https://…", "ownerEmail": "owner@…", "timezone": "Europe/Rome",
  "bookingRules": { "minAdvanceHours": 1, "minCancellationHours": 24, "visibleDaysAhead": 30,
                    "staffChoiceEnabled": true, "notificationMethod": "email" },
  "businessHours": [ { "dayOfWeek": 1, "isOpen": true, "openTime": "09:00", "closeTime": "19:00",
                       "breakStart": "13:00", "breakEnd": "14:00" } ],
  "specialClosures": [ { "dateFrom": "2026-08-15", "dateTo": "2026-08-20", "reason": "Ferie" } ],
  "services": [ { "localId": "s1", "name": "Taglio Uomo", "durationMinutes": 30, "basePrice": 18.0,
                  "parallelSlots": 1, "bufferEnabled": false, "bufferMinutes": 0, "bufferPosition": "After" } ],
  "staff": [ { "localId": "st1", "name": "Marco", "role": "Barbiere",
               "businessHours": [ { "dayOfWeek": 1, "isAvailable": true, "startTime": "09:00", "endTime": "19:00" } ],
               "services": [ { "serviceLocalId": "s1", "priceOverride": null } ] } ]
}
```
> `localId`/`serviceLocalId` sono identificatori **interni al file** per collegare staff↔servizi; la CLI genera
> gli UUID reali. Dettagli completi: `tools/WebAgency_BookingSystem.TenantProvisioning/README.md`.

---

## 5. Note operative per la produzione

- **HTTPS**: il backend è esposto **solo in HTTPS** (TLS terminato dal proxy Railway; il container parla HTTP
  dietro il proxy, con `UseForwardedHeaders` per scheme/IP reali e `UseHttpsRedirection`). In locale è HTTP.
- **Variabili d'ambiente** (Railway): `DATABASE_URL`, `JWT_SECRET` (≥32 char), `EMAIL_PROVIDER=Brevo`,
  `BREVO_API_KEY`, `BREVO_SENDER_EMAIL` (mittente **verificato** su Brevo), `BREVO_SENDER_NAME`,
  eventuali `RATE_LIMIT_*`, `Cors__AllowedOrigins__0=…`. La porta è iniettata via `PORT`.
  Ricorda di applicare le **migration** (incl. `AddEmailOutbox`) al deploy.
- **Email**: `Mailpit` in sviluppo (cattura, UI `:8025`), `Brevo` in produzione. Invio via **outbox
  transazionale** con retry/backoff (dispatcher in background, `Email:Outbox:PollSeconds`). Vedi `.env.example`.
- **CORS multi-tenant (PH-1)**: le origini ammesse derivano **automaticamente** dai `siteUrl` dei tenant attivi,
  aggiornate in background ogni `Cors:OriginRefreshSeconds` (default 60s) — onboardare un nuovo sito non richiede
  modifiche di config. `Cors:AllowedOrigins` resta una allowlist statica **aggiuntiva** (es. tool interni).
  Nota: il preflight CORS non porta `X-Api-Key`, quindi l'autorizzazione è sull'origine, non per-chiave.
- **Doc API**: Scalar/OpenAPI sono esposti solo in non-produzione.
