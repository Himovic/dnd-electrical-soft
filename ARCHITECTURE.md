# Architecture

**D&D Electrical & Automation Engineering Suite** is a .NET 10 modular monolith following clean-architecture layering: engineering engines are cross-platform libraries without UI dependencies; the application layer orchestrates use cases and enforces licensing; the presentation layer (MVVM view models) is cross-platform and tested headlessly; the WPF shell and the CLI are thin hosts. The 2D CAD and schematic internals are described in [documentation/Architecture/Architecture.md](documentation/Architecture/Architecture.md).

## Layers and projects

```
Shared ─┬─ Core (geometry) ─ CAD ─┬─ Rendering (SkiaSharp)
        │                          └─ Symbols ─ Domain ─ Schematic ─┬─ Wiring
        │                                                            ├─ PLC
        │                                                            └─ Panel ─ Validation ─ Reports
        ├─ Licensing (ECDSA licences, entitlements)
        ├─ Calculations                Domain ─ ThreeD ─ Bim (xBIM)
        ├─ Documentation, Plugins      Domain ─ Twin (Modbus TCP, MQTTnet)
        └─ Power (PowerStudio)
                         ↓
        Application (use cases, services, demos, templates, licence enforcement, Anthropic SDK)
                         ↓
        Infrastructure (EF Core SQLite/PostgreSQL, file services, DPAPI secrets)
                         ↓
        Presentation (MVVM view models, cross-platform)
                         ↓
        App (WPF, HelixToolkit viewer)        Cli (dnd-electrical)

        LicenseServer (ASP.NET Core minimal API + admin portal)   LicenseTool (dnd-license)
```

| Project | Responsibility |
|---|---|
| `DND.Electrical.Power` | Network model, per-unit admittance matrices, Newton–Raphson load flow, IEC 60909 short circuit, protection coordination, harmonic scan. Depends only on `Shared`. |
| `DND.Electrical.ThreeD` | Scene model, oriented boxes, meshes, SAT clashes, clearances, sections, explode, cabinet generation, glTF/GLB/STL writers. |
| `DND.Electrical.Bim` | IFC4 exchange through the xBIM toolkit. |
| `DND.Electrical.Twin` | Modbus TCP protocol client, MQTT source, simulator, twin engine. |
| `DND.Electrical.Licensing` | Signed licence model, verification, device identity, entitlements, server client. |
| `DND.Electrical.Application` | Services per module (`PowerStudioService`, `ThreeDService`, `BimService`, `AutomationService`, `TwinService`, `IntegrationService`, `EngineeringObjectService`, `ApprovalService`, `ClaudeAssistantProvider`, …). |

Third-party components (all permissive licences): SkiaSharp, HelixToolkit.Wpf, Xbim.Essentials, MQTTnet, Anthropic C# SDK, Entity Framework Core (SQLite, Npgsql), ClosedXML, CommunityToolkit.Mvvm, Serilog. No geometry kernel, IFC parser or protocol stack beyond Modbus TCP framing is written from scratch.

## Engineering object identity

A `Device` GUID is the identity of an engineering object. Every representation references it: schematic symbol instances, motors, cabinet items, PLC channels, cables (through connected wires), loads, catalogue products, 3D scene objects (`SceneObject3D.ObjectId`), IFC elements (`Pset_DnD_EngineeringObject.EngineeringObjectId`), instruments, twin points, maintenance and commissioning records. `EngineeringObjectService.Describe` assembles the cross-representation view; conflicts (tag, rating, part number, dimensions) are detected and synchronised **only** after the user chooses the authoritative representation; each synchronisation is audited. 3D↔2D changes follow the same rule (`CabinetModel.Compare` reports, `PushToLayout` writes back after confirmation).

## Licence enforcement points

`IEntitlements.Demand(module)` is called in the services, not in the UI: `DocumentationService` (DOCS), calculators (ELEC/POWER), `PowerStudioService` (POWER), `ThreeDService` (3D), `BimService` (BIM), `AutomationService` and PLC actions (AUTO), `TwinService` (TWIN), `IntegrationService` (INTEGRATION), project management window (PROJECT), assistant (AI). Without ELEC, CAD sessions are read-only at engine level. The UI only shows the upgrade page for a locked module.

## Persistence

One SQLite file per project (`.dndx`) written atomically through EF Core with migrations; aggregates stored relationally, large structured documents (sheets, 3D scenes, power networks, C&E matrices) in JSON columns. See [DATABASE.md](DATABASE.md).

## Concurrency and responsiveness

Engines are synchronous and deterministic; long operations (twin polling, AI requests, exports) are asynchronous. The twin engine polls on a background `PeriodicTimer` and marshals results to the UI synchronisation context. Clash detection uses sweep-and-prune; short circuit factorises the bus admittance matrix once per study.

## Testing strategy

Unit tests for engines (published benchmarks and closed forms), integration tests for persistence, services, exports and protocols (real sockets, embedded MQTT broker, mocked HTTP), UI workflow tests driving view models with fake dialogs, and third-party verification of exported files (Khronos glTF-Validator, trimesh, IfcOpenShell, ezdxf, pypdf, openpyxl). See [ENGINEERING_VALIDATION.md](ENGINEERING_VALIDATION.md).
