# Design — GDPR: DSAR on-demand + consenso arricchito (filone 4.3)

> Data: 2026-06-19 · Branch: `AutoDev` · Filone: VISIONE §4.3 (Compliance GDPR)
> Stato: **approvato in brainstorming**, in attesa di review utente sullo spec.

## 1. Obiettivo e scope

Dare al titolare (barber, via agenzia) gli strumenti per rispondere alle richieste GDPR del cliente finale
**on-demand**, oltre all'anonimizzazione automatica già esistente (`DataRetentionService`), e arricchire la
prova del consenso. Tre parti coese:

- **A. DSAR on-demand** (codice, il grosso): endpoint admin per **export** (diritto d'accesso) e **cancellazione** (diritto all'oblio) dei dati di uno specifico cliente, identificato per **email**.
- **B. Versione informativa nel consenso** (codice, piccolo): registrare *quale* informativa è stata accettata (il *quando* esiste già).
- **C. Documentazione** (doc): sub-responsabili, data-flow, retention, catena ruoli.

### Decisioni prese in brainstorming (non rinegoziare)
- **Erasure = anonimizzazione** (riusa marker `[rimosso]` + azzeramento PII di `DataRetentionService`), non hard-delete: conserva la riga per le statistiche, coerente col codice esistente.
- **Identificativo cliente = email** (case-insensitive, entro il tenant).
- **Versione informativa = inviata dal client** nel `POST /bookings` e salvata sul booking.
- **Audit sia su export sia su erase.**

### Già esistente — NON rifare
- `Booking.GdprConsent` (bool) **e** `Booking.GdprConsentAt` (timestamp) già presenti ([Booking.cs:48-52]). Il *timestamp* del consenso c'è già: 4.3 aggiunge solo la **versione**.
- Anonimizzazione automatica oltre retention: `DataRetentionService` (marker `[rimosso]`).

### Fuori scope
- Hard-delete delle prenotazioni (scelto: anonimizzazione).
- Export self-service per il cliente finale (resta admin-only; l'agenzia gestisce le richieste).
- DPA agenzia↔piattaforma (legale, non codice).

## 2. Vincolo critico — audit PII-free

`AuditLog` ha una regola di progetto esplicita: **nessun dato personale del cliente nei log**
([AuditLog.cs:2-3]). Quindi l'audit DSAR **non** può contenere l'email in chiaro. Scelta:
- Metadata audit = `{ "matched": N }` (export) / `{ "anonymized": N }` (erase) **+** `"subjectRef"` = **SHA-256 (hex) dell'email normalizzata**. L'hash è PII-minimizzato (non reversibile in pratica) ma permette di **correlare** più eventi sullo stesso soggetto senza esporre l'email. Il paper-trail legale "quale cliente, quando" resta presso il titolare (che ha l'email nella richiesta DSAR).
- `Action` = `customer_data_exported` | `customer_data_erased`; `Actor` = `owner`.

> ⚠ **Punto da validare in review:** accettabile usare un hash dell'email come `subjectRef` nell'audit? In
> alternativa si memorizza solo il conteggio (nessuna correlazione per-soggetto). Default proposto: con hash.

## 3. Architettura

DSAR è un servizio tenant-scoped: l'isolamento è **automatico** grazie al global query filter su `tenant_id`
(il JWT admin popola `ITenantContext`). Nessun `IgnoreQueryFilters` (a differenza del job di retention, che è
cross-tenant). Pattern endpoint identico agli altri admin (`RouteGroupBuilder` + `RequireAuthorization(AdminClaims.AdminPolicy)` + servizio che ritorna `Result<T>`).

```
GET  /api/v1/admin/gdpr/customer?email=...   → IGdprDsarService.ExportAsync(email)
   └─ query Bookings (tenant-scoped) WHERE lower(CustomerEmail)=lower(email)
        ├─ vuoto → Result NotFound (404)
        └─ trovato → audit "customer_data_exported" + 200 CustomerDataExport

POST /api/v1/admin/gdpr/customer/erase  { email }  → IGdprDsarService.EraseAsync(email)
   └─ transazione:
        count = ExecuteUpdate(anonimizza Bookings WHERE lower(email) match AND non già anonimizzate)
        ├─ count==0 → rollback → Result NotFound (404)
        └─ count>0 → audit "customer_data_erased" + commit → 200 { anonymized: count }
```

## 4. Componenti (file)

| File | Progetto | Responsabilità |
|---|---|---|
| `Booking.cs` (modifica) | Core | + `string? GdprConsentVersion` (nullable → retrocompatibile). |
| `CreateBookingRequest.cs` (modifica) | Core | + `string? GdprConsentVersion = null` (opzionale, in coda ai parametri record). |
| `Dtos/Admin/CustomerDataExport.cs` | Core | `record CustomerDataExport(string Email, int Count, IReadOnlyList<BookingExportItem> Bookings)` + `record BookingExportItem(...)` (tutti i campi prenotazione + PII + consenso). |
| `Dtos/Admin/EraseCustomerRequest.cs` | Core | `record EraseCustomerRequest(string Email)`. |
| `Dtos/Admin/ErasureResult.cs` | Core | `record ErasureResult(int Anonymized)`. |
| `Abstractions/Services/IGdprDsarService.cs` | Core | `ExportAsync(string email, ct)` → `Task<Result<CustomerDataExport>>`; `EraseAsync(string email, ct)` → `Task<Result<ErasureResult>>`. |
| `Services/GdprDsarService.cs` | Infrastructure | Implementazione (vedi §3). Usa `BookingSystemDbContext` + `ITenantContext`. Riusa `DataRetentionService.AnonymizedMarker`. |
| `Endpoints/Admin/AdminGdprEndpoints.cs` | Api | I due endpoint sotto `/api/v1/admin/gdpr`, metadati OpenAPI completi. |
| `AdminEndpoints.cs` (modifica) | Api | + `app.MapAdminGdprEndpoints();`. |
| `Validation/EraseCustomerRequestValidator.cs` | Api | email non vuota e formato plausibile. |
| `DependencyInjection.cs` (modifica) | Infrastructure | `services.AddScoped<IGdprDsarService, GdprDsarService>();`. |
| `BookingService.cs` (modifica) | Infrastructure | salva `GdprConsentVersion = request.GdprConsentVersion` (accanto a `GdprConsentAt`, riga ~188). |
| Migration `AddGdprConsentVersion` | Infrastructure | colonna nullable `gdpr_consent_version` su `bookings`. |
| `Claude_Instructions/GDPR_COMPLIANCE.md` | doc | sub-responsabili, data-flow, retention, ruoli, riferimento DSAR. |
| `CLAUDE.md` (modifica) | doc | endpoint DSAR nel sommario; nota consenso-versione; stato. |

Ogni file con `// [INTENT]`; `// WHY:` sulle parti non ovvie (riuso marker, audit hash, transazione erase).

## 5. Dettaglio anonimizzazione (deroga DRY consapevole)

`GdprDsarService.EraseAsync` replica il blocco `SetProperty` di `DataRetentionService` (nome→`[rimosso]`,
telefono/email→`""`, note→`null`). **WHY deroga:** gli expression-tree di EF `ExecuteUpdate` non si
fattorizzano in un helper senza complicazioni; condividiamo la **costante** `DataRetentionService.AnonymizedMarker`
(unica fonte del marker) e replichiamo i setter con commento di accoppiamento. Alternativa (helper generico)
sconsigliata: YAGNI, più astrazione che valore. Filtro erase: `lower(CustomerEmail)==lower(email) AND
CustomerName != AnonymizedMarker` (idempotenza: le già-anonimizzate non rientrano; inoltre dopo l'erase l'email
è azzerata e non rimatcha).

## 6. Consenso arricchito (B)

- `GdprConsentVersion` è una stringa opaca (es. `"2026-06-01"` o un hash dell'informativa) decisa dall'agenzia; il backend la **memorizza e basta**, non la interpreta.
- Retrocompatibile: campo nullable; le prenotazioni esistenti e i client che non lo inviano restano validi (`null`).
- Esposto nell'export DSAR (`BookingExportItem.GdprConsentVersion`).

## 7. Testing

Unit (xUnit, EF InMemory + `ITenantContext` fake, come `OutboxProcessorTests`):
- **Export**: bookings del cliente restituiti con tutti i campi; conteggio corretto.
- **Export isolamento tenant**: bookings di un altro tenant NON compaiono (fake tenant context su tenant A, dati di B → export vuoto/404).
- **Export 404**: email senza prenotazioni → `Result` NotFound.
- **Export email case-insensitive**: `Mario@x.it` matcha `mario@x.it`.
- **Erase**: anonimizza le righe del cliente, ritorna il conteggio, i campi PII risultano azzerati/marcati.
- **Erase idempotente**: già-anonimizzate non ricontate; seconda erase → 0 → 404.
- **Erase 404**: nessuna riga → NotFound, nessun audit.
- **Audit**: export ed erase scrivono una riga `audit_log` con `Action` corretta e `subjectRef` = hash (no email in chiaro).
- **Booking salva versione consenso**: `BookingService` persiste `GdprConsentVersion` dalla request (e resta `null` se omessa).

> Nota: con EF InMemory `ExecuteUpdateAsync` è supportato in EF Core 10; se in test risultasse non valutabile,
> l'erase userà un percorso equivalente (caricamento + update) dietro la stessa firma — la scelta finale è
> dell'implementer in fase di piano, mantenendo il contratto `Result<ErasureResult>`.

## 8. Documentazione (C) — `GDPR_COMPLIANCE.md`

Contenuti: catena ruoli (**barber = titolare**, agenzia = responsabile, piattaforma/dev = sub-responsabile);
**sub-responsabili** (Brevo = invio email, EU; Railway = hosting, EU); **data-flow** (dove vivono i dati,
quali PII, in quali tabelle: `bookings`, `outbox_emails`, `audit_log` PII-free, `logs` PII-free); **retention**
(prenotazioni anonimizzate oltre `Gdpr:RetentionDays`=365; outbox purgata oltre `Gdpr:OutboxRetentionDays`=30;
logs 90gg); **DSAR** (come usare gli endpoint export/erase); **consenso** (bool + timestamp + versione).

## 9. Build & verifica

`dotnet build` verde (0 warning) e tutti i test verdi prima di chiudere. Migration applicabile
(`dotnet ef migrations add AddGdprConsentVersion`). Smoke opzionale: creare un booking con versione consenso,
export per email, erase, ri-export (vuoto).

## 10. Esecuzione

Branch `AutoDev`, subagent-driven-development, review per task + whole-branch finale, build+test ad ogni step.
Lo spec passa a `writing-plans`.
