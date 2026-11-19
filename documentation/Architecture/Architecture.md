# Architecture

D&D Electrical Engineering Suite is a **modular monolith** following Clean Architecture: engineering logic is independent of persistence and UI, and every dependency points inwards.

```
                         ┌──────────────┐   ┌──────────────┐
                         │ App (WPF)    │   │ Cli          │
                         └──────┬───────┘   └──────┬───────┘
                                │                  │
                      ┌─────────▼────────┐         │
                      │ Presentation     │ (MVVM, cross-platform)
                      └─────────┬────────┘         │
                ┌───────────────▼──────────────────▼──┐
                │ Infrastructure (EF Core, files)     │──implements──┐
                └───────────────┬─────────────────────┘              │
                      ┌─────────▼─────────┐                          │
                      │ Application       │ ◄── abstractions ────────┘
                      └─────────┬─────────┘
     ┌──────────┬───────────┬───┴──────┬────────────┬──────────────┐
  Reports   Validation   Documentation Plugins   Calculations   (Rendering)
     │          │                                    │
  Wiring · PLC · Panel                               Shared
     │
  Schematic ── Domain ── Symbols ── CAD ── Core ── Shared
```

## Layers

| Project | Responsibility | Depends on |
|---|---|---|
| Shared | Product identity, `Result`, guards, natural sorting | – |
| Core | Geometry: `Point2`, `Rect2`, `Matrix2` (exact 90° rotations) | Shared |
| CAD | Entities, layers, `CadDrawing` + spatial index, undo/redo, viewport, snapping, selection, tools, command processor, DXF | Core |
| Rendering | SkiaSharp renderer and PNG/PDF/SVG exporters | CAD |
| Symbols | Symbol definitions, libraries, built-in IEC library | CAD |
| Domain | `ElectricalProject` aggregate and engineering entities | Symbols |
| Schematic | Symbol instances, wires, junctions, connectivity graph, numbering, tagging, binder, editor and tools | Domain |
| Wiring / PLC / Panel | Connection lists, terminal diagrams, cables; PLC profiles and addressing; cabinet layout | Schematic |
| Calculations | Calculation engine (no project dependency) | Shared |
| Validation | Rules engine | Wiring, PLC, Panel |
| Reports | Report tables, BOM, PDF/XLSX/CSV writers | Validation, Rendering |
| Documentation | Markdown parser, help library, tutorials | Shared |
| Plugins | Plugin contracts and host | Domain |
| Application | Use cases: project sessions, templates, demos, exports, services; abstractions (`IProjectStore`, …) | all engineering modules |
| Infrastructure | EF Core `ProjectDbContext`, SQLite/PostgreSQL stores, locks, backups, recovery, settings, secrets | Application |
| Presentation | View models (CommunityToolkit.Mvvm), dialog abstractions | Application |
| App | WPF views, CAD canvas control, DI host, logging | Presentation, Infrastructure |
| Cli | Headless commands | Infrastructure |

## Key design decisions

### Connectivity is explicit
A wire stores references to what it connects (`WireEnd`: instance + pin, or junction). The `ConnectivityGraph` builds potentials with union–find over pins, junctions, symbol internal connections, named potential sources and interruption points. Geometry is used only for drawing; two symbols touching are not connected. Moving a symbol updates wire end points (rubber-banding) inside the same undo step.

### Derived data is bound, persistent data is stored
Display values (tags, cross-references, contact mirrors, PLC addresses, cable labels) are computed by `SchematicBinder` after loading and after edits. Wire numbers and device tags are persisted because engineering identifiers must remain stable; automatic numbering is an explicit, undoable action.

### Extensible entity types
CAD entities are polymorphic and registered in `CadEntityTypeRegistry`; the schematic module registers its types without the CAD layer knowing about electrical concepts. Module entities render and export through `IExplodableEntity` / `IDxfBlockSource`.

### Persistence
Each project is one SQLite database (`.dndx`). Engineering entities are relational (queryable); sheet geometry is a JSON column (efficient, schema-stable). A derived `Connections` table is written on save for SQL access. Saves are atomic (temporary file + replace) and preceded by a rolling backup; opening an older format migrates a temporary copy, never the user's file. `CentralProjectStore` hosts many projects in PostgreSQL (or shared SQLite) and replaces a project's rows in a transaction.

### Projects embed their symbols
Every symbol used is cached in the project (`ProjectSymbols` table), so projects open on machines without the original library. Newer library versions update the cache on open.

### UI independence
All interactive CAD behaviour (tools, snapping, selection, undo) lives in `CadEditor`; the WPF `CadCanvas` only forwards pointer/keyboard input and paints. All UI workflows live in view models, tested on Linux with a scripted `IDialogService`.

### Safety
- Validation never modifies the design; fixes are offered with an id and executed only after confirmation.
- The assistant has no write access; suggestions also require confirmation.
- Plugins load only when compatible, hash-verified and explicitly trusted; host services are permission-gated.
- Archive import validates entry names, sizes, counts and compression ratios.
- CSV export neutralises formula injection.

## Data model (main tables)

`Projects` (owned `Info`, JSON `Settings`), `Revisions`, `Sheets` (JSON drawing), `Devices`, `Cables` → `CableCores`, `TerminalStrips` → `Terminals`, `PlcStations` → `PlcRacks` → `PlcModules` → `PlcChannels`, `Motors`, `Cabinets` → `CabinetItems`, `CatalogProducts`, `Procurement`, `Loads`, `Calculations`, `Attachments`, `AuditEvents`, `ValidationRuns`, `ProjectSymbols`, `Connections` (derived). Every child table has a `SortOrder` column to preserve list order. Migrations live in `src/DND.Electrical.Infrastructure/Persistence/Migrations`.

## Performance

- Uniform-grid spatial index with incremental updates (hit testing and culling; tested with 5 000 entities).
- Viewport culling and cached explosion of symbol graphics per drawing revision.
- Virtualised lists in the WPF shell; reports and exports run on demand from live data.
