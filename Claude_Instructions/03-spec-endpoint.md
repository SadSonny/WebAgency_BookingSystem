# 03 — Specifiche Endpoint API

> Documento vincolante per Claude Code. Il contratto API è DECISO e coincide
> con quanto atteso dal frontend esistente. Ogni sezione specifica:
> input, output, logica, errori, e note implementative.
> Modifiche che impattano request/response sono marcate ⚠️ IMPATTO CONTRATTO.

---

## CONVENZIONI GENERALI

### Headers richiesti (endpoint pubblici)
```
X-API-Key: {api_key_del_tenant}
Content-Type: application/json   (solo per POST/PATCH)
```

### Envelope risposta di errore
Tutti gli errori seguono questo formato:
```json
{
  "type": "error_type_snake_case",
  "message": "Messaggio leggibile in italiano",
  "errors": {            // presente solo per 422 validation_error
    "campo": ["messaggio errore"]
  }
}
```

Tipi di errore standardizzati:
| HTTP | type | Quando |
|---|---|---|
| 400 | `bad_request` | Request malformata (JSON invalido, parametri mancanti) |
| 401 | `unauthorized` | API key assente |
| 403 | `forbidden` | API key non valida, o operazione non permessa |
| 404 | `not_found` | Risorsa non trovata |
| 409 | `slot_unavailable` | Slot non più disponibile (race condition booking) |
| 422 | `validation_error` | Dati non validi (con dettaglio per campo) |
| 429 | `rate_limit_exceeded` | Troppe richieste |
| 500 | `internal_error` | Errore interno (non esporre dettagli) |

### Paginazione
Non applicata agli endpoint pubblici (le liste sono sempre complete e di dimensioni contenute). Prevista solo per endpoint admin futuri.

### Timezone
Tutti gli orari negli endpoint pubblici sono **orari locali del tenant** come stringhe (`"09:00"`, `"2024-03-15"`). Nessun timestamp UTC nelle response pubbliche.

---

## MIDDLEWARE — Esecuzione per ogni request pubblica

Prima di ogni handler viene eseguito in sequenza:

1. **`TenantResolutionMiddleware`**
   - Legge header `X-API-Key`
   - Se assente → `401 unauthorized`
   - Hasha la chiave (SHA-256)
   - Cerca in `tenant_api_keys` dove `key_hash = hash AND active = TRUE`
   - Se non trovata → `403 forbidden`
   - Inietta `TenantContext` (con `tenant_id`) nel `HttpContext`
   - Inizializza `BookingDbContext` con il `tenant_id` corretto

2. **`RateLimitingMiddleware`**
   - Sliding window: 100 request / 60 secondi per API key
   - Se superato → `429` con header `Retry-After: {secondi}`

---

## GET /api/v1/health

**Auth:** nessuna
**Descrizione:** health check del backend (usato da Railway per liveness probe)

**Response 200:**
```json
{
  "status": "ok",
  "timestamp": "2024-03-15T10:30:00Z"
}
```

**Logica:** verifica connessione DB con query `SELECT 1`. Se fallisce → `503`.

---

## GET /api/v1/tenant/config

**Auth:** API key
**Descrizione:** restituisce la configurazione pubblica del tenant necessaria al frontend per validare le prenotazioni lato client (anticipo minimo, giorni visibili, ecc.)

**Response 200:**
```json
{
  "tenantId": "uuid",
  "name": "Mario Barbershop",
  "timezone": "Europe/Rome",
  "staffChoiceEnabled": true,
  "minAdvanceHours": 1,
  "minCancellationHours": 24,
  "visibleDaysAhead": 30,
  "bufferMinutes": 0,
  "businessHours": [
    {
      "dayOfWeek": 1,
      "isOpen": true,
      "openTime": "09:00",
      "closeTime": "19:00",
      "breakStart": "13:00",
      "breakEnd": "14:00"
    }
    // 0=Dom, 1=Lun, ..., 6=Sab — sempre 7 elementi
  ],
  "specialClosures": [
    {
      "dateFrom": "2024-08-10",
      "dateTo": "2024-08-20",
      "reason": "Ferie estive"
    }
  ]
}
```

**Logica:**
1. Carica tenant dal DB tramite `tenant_id` dal contesto
2. Carica tutti i `tenant_business_hours` (sempre 7 righe — una per giorno)
3. Carica `tenant_special_closures` con `date_to >= TODAY`
4. Compone e restituisce la response

**Note implementative:**
- Questo endpoint è chiamato dal frontend all'avvio del widget di prenotazione
- Candidato per cache in-memory con TTL breve (es. 5 minuti) — implementare solo se necessario in futuro

---

## GET /api/v1/services

**Auth:** API key
**Descrizione:** lista dei servizi attivi del tenant

**Response 200:**
```json
[
  {
    "id": "uuid",
    "name": "Taglio Uomo",
    "category": "Capelli",
    "durationMin": 30,
    "price": 18.00,
    "description": "Taglio classico con rifinitura",
    "staffIds": ["uuid-staff-1", "uuid-staff-2"],
    "active": true
  }
]
```

**Logica:**
1. Carica servizi attivi del tenant (`active = TRUE`)
2. Per ogni servizio, carica gli UUID degli staff che lo eseguono tramite `staff_services`
3. Il `price` restituito è `services.base_price` (nessun contesto staff in questo endpoint)
4. Ordina per `display_order ASC, name ASC`

**Note:** `staffIds` è la lista di staff che eseguono quel servizio. Il frontend usa questi ID per il filtro dello staff.

---

## GET /api/v1/staff

**Auth:** API key
**Query params:**
- `serviceId` (opzionale, UUID): filtra staff per servizio

**Descrizione:** lista dello staff attivo del tenant

**Response 200:**
```json
[
  {
    "id": "uuid",
    "name": "Marco Rossi",
    "role": "Barbiere Senior",
    "specialization": "Tagli classici e barba",
    "photoUrl": "https://...",
    "active": true
  }
]
```

**Logica:**
1. Se `serviceId` presente: verifica che il servizio esista e appartenga al tenant (altrimenti `404`)
2. Carica staff attivi del tenant
3. Se `serviceId` presente: filtra tramite JOIN su `staff_services` dove `service_id = serviceId`
4. Ordina per `display_order ASC, name ASC`

**Note:** `photoUrl` può essere NULL — il frontend gestisce il caso senza foto.

---

## GET /api/v1/availability

**Auth:** API key
**Query params:**
- `serviceId` (obbligatorio, UUID)
- `staffId` (opzionale, UUID)
- `dateFrom` (obbligatorio, `YYYY-MM-DD`)
- `dateTo` (obbligatorio, `YYYY-MM-DD`, max 31 giorni da `dateFrom`)

**Descrizione:** restituisce gli slot disponibili per il range di date richiesto

**Response 200:**
```json
[
  {
    "date": "2024-03-15",
    "slots": [
      {
        "time": "09:00",
        "staffId": "uuid-staff",  // null se disponibilità aggregata (nessun staffId in input)
        "available": true
      },
      {
        "time": "09:30",
        "staffId": null,
        "available": false
      }
    ]
  }
]
```

**Validazioni:**
- `serviceId` non trovato o non attivo → `404`
- `staffId` non trovato, non attivo, o non esegue quel servizio → `422`
- `dateFrom` > `dateTo` → `422`
- Range > 31 giorni → `422`
- `dateFrom` nel passato → restituisce comunque i dati ma gli slot passati saranno tutti `available: false`

**Logica completa di generazione slot** (vedere anche `04-logica-disponibilita.md`):

Per ogni giorno nel range `[dateFrom, dateTo]`:

1. **Controllo chiusura giornaliera:**
   - Controlla `tenant_business_hours` per il `day_of_week`
   - Se `is_open = FALSE` → giorno saltato (non incluso nella response)
   - Controlla `tenant_special_closures` se `date BETWEEN date_from AND date_to`
   - Se match → giorno saltato

2. **Controllo disponibilità staff (se specificato):**
   - Controlla `staff_business_hours` per il giorno
   - Se `is_available = FALSE` → giorno saltato per quello staff
   - Se lo staff non ha righe in `staff_business_hours` → usa orari del tenant

3. **Generazione slot candidati:**
   - Granularità: **15 minuti**
   - Range: `[open_time, close_time - duration_minutes - buffer_minutes]`
   - Esclude slot che cadono nella pausa (`break_start` ≤ slot_start < `break_end`, oppure slot_end > `break_start`)
   - Esclude slot che violano `min_advance_hours` rispetto all'ora corrente del tenant

4. **Controllo capacità per slot:**
   - Carica le prenotazioni `confirmed` del tenant per la data
   - Per ogni slot candidato `[slot_start, slot_start + duration + buffer]`:
     - **Senza staffId:** conta le prenotazioni del servizio che si sovrappongono all'intervallo. Disponibile se count < `services.parallel_slots`
     - **Con staffId:** conta le prenotazioni dello staff che si sovrappongono. Disponibile se count = 0 (uno staff non può essere in due posti)

5. **Composizione response:**
   - Includi il giorno nella response anche se tutti gli slot sono `available: false`
   - Non includere giorni completamente chiusi

**Performance:** caricare le prenotazioni per l'intero range con una singola query, poi filtrare in memoria per giorno.

---

## POST /api/v1/bookings

**Auth:** API key
**Content-Type:** `application/json`

**Request body:**
```json
{
  "serviceId": "uuid",
  "staffId": "uuid",          // opzionale — null se non specificato
  "date": "2024-03-15",
  "time": "10:30",
  "customer": {
    "name": "Giovanni Bianchi",
    "phone": "+39 333 1234567",
    "email": "giovanni@email.com",
    "notes": "Prima visita"    // opzionale
  },
  "gdprConsent": true
}
```

**Response 201:**
```json
{
  "bookingId": "uuid",
  "status": "confirmed",
  "cancellationToken": "uuid"
}
```

**Response 409:**
```json
{
  "type": "slot_unavailable",
  "message": "Lo slot selezionato non è più disponibile. Ricarica la disponibilità e riprova."
}
```

**Response 422 (esempi):**
```json
{
  "type": "validation_error",
  "message": "Dati non validi",
  "errors": {
    "serviceId": ["Servizio non trovato o non attivo"],
    "customer.email": ["Formato email non valido"],
    "gdprConsent": ["Il consenso al trattamento dei dati è obbligatorio"],
    "time": ["Formato orario non valido. Usare HH:mm"]
  }
}
```

**Validazioni (pre-lock):**
- `serviceId` esiste e `active = TRUE` nel tenant → altrimenti `404`
- `staffId` (se presente): esiste, `active = TRUE`, esegue il servizio → altrimenti `422`
- `gdprConsent` deve essere `true` → altrimenti `422`
- `customer.email`: formato valido
- `customer.phone`: non vuoto
- `customer.name`: non vuoto, max 255 char
- `date`: formato `YYYY-MM-DD`, non nel passato
- `time`: formato `HH:mm`

**Logica di creazione (ATOMICA — transazione con lock):**

```
BEGIN TRANSACTION
  -- 1. Acquisisce lock advisory PostgreSQL per (tenant_id, service_id, date, time)
  --    Previene race condition senza bloccare l'intera tabella
  SELECT pg_try_advisory_xact_lock(hash(tenant_id, service_id, date, time))
  
  -- 2. Ri-verifica disponibilità dentro la transazione
  --    Stessa logica di GET /availability per il singolo slot
  IF NOT available → ROLLBACK → 409

  -- 3. Verifica regole business
  --    - Il giorno non è chiuso
  --    - Lo slot rispetta min_advance_hours
  --    - La data è entro visible_days_ahead
  IF violated → ROLLBACK → 422

  -- 4. Crea la prenotazione
  INSERT INTO bookings (...)

  -- 5. Crea audit log entry
  INSERT INTO audit_log (action='booking_created', ...)

COMMIT
```

**Post-commit (asincrono, non blocca la response):**
- Invia email di conferma al cliente
- Invia notifica al titolare (se `notification_method = 'email'`)

**Nota implementativa:** usare `pg_try_advisory_xact_lock` con un hash deterministico basato su `tenant_id + service_id + date + time`. In caso di lock non acquisito (altra transazione in corso sullo stesso slot), attendere brevemente e riprovare una volta, poi → `409`.

---

## GET /api/v1/bookings/{bookingId}

**Auth:** API key
**Query params:**
- `token` (obbligatorio): cancellation token UUID

**Descrizione:** recupera il dettaglio di una prenotazione (usato dalla pagina di conferma/gestione del sito)

**Response 200:**
```json
{
  "bookingId": "uuid",
  "status": "confirmed",
  "date": "2024-03-15",
  "time": "10:30",
  "durationMin": 30,
  "service": {
    "id": "uuid",
    "name": "Taglio Uomo"
  },
  "staff": {
    "id": "uuid",
    "name": "Marco Rossi"
  },
  "customer": {
    "name": "Giovanni Bianchi",
    "email": "giovanni@email.com"
  },
  "canCancel": true,          // false se oltre il limite di preavviso o già cancellata
  "cancellationDeadline": "2024-03-14T10:30:00"  // orario locale del tenant
}
```

**Logica:**
- Se `bookingId` non trovato nel tenant → `404` con risposta **neutra** (non rivelare se l'ID esiste con token sbagliato)
- Se `token` non corrisponde al `cancellation_token` della prenotazione → `404` con stessa risposta neutra
- `canCancel`: `status = 'confirmed'` AND ora attuale < `booking_datetime - min_cancellation_hours`
- `staff` può essere `null` se la prenotazione non ha staff specifico

**Risposta neutra per sicurezza:**
```json
{
  "type": "not_found",
  "message": "Prenotazione non trovata o token non valido"
}
```

---

## DELETE /api/v1/bookings/{bookingId}

**Auth:** API key
**Query params:**
- `token` (obbligatorio): cancellation token UUID

**Descrizione:** disdetta da parte del cliente finale

**Response 200:**
```json
{
  "bookingId": "uuid",
  "status": "cancelled",
  "message": "Prenotazione disdetta con successo"
}
```

**Response 403:**
```json
{
  "type": "cancellation_deadline_exceeded",
  "message": "Non è possibile disdire con meno di 24 ore di preavviso"
}
```

**Logica:**
1. Verifica `bookingId` + `token` → se non trovati: `404` neutro (stesso di GET)
2. Se `status ≠ 'confirmed'` → `422` con `{"type": "booking_not_cancellable", "message": "..."}`
3. Verifica preavviso minimo: `ora_attuale_tenant < booking_datetime - min_cancellation_hours`
   - Se viola → `403 cancellation_deadline_exceeded`
4. In transazione:
   - `UPDATE bookings SET status='cancelled', cancelled_at=NOW(), cancellation_reason='customer'`
   - `INSERT INTO audit_log (action='booking_cancelled_by_customer', ...)`
5. Post-commit (asincrono): invia email di conferma disdetta al cliente

---

## ENDPOINT ADMIN — PREDISPOSIZIONE FUTURA

I seguenti endpoint sono **registrati nel routing** ma restituiscono `501 Not Implemented`:

```
POST  /api/v1/auth/login
GET   /api/v1/admin/bookings
PATCH /api/v1/admin/availability
PATCH /api/v1/admin/staff/{id}
```

**Implementazione:**
```csharp
app.MapPost("/api/v1/auth/login", () => Results.StatusCode(501));
app.MapGet("/api/v1/admin/bookings", () => Results.StatusCode(501))
   .RequireAuthorization(); // placeholder
// ecc.
```

Questo garantisce che i path esistano nel router e non vengano intercettati dal middleware di tenant resolution (le rotte admin avranno autenticazione JWT separata in futuro).
