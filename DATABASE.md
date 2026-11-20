# Database

## Project files (`.dndx`)

Each project is a single SQLite database written through Entity Framework Core (`ProjectDbContext`). Saving writes a temporary file and replaces the target atomically; a lock file prevents concurrent editing; backups and autosave recovery are kept separately.

### Schema

Root table `Projects` (project info and settings as JSON). Child tables keep a `SortOrder` shadow column so list order is preserved.

| Area | Tables |
|---|---|
| Drawings and components | `Sheets` (CAD drawing as JSON), `ProjectSymbols`, `Devices`, `Revisions` |
| Wiring | `Cables`, `CableCores`, `TerminalStrips`, `Terminals`, `Connections` (derived, queryable connection list) |
| PLC | `PlcStations`, `PlcRacks`, `PlcModules`, `PlcChannels` |
| Equipment | `Motors`, `Loads`, `Cabinets`, `CabinetItems`, `CatalogProducts`, `Procurement` |
| Engineering | `Calculations`, `ValidationRuns`, `PowerNetworks` (network JSON), `Layouts3D` (scene JSON) |
| Automation | `Instruments`, `ControlLoops`, `Alarms`, `CauseEffectMatrices` (causes/effects/links as JSON), `ScadaNodes`, `TwinPoints` |
| Project management | `ProjectMembers`, `ProjectTasks`, `Milestones`, `EngineeringIssues`, `Approvals`, `Comments`, `DocumentRegister`, `MaintenanceRecords`, `CommissioningRecords` |
| Traceability | `AuditEvents`, `AiInteractions`, `Attachments` |

Enumerations are stored as strings. Engineering object identities are GUID columns (e.g. `Instruments.DeviceId`, `TwinPoints.InstrumentId`, `MaintenanceRecords.ObjectId`).

### Migrations

`src/DND.Electrical.Infrastructure/Persistence/Migrations`:

| Migration | Content |
|---|---|
| `InitialProjectSchema` | Version 0.4.0 schema |
| `LifecycleAndProjectManagement` | Phase, members, tasks, milestones, issues, approvals, comments, document register, maintenance, commissioning |
| `ThreeDLayouts` | `Layouts3D` |
| `PowerNetworks` | `PowerNetworks` |
| `AutomationAndInstrumentation` | Instruments, loops, alarms, C&E matrices, architecture nodes |
| `DigitalTwinPoints` | `TwinPoints` |
| `AiInteractionAudit` | `AiInteractions` |

Older project files are upgraded on open (`Database.Migrate`). Add a migration with:

```bash
cd src/DND.Electrical.Infrastructure && dotnet ef migrations add <Name> --output-dir Persistence/Migrations
```

The derived `Connections` table can be queried with any SQLite client for ad-hoc reporting; it is regenerated on save and must not be edited.

## Central store

A PostgreSQL-ready central store (Npgsql provider) exists for team deployments. It compiles and shares the model, but it is **not executed in CI**; multi-user collaboration is planned.

## Licence server

`DND.Electrical.LicenseServer` uses its own SQLite database (`LicensingDb`: customers, organizations, licences, activations, leases, audit, admin users/sessions), configured by `Licensing:Database`. The signing private key is stored only on the server (`Licensing:SigningKeyPath`) and never in the database or the client.

## Data retention

Licence expiry never deletes or encrypts project data; projects open read-only. Simulated twin values are not persisted. The AI audit stores questions, answer excerpts, proposals and decisions, but not the project context sent to an external provider.
