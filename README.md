# D&D Electrical & Automation Engineering Suite

![logo](resources/Icons/dnd-logo-64.png)

**D&D Engineering Suite** — *One Platform. Multiple Engineering Disciplines. Unlimited Possibilities.*

**Author & Developer:** D&D | Data&Development
**© 2026 D&D | Data&Development. All rights reserved.**

A modular engineering platform for electrical, automation and power systems: electrical CAD with real connectivity, PLC and instrumentation, power system studies (D&D PowerStudio), 3D cabinets and trays, IFC/BIM exchange, digital twin, documentation, project lifecycle and integrations — built around one engineering object identity shared by every representation.

> Calculation and study results are preliminary engineering aids; the software does not certify compliance with any standard. Demonstration projects contain fictional data only. Simulated data is always labelled as simulated.

## Modules (version 0.5.0)

| Code | Module | Highlights |
|---|---|---|
| ELEC | Electrical Engineering | Schematics, connectivity, wiring, cables, terminals, cabinet 2D, calculations, 22 validation rules |
| AUTO | Automation Engineering | PLC I/O, instruments, loops, alarms, cause and effect, control architecture, loop diagrams |
| POWER | D&D PowerStudio | Newton–Raphson load flow (IEEE 14-bus benchmarked), IEC 60909 short circuit, protection coordination, harmonics |
| 3D | 3D Engineering | Cabinet 3D from 2D, trays, clash/clearance, sections, explode, glTF/GLB/STL |
| BIM | BIM Engineering | IFC4 export/import (xBIM) with engineering identities |
| INFRA | Infrastructure | Data center, solar, railway, oil and gas templates and calculators |
| TWIN | Digital Twin | Modbus TCP, MQTT, labelled simulator, live alarms and trends |
| AI | Engineering Assistant | Local or Claude provider, confirmed proposals, audit trail |
| DOCS | Documentation | PDF/XLSX/CSV/DXF/SVG/PNG, handover packages |
| CATALOG | Equipment Catalog | Manufacturer-neutral, verification status, demo flagging |
| PROJECT | Project Management | Lifecycle, approvals, registers, engineering object conflicts |
| INTEGRATION | Integrations | ERP BOM file drop, CMMS assets, signed webhook |

Exact status of every feature — *implemented and tested*, *not fully validated*, *partial*, *simulated*, *planned* or *blocked by external dependency* — is in the [feature status register](MODULES.md#feature-status-register).

## Documentation

| Document | Content |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | Layers, projects, engineering object identity, licence enforcement points |
| [MODULES.md](MODULES.md) | Modules and feature status register |
| [LICENSING.md](LICENSING.md) | Licence models, security design, server and portal |
| [DATABASE.md](DATABASE.md) | Project database schema, migrations, licence server database |
| [INTEGRATIONS.md](INTEGRATIONS.md) | Connectors, field protocols, exchange formats, AI providers |
| [ENGINEERING_VALIDATION.md](ENGINEERING_VALIDATION.md) | References, benchmarks and tolerances of every calculation and study |
| [ROADMAP.md](ROADMAP.md) | Planned work |
| [CHANGELOG.md](CHANGELOG.md) | Version history |
| [User manual – English (PDF)](documentation/Manual/DND-Engineering-Suite-User-Manual-EN.pdf) · [Manuel utilisateur – français (PDF)](documentation/Manual/DND-Engineering-Suite-User-Manual-FR.pdf) | Complete manuals from installation to every module; rebuild with `python3 scripts/build-manual.py` |
| [documentation/](documentation) | In-app help articles, developer guide, installation |

## Demonstration projects

`DEMO-IND-001` motor control · `DEMO-AUT-002` automation · `DEMO-PWR-003` distribution · `DEMO-CAB-004` control cabinet · `DEMO-BLD-005` building · `DEMO-MFG-006` manufacturing packaging line · `DEMO-RAIL-007` railway station auxiliary power · `DEMO-DC-008` 2N data hall · `DEMO-OG-009` gas compression skid · `DEMO-PV-010` 2.5 MWp PV plant. All validate with zero errors; all data is fictional.

## Repository layout

```
src/
  DND.Electrical.Shared / Core / CAD / Rendering / Symbols     platform, geometry, CAD, rendering, symbols
  DND.Electrical.Domain / Schematic / Wiring / PLC / Panel     engineering model and engines
  DND.Electrical.Calculations / Validation / Reports           calculations, rules, reports
  DND.Electrical.Power                                         D&D PowerStudio engines
  DND.Electrical.ThreeD / Bim                                  3D engine, IFC exchange
  DND.Electrical.Twin                                          Modbus TCP, MQTT, simulator
  DND.Electrical.Licensing / LicenseServer / LicenseTool       licensing client, server + portal, issuing tool
  DND.Electrical.Documentation / Plugins                       help, tutorials, plugin host
  DND.Electrical.Application / Infrastructure                  use cases and services, persistence
  DND.Electrical.Presentation / App / Cli                      view models, WPF desktop, command line
tests/        unit, integration, UI workflow and licensing tests (288)
scripts/      build, publish and third-party verification scripts (exports, glTF, 3D, IFC)
resources/, documentation/, installers/, samples/
```

## Building and testing

Requirements: .NET 10 SDK. The WPF application builds on Windows (it also compiles on Linux through `EnableWindowsTargeting`, but runs only on Windows).

```bash
./scripts/build.sh
```

```bash
dotnet test tests/DND.Electrical.UnitTests
```

The build script runs all four test suites. Third-party verification of exports (also run in CI):

```bash
python3 scripts/verify-exports.py out/handover
```

```bash
python3 scripts/verify-ifc.py out/cab.ifc
```

## Command line

```bash
dotnet run --project src/DND.Electrical.Cli -- license evaluation
```

```bash
dotnet run --project src/DND.Electrical.Cli -- demo all --out ./demo
```

```bash
dotnet run --project src/DND.Electrical.Cli -- power sample:ieee14 --study lf
```

```bash
dotnet run --project src/DND.Electrical.Cli -- model3d ./demo/DEMO-CAB-004.dndx --out cab.glb --ifc cab.ifc
```

```bash
dotnet run --project src/DND.Electrical.Cli -- integrate ./demo/DEMO-OG-009.dndx --connector cmms-assets --target ./cmms
```

Desktop application (Windows):

```bash
dotnet run --project src/DND.Electrical.App
```

## Known limitations

- The WPF shell, including the new PowerStudio, Automation, 3D (HelixToolkit), Digital twin, Integrations and Project management windows, compiles but has not been run in this development environment; a Windows smoke test is required before release. The installer script has not been compiled here.
- PowerStudio: no asynchronous motor contributions, decaying components or method C peak factor; MCB bands are envelopes from IEC 60898-1 conventional values, not manufacturer curves; arc-flash requires licensed IEEE 1584 data (not implemented).
- Cable current-carrying capacity data covers copper, methods C and E only, transcribed from IEC 60364-5-52 and to be verified against the licensed standard.
- 3D/BIM: box-based geometry; IFC import converts extruded rectangular bodies only; STEP/IGES and OPC UA are blocked by external dependencies.
- Hazardous-area zones and Ex markings are recorded from user input, never derived or approved by the software.
- The Claude provider is tested against a mocked HTTP endpoint; live use requires the user's own API key. DWG is not supported. The PostgreSQL central store is not executed in CI; multi-user collaboration is planned.
