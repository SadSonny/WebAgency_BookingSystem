# 02 — Schema Database

> Documento vincolante per Claude Code. Definisce lo schema PostgreSQL completo,
> le relazioni, gli indici e le note per Entity Framework Core.
> Ogni modifica a questo schema va coordinata con `03-spec-endpoint.md`.

---

## PANORAMICA TABELLE

```
tenants
├── tenant_api_keys
├── tenant_business_hours        (orari settimanali)
├── tenant_special_closures      (chiusure straordinarie — range date)
├── services
│   └── staff_services           (associazione M:M con price_override)
├── staff
│   ├── staff_services           (associazione M:M)
│   └── staff_business_hours     (orari settimanali staff)
├── bookings
└── audit_log
users                            [PREDISPOSIZIONE FUTURA — admin]
```

---

## SCHEMA SQL COMPLETO

### tenants

```sql
CREATE TABLE tenants (
    id                          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    slug                        VARCHAR(100) NOT NULL UNIQUE,  -- es. "mario-barbershop"
    name                        VARCHAR(255) NOT NULL,          -- nome attività
    site_url                    VARCHAR(500) NOT NULL,          -- es. "https://mariobarbershop.it"
    owner_email                 VARCHAR(255) NOT NULL,          -- notifiche al titolare
    timezone                    VARCHAR(100) NOT NULL DEFAULT 'Europe/Rome',
    
    -- Regole prenotazione
    min_advance_hours           INTEGER NOT NULL DEFAULT 1,     -- anticipo minimo (ore)
    min_cancellation_hours      INTEGER NOT NULL DEFAULT 24,    -- preavviso disdetta (ore)
    visible_days_ahead          INTEGER NOT NULL DEFAULT 30,    -- giorni visibili in anticipo
    buffer_minutes              INTEGER NOT NULL DEFAULT 0,     -- buffer tra appuntamenti
    
    -- Configurazione
    staff_choice_enabled        BOOLEAN NOT NULL DEFAULT TRUE,  -- cliente può scegliere staff
    notification_method         VARCHAR(50) NOT NULL DEFAULT 'email', -- 'email' | 'none'
    
    -- Metadata
    active                      BOOLEAN NOT NULL DEFAULT TRUE,
    created_at                  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at                  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_tenants_slug ON tenants(slug);
```

**Note EF Core:** `TenantId` è la radice dell'aggregato. Il `DbContext` non applica global query filter su questa tabella (è la tabella di risoluzione del tenant).

---

### tenant_api_keys

```sql
CREATE TABLE tenant_api_keys (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    key_hash        VARCHAR(255) NOT NULL UNIQUE,  -- SHA-256 della chiave in chiaro
    key_prefix      VARCHAR(8) NOT NULL,            -- prime 8 char per identificazione (es. "a3f7b2c1")
    description     VARCHAR(255),                   -- es. "Chiave sito produzione"
    active          BOOLEAN NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    revoked_at      TIMESTAMPTZ NULL
);

CREATE INDEX idx_tenant_api_keys_hash ON tenant_api_keys(key_hash) WHERE active = TRUE;
CREATE INDEX idx_tenant_api_keys_tenant ON tenant_api_keys(tenant_id);
```

**Sicurezza:** la chiave in chiaro viene mostrata UNA SOLA VOLTA al provisioning e mai più. Nel DB si conserva solo l'hash SHA-256. La risoluzione `API key → tenant_id` avviene hashando l'header ricevuto e cercando in questa tabella.

---

### tenant_business_hours

```sql
CREATE TABLE tenant_business_hours (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    day_of_week     SMALLINT NOT NULL CHECK (day_of_week BETWEEN 0 AND 6), -- 0=Dom, 1=Lun, ..., 6=Sab
    is_open         BOOLEAN NOT NULL DEFAULT TRUE,
    open_time       TIME NULL,        -- NULL se is_open = false
    close_time      TIME NULL,        -- NULL se is_open = false
    break_start     TIME NULL,        -- NULL se nessuna pausa
    break_end       TIME NULL,        -- NULL se nessuna pausa
    
    CONSTRAINT uq_tenant_day UNIQUE (tenant_id, day_of_week),
    CONSTRAINT chk_times CHECK (
        (is_open = FALSE) OR
        (open_time IS NOT NULL AND close_time IS NOT NULL AND open_time < close_time)
    ),
    CONSTRAINT chk_break CHECK (
        (break_start IS NULL AND break_end IS NULL) OR
        (break_start IS NOT NULL AND break_end IS NOT NULL AND break_start < break_end)
    )
);

CREATE INDEX idx_tenant_business_hours_tenant ON tenant_business_hours(tenant_id);
```

---

### tenant_special_closures

```sql
CREATE TABLE tenant_special_closures (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    date_from       DATE NOT NULL,
    date_to         DATE NOT NULL,    -- uguale a date_from per chiusura singolo giorno
    reason          VARCHAR(255) NULL, -- es. "Ferie agosto", "Natale"
    
    CONSTRAINT chk_date_range CHECK (date_from <= date_to)
);

CREATE INDEX idx_special_closures_tenant ON tenant_special_closures(tenant_id);
CREATE INDEX idx_special_closures_dates ON tenant_special_closures(tenant_id, date_from, date_to);
```

---

### services

```sql
CREATE TABLE services (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    name                VARCHAR(255) NOT NULL,
    category            VARCHAR(100) NULL,          -- es. "Capelli", "Barba", "Trattamenti"
    description         TEXT NULL,
    duration_minutes    INTEGER NOT NULL CHECK (duration_minutes > 0),
    base_price          DECIMAL(10,2) NULL,         -- prezzo base (può essere sovrascritto per staff)
    parallel_slots      INTEGER NOT NULL DEFAULT 1 CHECK (parallel_slots > 0), -- postazioni parallele
    active              BOOLEAN NOT NULL DEFAULT TRUE,
    display_order       INTEGER NOT NULL DEFAULT 0,  -- ordine di visualizzazione nel frontend
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_services_tenant ON services(tenant_id);
CREATE INDEX idx_services_tenant_active ON services(tenant_id, active);
```

**Nota `parallel_slots`:** rappresenta quante prenotazioni simultanee sono accettabili per questo servizio (postazioni fisiche dedicate). Es: 2 = due cabine massaggio → due clienti possono prenotare lo stesso slot.

---

### staff

```sql
CREATE TABLE staff (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    name                VARCHAR(255) NOT NULL,
    role                VARCHAR(100) NULL,           -- es. "Barbiere Senior", "Estetista"
    specialization      VARCHAR(255) NULL,
    photo_url           VARCHAR(500) NULL,
    active              BOOLEAN NOT NULL DEFAULT TRUE,
    display_order       INTEGER NOT NULL DEFAULT 0,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_staff_tenant ON staff(tenant_id);
CREATE INDEX idx_staff_tenant_active ON staff(tenant_id, active);
```

---

### staff_services

Tabella di associazione M:M tra staff e servizi, con eventuale override di prezzo.

```sql
CREATE TABLE staff_services (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    staff_id        UUID NOT NULL REFERENCES staff(id) ON DELETE CASCADE,
    service_id      UUID NOT NULL REFERENCES services(id) ON DELETE CASCADE,
    tenant_id       UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE, -- denormalizzato per query filter
    price_override  DECIMAL(10,2) NULL,  -- NULL = usa base_price del servizio
    
    CONSTRAINT uq_staff_service UNIQUE (staff_id, service_id)
);

CREATE INDEX idx_staff_services_staff ON staff_services(staff_id);
CREATE INDEX idx_staff_services_service ON staff_services(service_id);
CREATE INDEX idx_staff_services_tenant ON staff_services(tenant_id);
```

**Nota `price_override`:** se non NULL, questo prezzo sovrascrive `services.base_price` per questo specifico membro dello staff. Usato nell'endpoint `GET /api/v1/services` quando si filtra per staff, e nella risposta di `GET /api/v1/availability`.

---

### staff_business_hours

Orari settimanali del singolo membro dello staff. Se uno staff non ha righe in questa tabella, si usano gli orari del tenant.

```sql
CREATE TABLE staff_business_hours (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    staff_id        UUID NOT NULL REFERENCES staff(id) ON DELETE CASCADE,
    tenant_id       UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE, -- denormalizzato
    day_of_week     SMALLINT NOT NULL CHECK (day_of_week BETWEEN 0 AND 6),
    is_available    BOOLEAN NOT NULL DEFAULT TRUE,  -- false = non lavora quel giorno
    start_time      TIME NULL,   -- NULL se is_available = false
    end_time        TIME NULL,   -- NULL se is_available = false
    break_start     TIME NULL,
    break_end       TIME NULL,
    
    CONSTRAINT uq_staff_day UNIQUE (staff_id, day_of_week),
    CONSTRAINT chk_staff_times CHECK (
        (is_available = FALSE) OR
        (start_time IS NOT NULL AND end_time IS NOT NULL AND start_time < end_time)
    )
);

CREATE INDEX idx_staff_hours_staff ON staff_business_hours(staff_id);
CREATE INDEX idx_staff_hours_tenant ON staff_business_hours(tenant_id);
```

---

### bookings

```sql
CREATE TABLE bookings (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id               UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    service_id              UUID NOT NULL REFERENCES services(id),
    staff_id                UUID NULL REFERENCES staff(id),  -- NULL = nessuno staff specifico
    
    -- Data e ora (locali del tenant — NON UTC)
    booking_date            DATE NOT NULL,
    booking_time            TIME NOT NULL,
    duration_minutes        INTEGER NOT NULL,  -- snapshot al momento della prenotazione
    
    -- Cliente finale
    customer_name           VARCHAR(255) NOT NULL,
    customer_phone          VARCHAR(50) NOT NULL,
    customer_email          VARCHAR(255) NOT NULL,
    customer_notes          TEXT NULL,
    
    -- GDPR
    gdpr_consent            BOOLEAN NOT NULL DEFAULT TRUE,
    gdpr_consent_at         TIMESTAMPTZ NOT NULL,
    
    -- Gestione prenotazione
    status                  VARCHAR(50) NOT NULL DEFAULT 'confirmed',
    -- valori: 'confirmed' | 'cancelled' | 'no_show' | 'completed'
    cancellation_token      UUID NOT NULL DEFAULT gen_random_uuid(),
    cancelled_at            TIMESTAMPTZ NULL,
    cancellation_reason     VARCHAR(255) NULL,  -- 'customer' | 'owner' | 'system'
    no_show_marked_at       TIMESTAMPTZ NULL,
    
    -- Prezzo snapshot
    price_at_booking        DECIMAL(10,2) NULL,  -- snapshot del prezzo al momento della prenotazione
    
    -- Metadata
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_bookings_tenant ON bookings(tenant_id);
CREATE INDEX idx_bookings_tenant_date ON bookings(tenant_id, booking_date);
CREATE INDEX idx_bookings_cancellation_token ON bookings(cancellation_token);
CREATE INDEX idx_bookings_tenant_status ON bookings(tenant_id, status);
CREATE INDEX idx_bookings_staff_date ON bookings(staff_id, booking_date) WHERE staff_id IS NOT NULL;

-- Indice per la query di disponibilità (overlap check)
CREATE INDEX idx_bookings_availability ON bookings(tenant_id, service_id, booking_date, status)
    WHERE status = 'confirmed';
```

**Note importanti:**
- `booking_date` e `booking_time` sono **orari locali del tenant** (come da contratto API)
- `duration_minutes` è uno snapshot: se la durata del servizio cambia in futuro, le prenotazioni esistenti mantengono la durata originale
- `price_at_booking` è uno snapshot per lo stesso motivo
- `cancellation_token` è UUID generato automaticamente — non è la PK

---

### audit_log

```sql
CREATE TABLE audit_log (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    booking_id      UUID NULL,          -- NULL per azioni non legate a una prenotazione
    action          VARCHAR(100) NOT NULL,
    -- valori: 'booking_created' | 'booking_cancelled_by_customer' |
    --         'booking_cancelled_by_owner' | 'booking_no_show' | 'tenant_created'
    actor           VARCHAR(100) NOT NULL,
    -- valori: 'customer' | 'owner' | 'system' | 'provisioning'
    ip_anonymized   VARCHAR(50) NULL,   -- es. "192.168.1.xxx" — ultimo ottetto rimosso
    metadata        JSONB NULL,         -- dati aggiuntivi non strutturati
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_audit_tenant ON audit_log(tenant_id);
CREATE INDEX idx_audit_booking ON audit_log(booking_id) WHERE booking_id IS NOT NULL;
CREATE INDEX idx_audit_created ON audit_log(tenant_id, created_at DESC);
```

---

### users [PREDISPOSIZIONE FUTURA]

```sql
CREATE TABLE users (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    email           VARCHAR(255) NOT NULL,
    password_hash   VARCHAR(255) NOT NULL,
    role            VARCHAR(50) NOT NULL DEFAULT 'owner',  -- 'owner' | 'admin'
    active          BOOLEAN NOT NULL DEFAULT TRUE,
    last_login_at   TIMESTAMPTZ NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    CONSTRAINT uq_user_email_tenant UNIQUE (tenant_id, email)
);

CREATE INDEX idx_users_tenant ON users(tenant_id);
CREATE INDEX idx_users_email ON users(email);
```

**Nota:** questa tabella è creata ora nello schema ma non viene utilizzata. Nessun endpoint admin è implementato. La tabella serve per non dover fare ALTER TABLE in futuro.

---

## MIGRAZIONI EF CORE

- Usare **EF Core Migrations** (non script SQL raw)
- Prima migrazione: `InitialSchema` — crea tutte le tabelle sopra
- Eseguire `dotnet ef database update` al deploy (o all'avvio dell'applicazione in ambiente non-Production)
- In Production: le migrazioni vengono applicate tramite script separato, non all'avvio automatico

### Configurazione DbContext

```csharp
// Esempio di configurazione global query filter nel DbContext
public class BookingDbContext : DbContext
{
    private readonly Guid _tenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Global query filter su tutte le entità tenant-scoped
        modelBuilder.Entity<Service>().HasQueryFilter(s => s.TenantId == _tenantId);
        modelBuilder.Entity<Staff>().HasQueryFilter(s => s.TenantId == _tenantId);
        modelBuilder.Entity<Booking>().HasQueryFilter(b => b.TenantId == _tenantId);
        // ... ecc.
    }
}
```

---

## INDICI — RIEPILOGO QUERY CRITICHE

| Query | Indice utilizzato |
|---|---|
| Risoluzione API key → tenant | `idx_tenant_api_keys_hash` |
| Slot disponibili per data+servizio | `idx_bookings_availability` |
| Slot disponibili per data+staff | `idx_bookings_staff_date` |
| Prenotazione per cancellation token | `idx_bookings_cancellation_token` |
| Lista prenotazioni per tenant+data | `idx_bookings_tenant_date` |
