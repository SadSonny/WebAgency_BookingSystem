# Visione di Prodotto & Roadmap

> Documento di contesto strategico. Chiarisce **per chi** è il prodotto, **cosa è dentro/fuori scope** (con il perché) e **cosa rimane da fare**. Serve a evitare che sessioni future ripropongano come "gap" cose escluse di proposito. Ultimo aggiornamento: **2026-06-18**.

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

## 4. Aperti — da discutere/approfondire

- **Onboarding self-serve (agency-facing).** Oggi il provisioning è una **CLI manuale** (JSON + connection string). Da valutare se l'agenzia vuole creare/gestire i tenant in modo programmatico (es. **API di provisioning** protetta da auth a livello agenzia) per automatizzare dal proprio tooling.
- **Osservabilità OPS.** Logging applicativo su DB già fatto (`logs` + retention). Da valutare il minimo utile per "sapere quando si rompe in produzione": health check con verifica DB, alerting su errori/outbox fallita, uptime monitoring.
- **Pacchetto legale / compliance GDPR.** Catena dei ruoli: il **barber/agenzia è titolare** del trattamento, la **piattaforma è responsabile (sub-responsabile)**. Da approfondire: DPA, elenco sub-responsabili (Brevo, Railway), informativa/consenso lato widget, export/cancellazione su richiesta (DSAR), data residency (Railway EU).

## 5. Deploy — strategia

Il **deploy è l'ultimo passo**: prima si completa tutto e si testa in locale, poi si esegue il deploy su **Railway**, **poi** si ri-testa **in produzione** (incluso lo smoke test login admin per i fix JWT e la **validazione email Brevo end-to-end** con mittente verificato). Riferimenti operativi del deploy in `CLAUDE.md` (sezione "Prossimo task").
