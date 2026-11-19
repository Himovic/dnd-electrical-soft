# D&D Electrical Engineering Suite

![logo](resources/Icons/dnd-logo-64.png)

**Author & Developer:** D&D | Data&Development
**© 2026 D&D | Data&Development. All rights reserved.**

An integrated electrical engineering environment: 2D electrical CAD with real connectivity, symbol libraries, wiring, cables and terminals, PLC I/O, control-cabinet layout, engineering calculations, validation and documentation generation.

> Calculation results are preliminary engineering aids; the software does not certify compliance with any standard. Demonstration projects contain fictional data only.

## Status (version 0.4.0)

| Area | State | Verification |
|---|---|---|
| Geometry kernel and 2D CAD engine (entities, layers, spatial index, snapping, undo/redo, tools, command line) | Implemented | Unit tests |
| Rendering (screen / PNG / vector PDF / SVG) with SkiaSharp | Implemented | Unit + integration tests, visual inspection |
| DXF R12 export (symbols as blocks with attributes) and import | Implemented | Round-trip tests; exports read by `ezdxf` with 0 errors |
| Symbol model, libraries, 46 built-in IEC-style symbols, custom libraries, symbol editor | Implemented | Unit + UI workflow tests |
| Schematic editor: insertion, wiring, T-junctions, rubber-banding, device lifecycle | Implemented | Unit tests (editor driven by simulated input) |
| Connectivity graph, potentials, short-circuit detection, interruption points | Implemented | Unit tests |
| Wire numbering, device tagging, cross-references (coil ↔ contacts) | Implemented | Unit tests |
| Cables, terminals, terminal diagrams, PLC (profiles, addressing, I/O list), cabinet layout | Implemented | Unit + integration tests |
| Calculations (11 calculators) with transparent reports and standard profiles | Implemented | Hand-computed reference tests |
| Validation engine (22 rules) | Implemented | Demo projects validate with 0 errors / 0 warnings |
| Reports: PDF (EN/FR), XLSX, CSV; handover package; JSON/XML interchange | Implemented | Integration tests; PDFs read by `pypdf`, workbooks by `openpyxl` |
| Persistence: EF Core + SQLite per project (atomic save, migrations), central store (PostgreSQL-ready) | Implemented | Integration tests (SQLite). PostgreSQL path compiled but **not executed** in CI |
| Backups, locking, autosave/recovery, archives with safe extraction | Implemented | Integration tests |
| Help center, 27 manual articles, tutorials, documentation administration | Implemented | Integration + UI workflow tests |
| Plugin host (manifest, version, SHA-256, trust list, permissions) | Implemented | End-to-end test with sample plugin |
| View models (all UI behaviour) | Implemented | 10 UI workflow tests |
| WPF desktop shell | Implemented, compiles | **Not executed in the development environment (Linux).** Must be smoke-tested on Windows |
| CLI `dnd-electrical` (Windows/Linux/macOS) | Implemented | Used to generate and verify demos and exports |
| Windows installer | Inno Setup script provided | **Not built** in the development environment |

See [Known limitations](#known-limitations).

## Repository layout

```
src/
  DND.Electrical.Shared          product identity, results, comparers
  DND.Electrical.Core            geometry kernel
  DND.Electrical.CAD             CAD model, editing, tools, DXF
  DND.Electrical.Rendering       SkiaSharp renderer, PNG/PDF/SVG export
  DND.Electrical.Symbols         symbol model, libraries, built-in IEC library
  DND.Electrical.Domain          project aggregate and engineering entities
  DND.Electrical.Schematic       schematic entities, connectivity, numbering, editor
  DND.Electrical.Wiring          connection lists, terminal diagrams, cables
  DND.Electrical.PLC             PLC profiles, addressing, I/O lists
  DND.Electrical.Panel           cabinet layout
  DND.Electrical.Calculations    calculation engine and standard profiles
  DND.Electrical.Validation      validation engine and rules
  DND.Electrical.Reports         report tables, PDF/XLSX/CSV writers
  DND.Electrical.Documentation   Markdown, help library, tutorials
  DND.Electrical.Plugins         plugin contracts and host
  DND.Electrical.Application     use cases: projects, templates, demos, exports, services
  DND.Electrical.Infrastructure  EF Core persistence, file services
  DND.Electrical.Presentation    MVVM view models (cross-platform)
  DND.Electrical.App             WPF desktop application (Windows)
  DND.Electrical.Cli             command-line tool
samples/                          sample plugin
tests/                            unit, integration and UI workflow tests
resources/                        branding, icons, fonts, symbols, demo projects
documentation/                    user manual (in-app help), architecture, guides
scripts/, installers/             build, publish and installer scripts
```

## Building

Requirements: .NET 10 SDK. The WPF application builds on Windows (and compiles on Linux thanks to `EnableWindowsTargeting`, but only runs on Windows).

```bash
dotnet build DND-Electrical-Engineering-Suite.sln
```

```bash
dotnet test tests/DND.Electrical.UnitTests
```

```bash
dotnet test tests/DND.Electrical.IntegrationTests
```

```bash
dotnet test tests/DND.Electrical.UIAutomationTests
```

Run the desktop application (Windows):

```bash
dotnet run --project src/DND.Electrical.App
```

Command-line tool (any OS):

```bash
dotnet run --project src/DND.Electrical.Cli -- demo all --out ./demo
```

```bash
dotnet run --project src/DND.Electrical.Cli -- export ./demo/DEMO-IND-001.dndx --format handover --out ./handover
```

See [documentation/DeveloperGuide](documentation/DeveloperGuide/DeveloperGuide.md) and [documentation/Installation](documentation/Installation/Installation.md).

## Known limitations

- The WPF shell has been compiled but not run in this development environment; a Windows smoke test (see `documentation/DeveloperGuide/Testing.md`) is required before release. Docking is implemented with resizable panels (no floating/undockable panels yet).
- Current-carrying capacity data covers copper conductors, reference methods C and E only; values are transcribed from IEC 60364-5-52 and must be verified against the licensed standard. Aluminium cable sizing, buried installation methods, harmonic and thermal short-circuit checks are not implemented. IEC 61439 verification is not implemented (profile documents this).
- Short-circuit calculation: maximum three-phase current only (no line-to-earth, no motor contribution).
- DWG is not supported. DXF import does not read HATCH, SPLINE, ELLIPSE or binary DXF.
- PLC profiles are addressing templates, not vendor catalogues, and there is no integration with vendor PLC programming software.
- The engineering assistant is an offline, rule-based provider; the `IAssistantProvider` interface allows adding an AI provider later.
- Central multi-user deployment: the PostgreSQL store and role model exist, but authentication, a server and real-time collaboration are not implemented. A REST API is planned (not implemented).
- 3D cabinet visualisation, Arabic localisation and licence activation are planned.
- The installer script has not been compiled here (requires Inno Setup on Windows).
