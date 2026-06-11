# 04 — Logica di Disponibilità

> Documento critico per Claude Code. Descrive in dettaglio la logica di
> generazione slot e verifica disponibilità. Questo è il cuore del sistema.
> Deve essere implementato con la massima attenzione e coperto da unit test completi.

---

## PRINCIPI FONDAMENTALI

1. **Granularità fissa a 15 minuti** — tutti gli slot iniziano a minuti multipli di 15 (09:00, 09:15, 09:30...)
2. **Orari locali del tenant** — nessuna conversione UTC nel calcolo degli slot. La timezone serve solo per confrontare "ora corrente" con gli slot del giorno corrente
3. **Durata fissa per servizio** — la durata non varia per staff né per postazione
4. **Postazioni parallele per servizio** — la capacità massima è definita a livello di servizio (`parallel_slots`)
5. **Staff individuale** — uno staff non può avere due prenotazioni sovrapposte

---

## STRUTTURA DEL SERVIZIO DI DISPONIBILITÀ

```csharp
// Interfaccia principale — in BookingBackend.Core
public interface IAvailabilityService
{
    Task<IReadOnlyList<DayAvailability>> GetAvailabilityAsync(
        AvailabilityRequest request,
        CancellationToken ct = default);

    Task<bool> IsSlotAvailableAsync(
        SlotCheckRequest request,
        CancellationToken ct = default);
}

public record AvailabilityRequest(
    Guid TenantId,
    Guid ServiceId,
    Guid? StaffId,
    DateOnly DateFrom,
    DateOnly DateTo);

public record SlotCheckRequest(
    Guid TenantId,
    Guid ServiceId,
    Guid? StaffId,
    DateOnly Date,
    TimeOnly Time);

public record DayAvailability(
    DateOnly Date,
    IReadOnlyList<SlotAvailability> Slots);

public record SlotAvailability(
    TimeOnly Time,
    Guid? StaffId,
    bool Available);
```

---

## ALGORITMO COMPLETO — GetAvailability

### Fase 1: Caricamento dati (una sola volta per l'intero range)

```
DATI DA CARICARE DAL DB:
  tenant          ← tenant con tutte le regole (min_advance_hours, buffer_minutes, ecc.)
  service         ← servizio con duration_minutes e parallel_slots
  businessHours   ← tenant_business_hours (tutti i 7 giorni)
  specialClosures ← tenant_special_closures dove date_to >= dateFrom AND date_from <= dateTo
  staffHours      ← staff_business_hours per lo staffId (se specificato)
  existingBookings ← bookings WHERE:
                      tenant_id = tenantId
                      AND booking_date BETWEEN dateFrom AND dateTo
                      AND status = 'confirmed'
                      AND (service_id = serviceId OR staff_id = staffId)
                      -- carica entrambi i subset in un'unica query con OR
  currentTenantTime ← ora attuale nel timezone del tenant
```

### Fase 2: Iterazione per ogni giorno

```
FOR EACH date IN [dateFrom .. dateTo]:

  === STEP 2.1 — Verifica chiusure ===
  
  dayOfWeek ← date.DayOfWeek (0=Dom, 1=Lun, ..., 6=Sab)
  hours ← businessHours[dayOfWeek]
  
  IF hours.is_open = FALSE → SKIP (non includere nella response)
  
  IF EXISTS closure IN specialClosures WHERE date BETWEEN closure.date_from AND closure.date_to
    → SKIP (non includere nella response)

  === STEP 2.2 — Determina orario effettivo del giorno ===
  
  IF staffId IS NOT NULL AND staffHours HAS entry FOR dayOfWeek:
    effectiveHours ← staffHours[dayOfWeek]
    IF effectiveHours.is_available = FALSE → SKIP
    openTime  ← effectiveHours.start_time
    closeTime ← effectiveHours.end_time
    breakStart ← effectiveHours.break_start  (può essere NULL)
    breakEnd   ← effectiveHours.break_end    (può essere NULL)
  ELSE:
    openTime  ← hours.open_time
    closeTime ← hours.close_time
    breakStart ← hours.break_start  (può essere NULL)
    breakEnd   ← hours.break_end    (può essere NULL)

  === STEP 2.3 — Genera slot candidati ===
  
  // L'ultimo slot possibile deve terminare entro closeTime
  // slot_end = slot_start + duration + buffer
  // Quindi: slot_start <= closeTime - duration - buffer
  
  lastPossibleStart ← closeTime - duration_minutes - buffer_minutes
  
  slots ← []
  current ← openTime arrotondato al prossimo multiplo di 15min >= openTime
  
  WHILE current <= lastPossibleStart:
    slots.append(current)
    current ← current + 15 minuti

  === STEP 2.4 — Filtra slot per pausa ===
  
  IF breakStart IS NOT NULL AND breakEnd IS NOT NULL:
    slots ← slots WHERE NOT overlaps(slot, breakStart, breakEnd, duration + buffer)
    
    // Definizione overlap:
    // slot_start < breakEnd AND slot_start + duration + buffer > breakStart

  === STEP 2.5 — Filtra slot per anticipo minimo ===
  
  IF date = currentTenantTime.Date:  // solo per il giorno corrente
    minAllowedTime ← currentTenantTime + min_advance_hours
    slots ← slots WHERE slot_time >= minAllowedTime
  
  IF date < currentTenantTime.Date:
    slots ← []  // giorno nel passato — nessuno slot disponibile

  === STEP 2.6 — Verifica capacità per ogni slot ===
  
  bookingsForDay ← existingBookings WHERE booking_date = date
  
  FOR EACH slot IN slots:
    slotStart ← slot
    slotEnd   ← slot + duration_minutes + buffer_minutes
    
    IF staffId IS NOT NULL:
      // Controllo individuale staff
      overlapping ← bookingsForDay WHERE:
        staff_id = staffId
        AND booking_time < slotEnd
        AND booking_time + duration_minutes > slotStart
      
      available ← overlapping.Count = 0
    
    ELSE:
      // Controllo capacità postazioni parallele
      overlapping ← bookingsForDay WHERE:
        service_id = serviceId
        AND booking_time < slotEnd
        AND booking_time + duration_minutes > slotStart
        -- nota: non include il buffer nelle prenotazioni esistenti
        -- il buffer è solo sul nuovo slot che stiamo valutando
      
      available ← overlapping.Count < service.parallel_slots
    
    result.append(SlotAvailability(slot, staffId, available))
  
  IF result IS NOT EMPTY:
    dayResults.append(DayAvailability(date, result))

RETURN dayResults
```

---

## ALGORITMO DI VERIFICA SLOT SINGOLO (per POST /bookings)

Questa funzione viene chiamata DENTRO la transazione con lock advisory.
Deve essere una versione semplificata e rapida dell'algoritmo sopra per il singolo slot.

```
FUNCTION IsSlotAvailable(tenantId, serviceId, staffId?, date, time):

  tenant ← load from DB
  service ← load from DB
  hours ← load business hours for date.DayOfWeek
  currentTenantTime ← NOW() in tenant.timezone

  // 1. Giorno aperto?
  IF NOT hours.is_open → RETURN false

  // 2. Chiusure straordinarie?
  IF EXISTS closure WHERE date BETWEEN closure.date_from AND closure.date_to → RETURN false

  // 3. Anticipo minimo
  IF date = currentTenantTime.Date AND time < currentTenantTime.Time + min_advance_hours
    → RETURN false
  IF date < currentTenantTime.Date → RETURN false

  // 4. Data entro visible_days_ahead
  IF date > currentTenantTime.Date + visible_days_ahead → RETURN false

  // 5. Slot dentro orario di apertura (considerando staff se specificato)
  effectiveOpen, effectiveClose, breakStart, breakEnd ← determine_hours(staffId, date)
  slotEnd ← time + duration_minutes + buffer_minutes
  
  IF time < effectiveOpen OR slotEnd > effectiveClose → RETURN false
  
  IF breakStart IS NOT NULL:
    IF time < breakEnd AND slotEnd > breakStart → RETURN false

  // 6. Capacità
  overlapping ← SELECT COUNT(*) FROM bookings WHERE
    tenant_id = tenantId
    AND booking_date = date
    AND status = 'confirmed'
    AND booking_time < (time + duration_minutes + buffer_minutes)
    AND (booking_time + duration_minutes) > time
    AND (
      (staffId IS NOT NULL AND staff_id = staffId)
      OR
      (staffId IS NULL AND service_id = serviceId)
    )
  
  IF staffId IS NOT NULL:
    RETURN overlapping = 0
  ELSE:
    RETURN overlapping < service.parallel_slots
```

---

## GESTIONE RACE CONDITION — Prenotazione Atomica

Il meccanismo di lock deve prevenire che due utenti prenotino lo stesso slot contemporaneamente.

### Soluzione: pg_try_advisory_xact_lock

```csharp
// Genera un hash a 64 bit deterministico per (tenantId, serviceId, date, time)
// da usare come chiave per il lock advisory PostgreSQL

private static long ComputeLockKey(Guid tenantId, Guid serviceId, DateOnly date, TimeOnly time)
{
    // Combina i valori in una stringa deterministica e hasha
    var input = $"{tenantId}:{serviceId}:{date:yyyy-MM-dd}:{time:HH:mm}";
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
    return BitConverter.ToInt64(bytes, 0);
}
```

### Flusso nella transazione:

```sql
BEGIN;

-- Tenta di acquisire il lock (non bloccante)
SELECT pg_try_advisory_xact_lock(:lockKey);
-- Se restituisce FALSE: un'altra transazione ha già il lock su questo slot

-- Se TRUE: procedi con la verifica e la creazione
-- ... IsSlotAvailable() ...
-- ... INSERT INTO bookings ...
-- ... INSERT INTO audit_log ...

COMMIT;
-- Il lock viene rilasciato automaticamente al COMMIT/ROLLBACK
```

### Strategia retry:

```
TENTATIVO 1: pg_try_advisory_xact_lock
  → SE acquisito: verifica + crea → successo
  → SE NON acquisito: aspetta 200ms

TENTATIVO 2: pg_try_advisory_xact_lock
  → SE acquisito: verifica + crea → successo (l'altro potrebbe aver preso l'ultimo slot → 409)
  → SE NON acquisito: → 409 (slot conteso)
```

---

## CASI LIMITE DA GESTIRE

### 1. Servizio senza staff associati
- `GET /api/v1/staff?serviceId=X` restituisce lista vuota
- La disponibilità si calcola solo sulla capacità del servizio (parallel_slots)
- Il campo `staffId` nella response degli slot è `null`

### 2. Staff senza orari personalizzati
- Se non esistono righe in `staff_business_hours` per uno staff → usa gli orari del tenant
- Questo è il caso "normale" — la tabella staff_business_hours è opzionale

### 3. Buffer minutes = 0
- La logica rimane identica — semplicemente `slotEnd = slotStart + duration`
- Non richiedere gestione speciale

### 4. Pausa pranzo attraversa uno slot
- Esempio: servizio 60 min, pausa 13:00-14:00, slot alle 12:30
  - `slotEnd = 12:30 + 60min = 13:30`
  - 12:30 < 14:00 AND 13:30 > 13:00 → slot escluso (corretto)
- Lo slot delle 12:00 con fine alle 13:00 NON viene escluso (13:00 = fine pausa, non overlap)

### 5. Data odierna con anticipo minimo = 0
- Tutti gli slot futuri del giorno corrente (rispetto all'ora attuale del tenant) sono validi
- Gli slot già passati (anche di pochi minuti) sono `available: false`

### 6. Giorno con tutti gli slot occupati
- Il giorno viene incluso nella response con tutti gli slot a `available: false`
- Il frontend può visualizzare il giorno come "pieno" anziché nasconderlo

### 7. Range che include oggi con ora avanzata
- Esempio: sono le 17:00, orario chiusura 19:00, servizio 60 min, buffer 0
- Slot validi: 17:00 (se min_advance=0), poi 17:15, 17:30 (slotEnd=18:30 ≤ 19:00)
- Slot 17:45 escluso: slotEnd = 18:45 ≤ 19:00 → incluso
- Slot 18:00 escluso: slotEnd = 19:00 ≤ 19:00 → **incluso** (bordo incluso)
- Slot 18:15 escluso: slotEnd = 19:15 > 19:00 → escluso

---

## UNIT TEST — Casi da coprire obbligatoriamente

Il file `AvailabilityServiceTests.cs` deve coprire questi scenari:

```
[ ] Giorno di chiusura settimanale → nessuno slot
[ ] Giorno con chiusura straordinaria → nessuno slot
[ ] Slot nella pausa pranzo → escluso
[ ] Slot che termina durante la pausa → escluso
[ ] Slot che termina esattamente alla chiusura → incluso
[ ] Slot che termina dopo la chiusura → escluso
[ ] Anticipo minimo: slot troppo vicino all'ora attuale → non disponibile
[ ] parallel_slots = 2: due prenotazioni sullo slot → non disponibile
[ ] parallel_slots = 2: una prenotazione sullo slot → disponibile
[ ] Staff specificato: prenotazione esistente → non disponibile
[ ] Staff specificato: prenotazione su altro staff stesso slot → disponibile
[ ] Staff senza orari personalizzati → usa orari tenant
[ ] Staff con orari personalizzati → usa orari staff
[ ] Staff non disponibile quel giorno → slot non disponibile
[ ] Buffer: slot immediatamente successivo a prenotazione esistente → non disponibile
[ ] Buffer = 0: slot immediatamente successivo → disponibile
[ ] Range con date nel passato → slot vuoti
[ ] Servizio senza staff → calcolo su parallel_slots del servizio
```

### Integration test obbligatori:

```
[ ] Race condition: due POST /bookings simultanee sullo stesso slot → una 201, una 409
[ ] POST /bookings con slot valido → booking creato, audit log inserito
[ ] DELETE /bookings con token corretto dentro preavviso → cancellato
[ ] DELETE /bookings con token corretto fuori preavviso → 403
[ ] GET /bookings con token errato → 404 neutro
```
