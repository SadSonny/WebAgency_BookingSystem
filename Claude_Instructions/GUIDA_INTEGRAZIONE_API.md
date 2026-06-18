<!-- [INTENT]: Guida operativa per integrare un sito esterno con il BookingSystem. Tre parti: (1) API esposte
con contratto completo, (2) onboarding di un nuovo sito tenant, (3) uso della CLI di provisioning. Documento
rivolto agli sviluppatori che collegano i siti dei clienti; allineato al codice al 2026-06-14. -->

# Guida Integrazione API — WebAgency BookingSystem

> Backend multi-tenant **API-only** per prenotazioni di attività locali. I siti esterni dei clienti
> (tenant) si collegano via **HTTPS** (in produzione; in sviluppo locale si usa HTTP — vedi §1).
> **Non esiste una Admin UI** (AD-09): la gestione avviene via Admin API + CLI di provisioning.
> Questa guida copre: API esposte, onboarding di un nuovo sito, uso della CLI.

Ultimo allineamento al codice: **2026-06-18** (include hardening PH-1..PH-5 + V2.3 onboarding Owner + Agency Provisioning API).

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

### Autenticazione — tre livelli distinti
| Livello | Header | Chi lo usa | Scope |
|---|---|---|---|
| **Pubblica** | `X-Api-Key: <chiave>` | Il widget/sito del cliente finale | Tutte le rotte `/api/v1/*` tranne `/health*`, `/admin/*` e `/platform/*` |
| **Admin** | `Authorization: Bearer <JWT>` | Backoffice/script del titolare | Rotte `/api/v1/admin/*` (tranne il login) |
| **Platform** | `Authorization: Bearer <JWT PlatformAdmin>` | Agenzia — gestione tenant | Rotte `/api/v1/platform/*` (tranne `/platform/auth/token` e `/platform/setup`) |

> **Identità separate:** il JWT `PlatformAdmin` usa un'audience dedicata (`Jwt:PlatformAudience`, default `WebAgency_BookingSystem.Platform`) e **non** è accettato sulle rotte admin tenant (e viceversa). Le rotte platform **non** usano `X-Api-Key`: il `TenantResolutionMiddleware` le esclude automaticamente.

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
- **Creazione prenotazioni** (`POST /bookings`): limite dedicato più basso per API key (default 10/min,
  `RATE_LIMIT_BOOKING_PER_MINUTE`) — anti-spam con chiave pubblica esposta (S1).
- **Account + login** (`/admin/auth/token`, `/admin/account/*`): limite per IP (default 10/min,
  `RATE_LIMIT_ACCOUNT_PER_MINUTE`) — policy `AccountSecurity` (V2.3).

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
  "gdprConsent": true,
  "additionalServiceIds": [] }
```
```json
// 201 Created
{ "bookingId": "…", "status": "confirmed", "cancellationToken": "…" }
```
→ Esiti: `201` ok · `409 slot_unavailable` slot pieno/conteso · `422 validation_error` dati o regole (anticipo minimo, finestra prenotabile, giorno chiuso) · `400 bad_request` JSON malformato.
> **Operatore (T1.2):** con `staffId: null` ("qualsiasi") il sistema **auto-assegna** un operatore qualificato
> libero; con `staffId` specifico verifica che esegua i servizi richiesti e sia libero.
> **Multi-servizio (T1.3):** `additionalServiceIds` (opzionale) aggiunge servizi svolti **consecutivamente dallo
> stesso operatore**; durata e prezzo dell'appuntamento sono la **somma**. L'operatore deve eseguire **tutti** i servizi.
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
  "canCancel": true, "cancellationDeadline": "2026-06-21T10:00:00",
  "services": [ { "id": "…", "name": "Taglio Uomo" } ] }
```
> `services` elenca tutti i servizi dell'appuntamento in ordine (T1.3); `service` resta il principale.

#### `PUT /api/v1/bookings/{id}/reschedule?token={guid}` — Sposta prenotazione (T2.2)
Sposta una prenotazione **confermata** a una nuova data/ora mantenendo servizi e operatore. Ri-verifica la
disponibilità del nuovo slot sotto advisory lock (escludendo se stessa).
```json
// Richiesta
{ "date": "2026-06-23", "time": "11:30" }
```
→ `200` con il dettaglio aggiornato · `403` oltre il preavviso · `409 slot_unavailable` nuovo slot occupato ·
`422` se la prenotazione non è modificabile o lo slot non è prenotabile · `404` neutro se id/token non combaciano.

#### `DELETE /api/v1/bookings/{id}?token={guid}` — Disdici prenotazione
Disdetta del cliente via `id` + `token`. `403` se oltre il preavviso minimo (`minCancellationHours`), `404` neutro se id/token non combaciano.
```json
{ "bookingId": "…", "status": "cancelled", "message": "Prenotazione disdetta con successo." }
```

---

### 2.3 Admin (auth: `Authorization: Bearer <JWT>`)

#### `POST /api/v1/admin/auth/token` — Login admin (anonimo)
L'email è **univoca a livello globale**: il tenant si ricava dall'account, non dallo slug (⚠ breaking change da V2.3 — rimosso `tenantSlug`).
```json
// Richiesta
{ "email": "owner@barbershop.it", "password": "…" }
// 200
{ "token": "<JWT>", "tokenType": "Bearer", "expiresAt": "2026-06-14T20:00:00Z" }
```
Il JWT (validità default 8h) porta `user_id`, `tenant_id`, `role`, `security_stamp`. Usalo come header su tutte le rotte admin.
> ⚠ **SecurityStamp:** al cambio/reset/attivazione password lo stamp si rigenera; i token emessi prima diventano invalidi (l'utente deve rifare il login). Gestire il 401 nel frontend redirigendo al login.

#### Prenotazioni
| Metodo | Path | Azione |
|---|---|---|
| `GET` | `/api/v1/admin/bookings` | Lista **paginata** con filtri: `dateFrom`, `dateTo`, `staffId`, `serviceId`, `status`, `page` (1-based, default 1), `pageSize` (default 50, max 200). Risposta: `{ items, page, pageSize, total }` (P4) |
| `PATCH` | `/api/v1/admin/bookings/{id}` | Aggiorna stato (`no_show`/`completed`/`cancelled`); audit. Il passaggio a `cancelled` **notifica il cliente** via email (T2.1) |

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

#### Assenze operatore (T1.1)
| Metodo | Path | Azione |
|---|---|---|
| `GET` | `/api/v1/admin/staff/{id}/time-off` | Elenca le assenze (ferie/malattia/permessi) |
| `POST` | `/api/v1/admin/staff/{id}/time-off` | Crea un'assenza → `201` |
| `DELETE` | `/api/v1/admin/staff/{id}/time-off/{timeOffId}` | Elimina → `204` |
```json
// POST body: giornata intera (orari null) oppure fascia oraria (startTime+endTime)
{ "dateFrom": "2026-08-12", "dateTo": "2026-08-16", "startTime": null, "endTime": null, "reason": "Ferie" }
```
> L'operatore assente è escluso dalla disponibilità (giorno intero → giorno escluso; fascia → slot sovrapposti non prenotabili).

#### Orari e chiusure (sostituzione in blocco)
| Metodo | Path | Body | Azione |
|---|---|---|---|
| `PUT` | `/api/v1/admin/business-hours` | `{ "days": [ … ] }` | Sostituisce tutti gli orari settimanali → `204` |
| `PUT` | `/api/v1/admin/closures` | `{ "closures": [ … ] }` | Sostituisce tutte le chiusure straordinarie → `204` |
> ⚠ Entrambi richiedono il body **wrappato** (`days` / `closures`), non un array nudo.

#### API key — rotazione/revoca (S4)
| Metodo | Path | Azione |
|---|---|---|
| `GET` | `/api/v1/admin/api-keys` | Elenca le chiavi del tenant (solo prefisso + metadati, mai il segreto) |
| `POST` | `/api/v1/admin/api-keys` | Genera una nuova chiave → `201` con il valore in chiaro **mostrato una sola volta** |
| `DELETE` | `/api/v1/admin/api-keys/{id}` | Revoca (disattiva) → `204` (effetto immediato, rimossa dalla cache) |
> Rotazione consigliata se una chiave è compromessa: genera la nuova, aggiorna il frontend, poi revoca la vecchia.

#### Account Owner — gestione credenziali (V2.3, Modello A)

> **Modello A:** l'agenzia costruisce il form di login e il pannello di gestione sul **sito del cliente**, che
> chiama le nostre API per ottenere il JWT. L'API **non** espone una dashboard — ospita solo le pagine
> di impostazione password raggiunte tramite i link email (deroga circoscritta ad AD-09).

**Flusso di attivazione (primo accesso):**
1. Il provisioning crea l'Owner **senza password** e accoda un'email con il link di attivazione.
2. Il link punta a `GET /api/v1/admin/account/activate?token=<token>` — pagina HTML servita dall'API.
3. L'Owner imposta la password: `POST /api/v1/admin/account/activate` `{ token, newPassword }` → `204`.
4. Da quel momento può fare login con `POST /api/v1/admin/auth/token` `{ email, password }`.

**Flusso normale (sito del cliente → API):**
```
// Il sito costruisce il form di login e chiama:
POST /api/v1/admin/auth/token  { "email": "owner@barbershop.it", "password": "…" }
// → { "token": "…", "tokenType": "Bearer", "expiresAt": "…" }
// Il sito conserva il JWT (sessionStorage / cookie httpOnly) e lo invia come:
Authorization: Bearer <JWT>
```

**Cambio password autenticato:**
```
POST /api/v1/admin/account/password
Authorization: Bearer <JWT>
{ "currentPassword": "…", "newPassword": "…" }   → 204
```
> Dopo il cambio, il `security_stamp` si rigenera: il JWT corrente diventa invalido. Il frontend deve
> gestire il 401 successivo redirigendo al login (il vecchio token non è più accettato).

**Reset password "dimenticata":**
```
// Passo 1 — richiesta reset (risposta SEMPRE 202, non rivela se l'email esiste):
POST /api/v1/admin/account/password/reset-request  { "email": "owner@barbershop.it" }   → 202

// Il link email punta a:
GET /api/v1/admin/account/password/reset?token=<token>   → pagina HTML servita dall'API

// Passo 2 — imposta nuova password:
POST /api/v1/admin/account/password/reset  { "token": "…", "newPassword": "…" }   → 204
// Token non valido o scaduto → 422
```

| Metodo | Path | Auth | Azione |
|---|---|---|---|
| `GET` | `/api/v1/admin/account/activate?token=` | nessuna | Pagina HTML "imposta password" |
| `POST` | `/api/v1/admin/account/activate` | nessuna | Attiva account + imposta password → `204` |
| `POST` | `/api/v1/admin/account/password` | JWT | Cambio password autenticato → `204` |
| `POST` | `/api/v1/admin/account/password/reset-request` | nessuna | Richiesta reset via email → `202` (sempre neutro) |
| `GET` | `/api/v1/admin/account/password/reset?token=` | nessuna | Pagina HTML reset password |
| `POST` | `/api/v1/admin/account/password/reset` | nessuna | Reset password con token → `204` |

> **Policy password:** minimo 12 caratteri (configurabile con `Account:PasswordMinLength`).
> **Rate limit:** tutte queste rotte + il login sono protetti dalla policy `AccountSecurity` (10 req/min per IP).

---

## 2.4 API Platform agenzia (auth: JWT PlatformAdmin)

La **Platform API** è lo strumento operativo dell'agenzia per gestire il ciclo di vita dei tenant senza accesso diretto al DB o alla CLI. L'identità `PlatformAdmin` è separata da tutti i tenant (nessun `tenant_id` nel JWT).

### Bootstrap (primo avvio — break-glass)

Prima di poter usare le rotte platform è necessario creare l'admin di piattaforma tramite il token segreto configurato nell'env `PLATFORM_SETUP_TOKEN`. Se la variabile non è configurata, l'endpoint risponde **404**.

```
POST /api/v1/platform/setup
// Nessuna auth richiesta
{ "setupToken": "<PLATFORM_SETUP_TOKEN>", "email": "admin@agenzia.it", "password": "..." }
// → 204 (crea-o-reimposta l'admin per quell'email)
```

> ⚠ Usare `setup` **solo al primo avvio** (o come break-glass in caso di perdita credenziali). In produzione configurare `PLATFORM_SETUP_TOKEN` come segreto Railway e rimuoverlo dall'env dopo il bootstrap iniziale.

### Login platform

```
POST /api/v1/platform/auth/token
// Nessuna auth richiesta
{ "email": "admin@agenzia.it", "password": "..." }
// → { "token": "<JWT>", "tokenType": "Bearer", "expiresAt": "..." }
```

Il JWT contiene il ruolo `PlatformAdmin` e l'audience platform. Usarlo come `Authorization: Bearer <JWT>` su tutte le rotte `/platform/*` che richiedono auth.

### Cambio password platform

```
POST /api/v1/platform/account/password
Authorization: Bearer <JWT PlatformAdmin>
{ "currentPassword": "...", "newPassword": "..." }
// → 204
```

### Gestione tenant

#### Crea tenant

```
POST /api/v1/platform/tenants
Authorization: Bearer <JWT PlatformAdmin>
```
Body — `ProvisioningInput` (stesso schema della CLI):
```jsonc
{
  "slug": "barbershop-mario",
  "name": "Barbershop Mario",
  "siteUrl": "https://barbershopmario.it",
  "ownerEmail": "owner@barbershop.it",
  "timezone": "Europe/Rome",
  "bookingRules": {
    "minAdvanceHours": 1, "minCancellationHours": 24, "visibleDaysAhead": 30,
    "staffChoiceEnabled": true, "notificationMethod": "email"
  },
  "businessHours": [
    { "dayOfWeek": 1, "isOpen": true, "openTime": "09:00", "closeTime": "19:00",
      "breakStart": "13:00", "breakEnd": "14:00" }
  ],
  "specialClosures": [],
  "services": [
    { "localId": "s1", "name": "Taglio Uomo", "durationMinutes": 30, "basePrice": 18.0,
      "parallelSlots": 1, "bufferEnabled": false, "bufferMinutes": 0, "bufferPosition": "After" }
  ],
  "staff": [
    { "localId": "st1", "name": "Marco", "role": "Barbiere",
      "businessHours": [ { "dayOfWeek": 1, "isAvailable": true, "startTime": "09:00", "endTime": "19:00" } ],
      "services": [ { "serviceLocalId": "s1", "priceOverride": null } ] }
  ]
}
```
→ `201` con la **API key in chiaro** (mostrata **una sola volta** — salvarla subito):
```json
{ "tenantId": "...", "slug": "barbershop-mario", "apiKey": "bk_live_..." }
```
→ `409` se lo slug esiste già · `422` per errori di validazione.

> La CLI di provisioning usa lo stesso `ITenantProvisioningService` internamente: il comportamento è identico.
> L'Owner riceve automaticamente l'email di attivazione (flusso V2.3).

#### Elenco e dettaglio tenant

```
GET /api/v1/platform/tenants?page=1&pageSize=20
// → { "items": [...], "page": 1, "pageSize": 20, "total": N }

GET /api/v1/platform/tenants/{id}
// → dettaglio tenant (configurazione, stato, slug, email owner, ...)
```

#### Attivazione / disattivazione

```
POST /api/v1/platform/tenants/{id}/deactivate   → 204  (tutte le X-Api-Key del tenant smettono di funzionare)
POST /api/v1/platform/tenants/{id}/reactivate   → 204
```

#### Gestione API key del tenant

```
GET    /api/v1/platform/tenants/{id}/api-keys
// → elenco chiavi (solo prefisso + metadati, mai il segreto)

POST   /api/v1/platform/tenants/{id}/api-keys
// → 201 con la chiave in chiaro (una sola volta)

DELETE /api/v1/platform/tenants/{id}/api-keys/{keyId}
// → 204 (effetto immediato, rimossa dalla cache)
```

#### Resend email attivazione Owner

```
POST /api/v1/platform/tenants/{id}/owner/resend-activation
// → 204  (genera nuovo token 72h e re-accoda l'email di attivazione)
```

> Usare se l'Owner non ha trovato l'email originale o se il link è scaduto.

---

## 3. Onboarding di un nuovo sito (tenant)

Quando un nuovo sito cliente deve collegarsi:

1. **Raccogli i dati dell'attività**: nome, slug univoco, URL del sito, email titolare, timezone, regole
   (anticipo/preavviso/giorni visibili), orari settimanali, eventuali chiusure, servizi (durata, prezzo,
   `parallelSlots`, buffer), staff (orari, servizi erogati).
2. **Componi il file JSON di provisioning** (vedi `samples/barbershop-demo.json` come modello).
3. **Esegui la CLI di provisioning** (sezione 4). Restituisce — **una sola volta** —:
   - la **API key** (`bk_live_…`) del tenant.
   - La CLI **non stampa più una password**: l'Owner riceverà un'email con il link di attivazione
     per impostare autonomamente la propria password (flusso V2.3).
   👉 Salva subito la API key in un gestore di segreti: non è più recuperabile (in DB c'è solo l'hash).
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
La CLI è lo strumento di **bootstrap** alternativo: esegue la stessa logica dell'API Platform (`ITenantProvisioningService` condiviso) ma in modalità locale, utile per automazioni o ambienti senza accesso rete. Con l'API Platform (`POST /api/v1/platform/tenants`) è ora possibile creare tenant via HTTP senza CLI né accesso diretto al DB — la CLI resta come fallback/automazione.

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
4. Genera la **API key** (`bk_live_…`, salvata come hash SHA-256) e l'**utente Owner** senza password (V2.3: la password viene impostata dall'Owner tramite il link di attivazione nell'email).
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
- **Variabili d'ambiente** (Railway): `DATABASE_URL`, `JWT_SECRET` (≥32 char, **non** il placeholder dev: in
  Production l'avvio fallisce se contiene `change-me` — S5), `EMAIL_PROVIDER=Brevo`, `BREVO_API_KEY`,
  `BREVO_SENDER_EMAIL` (mittente **verificato** su Brevo), `BREVO_SENDER_NAME`, eventuali `RATE_LIMIT_*`,
  `Cors__AllowedOrigins__0=…`. La porta è iniettata via `PORT`.
  Ricorda di applicare **tutte le migration** (incl. `AddEmailOutbox`, `AddStaffTimeOff`, `AddBookingItems`,
  `AddReminderFields`, `AddReminderScanIndex`, `AddUserLockout`, `MakeEmailGlobalAndAddSecurityFields`,
  `AddUserSecurityTokens`, `AddPlatformAdmin`) al deploy. Imposta anche `PUBLIC_BASE_URL=https://<servizio>.railway.app` (usata
  nei link email di attivazione e reset password).
  Per la Platform API: imposta `PLATFORM_SETUP_TOKEN` al primo avvio (break-glass), usalo per creare l'admin di piattaforma via `POST /platform/setup`, poi rimuovilo dall'env. Opzionale: `Jwt__PlatformAudience` (default `WebAgency_BookingSystem.Platform`).
  ⚠ **Smoke test obbligatorio al deploy**: verificare il round-trip login admin (fix JWT `MapInboundClaims=false` + `KeyId` stabile) **e** setup platform → login platform → crea tenant → verifica API key + attivazione Owner.
- **Sicurezza login admin (S3)**: 5 tentativi falliti consecutivi → blocco temporaneo (15 min) dell'utente.
- **GDPR retention (S2)**: un job giornaliero **anonimizza** le PII delle prenotazioni oltre `Gdpr:RetentionDays`
  (default 365) e **purga** le email outbox inviate oltre `Gdpr:OutboxRetentionDays` (default 30).
- **Email**: `Mailpit` in sviluppo (cattura, UI `:8025`), `Brevo` in produzione. Invio via **outbox
  transazionale** con retry/backoff (dispatcher in background, `Email:Outbox:PollSeconds`). Vedi `.env.example`.
- **Promemoria (T2.3)**: email pre-appuntamento inviata `Tenant.ReminderHoursBefore` ore prima (default 24,
  0=off, solo se notifiche email attive). Scheduler `Reminder:PollMinutes` (default 15).
- **CORS multi-tenant (PH-1)**: le origini ammesse derivano **automaticamente** dai `siteUrl` dei tenant attivi,
  aggiornate in background ogni `Cors:OriginRefreshSeconds` (default 60s) — onboardare un nuovo sito non richiede
  modifiche di config. `Cors:AllowedOrigins` resta una allowlist statica **aggiuntiva** (es. tool interni).
  Nota: il preflight CORS non porta `X-Api-Key`, quindi l'autorizzazione è sull'origine, non per-chiave.
- **Doc API**: Scalar/OpenAPI sono esposti solo in non-produzione.
