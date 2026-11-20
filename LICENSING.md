# Licensing

**D&D Electrical & Automation Engineering Suite** is licensed per module. Licences are signed by D&D | Data&Development and verified offline by the application.

## Modules

`ELEC` Electrical · `AUTO` Automation · `POWER` Power Systems (D&D PowerStudio) · `3D` 3D Engineering · `BIM` · `INFRA` Infrastructure · `TWIN` Digital Twin · `AI` Assistant · `DOCS` Documentation · `CATALOG` Equipment Catalog · `PROJECT` Project Management · `INTEGRATION` External Integrations.

Any combination can be licensed; there are no hard-coded editions.

## Licence models

Subscription types: Monthly, Annual, Perpetual, Perpetual with maintenance, Enterprise, Trial, Evaluation.
Seat models: Device (per activated device), Named user, Floating (concurrent seats leased from the licensing server). Offline use can be allowed or require periodic online revalidation.

## Security design

| Aspect | Implementation |
|---|---|
| Signature | ECDSA P-256 / SHA-256 over the canonical JSON claims (`SignedLicense`, `.dndlic`) |
| Keys | Private key only on the licensing server (`Licensing:SigningKeyPath`). Clients contain public keys (`ProductKeys`, optional `Licensing/trusted-keys.pem`) identified by key id |
| Claims | Licence, customer, organization, product, modules, subscription, seat model, issue date, validity, maintenance, user/device/concurrency limits, offline policy, grace period, revalidation interval, status, device binding, schema version, algorithm |
| Device identity | Salted SHA-256 of product id, machine name, OS platform and a random installation id – no hardware serials, MAC addresses or personal data |
| Enforcement | `LicenseManager.Demand(module)` in application services: `DocumentationService` (DOCS), calculators (ELEC/POWER), `PowerStudioService` (POWER), `AutomationService` and PLC configuration (AUTO), `ThreeDService` (3D), `BimService` (BIM), `TwinService` (TWIN), `IntegrationService` (INTEGRATION), assistant (AI), project management (PROJECT), and engine-level read-only CAD sessions when `ELEC` is missing. UI gating only adds the upgrade page |
| Expiry | Grace period after expiry; afterwards projects stay accessible read-only. Project data is never deleted or encrypted |
| Revocation | Signed status in refreshed licences and a signed revocation list (`/api/v1/revocations`) for offline clients |
| Tampering | Signature check on every evaluation; clock rollback detection (time earlier than the last recorded use by more than 24 h locks features until an online refresh) |
| Network loss | Licences work offline within their terms; online licences have a revalidation interval plus grace period |
| Floating seats | Signed short-lived leases (`/api/v1/floating/checkout`, heartbeat, release) with concurrency limits |
| Offline activation | Client exports an activation request; administrator generates a device-bound licence in the portal |

## Components

- `src/DND.Electrical.Licensing` – contracts, signing/verification, `LicenseManager`, server client.
- `src/DND.Electrical.LicenseServer` – ASP.NET Core licensing server and **License Administration Portal** (`/`), SQLite database, rate limiting, PBKDF2 admin passwords, bearer sessions, audit log.
- `src/DND.Electrical.LicenseTool` – `dnd-license keygen | issue | verify | request-info` (offline administration).
- In the desktop application: **Help ▸ Licensing** (status, modules, online activation, refresh, deactivation, licence file import, activation request export, evaluation licence).
- In the CLI: `dnd-electrical license status | evaluation | install | request | remove`.

## Running the licensing server

```bash
dotnet run --project src/DND.Electrical.LicenseServer -- --Licensing:SigningKeyPath=/secure/signing-key.pem --Licensing:BootstrapAdminPassword='<at least 12 characters>'
```

Deploy behind HTTPS (reverse proxy or Kestrel certificate). The first start creates the administrator account. Back up the database and the signing key separately; losing the key means existing licences cannot be refreshed.

## Keys in this repository

The repository contains only the **development public key** (`ProductKeys.DevelopmentPublicKey`) and the evaluation licence signed with it (`resources/Licensing/evaluation.dndlic`, all modules, until 2027-12-31, evaluation use only). The matching private key is not in the repository. Before commercial release, generate a production key pair (`dnd-license keygen`), deploy the private key to the licensing server only, and add the production public key to `ProductKeys`.

## Tests

`tests/DND.Electrical.LicensingTests` (16 tests): signature verification and tamper detection, module enforcement, grace periods, expiry, clock rollback, device binding, revalidation, signed revocation lists, and the real server in-process (activation limits, refresh, revocation propagation, deactivation, floating concurrency, offline activation, admin authentication, audit, CSV report). Application-level enforcement is covered by UI workflow tests.
