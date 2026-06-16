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

### 2.2 Cambio password — IMPLEMENTATO ✅ (2026-06-16, V2.3)

**Stato aggiornato:** la gestione autonoma delle credenziali Owner è stata completamente implementata nel
task V2.3 "Onboarding credenziali Owner". Di seguito un riepilogo di quanto implementato; per i dettagli
architetturali e il piano originale vedi `docs/superpowers/plans/2026-06-16-onboarding-owner-credenziali.md`.

**Cosa è stato implementato:**

- **Login per email globale:** `POST /admin/auth/token` ora accetta `{ email, password }` (rimosso `tenantSlug`
  — breaking change). L'email è univoca globalmente (un'email = un account = un'attività).
- **Attivazione via link:** il provisioning crea l'Owner senza password (`PasswordHash` null), genera un token
  di attivazione (hash in DB, scadenza configurabile `Account:ActivationTokenHours`, default 72h) e accoda
  un'email con il link. La CLI non stampa più password.
- **Cambio password autenticato:** `POST /admin/account/password` (JWT) `{ currentPassword, newPassword }` → 204.
- **Reset password "dimenticata":** `POST /admin/account/password/reset-request` `{ email }` → sempre 202
  (neutro); `POST /admin/account/password/reset` `{ token, newPassword }` → 204; token hash in DB con scadenza
  `Account:ResetTokenHours` (default 1h).
- **Pagine HTML set-password:** servite direttamente dall'API (deroga circoscritta ad AD-09: solo pagine
  tecniche raggiunte dai link email, non una dashboard). `GET /admin/account/activate?token=` e
  `GET /admin/account/password/reset?token=`.
- **SecurityStamp + invalidazione JWT:** claim `security_stamp` nel JWT; al cambio/reset/attivazione lo stamp
  si rigenera (validazione cache-first nel middleware JWT) → i vecchi token diventano invalidi.
- **Policy password:** minimo 12 caratteri (`Account:PasswordMinLength`).
- **Rate limit `AccountSecurity`:** 10 req/min per IP su tutte le rotte account+login
  (`RATE_LIMIT_ACCOUNT_PER_MINUTE` / `RateLimiting:AccountPerMinute`).
- **2 migration nuove:** `MakeEmailGlobalAndAddSecurityFields` (email univoca globale + SecurityStamp),
  `AddUserSecurityTokens` (token di attivazione e reset).
- **Fix JWT:** `MapInboundClaims=false` e `KeyId` stabile su chiave HS256 (⚠ eseguire smoke test login admin
  al deploy).
- **Test:** 6 nuovi test del flusso account (unit + integration), totale suite 127 test verdi (96 unit + 31 integration).

> **Conclusione Q2 (aggiornata):** **entrambi i punti coperti.** Email scegliibile al provisioning ✅;
> cambio/reset password ✅ (V2.3, 2026-06-16). Il cambio email self-service (S-PW3 del piano originale)
> resta non implementato — priorità bassa, da valutare in futuro.

---

## 3. ~~Piano di intervento~~ — (storico, superato da V2.3)

> Il piano originale (S-PW1..S-PW4) è stato eseguito integralmente nel task V2.3 (2026-06-16), con alcune
> differenze rispetto alla sequenza pianificata: S-PW1, S-PW2, S-PW4 implementati; S-PW3 (cambio email)
> non implementato (priorità bassa). È stato aggiunto il flusso di attivazione iniziale (Owner senza password
> al provisioning) non previsto nel piano originale.
> Dettagli storici del piano conservati in `docs/superpowers/plans/2026-06-16-onboarding-owner-credenziali.md`.
