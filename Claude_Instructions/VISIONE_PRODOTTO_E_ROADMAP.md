# Visione di Prodotto & Roadmap

> Documento di contesto strategico. Chiarisce **per chi** è il prodotto, **cosa è dentro/fuori scope** (con il perché) e **cosa rimane da fare**. Serve a evitare che sessioni future ripropongano come "gap" cose escluse di proposito. Ultimo aggiornamento: **2026-06-18** (Monitor OPS 4.2: health check + alerting/Telegram completati).

## 1. Posizionamento — chi è il cliente

Il **cliente del prodotto è un'agenzia web**, non l'attività finale (barber, estetista, medico, ecc.).

- L'agenzia realizza **siti web, landing page, siti statici**, alcuni con una **sezione di prenotazione**.
- Vuole un **backend di prenotazioni centralizzato** a cui collegare, **sempre nello stesso modo**, i vari siti che produce.
- Per ogni cliente l'agenzia **rifà il front-end** e lo collega all'API (integrazione diretta).
- C'è (ad oggi) **un solo cliente**: quell'agenzia.

**Conseguenza chiave:** l'architettura **headless / API-only (AD-09) è una scelta deliberata e corretta**, non una mancanza. L'agenzia è sempre l'intermediario tecnico: costruisce lei il widget di prenotazione (lato cliente del barber) e l'eventuale pannello di gestione (lato Owner) sul sito che realizza. **Il prodotto non deve fornire una UI per il cliente finale.**

## 2. Fuori scope — deciso, NON riproporre come gap

| Voce | Perché è fuori scope |
|---|---|
| **UI di prodotto** (widget prenotazione, dashboard Owner) | La costruisce l'agenzia sul sito del cliente. Il backend espone solo API (+ le pagine tecniche set-password, deroga AD-09 circoscritta). |
| **Pagamenti / caparre interni** | Il dev è pagato **fuori dall'app**, a consegna del progetto. Il barber **non** paga tramite l'app; nessun incasso passa dal backend. |
| **Billing / abbonamenti** | Un solo cliente (l'agenzia), pagamento **esterno e differito**. Nessun bisogno di piani/quote/fatturazione in-app. |
| **Sync calendario** (Google Calendar, ecc.) | **Bocciato dall'agenzia.** |

## 3. Rimandato — buoni TODO futuri (non ora)

| Voce | Nota |
|---|---|
| **Notifiche SMS / WhatsApp** | Alto valore (riducono i no-show), ma **rimandate per costo**: il canale SMS/WhatsApp ha una spesa extra che si vuole posticipare. Oggi le notifiche sono **solo email** (outbox transazionale già pronto, basterebbe aggiungere un trasporto). |
| **Multi-utente per tenant** | I ruoli sono già nello schema `users`; manca il **flusso di invito** di altri operatori come utenti admin. |
| **Reporting / analytics** per il titolare | Prenotazioni nel tempo, no-show rate, ore di punta, ecc. Utile ma successivo. |

## 4. Backlog deciso (pre-deploy) — scelte del 2026-06-18

Tre filoni **approvati**, da realizzare **prima del deploy** (il deploy è l'ultimo passo, §5).

### 4.1 Console agenzia (UI interna) + API di provisioning/gestione

> **Backend COMPLETATO (2026-06-18).** L'API di provisioning/gestione è implementata sul branch `AutoDev` (non ancora mergiata su main).

Scelta: **console interna per l'agenzia** (non per il barber → coerente con l'approccio headless, è tooling dell'agenzia/dev). Comporta due pezzi:
- **(Backend — FATTO)** API Platform (`/api/v1/platform/*`) con identità `PlatformAdmin` separata dai tenant: creare/elencare/disattivare tenant, gestire API key, resend attivazione Owner — senza CLI né accesso DB. Setup break-glass via `POST /platform/setup` (gated da `PLATFORM_SETUP_TOKEN`). Logica provisioning unificata in `ITenantProvisioningService` (CLI + API). Migration `AddPlatformAdmin`. 4 nuovi test integration.
- **(Frontend, nuovo progetto separato — DA FARE)** l'app console che consuma quell'API.
- **Unificazione possibile** con la "dashboard interna di osservabilità" (cross-tenant: log, volumi prenotazioni, stati) già prevista → **un'unica console agenzia** provisioning + osservabilità.
- La **CLI** resta come fallback/automazione.

**Follow-up rimandati (spec §9):** invito multi-admin per tenant, reset password platform via email, attivazione primo admin platform via email, `PATCH /platform/tenants/{id}` (edit tenant), audit completo per-sorgente (attore `platform-admin:{id}` nelle righe `audit_log`).

### 4.2 Osservabilità OPS — alerting fai-da-te

> **COMPLETATO (2026-06-19).** Health check DB reale + alerting errori applicativi (incl. outbox fallita, già coperta dal digest) + canale Telegram/LogOnly implementati e testati (162 test verdi). Resta solo la **configurazione** Telegram al deploy (env, zero codice) e l'uptime monitor esterno. Canale email rimandato (non necessario).

Scelta: **alerting leggero su email/Telegram** (no APM esterno per ora). Comprende:
- [x] **Health check reale** (`DbHealthProbe`, `IDbHealthProbe`) — verifica connettività DB per la salute del deploy Railway; già esposto su `GET /api/v1/health`.
- [x] **Alert su errori applicativi** — `OpsAlertScanner` + `OpsAlertMonitorJob` (BackgroundService): scansiona la tabella `logs` ogni `PollSeconds` (default 60s), aggrega errori `>= MinLevel`, rileva transizioni DB-down/recovered; recapita via `IOpsAlertChannel`.
- [x] **Canale Telegram** (`TelegramAlertChannel`) + **canale LogOnly** (`LogOnlyAlertChannel`) — selezione via `Ops:Alerting:Channel` / `OPS_ALERT_CHANNEL`; fallback automatico a LogOnly se le credenziali Telegram mancano.
- [x] **Alert su `OutboxEmail.Status == Failed`** — **GIÀ COPERTO (2026-06-19)**: `EmailOutboxProcessor` logga un `LogError` quando un'email fallisce definitivamente (oltre `MaxAttempts`); quel log Error finisce in `logs` e viene già incluso nel digest errori dell'OPS (non ha il marcatore `[OPS-ALERT]`, quindi non è escluso). Una sorgente dedicata sarebbe ridondante (YAGNI). Verificato a 2026-06-19 (162 test verdi).
- [ ] **Canale email** — eventuale: notifiche anche via email per ambienti senza Telegram. Rimandato (Telegram è sufficiente).
- **Uptime monitor esterno** gratuito (es. UptimeRobot), zero codice — da configurare post-deploy su `GET /api/v1/health`.
- **Config Telegram al deploy** (zero codice, già implementato): impostare `OPS_ALERT_CHANNEL=Telegram`, `OPS_ALERT_TELEGRAM_BOT_TOKEN`, `OPS_ALERT_TELEGRAM_CHAT_ID` per far recapitare davvero gli alert; senza credenziali resta `LogOnly` (alert solo su console/DB).
- Si appoggia al logging su DB già esistente (`logs` + retention 90gg).

### 4.3 Compliance GDPR — DSAR + consenso arricchito (codice) + doc
Scelta: implementare lato codice **DSAR on-demand** e **consenso arricchito**, più documentazione.
- **DSAR**: endpoint admin per **export** e **cancellazione on-demand** dei dati di uno specifico cliente (diritto di accesso/oblio), oltre all'anonimizzazione automatica già presente.
- **Consenso arricchito**: oltre al booleano `gdprConsent`, registrare **timestamp** e **versione dell'informativa** (prova del consenso).
- **Documentazione** (da bozzare): elenco **sub-responsabili** (Brevo = email/EU, Railway = hosting/EU), **data-flow**, retention. Il **DPA** agenzia↔piattaforma è legale (non codice).
- Catena ruoli: **barber = titolare**, agenzia/piattaforma = **responsabile/sub-responsabile**.

## 5. Deploy — strategia

Il **deploy è l'ultimo passo**: prima si completa tutto e si testa in locale, poi si esegue il deploy su **Railway**, **poi** si ri-testa **in produzione** (incluso lo smoke test login admin per i fix JWT e la **validazione email Brevo end-to-end** con mittente verificato). Riferimenti operativi del deploy in `CLAUDE.md` (sezione "Prossimo task").
