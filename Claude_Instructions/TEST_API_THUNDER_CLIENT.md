<!-- [INTENT]: Istruzioni per testare le API del BookingSystem con Thunder Client (estensione VS Code). Spiega
import della collection/environment, prerequisiti, variabili auto-popolate e flusso di test consigliato. I file
importabili sono in `thunder-tests/` (non .md, quindi fuori da questa cartella). -->

# Test API con Thunder Client

File pronti all'uso (cartella `thunder-tests/` nella root del repo):
- `thunder-collection_BookingSystem.json` — collection con **tutti** gli endpoint (Sistema, Pubbliche, Admin).
- `thunder-environment_BookingSystem_Local.json` — environment con le variabili (`baseUrl`, `apiKey`, JWT, ecc.).

## 1. Prerequisiti
1. **Estensione**: installa *Thunder Client* in VS Code.
2. **Infrastruttura su**: `docker compose up -d` (PostgreSQL).
3. **API in esecuzione**: `dotnet run --project src/WebAgency_BookingSystem.Api` → ascolta su `http://localhost:5022`.
4. **Un tenant provisionato**: serve per avere `apiKey` e password admin. Esegui la CLI (vedi
   `Claude_Instructions/GUIDA_INTEGRAZIONE_API.md` §4):
   ```bash
   dotnet run --project tools/WebAgency_BookingSystem.TenantProvisioning -- --file samples/barbershop-demo.json
   ```
   Annota **API key** (`bk_live_…`) e **password admin** stampate **una sola volta**.

## 2. Import in Thunder Client
1. Apri Thunder Client → tab **Collections** → menu **⋯ → Import** → seleziona `thunder-collection_BookingSystem.json`.
2. Tab **Env** → menu **⋯ → Import** → seleziona `thunder-environment_BookingSystem_Local.json`.
3. **Attiva** l'environment *“BookingSystem - Local”* (clic sulla spunta accanto al nome).
4. Compila le variabili nell'environment:
   - `apiKey` → la tua `bk_live_…`
   - `adminPassword` → la password admin del provisioning
   - `tenantSlug` / `adminEmail` → coerenti col tenant creato (default del sample: `barbershop-mario` / `owner@barbershop.it`)

> In alternativa, Thunder Client può importare anche l'**OpenAPI** generato dall'API: *Import → URL* →
> `http://localhost:5022/openapi/v1.json` (esposto solo in non-produzione). La collection qui fornita è però
> già pronta con variabili e concatenazioni.

## 3. Variabili auto-popolate (niente copia-incolla)
Alcuni request, tramite i **Tests** di Thunder Client, salvano valori nell'environment per i request successivi:
| Request | Salva in |
|---|---|
| `GET services` | `serviceId` (primo servizio della lista) |
| `GET staff` | `staffId` (primo staff) |
| `POST bookings` | `bookingId`, `cancellationToken` |
| `POST admin/auth/token` | `adminJwt` |
| `POST admin/services` | `serviceId` (servizio appena creato) |
| `POST admin/staff` | `staffId` (staff appena creato) |

## 4. Flusso di test consigliato
**Pubblico:**
1. `1. Sistema → Readiness (DB)` — deve dare `200` (se `503`, il DB non è raggiungibile).
2. `2. Pubbliche → GET services` — popola `serviceId`.
3. `GET availability` — verifica gli slot liberi (usa `serviceId`, `dateFrom`, `dateTo`).
4. `POST bookings (crea)` — `201`, popola `bookingId` + `cancellationToken`.
5. `GET bookings/{id}` e `DELETE bookings/{id}` — usano le variabili appena salvate.

**Admin:**
1. `3. Admin → POST admin/auth/token (LOGIN)` **per primo** — salva `adminJwt` (usato da tutte le altre rotte admin).
2. Poi qualsiasi altra rotta admin: `GET admin/bookings`, CRUD servizi/staff, `PUT business-hours`, `PUT closures`.
   - `POST admin/services` rigenera `serviceId`; `POST admin/staff` rigenera `staffId`.

## 5. Note
- **Date**: aggiorna `date`/`dateFrom`/`dateTo` nell'environment se cadono nel passato o in un giorno chiuso
  (il sample è chiuso la domenica). Servono date **future** e in un giorno lavorativo.
- **Orari/chiusure** (`PUT`): il body è **wrappato** (`{ "days": [...] }` / `{ "closures": [...] }`), già corretto nella collection.
- **Produzione (HTTPS)**: duplica l'environment, rinominalo *“Production”* e imposta `baseUrl` a
  `https://<servizio>.railway.app` (in prod le API sono solo HTTPS — TLS al proxy). Scalar/OpenAPI sono
  disattivati in produzione.
- I file in `thunder-tests/` sono versionati: se usi il *Git Sync* di Thunder Client puoi puntarlo a quella cartella.
