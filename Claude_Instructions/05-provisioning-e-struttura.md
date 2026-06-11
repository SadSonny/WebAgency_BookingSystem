# 05 — Provisioning Tenant e Struttura Progetto

> Documento operativo per Claude Code. Descrive:
> 1. La struttura completa del progetto .NET da creare
> 2. Il formato del file di input per il provisioning
> 3. Il flusso del CLI tool di provisioning
> 4. Le email transazionali (template e logica di invio)

---

## PARTE 1 — STRUTTURA DEL PROGETTO

### Struttura cartelle completa

```
BookingBackend/
├── BookingBackend.sln
│
├── src/
│   ├── BookingBackend.Api/
│   │   ├── BookingBackend.Api.csproj
│   │   ├── Program.cs                    # Entry point, DI, middleware, routing
│   │   ├── Dockerfile
│   │   ├── appsettings.json
│   │   ├── appsettings.Production.json
│   │   ├── Endpoints/
│   │   │   ├── HealthEndpoints.cs
│   │   │   ├── TenantEndpoints.cs        # GET /tenant/config
│   │   │   ├── ServiceEndpoints.cs       # GET /services
│   │   │   ├── StaffEndpoints.cs         # GET /staff
│   │   │   ├── AvailabilityEndpoints.cs  # GET /availability
│   │   │   ├── BookingEndpoints.cs       # POST, GET, DELETE /bookings
│   │   │   └── AdminEndpoints.cs         # Stub 501 per rotte future
│   │   ├── Middleware/
│   │   │   ├── TenantResolutionMiddleware.cs
│   │   │   └── GlobalExceptionHandler.cs
│   │   └── DTOs/                         # Request e Response records
│   │       ├── Requests/
│   │       └── Responses/
│   │
│   ├── BookingBackend.Core/
│   │   ├── BookingBackend.Core.csproj
│   │   ├── Entities/                     # Entità di dominio (mappate da EF Core)
│   │   │   ├── Tenant.cs
│   │   │   ├── TenantApiKey.cs
│   │   │   ├── TenantBusinessHours.cs
│   │   │   ├── TenantSpecialClosure.cs
│   │   │   ├── Service.cs
│   │   │   ├── Staff.cs
│   │   │   ├── StaffService.cs
│   │   │   ├── StaffBusinessHours.cs
│   │   │   ├── Booking.cs
│   │   │   ├── AuditLog.cs
│   │   │   └── User.cs                  # Predisposizione futura
│   │   ├── Interfaces/
│   │   │   ├── IAvailabilityService.cs
│   │   │   ├── IBookingService.cs
│   │   │   ├── ITenantRepository.cs
│   │   │   ├── IBookingRepository.cs
│   │   │   ├── IServiceRepository.cs
│   │   │   ├── IStaffRepository.cs
│   │   │   └── IEmailService.cs
│   │   ├── Services/
│   │   │   ├── AvailabilityService.cs   # Logica disponibilità (vedere doc 04)
│   │   │   └── BookingService.cs        # Orchestrazione creazione/disdetta booking
│   │   └── Models/                      # Value objects, result types
│   │       ├── Result.cs                # Result<T, TError> pattern
│   │       ├── TenantContext.cs         # Tenant corrente per la request
│   │       ├── AvailabilityRequest.cs
│   │       ├── SlotAvailability.cs
│   │       └── BookingErrors.cs         # Enum errori di dominio
│   │
│   └── BookingBackend.Infrastructure/
│       ├── BookingBackend.Infrastructure.csproj
│       ├── Persistence/
│       │   ├── BookingDbContext.cs       # DbContext con global query filter
│       │   ├── Migrations/              # EF Core migrations (generate automaticamente)
│       │   └── Repositories/
│       │       ├── TenantRepository.cs
│       │       ├── BookingRepository.cs
│       │       ├── ServiceRepository.cs
│       │       └── StaffRepository.cs
│       └── Email/
│           ├── BrevoEmailService.cs     # Implementazione IEmailService via Brevo HTTP API
│           └── Templates/
│               ├── BookingConfirmationTemplate.cs
│               ├── BookingCancellationTemplate.cs
│               └── OwnerNotificationTemplate.cs
│
├── tests/
│   ├── BookingBackend.UnitTests/
│   │   ├── BookingBackend.UnitTests.csproj
│   │   └── Availability/
│   │       └── AvailabilityServiceTests.cs   # Tutti i casi del documento 04
│   │
│   └── BookingBackend.IntegrationTests/
│       ├── BookingBackend.IntegrationTests.csproj
│       ├── BookingFlowTests.cs               # Race condition, flow completo
│       └── Fixtures/
│           └── DatabaseFixture.cs            # Testcontainers PostgreSQL setup
│
└── tools/
    └── TenantProvisioning/
        ├── TenantProvisioning.csproj
        ├── Program.cs
        ├── ProvisioningModels.cs         # Classi per deserializzare il JSON input
        └── README.md                     # Guida operativa per chi crea tenant
```

---

### Dipendenze NuGet per progetto

**BookingBackend.Api.csproj**
```xml
<PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="9.*" />
<PackageReference Include="FluentValidation.AspNetCore" Version="11.*" />
<PackageReference Include="Serilog.AspNetCore" Version="8.*" />
<PackageReference Include="Serilog.Sinks.Console" Version="6.*" />
<PackageReference Include="Microsoft.AspNetCore.RateLimiting" /> <!-- built-in .NET 9 -->
```

**BookingBackend.Infrastructure.csproj**
```xml
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.*" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.*" />
```

**BookingBackend.UnitTests.csproj**
```xml
<PackageReference Include="xunit" Version="2.*" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
<PackageReference Include="NSubstitute" Version="5.*" />
<PackageReference Include="FluentAssertions" Version="6.*" />
```

**BookingBackend.IntegrationTests.csproj**
```xml
<PackageReference Include="xunit" Version="2.*" />
<PackageReference Include="Testcontainers.PostgreSql" Version="3.*" />
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.*" />
<PackageReference Include="FluentAssertions" Version="6.*" />
```

---

### Configurazione appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": ""  // Valorizzato da DATABASE_URL in Railway
  },
  "Brevo": {
    "ApiKey": "",            // Da variabile d'ambiente BREVO_API_KEY
    "SenderEmail": "noreply@dominio.it",
    "SenderName": "Sistema Prenotazioni"
  },
  "RateLimit": {
    "PermitLimit": 100,
    "WindowSeconds": 60
  },
  "Logging": {
    "MinimumLevel": "Information"
  }
}
```

---

## PARTE 2 — FORMATO FILE PROVISIONING TENANT

Il CLI tool legge un file JSON con questa struttura. Questo file viene creato dal developer/commerciale per ogni nuovo cliente.

```json
{
  "slug": "mario-barbershop",
  "name": "Mario Barbershop",
  "siteUrl": "https://mariobarbershop.it",
  "ownerEmail": "mario@mariobarbershop.it",
  "timezone": "Europe/Rome",

  "bookingRules": {
    "minAdvanceHours": 1,
    "minCancellationHours": 24,
    "visibleDaysAhead": 30,
    "bufferMinutes": 0,
    "staffChoiceEnabled": true,
    "notificationMethod": "email"
  },

  "businessHours": [
    { "dayOfWeek": 0, "isOpen": false },
    { "dayOfWeek": 1, "isOpen": true, "openTime": "09:00", "closeTime": "19:00", "breakStart": "13:00", "breakEnd": "14:00" },
    { "dayOfWeek": 2, "isOpen": true, "openTime": "09:00", "closeTime": "19:00", "breakStart": "13:00", "breakEnd": "14:00" },
    { "dayOfWeek": 3, "isOpen": true, "openTime": "09:00", "closeTime": "19:00", "breakStart": "13:00", "breakEnd": "14:00" },
    { "dayOfWeek": 4, "isOpen": true, "openTime": "09:00", "closeTime": "19:00", "breakStart": "13:00", "breakEnd": "14:00" },
    { "dayOfWeek": 5, "isOpen": true, "openTime": "09:00", "closeTime": "19:00", "breakStart": "13:00", "breakEnd": "14:00" },
    { "dayOfWeek": 6, "isOpen": false }
  ],

  "specialClosures": [
    { "dateFrom": "2024-12-25", "dateTo": "2024-12-26", "reason": "Natale" },
    { "dateFrom": "2024-08-10", "dateTo": "2024-08-20", "reason": "Ferie estive" }
  ],

  "services": [
    {
      "localId": "taglio-uomo",
      "name": "Taglio Uomo",
      "category": "Capelli",
      "description": "Taglio classico con rifinitura",
      "durationMinutes": 30,
      "basePrice": 18.00,
      "parallelSlots": 2,
      "displayOrder": 1
    },
    {
      "localId": "taglio-barba",
      "name": "Taglio e Barba",
      "category": "Capelli",
      "description": "Taglio capelli con trattamento barba",
      "durationMinutes": 45,
      "basePrice": 25.00,
      "parallelSlots": 2,
      "displayOrder": 2
    }
  ],

  "staff": [
    {
      "localId": "marco",
      "name": "Marco Rossi",
      "role": "Barbiere Senior",
      "specialization": "Tagli classici e barba",
      "photoUrl": null,
      "displayOrder": 1,
      "businessHours": [
        { "dayOfWeek": 1, "isAvailable": true, "startTime": "09:00", "endTime": "19:00" },
        { "dayOfWeek": 2, "isAvailable": true, "startTime": "09:00", "endTime": "19:00" },
        { "dayOfWeek": 3, "isAvailable": false },
        { "dayOfWeek": 4, "isAvailable": true, "startTime": "09:00", "endTime": "19:00" },
        { "dayOfWeek": 5, "isAvailable": true, "startTime": "09:00", "endTime": "19:00" },
        { "dayOfWeek": 6, "isAvailable": false }
      ],
      "services": [
        { "serviceLocalId": "taglio-uomo", "priceOverride": null },
        { "serviceLocalId": "taglio-barba", "priceOverride": 28.00 }
      ]
    },
    {
      "localId": "luigi",
      "name": "Luigi Verdi",
      "role": "Barbiere",
      "specialization": null,
      "photoUrl": null,
      "displayOrder": 2,
      "businessHours": [],
      "services": [
        { "serviceLocalId": "taglio-uomo", "priceOverride": null }
      ]
    }
  ]
}
```

**Note sul formato:**
- `localId`: ID interno al file JSON, usato per collegare staff ai servizi nel file. **Non corrisponde agli UUID del database** — il CLI genera UUID reali.
- `businessHours` dello staff: se array vuoto `[]` → usa orari del tenant. Se specificati, devono coprire tutti i giorni in cui lo staff lavora.
- `priceOverride: null` → usa il `basePrice` del servizio
- `parallelSlots`: quante prenotazioni simultanee accetta il servizio (postazioni fisiche)

---

## PARTE 3 — FLUSSO CLI PROVISIONING

### Uso

```bash
# Crea nuovo tenant
dotnet run --project tools/TenantProvisioning -- \
  --input ./clienti/mario-barbershop.json \
  --connection "Host=localhost;Database=booking;Username=postgres;Password=xxx"

# Aggiorna tenant esistente (per aggiornare orari, servizi, ecc.)
dotnet run --project tools/TenantProvisioning -- \
  --input ./clienti/mario-barbershop.json \
  --connection "..." \
  --update
```

### Output atteso

```
=== PROVISIONING TENANT ===
Slug: mario-barbershop
Modalità: CREA NUOVO

✓ Validazione input completata
✓ Tenant creato (id: a1b2c3d4-...)
✓ Orari settimanali configurati (6 giorni aperti)
✓ Chiusure straordinarie inserite (2)
✓ Servizi creati: Taglio Uomo (id: ...), Taglio e Barba (id: ...)
✓ Staff creato: Marco Rossi (id: ...), Luigi Verdi (id: ...)
✓ Associazioni staff-servizi configurate
✓ API key generata

=== API KEY (mostrare UNA SOLA VOLTA) ===
Chiave: 8f3a2b1c-4d5e-6f7a-8b9c-0d1e2f3a4b5c
Prefisso: 8f3a2b1c

Da inserire nel frontend come: VITE_BOOKING_API_KEY=8f3a2b1c-4d5e-6f7a-8b9c-0d1e2f3a4b5c
Da inserire nel frontend come: VITE_BOOKING_API_URL=https://booking-backend.railway.app

⚠️  Salva questa chiave: non sarà più visibile.

=== PROVISIONING COMPLETATO ===
```

### Logica interna del CLI

```
1. Leggi e valida il file JSON (schema validation)
2. Apri connessione DB
3. BEGIN TRANSACTION
4. IF --update mode:
     carica tenant esistente per slug
     aggiorna i campi modificabili
   ELSE:
     verifica che slug non esista già
     INSERT tenant
5. Elimina e ricrea business hours (più semplice di un diff)
6. Merge special closures (aggiungi nuove, mantieni esistenti non nel file)
7. Per ogni servizio nel file:
     IF esiste già per nome → aggiorna
     ELSE → INSERT (genera UUID)
     Mappa localId → UUID reale
8. Per ogni staff nel file:
     IF esiste già per nome → aggiorna
     ELSE → INSERT (genera UUID)
     Mappa localId → UUID reale
9. Elimina e ricrea staff_business_hours
10. Elimina e ricrea staff_services (con price_override)
11. IF NOT --update:
      genera UUID come API key
      calcola SHA-256 hash
      INSERT tenant_api_keys
      salva chiave in chiaro per output finale
12. INSERT audit_log (action='tenant_created' o 'tenant_updated')
13. COMMIT
14. Stampa output con API key (solo se nuova)
```

---

## PARTE 4 — EMAIL TRANSAZIONALI (Brevo)

### Chiamata API Brevo

```csharp
// POST https://api.brevo.com/v3/smtp/email
// Authorization: api-key {BREVO_API_KEY}

{
  "sender": { "name": "Mario Barbershop", "email": "noreply@booking.it" },
  "to": [{ "email": "cliente@email.com", "name": "Giovanni Bianchi" }],
  "subject": "Conferma prenotazione — Mario Barbershop",
  "htmlContent": "..."  // HTML generato dal template C#
}
```

### Template 1: Conferma prenotazione (al cliente)

**Oggetto:** `Conferma prenotazione — {tenant.Name}`

**Contenuto HTML (struttura):**
```
Logo/Nome attività

Ciao {customer.Name},

La tua prenotazione è confermata!

┌─────────────────────────────┐
│ Servizio:   Taglio Uomo     │
│ Data:       Venerdì 15 marzo │
│ Orario:     10:30           │
│ Con:        Marco Rossi     │  ← solo se staff specificato
│ Presso:     Mario Barbershop│
└─────────────────────────────┘

Per disdire o visualizzare la tua prenotazione:
[GESTISCI PRENOTAZIONE] → link a {siteUrl}/conferma?id={bookingId}&token={cancellationToken}

Puoi disdire fino a {minCancellationHours} ore prima dell'appuntamento.

— Il team di Mario Barbershop
```

### Template 2: Notifica nuova prenotazione (al titolare)

**Oggetto:** `Nuova prenotazione — {date} {time}`

**Contenuto HTML:**
```
Nuova prenotazione ricevuta

Servizio:    Taglio Uomo (30 min)
Data:        Venerdì 15 marzo alle 10:30
Staff:       Marco Rossi

Cliente:
  Nome:      Giovanni Bianchi
  Telefono:  +39 333 1234567
  Email:     giovanni@email.com
  Note:      Prima visita

[Visualizza nel pannello] → link futuro (per ora omettere o linkare al sito)
```

### Template 3: Conferma disdetta (al cliente)

**Oggetto:** `Prenotazione disdetta — {tenant.Name}`

**Contenuto HTML:**
```
Ciao {customer.Name},

La tua prenotazione è stata disdetta con successo.

Servizio:  Taglio Uomo
Data:      Venerdì 15 marzo alle 10:30

Se vuoi prenotare di nuovo:
[PRENOTA ORA] → link a {siteUrl}

— Il team di Mario Barbershop
```

### Invio asincrono

Le email vengono inviate in modo **fire-and-forget** dopo il commit della transazione. Un fallimento nell'invio email non deve causare rollback della prenotazione.

```csharp
// Dopo il commit:
_ = Task.Run(async () =>
{
    try
    {
        await _emailService.SendBookingConfirmationAsync(booking, tenant);
        if (tenant.NotificationMethod == "email")
            await _emailService.SendOwnerNotificationAsync(booking, tenant);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Errore invio email per booking {BookingId}", booking.Id);
        // Non rilanciare — la prenotazione è già confermata
    }
}, CancellationToken.None);
```

---

## PARTE 5 — GUIDA OPERATIVA (per chi crea i tenant)

> Questo contenuto andrà nel file `tools/TenantProvisioning/README.md`

### Prerequisiti
- .NET 9 SDK installato
- Accesso alla connection string del database di produzione (Railway)
- File JSON del cliente preparato (template: `tools/TenantProvisioning/template-cliente.json`)

### Step operativi per un nuovo cliente

1. **Copia il template** e compilalo con i dati del cliente:
   ```bash
   cp tools/TenantProvisioning/template-cliente.json clienti/nome-cliente.json
   ```

2. **Compila tutti i campi** del JSON (vedere Parte 2 di questo documento)

3. **Esegui il provisioning** puntando al DB di produzione:
   ```bash
   dotnet run --project tools/TenantProvisioning -- \
     --input clienti/nome-cliente.json \
     --connection "Host=...railway...;Database=booking;Username=postgres;Password=..."
   ```

4. **Copia l'API key** mostrata nell'output (apparirà UNA SOLA VOLTA)

5. **Comunicare al developer frontend:**
   ```
   VITE_BOOKING_API_URL=https://booking-backend.railway.app
   VITE_BOOKING_API_KEY={api_key_copiata}
   ```

6. **Archivia il file JSON** del cliente (es. in cartella privata condivisa) per riferimento futuro

### Aggiornare un tenant esistente
Se cambiano orari, servizi o staff di un cliente già attivo:
1. Modifica il file JSON del cliente
2. Riesegui con flag `--update`
3. L'API key NON cambia (a meno di revocarla manualmente)
