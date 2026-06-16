# Design — Onboarding Owner: attivazione account, login e gestione password

> Data: 2026-06-16 · Stato: approvato (design) · Prossimo passo: piano di implementazione
> Contesto: WebAgency BookingSystem (multi-tenant, .NET 10, API-only senza Admin UI — AD-09).

## 1. Problema e obiettivo

Oggi l'account Owner è creato **solo** dal provisioning CLI con una password **generata** e mostrata una volta all'operatore. L'Owner:
- non può **cambiare** la propria password (nessun endpoint);
- non può recuperarla se la perde;
- riceve la password tramite un canale fuori banda (l'operatore gliela comunica).

Obiettivo: dare all'Owner il controllo sicuro delle proprie credenziali tramite un flusso di **attivazione via link** (nessuna password trasmessa via email), **login con sola email**, **cambio password autenticato**, **reset "password dimenticata"** e **invalidazione dei token** dopo un cambio password.

## 2. Decisioni di prodotto (prese con l'utente)

| # | Decisione | Scelta |
|---|---|---|
| D1 | Password iniziale | **Link di attivazione**: il provisioning NON genera una password usabile; l'Owner imposta la sua al primo accesso. Nessuna credenziale via email. |
| D2 | Identità di login | **Solo email** (univoca a livello globale tra tutti i tenant). Un'email = un account = un'attività. |
| D3 | Landing del link | **Pagina HTML minimale servita dall'API** (deroga controllata ad AD-09: solo pagine tecniche di set-password, non una dashboard). |
| D4 | Scope opzionale | Inclusi **tutti e tre**: reset "password dimenticata", invalidazione JWT post-cambio (SecurityStamp), policy password configurabile. |

## 3. Flusso end-to-end

### 3.1 Onboarding (provisioning)
1. Provisioning con `ownerEmail` → crea `User` con `PasswordHash = null`, `ActivatedAt = null`, `Active = true`.
2. Genera un **token di attivazione** (random 32 byte): salva **solo l'hash** (`UserSecurityToken`, `Purpose = Activation`, scadenza 72h).
3. Accoda in **outbox** un'email all'Owner ("Attiva il tuo account") con link `{PublicBaseUrl}/api/v1/admin/account/activate?token=<raw>`.
4. Il CLI **non stampa più una password**: stampa "Email di attivazione inviata a `<ownerEmail>`". `ProvisioningResult` perde `AdminPassword`.

> L'email di attivazione assolve anche alla "conferma creazione account": i due passi si fondono.

### 3.2 Attivazione
5. L'Owner apre il link → `GET /admin/account/activate?token=` serve una **pagina HTML minimale** con form (nuova password + conferma).
6. Submit → `POST /admin/account/activate` `{ token, newPassword }`:
   - valida token (esiste, `Purpose = Activation`, non scaduto, non usato);
   - imposta `PasswordHash` (bcrypt), `ActivatedAt = now`, rigenera `SecurityStamp`;
   - marca il token `UsedAt = now`;
   - accoda email di **conferma attivazione**.

### 3.3 A regime
7. **Login**: `POST /admin/auth/token` `{ email, password }` → risolve l'utente per email (globale), deriva il tenant, verifica `tenant.Active`, lockout (S3), attivazione (password presente), password → JWT (con claim `SecurityStamp`).
8. **Cambio password** (autenticato): `POST /admin/account/password` `{ currentPassword, newPassword }` → verifica corrente, aggiorna hash, rigenera `SecurityStamp`, accoda email di conferma.
9. **Password dimenticata**:
   - `POST /admin/account/password/reset-request` `{ email }` → **risposta sempre neutra**; se l'utente esiste ed è attivo, genera token (`Purpose = PasswordReset`, scadenza 1h) e accoda email con link.
   - `GET /admin/account/password/reset?token=` → pagina HTML form.
   - `POST /admin/account/password/reset` `{ token, newPassword }` → valida token, aggiorna hash, rigenera `SecurityStamp`, marca token usato, accoda email di conferma.

## 4. Modello dati / migration

### 4.1 `User`
- `PasswordHash` → **nullable** (l'account esiste prima di avere una password). Il login rifiuta con errore **neutro** un utente con `PasswordHash` null (non ancora attivato).
- `ActivatedAt` (`DateTimeOffset?`) — istante di attivazione.
- `SecurityStamp` (`Guid`, non-null, default `Guid.NewGuid()`) — rigenerato a ogni mutazione di password.
- Indice univoco **globale** su `Email` (sostituisce `(TenantId, Email)`).

### 4.2 `UserSecurityToken` (nuova entità)
| Campo | Tipo | Note |
|---|---|---|
| `Id` | Guid | PK |
| `TenantId` | Guid | per coerenza/diagnostica; le query pre-auth usano `IgnoreQueryFilters` |
| `UserId` | Guid | FK → User |
| `TokenHash` | string | **hash** del token (mai il valore in chiaro); confronto per hash |
| `Purpose` | enum string | `Activation` \| `PasswordReset` |
| `ExpiresAt` | DateTimeOffset | scadenza |
| `UsedAt` | DateTimeOffset? | monouso: se valorizzato, token rifiutato |
| `CreatedAt` | DateTimeOffset | timestamp interceptor |

Indice su `TokenHash`. I token precedenti dello stesso `UserId`+`Purpose` ancora validi vengono invalidati alla generazione di uno nuovo (un solo token attivo per scopo).

### 4.3 Migration
- `MakeEmailGloballyUnique` (drop indice `(tenant_id, email)`, crea unico su `email`; rende `password_hash` nullable; aggiunge `activated_at`, `security_stamp`).
- `AddUserSecurityTokens` (nuova tabella).

> **Rischio dato:** la creazione dell'indice univoco globale fallisce se esistono email duplicate tra tenant. Su DB nuovo non è un problema; in presenza di dati va verificato prima.

## 5. Endpoint e componenti

### 5.1 Endpoint

| Metodo | Rotta | Auth | Scopo |
|---|---|---|---|
| `POST` | `/api/v1/admin/auth/token` | — | Login **modificato**: body `{ email, password }` (rimosso `tenantSlug`) |
| `GET` | `/api/v1/admin/account/activate?token=` | token | Pagina HTML "imposta password" |
| `POST` | `/api/v1/admin/account/activate` | token | `{ token, newPassword }` → attiva account |
| `POST` | `/api/v1/admin/account/password` | **JWT** | `{ currentPassword, newPassword }` → cambio a regime |
| `POST` | `/api/v1/admin/account/password/reset-request` | — | `{ email }` → invio email reset (risposta neutra) |
| `GET` | `/api/v1/admin/account/password/reset?token=` | token | Pagina HTML reset |
| `POST` | `/api/v1/admin/account/password/reset` | token | `{ token, newPassword }` → reset |

### 5.2 Componenti (SRP)
- **`IAdminAccountService`** (nuovo, Infrastructure): attivazione, cambio password, reset-request, reset. Separato da `IAdminAuthService` (che resta responsabile del solo login).
- **`IAdminAuthService`**: login adattato all'identità per email globale.
- **`IUserSecurityTokenService`** (o metodi sul repository): genera/valida/consuma token; hashing riusando il pattern di `ApiKeyHasher`.
- **`IUserRepository`**: aggiunti `GetByEmailAsync(email)` (globale, `IgnoreQueryFilters`), `GetBySecurityTokenAsync`, update password/stamp.
- **Pagine HTML**: endpoint minimal che restituiscono HTML statico parametrizzato (token nascosto nel form). `// EXCEPTION: AD-09` documentato.
- **Validator FluentValidation**: `SetPasswordRequestValidator`, `ChangePasswordRequestValidator` (policy password).

### 5.3 Email (riuso outbox + `EmailTemplateRenderer`)
Tre nuovi template HTML inline italiani:
1. **Invito attivazione** (link).
2. **Conferma** (attivazione completata / password cambiata / reset completato — può essere un template parametrizzato).
3. **Invito reset** (link).

## 6. Sicurezza

- Token: random 32 byte (`RandomNumberGenerator`), salvati come hash, **monouso**, a scadenza (attivazione 72h, reset 1h — configurabili).
- **Risposta neutra** su `reset-request` (non rivela l'esistenza dell'email) e sugli errori token (non distingue "scaduto" da "inesistente").
- **Rate-limit** dedicato su `auth/token`, `activate`, `reset-request`, `reset`.
- **Lockout S3** invariato sul login.
- **Policy password** (configurabile `Security:Password:*`): min 12 caratteri, diversa dalla precedente. Default ragionevoli; deroga via config.
- **SecurityStamp**: incluso come claim nel JWT. Il middleware admin confronta lo stamp del token con quello **corrente** dell'utente; mismatch → 401. Per evitare una query a ogni richiesta, lo stamp corrente è letto da una **cache `IMemoryCache` con chiave `user-stamp:{userId}`** (riusando l'infrastruttura di cache già presente per la tenant resolution): cache miss → lettura DB e popolamento; ogni mutazione di password (`activate`/`password`/`reset`) **invalida** la entry. TTL di fallback breve (es. 5 min) per coprire invalidazioni mancate.
- Nuovo config **`App:PublicBaseUrl`** (CLI + API) per costruire i link assoluti.

## 7. Impatti collaterali (da aggiornare nello stesso intervento)

- **Breaking change login**: `AdminLoginRequest` (rimosso `TenantSlug`), `AdminLoginRequestValidator`, `AdminAuthService`, `AdminAuthEndpoints`.
- **Provisioning**: `TenantProvisioner` (no password, genera token + outbox email), `ProvisioningResult`, README del tool, `samples/*.json` se necessario.
- **Documentazione**: `CLAUDE.md` (sommario endpoint admin + nota onboarding), `Claude_Instructions/GUIDA_INTEGRAZIONE_API.md`, `Claude_Instructions/SICUREZZA_SQL_E_CREDENZIALI.md` (chiude il gap §2), `DEVELOPMENT_PLAN.md` (changelog + checkbox), Thunder Client.

## 8. Testing

- **Unit**: validazione policy password; generazione/validazione/scadenza/riuso token; verifica password bcrypt; login per email (utente inesistente, non attivato, tenant disattivato, lockout, stamp).
- **Integration**: attivazione happy path; token scaduto/già usato; reset-request neutro (email esistente vs inesistente → stessa risposta); cambio password con corrente errata; invalidazione JWT dopo cambio password (token vecchio → 401); unicità email globale.

## 9. Fuori scope (YAGNI)

- Gestione multi-utente per tenant (inviti ad altri admin) — non richiesto ora.
- Cambio **email** Owner post-provisioning — rimandato (l'email è scelta al provisioning).
- 2FA / MFA.
- Dashboard admin (resta AD-09).

## 10. Sequenza di build suggerita

1. Schema + migration (`User`, `UserSecurityToken`).
2. Token service + repository.
3. Login per email globale (+ test).
4. Attivazione (endpoint + pagina + email + test).
5. Cambio password autenticato + SecurityStamp nel JWT/middleware (+ test).
6. Reset "password dimenticata" (+ test).
7. Provisioning: rimozione password, token + email.
8. Documentazione e Thunder Client.
