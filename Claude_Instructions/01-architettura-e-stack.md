# 01 — Architettura e Stack Tecnologico

> Documento vincolante per Claude Code. Tutte le decisioni qui sono DECISE e non
> vanno rimesse in discussione durante lo sviluppo. Le sezioni marcate
> [PREDISPOSIZIONE FUTURA] indicano scelte fatte ora per abilitare funzionalità
> future senza riscrivere.

---

## 1. STACK TECNOLOGICO

| Layer | Tecnologia | Versione |
|---|---|---|
| Runtime | .NET | 9 |
| Framework | ASP.NET Core Minimal API | 9 |
| ORM | Entity Framework Core | 9 |
| Driver DB | Npgsql.EntityFrameworkCore.PostgreSQL | 9.x |
| Validazione | FluentValidation | latest stable |
| Email | Brevo (ex Sendinblue) HTTP API | v3 |
| Test unitari | xUnit + NSubstitute | latest stable |
| Test integrazione | xUnit + Testcontainers | latest stable |
| Linguaggio | C# | 13 |

### Motivazioni Minimal API vs Controller
Si usa **Minimal API** perché:
- Progetto con numero di endpoint contenuto e contratto API già definito
- Meno boilerplate, più leggibile per Claude Code
- Routing esplicito e dichiarativo vicino alla definizione degli endpoint
- Performance leggermente superiore

### Struttura Progetti (Solution)

```
BookingBackend.sln
├── src/
│   ├── BookingBackend.Api/          # Progetto principale ASP.NET Core
│   ├── BookingBackend.Core/         # Dominio, entità, interfacce, logica disponibilità
│   └── BookingBackend.Infrastructure/  # EF Core, repository, email, provider esterni
├── tests/
│   ├── BookingBackend.UnitTests/    # xUnit — logica disponibilità
│   └── BookingBackend.IntegrationTests/ # xUnit + Testcontainers — booking atomico
└── tools/
    └── TenantProvisioning/          # CLI tool per creazione tenant
```

### Convenzioni di codice
- **Namespace** corrispondono alla struttura cartelle
- **Record** C# per DTO request/response (immutabilità)
- **Result pattern** per operazioni che possono fallire (no eccezioni per flow control)
- **Async/await** ovunque per operazioni I/O
- **Cancellation token** propagato su tutti gli handler
- Naming: `PascalCase` per tipi/metodi, `camelCase` per variabili locali
- Niente `var` implicito quando il tipo non è ovvio dalla destra dell'assegnazione

---

## 2. DATABASE

**PostgreSQL** (versione 16+) — database condiviso multi-tenant con isolamento logico per `tenant_id`.

### Principi
- Ogni tabella che contiene dati di tenant ha colonna `tenant_id UUID NOT NULL`
- Indice su `tenant_id` su tutte le tabelle tenant-scoped
- Nessun Row-Level Security PostgreSQL per ora (isolamento a livello applicativo via EF Core — `TenantDbContext` con filtro globale su `tenant_id`)
- Tutte le chiavi primarie: `UUID` (non autoincrement — evita enumerazione e semplifica distribuzione futura)
- Timestamp sempre `TIMESTAMPTZ` (UTC nel DB, conversione nel dominio)
- Soft delete dove indicato (campo `deleted_at TIMESTAMPTZ NULL`)

### Global Query Filter EF Core
Il `DbContext` applica automaticamente `.Where(x => x.TenantId == _currentTenantId)` su tutte le entità tenant-scoped tramite `HasQueryFilter`. Questo previene data leak cross-tenant per dimenticanza.

---

## 3. HOSTING — Railway

**Provider:** Railway (railway.app)
**Regione:** Europa (EU West)

### Servizi su Railway
| Servizio | Tipo Railway | Note |
|---|---|---|
| Backend .NET | Web Service (Docker) | Deploy da GitHub, Dockerfile nel repo |
| PostgreSQL | Railway Plugin PostgreSQL | Managed, backup automatici |

### Variabili d'ambiente (Railway Environment)
```
DATABASE_URL          # Fornita automaticamente da Railway plugin PostgreSQL
BREVO_API_KEY         # API key Brevo per email transazionali
INTERNAL_PROVISIONING_SECRET  # Secret per CLI provisioning tenant
ASPNETCORE_ENVIRONMENT        # Production
```

### Dockerfile (da creare nel progetto)
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish src/BookingBackend.Api/BookingBackend.Api.csproj \
    -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BookingBackend.Api.dll"]
```

Railway legge la porta dalla variabile `PORT` — configurare Kestrel per ascoltare su `$PORT`.

---

## 4. EMAIL TRANSAZIONALE — Brevo

**Provider:** Brevo (brevo.com)
**Piano iniziale:** Free (300 email/giorno) — sufficiente per la fase iniziale

### Utilizzo
- Email di conferma prenotazione → cliente finale
- Notifica nuova prenotazione → titolare attività
- Email di conferma disdetta → cliente finale (da valutare)
- Lingua: **italiano** per tutti i template

### Integrazione
Chiamate dirette all'API REST Brevo v3 (`https://api.brevo.com/v3/smtp/email`) tramite `HttpClient` con `IHttpClientFactory`. Nessun SDK Brevo per .NET — l'API REST è semplice e stabile.

### Template email
I template sono definiti come classi C# che generano HTML. Non si usano template Brevo (mantiene il controllo nel codice, versionabile su Git).

---

## 5. AUTENTICAZIONE

### Livello 1 — API Key pubblica (implementato ora)
- Header: `X-API-Key`
- Il backend risolve `API key → tenant_id` da tabella `tenant_api_keys`
- Le chiavi sono revocabili e rigenerabili per singolo tenant
- Rate limiting: **100 request/minuto per API key** (configurabile)
- Implementazione: middleware `TenantResolutionMiddleware` eseguito prima di ogni endpoint pubblico

### Livello 2 — JWT proprietario [PREDISPOSIZIONE FUTURA]
- Tabella `users` presente nello schema DB ora
- Routing `/api/v1/admin/*` registrato ma con risposta `501 Not Implemented`
- Endpoint `POST /api/v1/auth/login` registrato ma non implementato
- Quando implementato: JWT con claims `tenant_id`, `user_id`, `role`

---

## 6. RATE LIMITING

Implementazione con `Microsoft.AspNetCore.RateLimiting` (built-in .NET 7+):
- Policy: sliding window, 100 request/60 secondi per API key
- Risposta su limite superato: `429 Too Many Requests` con header `Retry-After`
- Applicato solo agli endpoint pubblici autenticati con API key

---

## 7. PROVISIONING TENANT — CLI Tool

**Strumento:** applicazione console .NET nella cartella `tools/TenantProvisioning/`

### Funzionamento
```bash
dotnet run --project tools/TenantProvisioning -- \
  --input cliente-mario-barbershop.json \
  --env production
```

Il tool:
1. Legge un file JSON con tutti i dati del tenant (schema in documento `05-provisioning.md`)
2. Si connette al DB (stessa connection string del backend)
3. Crea in una singola transazione: tenant, API key, orari, chiusure, staff, orari staff, servizi, associazioni staff-servizi
4. Stampa a console la API key generata (da comunicare al developer frontend)
5. In caso di errore: rollback completo

### Sicurezza
- Il tool gira in locale o in CI/CD — non è esposto via HTTP
- La API key generata è un UUID v4 senza prefissi speciali
- Il tool è idempotente: se il tenant esiste già (per slug), aggiorna invece di creare (con flag `--update`)

---

## 8. LOGGING E AUDIT

- **Logging strutturato:** Serilog con sink Console (Railway cattura stdout)
- **Livelli:** Information per operazioni normali, Warning per 409/rate limit, Error per eccezioni
- **Audit log:** tabella `audit_log` per creazioni e disdette prenotazione (tenant_id, booking_id, action, timestamp, IP anonimizzato)
- **Non loggare mai:** dati personali nei log (nome, email, telefono cliente)

---

## 9. GDPR

- `gdpr_consent` e `gdpr_consent_at` persistiti sulla prenotazione
- IP del cliente NON persistito (solo anonimizzato nei log)
- Procedura di cancellazione/anonimizzazione: metodo su repository `BookingRepository.AnonymizeCustomerData(bookingId)` — sostituisce nome/email/telefono con placeholder
- Server in UE (Railway EU West)
- [PREDISPOSIZIONE FUTURA] Endpoint admin per anonimizzazione su richiesta
