# Visione di Prodotto & Roadmap

> Documento di contesto strategico. Chiarisce **per chi** è il prodotto, **cosa è dentro/fuori scope** (con il perché) e **cosa rimane da fare**. Serve a evitare che sessioni future ripropongano come "gap" cose escluse di proposito. Ultimo aggiornamento: **2026-06-18** (Agency Provisioning API completata).

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

> **Backend COMPLETATO (2026-06-18).** L'API di provisioning/gestione è implementata e merge su `main`.

Scelta: **console interna per l'agenzia** (non per il barber → coerente con l'approccio headless, è tooling dell'agenzia/dev). Comporta due pezzi:
- **(Backend — FATTO)** API Platform (`/api/v1/platform/*`) con identità `PlatformAdmin` separata dai tenant: creare/elencare/disattivare tenant, gestire API key, resend attivazione Owner — senza CLI né accesso DB. Setup break-glass via `POST /platform/setup` (gated da `PLATFORM_SETUP_TOKEN`). Logica provisioning unificata in `ITenantProvisioningService` (CLI + API). Migration `AddPlatformAdmin`. 4 nuovi test integration.
- **(Frontend, nuovo progetto separato — DA FARE)** l'app console che consuma quell'API.
- **Unificazione possibile** con la "dashboard interna di osservabilità" (cross-tenant: log, volumi prenotazioni, stati) già prevista → **un'unica console agenzia** provisioning + osservabilità.
- La **CLI** resta come fallback/automazione.

**Follow-up rimandati (spec §9):** invito multi-admin per tenant, reset password platform via email, attivazione primo admin platform via email, `PATCH /platform/tenants/{id}` (edit tenant), audit completo per-sorgente (attore `platform-admin:{id}` nelle righe `audit_log`).

### 4.2 Osservabilità OPS — alerting fai-da-te
Scelta: **alerting leggero su email/Telegram** (no APM esterno per ora). Comprende:
- **Health check reale** (verifica connettività DB) per la salute del deploy Railway.
- **Alert su errori applicativi e outbox email fallita** verso email/Telegram.
- **Uptime monitor esterno** gratuito (es. UptimeRobot), zero codice.
- Si appoggia al logging su DB già esistente (`logs` + retention 90gg).

### 4.3 Compliance GDPR — DSAR + consenso arricchito (codice) + doc
Scelta: implementare lato codice **DSAR on-demand** e **consenso arricchito**, più documentazione.
- **DSAR**: endpoint admin per **export** e **cancellazione on-demand** dei dati di uno specifico cliente (diritto di accesso/oblio), oltre all'anonimizzazione automatica già presente.
- **Consenso arricchito**: oltre al booleano `gdprConsent`, registrare **timestamp** e **versione dell'informativa** (prova del consenso).
- **Documentazione** (da bozzare): elenco **sub-responsabili** (Brevo = email/EU, Railway = hosting/EU), **data-flow**, retention. Il **DPA** agenzia↔piattaforma è legale (non codice).
- Catena ruoli: **barber = titolare**, agenzia/piattaforma = **responsabile/sub-responsabile**.

## 5. Deploy — strategia

Il **deploy è l'ultimo passo**: prima si completa tutto e si testa in locale, poi si esegue il deploy su **Railway**, **poi** si ri-testa **in produzione** (incluso lo smoke test login admin per i fix JWT e la **validazione email Brevo end-to-end** con mittente verificato). Riferimenti operativi del deploy in `CLAUDE.md` (sezione "Prossimo task").
