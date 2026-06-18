# Design — Agency Provisioning/Management API (identità di piattaforma)

> Data: 2026-06-18 · Stato: in design · Prossimo passo: piano di implementazione (writing-plans)
> Contesto prodotto: il cliente è **un'agenzia web** (vedi `Claude_Instructions/VISIONE_PRODOTTO_E_ROADMAP.md`). Questo è il **backend della console agenzia**, limitatamente a **provisioning + gestione tenant**.

## 1. Obiettivo e confini

Dare all'agenzia un'**API per creare e gestire i tenant** senza CLI né accesso diretto al DB, con un'**identità di piattaforma** (agency-admin) autenticata. Sarà consumata dalla futura **console agenzia** (UI browser interna).

**Dentro scope (questo spec):**
- Identità + auth di piattaforma (`PlatformAdmin`, login → JWT platform).
- Bootstrap del primo admin (endpoint di setup blindato).
- API di gestione tenant: crea, elenca, dettaglio, disattiva/riattiva, gestione API key, re-invio attivazione Owner.
- Cambio password dell'agency-admin autenticato.
- Refactor: estrazione della logica di provisioning oggi nella CLI in un **servizio condiviso** (CLI + API).

**Fuori scope (spec/progetti separati):**
- **API di osservabilità** (log/volumi prenotazioni/health cross-tenant) → spec successiva.
- **Frontend della console agenzia** → progetto separato.
- Vedi §9 per i **follow-up rimandati** (invito multi-admin, reset password platform via email).

## 2. Decisioni di design (prese con l'utente, 2026-06-18)

| # | Decisione | Scelta |
|---|---|---|
| D1 | Auth agenzia | **Account agency-admin con login JWT** (adatto a console browser; audit per-utente). |
| D2 | Modello identità | **Entità separata `PlatformAdmin`** (NO TenantId) → l'invariante tenant-isolation resta intatto; nessun `TenantId` nullable su `User`. |
| D3 | Bootstrap primo admin | **Endpoint di setup una tantum**, blindato (vedi §4.3). |
| D4 | Scope follow-up | **Rimandati** invito multi-admin e reset-password-platform-via-email (email non validata fino al deploy). Documentati in §9. |

## 3. Architettura e componenti

### 3.1 Entità `PlatformAdmin` (nuova, Core)
Tabella `platform_admin`, **separata** da `users`, **senza** `TenantId`:

| Campo | Tipo | Note |
|---|---|---|
| `Id` | Guid | PK |
| `Email` | string | unique (globale), login |
| `PasswordHash` | string? | null finché non attivato (qui: impostata dal setup) |
| `SecurityStamp` | Guid | rigenerata al cambio password → invalida i JWT precedenti |
| `Active` | bool | default true |
| `FailedAccessCount` | int | lockout (S3, parallelo a `User`) |
| `LockoutEnd` | DateTimeOffset? | lockout |
| `ActivatedAt` | DateTimeOffset? | per coerenza/futuro flusso invito |
| `LastLoginAt` | DateTimeOffset? | |
| `CreatedAt` / `UpdatedAt` | DateTimeOffset | TimestampInterceptor |

Indice unique su `Email`. **Non** soggetta al global query filter tenant (come `UserSecurityToken`).

### 3.2 Riuso dei primitivi auth (no duplicazione inutile)
I primitivi già esistenti sono riusati così com'è dove sono statici/condivisibili:
- **Password hashing**: `BCrypt` (statico) — riuso diretto.
- **Token sicurezza**: `SecurityTokenGenerator` + `ApiKeyHasher` (statici) — riuso per i follow-up.
- **JWT**: si **estende `IJwtTokenGenerator`** con un metodo `GeneratePlatform(Guid platformAdminId, Guid securityStamp)` → stesso segreto/KeyId, claim `sub = adminId`, `role = PlatformAdmin`, `security_stamp`, **senza** `tenant_id`. (Stessa firma HS256, coerente con il fix KeyId/MapInboundClaims.)
- **SecurityStamp**: **nuovo** `IPlatformSecurityStampService` (cache-first, parallelo a `IUserSecurityStampService`) che legge da `platform_admin`. La validazione nel JWT `OnTokenValidated` **discrimina sul claim `role`**: se `PlatformAdmin` → valida contro lo store platform; altrimenti contro quello tenant.
- **Lockout**: `IPlatformAdminRepository` con metodi `RegisterFailedAttempt`/`RegisterSuccessfulLogin` analoghi a `UserRepository` (duplicazione minima, isolamento netto > DRY estremo qui).

### 3.3 Login di piattaforma
`POST /api/v1/platform/auth/token` `{ email, password }` → `IPlatformAuthService.LoginAsync`:
- Risolve `PlatformAdmin` per email; verifica `Active`, `PasswordHash` non-null, lockout; verifica password (con **timing-equalizer** come `AdminAuthService`); errore **neutro** in ogni fallimento.
- Successo → `GeneratePlatform(...)` → JWT platform. Risposta `{ token, tokenType, expiresAt }`.

### 3.4 Authorization & pipeline
- **Policy `PlatformAdmin`**: richiede claim `role = PlatformAdmin`. Le rotte `/api/v1/platform/*` (tranne `auth` e `setup`, anonime) la usano via `RequireAuthorization("PlatformAdmin")`.
- `AdminContextMiddleware` (risoluzione tenant da JWT) agisce solo su `/api/v1/admin` → **non tocca** `/api/v1/platform`. Le rotte platform non hanno tenant corrente: i servizi platform usano `IgnoreQueryFilters` dove operano cross-tenant.

### 3.5 Refactor provisioner (condiviso CLI + API)
Oggi `ProvisioningInput` / `ProvisioningValidator` / `TenantProvisioner` sono `internal` in `tools/…TenantProvisioning`. Si estraggono:
- **Core**: `ProvisioningInput` (+ record annidati) e `ProvisioningValidator` diventano **public** in `WebAgency_BookingSystem.Core` (es. namespace `Core.Provisioning`).
- **Core.Abstractions**: `ITenantProvisioningService` con `Task<Result<ProvisioningOutput>> CreateAsync(ProvisioningInput input, CancellationToken ct)` (`ProvisioningOutput` = tenantId, slug, apiKey [una volta], conteggi).
- **Infrastructure**: implementazione `TenantProvisioningService` (la logica attuale di `TenantProvisioner`, incluso il collegamento `localId` staff↔servizi, la generazione API key, il token+email di attivazione Owner, un solo `SaveChanges` atomico).
- **CLI**: `Program.cs` risolve `ITenantProvisioningService` da DI invece di `new TenantProvisioner(...)`. La CLI resta come fallback/automazione.
- **Slug duplicato**: oggi la CLI lancia `ProvisioningException` su slug esistente; nel servizio diventa un `Result` di fallimento (`Error.Conflict`) → l'API risponde 409, la CLI stampa l'errore.

## 4. Superficie API (platform JWT)

Tutte sotto `/api/v1/platform`, gruppo con `WithTags("Platform")`. OpenAPI completa come da regole di progetto.

### 4.1 Gestione tenant
| Metodo | Rotta | Scopo |
|---|---|---|
| `POST` | `/tenants` | Crea tenant (body = `ProvisioningInput`). → 201 `{ tenantId, slug, apiKey (una volta), keyPrefix, counts }`; 409 se slug esiste; 422 se validazione fallisce. Accoda email attivazione Owner. |
| `GET` | `/tenants` | Elenco tenant (id, slug, name, siteUrl, active, ownerEmail, createdAt). Paginato (`page`/`pageSize`, riusa `PagedResponse<T>`). |
| `GET` | `/tenants/{id}` | Dettaglio tenant. |
| `POST` | `/tenants/{id}/deactivate` | `Active=false` (il tenant non risolve più API key/JWT). 204. |
| `POST` | `/tenants/{id}/reactivate` | `Active=true`. 204. |
| `GET` | `/tenants/{id}/api-keys` | Elenca le API key del tenant (prefisso/metadati). |
| `POST` | `/tenants/{id}/api-keys` | Genera una nuova API key per il tenant (valore una volta). |
| `DELETE` | `/tenants/{id}/api-keys/{keyId}` | Revoca una API key del tenant (+ invalidazione cache `apikey:{hash}`). |
| `POST` | `/tenants/{id}/owner/resend-activation` | Rigenera token + ri-accoda email di attivazione Owner. 202. |

> La gestione API key qui è la versione **platform-scoped** (qualsiasi tenant). Riusa `ApiKeyGenerator`/`ApiKeyHasher` e la logica dell'`AdminApiKeyManager` esistente, generalizzata per accettare un `tenantId` esplicito (invece di leggerlo da `ITenantContext`).

### 4.2 Account agency-admin
| Metodo | Rotta | Auth | Scopo |
|---|---|---|---|
| `POST` | `/platform/auth/token` | — | Login → JWT platform. |
| `POST` | `/platform/account/password` | JWT platform | Cambio password (`currentPassword`, `newPassword`) → 204; rigenera SecurityStamp (invalida vecchi JWT). |
| `POST` | `/platform/setup` | setup token | Bootstrap primo admin (§4.3). |

### 4.3 Bootstrap — `POST /api/v1/platform/setup`
Body `{ setupToken, email, password }`. **Guardie combinate**:
1. Se `PLATFORM_SETUP_TOKEN` (env) **non** è configurato → endpoint **disabilitato** (404).
2. Se `setupToken` ≠ valore env → 401.
3. Se esiste **già** un `PlatformAdmin` → 409 (inerte dopo il primo). Race gestita dall'indice unique su email + transazione.
4. Valida policy password (riusa `Account:PasswordMinLength`).
5. Crea il `PlatformAdmin` (Active, password impostata, ActivatedAt=now). Audit `platform_admin_created`.
6. Rate-limit stretto (policy `AccountSecurity`).

> Dopo il primo admin, l'endpoint risponde sempre 409. In produzione si imposta `PLATFORM_SETUP_TOKEN` solo per la finestra di setup.

## 5. Sicurezza & trasversali
- **Rate-limit**: `/platform/auth`, `/platform/setup`, `/platform/account/*` sotto la policy per-IP `AccountSecurity` (già esistente).
- **Audit** (`audit_log`): azioni platform con attore `platform-admin:{id}` (es. `tenant_created`, `tenant_deactivated`, `apikey_created`, `apikey_revoked`, `owner_activation_resent`). `audit_log.TenantId` valorizzato col tenant interessato dove applicabile.
- **JWT**: stesso `JWT_SECRET`/KeyId; il platform JWT si distingue per `role=PlatformAdmin` e assenza di `tenant_id`. `MapInboundClaims=false` (già impostato) garantisce la lettura dei claim.
- **Config/env nuovi**: `PLATFORM_SETUP_TOKEN` (abilita/gate del setup). `JWT_EXPIRY_HOURS` riusato.
- **Isolamento**: i servizi platform operano cross-tenant con `IgnoreQueryFilters`; nessun `ITenantContext` richiesto sulle rotte platform.

## 6. Modello dati / migration
- Migration **`AddPlatformAdmin`**: tabella `platform_admin` (+ unique index email). Nessuna modifica a `users`/tenant (invariante preservato).

## 7. Data flow (crea tenant)
1. Agency-admin (console) → login `/platform/auth/token` → JWT platform.
2. `POST /platform/tenants` con `ProvisioningInput` → policy `PlatformAdmin` → `ITenantProvisioningService.CreateAsync` (transazione: tenant, orari, servizi, staff, API key, Owner senza password, token attivazione, email outbox) → 201 con API key (una volta).
3. Dispatcher outbox invia l'email di attivazione all'Owner → Owner imposta password (flusso V2.3).
4. L'agenzia collega il sito del cliente usando l'API key restituita.

## 8. Error handling & testing
- **Errori**: pattern `Result`/`Error` → mapping HTTP esistente (`Validation`→422, `Conflict`→409, `Unauthorized`→401, `Forbidden`→403, `NotFound`→404). Login/credenziali: messaggio **neutro**.
- **Unit**: login platform (utente inesistente/non attivo/lockout/stamp), validazione setup (token errato/admin già presente), policy password, mapping errori provisioning (slug duplicato→409).
- **Integration**: setup (gate token, admin già presente→409, env assente→404); login platform → JWT → accesso rotta platform (200) vs JWT tenant su rotta platform (403) e viceversa; crea tenant end-to-end (verifica tenant+API key+outbox); deactivate/reactivate; gestione API key cross-tenant; resend activation; cambio password platform invalida vecchio JWT (stamp). **Sink log DB disattivato nei test** (già così).
- Per i test platform: seed di un `PlatformAdmin` attivo nel `TestData` (con password nota), analogo al seed Owner.

## 9. Follow-up RIMANDATI (documentati per sviluppo successivo)

> Rimandati di proposito (D4): l'email transazionale non è validata end-to-end fino al deploy, quindi i flussi via email si realizzano insieme a quella validazione. Entrambi riusano i pattern V2.3.

### 9.1 Invito di altri agency-admin
- **Endpoint**: `POST /platform/admins` (JWT platform) `{ email }` → crea un `PlatformAdmin` **senza password** + token di attivazione (`UserSecurityToken` o tabella analoga per platform) + email di attivazione (pagina set-password riusata/parallela). `GET /platform/admins` per elencarli; `POST /platform/admins/{id}/deactivate`.
- **Riuso**: `SecurityTokenGenerator`, outbox (`EnqueueAccountActivation`), pagine `AccountHtmlPages` (estese a un endpoint platform di attivazione).
- **Note**: serve un meccanismo token per i platform admin (riusare `UserSecurityToken` con un `PlatformAdminId` nullable accanto a `UserId`, **oppure** una `platform_security_token` dedicata — preferibile la tabella dedicata per non sporcare `UserSecurityToken`).

### 9.2 Reset password agency-admin via email
- **Endpoint**: `POST /platform/account/password/reset-request` `{ email }` (risposta neutra 202) + `POST /platform/account/password/reset` `{ token, newPassword }`, con pagina HTML di reset. Identico a V2.3 ma sull'identità `PlatformAdmin`.
- **Dipendenza**: email validata in produzione.

### 9.3 Attivazione del PRIMO admin via email (alternativa al setup endpoint)
- Se in futuro si preferisce non usare il setup endpoint, si può bootstrappare il primo admin da env (`PLATFORM_ADMIN_EMAIL`) con email di attivazione (come valutato e non scelto ora). Mantenere il setup endpoint comunque gated.

## 10. Sequenza di build suggerita
1. Entità `PlatformAdmin` + config + migration.
2. Estrazione provisioner condiviso (Core models/validator + `ITenantProvisioningService` in Infrastructure) + adattamento CLI (build verde).
3. Auth platform: `GeneratePlatform` nel JWT, `IPlatformSecurityStampService`, `IPlatformAdminRepository`, `IPlatformAuthService`, policy + discriminazione in `OnTokenValidated`.
4. Setup endpoint (gated) + cambio password platform.
5. Endpoint gestione tenant (crea/list/get/deactivate/reactivate) → poi API key cross-tenant → resend activation.
6. Audit + rate-limit + config env.
7. Seed `PlatformAdmin` nei test + suite unit/integration.
8. Documentazione (CLAUDE.md sommario endpoint + env; roadmap: spuntare il filone).
