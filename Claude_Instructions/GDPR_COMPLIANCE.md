# GDPR — Compliance, ruoli e DSAR

> Documento di riferimento sulla conformità GDPR del backend. Spiega **chi tratta cosa**, **dove vivono i dati
> personali**, le **regole di retention** e gli strumenti **DSAR on-demand** (diritto d'accesso e all'oblio).
> Aggiornato: **2026-06-19** (filone 4.3). Il DPA tra le parti è un documento legale, fuori da questo repo.

## 1. Catena dei ruoli

| Ruolo GDPR | Soggetto | Cosa fa |
|---|---|---|
| **Titolare del trattamento** | Il **barber / attività** (tenant) | Decide finalità e mezzi del trattamento dei dati dei propri clienti finali. |
| **Responsabile del trattamento** | L'**agenzia web** | Tratta i dati per conto del titolare; realizza il sito e lo collega al backend. |
| **Sub-responsabile** | La **piattaforma / dev** (questo backend) | Fornisce il software e l'hosting per conto dell'agenzia/titolare. |

Il consenso del cliente finale è raccolto dal sito (costruito dall'agenzia) e registrato dal backend su ogni
prenotazione. Gli accordi DPA barber↔agenzia↔piattaforma sono documenti legali esterni al codice.

## 2. Sub-responsabili (processori esterni)

| Processore | Trattamento | Region |
|---|---|---|
| **Brevo** | Invio delle email transazionali (conferme, promemoria, attivazione account) | UE |
| **Railway** | Hosting dell'applicazione e del database PostgreSQL | UE (EU West) |

Entrambi trattano dati personali per conto del titolare. Nessun altro servizio esterno riceve PII dei clienti.

## 3. Dati personali trattati e dove vivono

| Tabella | Contenuto | PII |
|---|---|---|
| `bookings` | Prenotazioni: nome, telefono, email, note del cliente; estremi del consenso | **Sì** (dati identificativi e di contatto) |
| `outbox_emails` | Coda email transazionali: l'HTML/testo congelato contiene nome ed email | **Sì** (copia effimera) |
| `audit_log` | Eventi di business (creazione/disdetta/DSAR) | **No — PII-free**: solo IP **anonimizzato** (ultimo ottetto rimosso) e `subjectRef` = **HMAC** dell'email; mai email/nome in chiaro |
| `logs` | Log applicativi (Serilog) | **No — PII-free**: nessun IP loggato, parametri SQL mascherati |

## 4. Retention, anonimizzazione e DSAR

### Retention automatica
- **Prenotazioni**: anonimizzate (nome→`[rimosso]`, telefono/email→vuoto, note→null; la riga resta per le statistiche) oltre `Gdpr:RetentionDays` (default **365 giorni**) — `DataRetentionService`/`DataRetentionJob`.
- **Outbox email**: le email inviate sono purgate oltre `Gdpr:OutboxRetentionDays` (default **30 giorni**).
- **Logs applicativi**: purgati oltre `DatabaseLogging:RetentionDays` (default **90 giorni**) — `LogRetentionJob`.

### DSAR on-demand (admin, JWT)
Per rispondere a una richiesta del cliente **prima** della retention automatica:

| Operazione | Endpoint | Comportamento |
|---|---|---|
| **Diritto d'accesso** | `GET /api/v1/admin/gdpr/customer?email=<email>` | Esporta tutte le prenotazioni del cliente (PII + consenso) del tenant corrente. Riesce **sempre** (lista vuota se non ci sono dati). |
| **Diritto all'oblio** | `POST /api/v1/admin/gdpr/customer/erase` body `{ "email": "<email>" }` | **Anonimizza** le prenotazioni **ed elimina** le email outbox del cliente, subito e in modo atomico. `404` se non c'è nulla; `422` se email mancante/invalida. |

Entrambe le operazioni sono **tenant-scoped** (un tenant non può accedere ai dati di un altro) e scrivono una riga
`audit_log` PII-free (`customer_data_exported` / `customer_data_erased`) con `subjectRef` HMAC per correlare gli
eventi sullo stesso soggetto senza esporre l'email.

## 5. Consenso

Ogni prenotazione registra la prova del consenso:
- `GdprConsent` (bool) — consenso fornito;
- `GdprConsentAt` (timestamp UTC) — **quando**;
- `GdprConsentVersion` (stringa opaca, opzionale) — **quale** versione dell'informativa è stata accettata, inviata dal sito al momento della prenotazione.

Il backend memorizza la versione e non la interpreta: la gestione del testo dell'informativa resta al titolare/agenzia.
