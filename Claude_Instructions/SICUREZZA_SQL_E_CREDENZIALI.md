# Sicurezza — SQL Injection & Gestione Credenziali Owner

> Documento di audit creato il **2026-06-16**. Copre due verifiche richieste:
> 1. Esposizione a SQL injection di progetto e API.
> 2. Capacità del proprietario dell'attività (Owner) di scegliere l'email dell'utenza e cambiare la password.

---

## 1. SQL Injection — ESITO: SICURO ✅

### Sintesi
Il progetto **non è esposto a SQL injection**. Tutto l'accesso ai dati passa da **EF Core 10 / Npgsql** con query LINQ, che sono **parametrizzate per costruzione**. Nei pochi punti in cui si usa SQL grezzo, gli input dinamici sono passati come **parametri** (`{0}`), mai per concatenazione/interpolazione di stringa.

### Evidenze

#### 1.1 Accesso dati standard → LINQ parametrizzato
Tutti i repository, i service e gli endpoint usano LINQ (`Where`, `FirstOrDefaultAsync`, ecc.). EF Core traduce ogni valore in un parametro Npgsql. Esempi:
- Lookup utente al login: `FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Email == email, ct)` — [UserRepository.cs:17-21](../src/WebAgency_BookingSystem.Infrastructure/Persistence/Repositories/UserRepository.cs#L17-L21).
- Filtri admin prenotazioni: parametri tipizzati (`DateOnly?`, `Guid?`, enum) — [AdminBookingEndpoints.cs:18-36](../src/WebAgency_BookingSystem.Api/Endpoints/Admin/AdminBookingEndpoints.cs#L18-L36). Il parametro `status` è validato contro una whitelist enum (`TryParseApi`) e **mai** inserito in SQL come testo.
- Route con `Guid` vincolate dal constraint `:guid`, quindi nessun input arbitrario raggiunge la query.

#### 1.2 SQL grezzo → solo 2 punti, entrambi sicuri
In [BookingService.cs:739-745](../src/WebAgency_BookingSystem.Infrastructure/Services/BookingService.cs#L739-L745):

```csharp
await _db.Database.ExecuteSqlRawAsync($"SET LOCAL lock_timeout = '{LockTimeoutMs}ms'", ct);
await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [lockKey], ct);
```

- `pg_advisory_xact_lock({0})`: `lockKey` è passato come **parametro** (`{0}`). È inoltre un `long` calcolato da un hash SHA-256 (`ComputeLockKey`), non una stringa utente.
- `SET LOCAL lock_timeout = '{LockTimeoutMs}ms'`: l'interpolazione è su `LockTimeoutMs`, una **costante interna** `private const int LockTimeoutMs = 5000` ([BookingService.cs:31](../src/WebAgency_BookingSystem.Infrastructure/Services/BookingService.cs#L31)), non input utente. `SET LOCAL` in PostgreSQL non accetta parametri preparati, quindi l'interpolazione di una costante è l'unica via ed è priva di rischio.

#### 1.3 Nessun pattern pericoloso presente
Ricerca su tutto il codebase di `FromSqlRaw` / `FromSqlInterpolated` / `ExecuteSqlRaw` / `SqlQueryRaw`: gli unici usi applicativi sono i due sopra. Gli altri occorrono nei **test** (`tests/...`), anch'essi parametrizzati (`DELETE FROM ... WHERE tenant_id = {0}`). Nessuna costruzione di query per concatenazione di stringhe con input utente.

### Conclusione
**Nessun fix richiesto.** Mantenere la disciplina: per qualsiasi futuro SQL grezzo usare **sempre** placeholder parametrici (`{0}`), mai interpolazione di input. Se in futuro venisse aggiunto un ordinamento/colonna dinamica lato API, validare contro una whitelist (mai inserire il nome colonna/direzione nella stringa SQL).

---

## 2. Email & Password dell'Owner

### 2.1 Scelta dell'email — DISPONIBILE (in fase di provisioning) ✅
L'email dell'utenza Owner **è scelta dall'attività**: viene fornita nel JSON di onboarding come `ownerEmail` e usata sia per il `Tenant.OwnerEmail` sia per l'`User.Email` dell'account admin — [TenantProvisioner.cs:39,73](../tools/WebAgency_BookingSystem.TenantProvisioning/TenantProvisioner.cs#L39-L73).

**Limite:** la scelta avviene **solo al provisioning** (eseguito dall'agenzia tramite CLI). Non esiste un endpoint self-service per **modificare** l'email in seguito.

### 2.2 Cambio password — NON DISPONIBILE ❌
La password admin è **generata automaticamente** al provisioning (18 caratteri esadecimali random, mostrata una sola volta) — [TenantProvisioner.cs:68,223-225](../tools/WebAgency_BookingSystem.TenantProvisioning/TenantProvisioner.cs#L68-L74).

Non esiste **alcun** endpoint per:
- cambiare la propria password (l'Owner resta legato alla password generata);
- reset password / "password dimenticata";
- modificare la propria email post-provisioning.

Gli unici endpoint admin disponibili sono `auth/token`, bookings, services, staff, business-hours, closures, api-keys — nessuno di gestione account/utente.

> **Conclusione Q2:** parzialmente coperto. **Email scegliibile sì** (al provisioning), **cambio password no**. Segue il piano di intervento (NON ancora eseguito).

---

## 3. Piano di intervento — Self-service credenziali Owner (DA APPROVARE, non eseguito)

Obiettivo: dare all'Owner il controllo sulle proprie credenziali, mantenendo le regole del progetto (SOLID, `Result<T>`, errori in italiano, JWT admin, `[INTENT]`/`WHY`, OpenAPI completa, test dopo l'implementazione).

### S-PW1 — Cambio password autenticato (priorità ALTA)
- **Endpoint:** `POST /api/v1/admin/account/password` (JWT richiesto).
- **DTO:** `record ChangePasswordRequest(string CurrentPassword, string NewPassword)`.
- **Logica (nuovo `IAdminAccountService`):**
  1. Risolvi `userId` dal claim JWT.
  2. Verifica `CurrentPassword` con `BCrypt.Verify` (riuso di `VerifyPassword`).
  3. Valida `NewPassword` (FluentValidation: min 12 char, non uguale alla corrente).
  4. `PasswordHash = BCrypt.HashPassword(NewPassword)`, `SaveChanges`, audit log `password_changed`.
- **Sicurezza:** rate-limit dedicato; in caso di `CurrentPassword` errata, riusare l'errore neutro 401/400; opzionale invalidazione token (vedi S-PW4).
- **Test:** unit (verifica/validazione) + integration (happy path, password corrente errata, nuova password debole).

### S-PW2 — Reset password "dimenticata" (priorità MEDIA)
- **Flusso a token via email** (l'infrastruttura outbox/email esiste già — `OutboxEmail` + `EmailOutboxDispatcher`):
  - `POST /api/v1/admin/account/password/reset-request` `{ tenantSlug, email }` → genera token monouso con scadenza (hash in DB, nuova entità `PasswordResetToken` o campi su `User`), accoda email. **Risposta sempre neutra** (non rivela se l'email esiste).
  - `POST /api/v1/admin/account/password/reset` `{ token, newPassword }` → valida token non scaduto/non usato, aggiorna hash, invalida token.
- **Migration:** nuova tabella/colonne per i token di reset.
- **Test:** scadenza token, riuso token, token inesistente.

### S-PW3 — Cambio email Owner (priorità BASSA/opzionale)
- **Endpoint:** `PUT /api/v1/admin/account/email` (JWT) `{ newEmail, currentPassword }`.
- Verifica password corrente; valida unicità `(tenantId, email)`; opzionale doppio opt-in via email di conferma.
- Aggiornare sia `User.Email` sia, se desiderato, `Tenant.OwnerEmail` (decisione da confermare con l'utente).

### S-PW4 — (Opzionale) Invalidazione sessioni dopo cambio password
- Aggiungere `User.SecurityStamp` (o `PasswordChangedAt`) e includerlo come claim nel JWT; in fase di validazione rifiutare token con stamp obsoleto. Mitiga il riuso di token rubati dopo un cambio password.

### Note trasversali
- Tutti i nuovi endpoint: `WithName/WithSummary/WithDescription/WithTags("Admin")/Produces<T>/ProducesProblem` + `RequireAuthorization` come da regola OpenAPI del progetto.
- Aggiornare `CLAUDE.md` (sommario endpoint admin) e `DEVELOPMENT_PLAN.md` (changelog + checkbox) nello stesso commit dell'implementazione.
- Considerare che il prodotto è **API-only, senza Admin UI** (AD-09): questi endpoint saranno consumati dal sito/integrazione del cliente o da tooling interno.

### Sequenza consigliata
`S-PW1` → `S-PW4` → `S-PW2` → `S-PW3`. S-PW1 è il minimo indispensabile per chiudere il gap principale (l'Owner non può cambiare la password auto-generata).
