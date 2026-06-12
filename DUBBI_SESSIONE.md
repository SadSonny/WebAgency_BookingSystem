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

### D-09 — Soft delete via `DeletedAt` su services/staff 🟡
**Contesto:** lo schema SQL (02) non mostra `deleted_at` su `services`/`staff`, ma CLAUDE.md (convenzioni) e `01-architettura` indicano soft delete tramite `deleted_at`.
**Default adottato:** aggiunto `DeletedAt TIMESTAMPTZ NULL` a `Service` e `Staff`; il global query filter escluderà i record soft-deleted. Deviazione di schema annotata nel piano.
**Da confermare:** ok aggiungere `deleted_at` oltre al flag `active` esistente?
