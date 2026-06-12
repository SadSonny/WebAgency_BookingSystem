# Dubbi & Decisioni aperte — Sessione autonoma (Blocco A→E, V1 fino a 5.8)

> File creato per raccogliere domande/dubbi emersi durante la sessione autonoma,
> così da non bloccare il lavoro e permetterti di risponderli in un secondo momento.
> Aggiornato man mano. Ogni voce ha: contesto, decisione presa autonomamente (default), e cosa confermare.

## Legenda stato
- 🟡 APERTO — attendo tua conferma, ho proceduto con un default ragionevole
- 🟢 RISOLTO — chiarito (da te o da spec)

---

## Decisioni prese autonomamente (default ragionevoli, da confermare)

### D-01 — Versioni NuGet 🟡
**Contesto:** Step 1.5 richiede di aggiungere i pacchetti. Le versioni esatte non sono fissate nel piano.
**Default adottato:** ultime stabili compatibili con `net10.0`:
- `Npgsql.EntityFrameworkCore.PostgreSQL` (10.x)
- `Microsoft.EntityFrameworkCore.Design` (10.x)
- `Serilog.AspNetCore`, `Serilog.Sinks.Console`
- `Microsoft.OpenApi 2.0.0` (già richiesto da CLAUDE.md), `Scalar.AspNetCore 2.16.3` (già presente da step 1.0)
**Da confermare:** vuoi pinnare versioni specifiche o va bene "ultima stabile"?

### D-02 — Design-time DbContext factory 🟡
**Contesto:** `dotnet ef migrations add` (step 3.3) deve istanziare il DbContext a design-time. Senza DB avviato non si connette, ma serve una connection string in config o una `IDesignTimeDbContextFactory`.
**Default adottato:** aggiungo `IDesignTimeDbContextFactory` che legge `DATABASE_URL` con fallback a una stringa locale di default (Host=localhost...). La migrazione viene *generata* ma NON applicata.
**Da confermare:** ok l'approccio factory? (alternativa: solo connection string in appsettings)

### D-03 — Algoritmo disponibilità: casi limite 🟡
**Contesto:** Step 5.5, spec `04-logica-disponibilita.md`. Implemento granularità 15 min, buffer per servizio, parallelSlots senza staff.
**Default adottato:** seguo la spec alla lettera. Eventuali ambiguità (es. arrotondamento slot a cavallo di chiusura, gestione DST nel giorno di cambio ora) → annotate qui sotto se emergono.
**Da confermare:** —

### D-04 — Advisory lock key 🟡
**Contesto:** Step 5.6, `pg_try_advisory_xact_lock` richiede una chiave `bigint`. La spec indica hash di tenant+service+date+time.
**Default adottato:** derivo la chiave con hash deterministico (es. xxHash/SHA ridotto a int64) della tupla. WHY documentato nel codice.
**Da confermare:** vincoli sul rischio collisione hash? (probabilità trascurabile alla granularità slot)

### D-05 — Conversione timezone tenant 🟡
**Contesto:** Storage in UTC (`DateTimeOffset`/TIMESTAMPTZ), response in orario locale tenant.
**Default adottato:** ogni tenant ha un IANA timezone; converto in uscita. Se lo schema non espone ancora il campo timezone, lo aggiungo all'entità Tenant e lo annoto come modifica schema.
**Da confermare:** —

---

## Fuori scope confermato (NON tocco in questa sessione)
- Admin API (6.x), CLI provisioning (7.x), test (9.x), email Brevo (8.x)
- `dotnet ef database update`, avvio API, smoke-test → confluiscono in `DOCKER_SESSION_TODO.md`

---

## Dubbi emersi durante l'implementazione

### D-06 — Header API key: `X-Api-Key` vs `X-API-Key` 🟡
**Contesto:** `03-spec-endpoint.md` usa `X-API-Key`; CLAUDE.md (sommario endpoint + descrizione OpenAPI step 1.0) usa `X-Api-Key`.
**Default adottato:** `X-Api-Key` (CLAUDE.md prevale sulle spec, come da regola di precedenza).
**Da confermare:** il frontend esistente quale header invia esattamente? Gli header HTTP sono case-insensitive lato server, quindi a runtime non cambia nulla; conta solo per la doc.

### D-07 — `tenant/config.bufferMinutes` con buffer per-servizio 🟡
**Contesto:** la response di `GET /tenant/config` include `bufferMinutes` a livello tenant (spec 03), ma per AD-03 il buffer è stato spostato sul singolo servizio e rimosso da `Tenant`.
**Default adottato:** mantengo il campo `bufferMinutes` nella response (forma del contratto invariata per il frontend) ma valorizzato sempre a `0`. Il buffer effettivo è esposto/usato a livello servizio.
**Da confermare:** il frontend usa `tenant/config.bufferMinutes`? Se sì, va deciso se rimuoverlo o derivarlo.

### D-08 — DTO admin (step 2.8) rinviati 🟢
**Contesto:** lo scope concordato è 1.1→5.8 (endpoint pubblici). I DTO admin servono agli endpoint admin (6.x), fuori scope.
**Decisione:** step 2.8 NON implementato in questa sessione per non introdurre dead code (CLAUDE.md vieta implementazioni parziali/non usate). Sarà fatto insieme agli endpoint admin (6.x). Step 2.8 resta `[ ]` nel piano con nota.

### D-12 — Provisioning: utente admin con password generata 🟡
**Contesto:** lo step 7.5 richiede di creare l'utente admin (Owner) con password bcrypt, ma il JSON di provisioning (spec 05) NON contiene un campo password.
**Default adottato:** il CLI genera una password casuale (18 hex), la salva come hash bcrypt e la mostra **una sola volta** in output (come l'API key). L'email admin = `ownerEmail` del tenant.
**Da confermare:** è il flusso desiderato? Alternativa: non creare l'utente al provisioning e prevedere un flusso "imposta password" al primo accesso admin (6.x).

### D-13 — Buffer per-servizio nel file di provisioning 🟡
**Contesto:** AD-03 ha spostato il buffer sul servizio, ma il JSON di provisioning (spec 05) ha `bufferMinutes` solo a livello `bookingRules` (tenant) e nessun campo buffer per servizio.
**Default adottato:** aggiunti a `services[]` i campi opzionali `bufferEnabled`/`bufferMinutes`/`bufferPosition` (default: disattivato). Il `bookingRules.bufferMinutes` tenant-level NON viene applicato (coerente con D-07).
**Da confermare:** ok i campi buffer per-servizio nel file? Va rimosso `bufferMinutes` dal blocco `bookingRules` del template?

### D-14 — Provisioning solo CREATE (no `--update`) 🟡
**Contesto:** la spec 05 descrive una modalità `--update` (delete/ricrea orari, merge chiusure, upsert servizi/staff per nome). Lo step 7.3 del piano elenca solo il flusso transazionale di creazione.
**Default adottato:** implementata SOLO la modalità CREATE (sufficiente a creare tenant di test e sbloccare la sessione Docker). Con `--update` il CLI esce con messaggio "non ancora supportato"; se lo slug esiste, errore.
**Da confermare:** serve `--update` in V1 o può attendere?

### D-11 — Warning MSB3277 in TenantProvisioning 🟢 RISOLTO
**Risoluzione:** allineate le versioni EF Core a 10.0.9 (R-33). 0 warning su tutta la solution.

### D-11b — Warning MSB3277 in TenantProvisioning (storico) 🟡
**Contesto:** la solution compila con 0 errori; restano 6 warning MSB3277 SOLO nel progetto `TenantProvisioning` (tool fuori scope, step 7), per unificazione di versione di `Microsoft.EntityFrameworkCore.Abstractions` (10.0.4 vs 10.0.9) ereditata transitivamente da Infrastructure. Nessun impatto funzionale.
**Default adottato:** lasciati i warning, dato che il tool sarà implementato e referenziato correttamente nello step 7. I progetti in scope (Core/Infrastructure/Api) compilano con 0 warning.
**Da confermare:** ok rinviare la pulizia a quando si implementa la CLF (7.x)? In alternativa basta un `PackageReference` diretto a EF Core 10.0.9 nel tool.

### D-10 — Semantica del buffer nell'algoritmo di disponibilità 🟡
**Contesto:** la spec `04-logica-disponibilita.md` (precedente ad AD-03) modella il buffer solo come tempo aggiunto DOPO l'appuntamento e dichiara (riga 165) che le prenotazioni esistenti NON includono il buffer. Ma il caso di test obbligatorio (riga 345) richiede che, con buffer > 0, uno slot immediatamente successivo a una prenotazione esistente risulti NON disponibile — il che è impossibile col modello "solo new slot, solo dopo".
**Default adottato:** il buffer estende l'intervallo "occupato" di OGNI appuntamento (nuovo ed esistente) secondo `BufferPosition` (Before/After/Both). Due appuntamenti confliggono se gli intervalli estesi si sovrappongono. Questo soddisfa sia AD-03 sia i test 345/346 e degrada esattamente al comportamento spec quando buffer = 0.
**Conseguenza:** con `BufferPosition.Both` la distanza imposta tra due appuntamenti adiacenti è 2×buffer; con Before/After è 1×buffer. L'appuntamento+buffer deve stare dentro l'orario di apertura.
**Da confermare:** la semantica del buffer (in particolare Both = isolamento completo) corrisponde alle attese di business? Per servizi a staff con buffer di servizi diversi, si applica il buffer del servizio in prenotazione.

### D-09 — Soft delete via `DeletedAt` su services/staff 🟡
**Contesto:** lo schema SQL (02) non mostra `deleted_at` su `services`/`staff`, ma CLAUDE.md (convenzioni) e `01-architettura` indicano soft delete tramite `deleted_at`.
**Default adottato:** aggiunto `DeletedAt TIMESTAMPTZ NULL` a `Service` e `Staff`; il global query filter escluderà i record soft-deleted. Deviazione di schema annotata nel piano.
**Da confermare:** ok aggiungere `deleted_at` oltre al flag `active` esistente?
