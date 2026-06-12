# TenantProvisioning — CLI di provisioning tenant

Crea un nuovo tenant (attività) sul database a partire da un file JSON: tenant, regole di prenotazione,
orari settimanali, chiusure straordinarie, servizi, staff e associazioni staff↔servizi. Genera inoltre
l'**API key** pubblica e l'**utente admin Owner**, mostrandone i segreti **una sola volta**.

## Prerequisiti
- .NET 10 SDK
- Database PostgreSQL raggiungibile, con lo **schema già applicato** (`dotnet ef database update`)
- File JSON del cliente (vedi `samples/barbershop-demo.json` come template)

## Uso

```bash
dotnet run --project tools/WebAgency_BookingSystem.TenantProvisioning -- \
  --input samples/barbershop-demo.json \
  --connection "Host=localhost;Port=5432;Database=bookingsystem;Username=postgres;Password=postgres"
```

In alternativa alla `--connection` si può impostare la variabile d'ambiente `DATABASE_URL`.
Il flag `--file` è un alias di `--input`.

### Codici di uscita
- `0` — provisioning completato
- `1` — errore di runtime (DB irraggiungibile, slug già esistente, ecc.)
- `2` — errore di input (argomenti mancanti, file non trovato, JSON o validazione non validi)

## Formato del file JSON
Vedi `samples/barbershop-demo.json` e la spec `Claude_Instructions/05-provisioning-e-struttura.md` (Parte 2).
Note principali:
- `localId` / `serviceLocalId`: identificatori **interni al file**, usati per collegare staff e servizi.
  Il CLI genera gli UUID reali nel DB.
- `staff.businessHours` vuoto `[]` → lo staff usa gli orari del tenant.
- `priceOverride: null` → usa il `basePrice` del servizio.
- Buffer **per servizio** (AD-03): campi opzionali `bufferEnabled`, `bufferMinutes`, `bufferPosition`
  (`Before` | `After` | `Both`); di default disattivato. Il `bufferMinutes` a livello tenant non è usato.

## Output
Al termine stampa l'**API key** (formato `bk_live_...`) e le **credenziali admin** (email + password generata).
Questi valori **non sono recuperabili** in seguito (nel DB si salva solo l'hash): vanno salvati subito.

```
VITE_BOOKING_API_KEY=bk_live_xxxxxxxx...
VITE_BOOKING_API_URL=<url-del-backend>
```

## Limitazioni V1
- Solo modalità **CREA**: se lo slug esiste già il comando fallisce. La modalità `--update` (aggiornamento di
  un tenant esistente) non è ancora implementata.
- L'esecuzione richiede un database raggiungibile con lo schema applicato (la migrazione non viene applicata dal tool).
