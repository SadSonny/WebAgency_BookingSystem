# 00 — Indice e Guida all'uso per Claude Code

> Questo file è il punto di ingresso per Claude Code.
> Leggilo prima di qualsiasi altro documento o prima di scrivere qualsiasi codice.

---

## CONTESTO DEL PROGETTO

Backend centralizzato multi-tenant per la gestione di prenotazioni di attività locali italiane
(barbershop, estetica, medici, consulenti, fitness, ristoranti).

Un solo backend ASP.NET Core (.NET 9), un solo database PostgreSQL, N siti frontend
Vite/React che vi si collegano via API REST con API key.

---

## DOCUMENTI DISPONIBILI

| File | Contenuto | Leggere quando... |
|---|---|---|
| `00-indice.md` | Questo file | Sempre — è il punto di ingresso |
| `01-architettura-e-stack.md` | Stack tecnologico, struttura solution, hosting, email, autenticazione, GDPR | Prima di creare qualsiasi file di progetto |
| `02-schema-database.md` | Schema PostgreSQL completo, relazioni, indici, note EF Core | Prima di creare entità, DbContext o migrazioni |
| `03-spec-endpoint.md` | Spec complete di ogni endpoint (input, output, logica, errori) | Prima di implementare qualsiasi endpoint |
| `04-logica-disponibilita.md` | Algoritmo dettagliato di generazione slot e verifica disponibilità | Prima di implementare AvailabilityService e BookingService |
| `05-provisioning-e-struttura.md` | Struttura cartelle progetto, formato JSON provisioning, template email, guida operativa | Prima di creare la struttura del progetto e il CLI tool |

---

## REGOLE OPERATIVE PER CLAUDE CODE

### 1. Ordine di lettura obbligatorio
Prima di scrivere codice, leggere nell'ordine:
1. `01-architettura-e-stack.md` — capire lo stack e le convenzioni
2. `02-schema-database.md` — capire il modello dati
3. `05-provisioning-e-struttura.md` (Parte 1) — capire la struttura del progetto
4. Il documento specifico per il task corrente (03 o 04)

### 2. Decisioni vincolanti — non rimettere in discussione
Queste decisioni sono FINALI e non vanno cambiate:
- **Stack:** ASP.NET Core 9 Minimal API, EF Core 9, PostgreSQL, Brevo, Railway
- **Contratto API:** ogni endpoint in `03-spec-endpoint.md` — path, metodo, input, output
- **Schema DB:** struttura tabelle in `02-schema-database.md`
- **Granularità slot:** 15 minuti fissi
- **Orari:** sempre locali del tenant, mai UTC nelle response pubbliche

### 3. Convenzioni di codice da rispettare sempre
- Record C# per tutti i DTO (request e response)
- Result pattern per operazioni che possono fallire (non eccezioni per flow control)
- Async/await su tutte le operazioni I/O con CancellationToken
- Global query filter EF Core per tenant isolation (non aggiungere `.Where(x => x.TenantId == ...)` manualmente)
- Nessun dato personale nei log (nome, email, telefono cliente)
- Validazione con FluentValidation (non DataAnnotations)

### 4. Segnalare prima di deviare
Se durante l'implementazione emerge un conflitto, un'ambiguità o una necessità
di modifica rispetto ai documenti, **segnalarlo esplicitamente** prima di procedere.
In particolare:
- `⚠️ IMPATTO CONTRATTO` se la modifica tocca input/output degli endpoint
- `⚠️ IMPATTO SCHEMA` se la modifica tocca la struttura del DB

### 5. Test obbligatori
I test in `04-logica-disponibilita.md` (sezione "Unit test — casi da coprire obbligatoriamente")
**devono tutti passare** prima di considerare l'implementazione completa.

---

## STATO DI IMPLEMENTAZIONE

Aggiornare questa sezione man mano che i task vengono completati.

### ☐ Setup iniziale
- [ ] Creazione solution e progetti (.sln, .csproj)
- [ ] Configurazione Dockerfile
- [ ] Configurazione appsettings.json e variabili d'ambiente
- [ ] Setup Serilog

### ☐ Database
- [ ] Creazione entità EF Core (da schema in `02-schema-database.md`)
- [ ] Configurazione BookingDbContext con global query filter
- [ ] Prima migrazione EF Core (InitialSchema)

### ☐ Middleware e infrastruttura
- [ ] TenantResolutionMiddleware (API key → tenant_id)
- [ ] GlobalExceptionHandler
- [ ] Rate limiting (Microsoft.AspNetCore.RateLimiting)

### ☐ Endpoint pubblici
- [ ] GET /api/v1/health
- [ ] GET /api/v1/tenant/config
- [ ] GET /api/v1/services
- [ ] GET /api/v1/staff
- [ ] GET /api/v1/availability
- [ ] POST /api/v1/bookings (con lock atomico)
- [ ] GET /api/v1/bookings/{id}
- [ ] DELETE /api/v1/bookings/{id}

### ☐ Endpoint admin stub
- [ ] Registrazione rotte con risposta 501

### ☐ Email (Brevo)
- [ ] BrevoEmailService (HttpClient)
- [ ] Template conferma prenotazione (cliente)
- [ ] Template notifica nuova prenotazione (titolare)
- [ ] Template conferma disdetta (cliente)

### ☐ Test
- [ ] AvailabilityServiceTests (tutti i casi di `04-logica-disponibilita.md`)
- [ ] BookingFlowTests (race condition, flow completo)

### ☐ CLI Provisioning
- [ ] TenantProvisioning CLI tool
- [ ] README operativo

---

## SEQUENZA CONSIGLIATA DI SVILUPPO

Seguire questo ordine per avere sempre un sistema funzionante e testabile:

```
1. Setup solution e progetti vuoti
2. Entità EF Core + DbContext + prima migrazione
3. TenantResolutionMiddleware + GET /health
4. GET /tenant/config, /services, /staff
5. AvailabilityService + unit test
6. GET /availability
7. POST /bookings (con lock) + integration test race condition
8. GET e DELETE /bookings/{id}
9. Email Brevo
10. Rate limiting
11. CLI Provisioning
12. Stub endpoint admin
13. Dockerfile + deploy Railway
```

---

## VARIABILI D'AMBIENTE

| Variabile | Descrizione | Esempio |
|---|---|---|
| `DATABASE_URL` | Connection string PostgreSQL (fornita da Railway) | `postgresql://user:pass@host:5432/db` |
| `BREVO_API_KEY` | API key Brevo per email | `xkeysib-...` |
| `ASPNETCORE_ENVIRONMENT` | Ambiente | `Production` |
| `PORT` | Porta Kestrel (fornita da Railway) | `8080` |

**In locale per sviluppo:** usare `dotnet user-secrets` o file `.env` non committato.
