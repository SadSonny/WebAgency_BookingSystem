# Deploy su Railway — Checklist operativa

> Guida passo-passo per il deploy del backend su **Railway** (EU West), istanza continuativa. Il deploy è
> l'ultimo passo del progetto (vedi `VISIONE_PRODOTTO_E_ROADMAP.md` §5). Ultimo aggiornamento: **2026-06-19**.

## Prerequisiti tecnici — già pronti nel repo
- **Dockerfile di produzione** (`/Dockerfile`): multi-stage `sdk:10.0`→`aspnet:10.0`, utente non-root, entrypoint corretto. Railway lo rileva e lo builda automaticamente.
- **Porta**: l'app legge `$PORT` iniettata da Railway (`Program.cs`, `UseUrls`).
- **DATABASE_URL**: l'app converte automaticamente il formato URI (`postgresql://…`) di Railway nel formato keyword di Npgsql (`DatabaseConnectionString.Normalize`). **Puoi puntare `DATABASE_URL` direttamente alla variabile di riferimento del Postgres**, senza comporla a mano.

> Nota costo: Railway non ha più un free tier reale — piano **Hobby ~5$/mese** a consumo. Il free-tier "che dorme"
> non è adatto perché l'app ha **background service sempre attivi** (outbox email, promemoria, monitor OPS, retention).

## 1. Progetto e database
1. Crea un nuovo progetto su Railway.
2. Aggiungi il plugin **PostgreSQL** (managed). Espone le variabili `DATABASE_URL` (proxy pubblico) e `DATABASE_PRIVATE_URL` (rete interna).

## 2. Servizio API
1. Collega il repo GitHub (branch **`main`**) o usa la CLI Railway. Railway rileva il `Dockerfile` in root.
2. Attendi la prima build.

## 3. Variabili d'ambiente (servizio API)

| Variabile | Valore | Note |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | ⚠️ Obbligatoria: attiva i guard di produzione (es. rifiuto del JWT secret `change-me`). |
| `DATABASE_URL` | `${{Postgres.DATABASE_PRIVATE_URL}}` | Rete interna (egress gratis, più veloce). L'app converte l'URI. |
| `JWT_SECRET` | random reale ≥ 32 char | Genera un segreto forte. |
| `JWT_EXPIRY_HOURS` | `8` | Opzionale (default 8). |
| `EMAIL_PROVIDER` | `Brevo` | |
| `BREVO_API_KEY` | `xkeysib-…` | |
| `BREVO_SENDER_EMAIL` | mittente **verificato** su Brevo | Se non verificato, le email non partono. |
| `BREVO_SENDER_NAME` | es. `BookingSystem` | |
| `PUBLIC_BASE_URL` | `https://<servizio>.up.railway.app` | Base dei link nelle email (attivazione/reset). Impostala dopo aver ottenuto il dominio Railway. |
| `PLATFORM_SETUP_TOKEN` | token segreto random | Abilita il bootstrap break-glass. **Rimuovilo dopo il setup.** |
| `DB_AUTO_MIGRATE` | `true` | Istanza singola → applica le migration all'avvio. Con più istanze, preferire uno step di migrazione in pipeline. |
| `OPS_ALERT_CHANNEL` | `Telegram` | Opzionale — per alert reali. Senza, resta `LogOnly`. |
| `OPS_ALERT_TELEGRAM_BOT_TOKEN` / `OPS_ALERT_TELEGRAM_CHAT_ID` | credenziali bot | Solo se `Channel=Telegram`. |

## 4. Migration
Con `DB_AUTO_MIGRATE=true` le migration EF si applicano all'avvio del servizio (incluse `AddPlatformAdmin` e
`AddGdprConsentVersion`). Verifica nei log di avvio che la migrazione sia andata a buon fine.

## 5. Smoke test in produzione (l'ordine conta)
1. `GET /api/v1/health/live` → **200** (processo vivo).
2. `GET /api/v1/health` → **200** (DB raggiungibile; 503 se il DB non risponde).
3. `POST /api/v1/platform/setup` body `{ setupToken, email, password }` → crea l'admin di piattaforma.
4. `POST /api/v1/platform/auth/token` → login platform, ottieni il JWT.
5. `POST /api/v1/platform/tenants` (JWT platform) → crea un tenant → ricevi **API key** (una sola volta) e viene accodata l'**email di attivazione Owner**. **Verifica che Brevo la consegni davvero.**
6. Attiva l'Owner (link email o `POST /api/v1/admin/account/activate`) → `POST /api/v1/admin/auth/token` → **smoke login admin** (valida i fix JWT `MapInboundClaims=false` + `KeyId`).
7. Con la API key: `POST /api/v1/bookings` (crea prenotazione) → verifica email di conferma → poi `GET/POST /api/v1/admin/gdpr/customer[/erase]` (DSAR).
8. **Rimuovi `PLATFORM_SETUP_TOKEN`** dalle env → l'endpoint di setup torna a rispondere **404**.

## 6. Monitoraggio (opzionale, gratis)
- **Uptime monitor** esterno (UptimeRobot o simile) su `GET /api/v1/health/live`.
- Se hai configurato Telegram, verifica di ricevere un alert forzando un errore o fermando il DB.

## Note
- **CORS per-tenant**: gli origin ammessi derivano dai `siteUrl` dei tenant (catalogo + refresh job) — assicurati che i tenant abbiano il `siteUrl` corretto.
- **Log**: Serilog scrive su console (catturata da Railway) e sulla tabella `logs` del DB (retention 90gg).
- **Segreti**: in produzione si usano SOLO variabili d'ambiente (né appsettings né user-secrets).
