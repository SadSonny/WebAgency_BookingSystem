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
> (vuoto all'avvio — popolato man mano)
