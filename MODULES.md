# Modules and feature status register

**D&D Electrical & Automation Engineering Suite** (short name *D&D Engineering Suite*), version 0.5.0 – © 2026 D&D | Data&Development.

Every module is licensed separately (see [LICENSING.md](LICENSING.md)) and enforced in application services, not only in the user interface. All modules share one project file and one **engineering object identity**: a device created in a schematic is the same object (same GUID) in the equipment list, cabinet layout, 3D model, IFC export, PLC I/O, cable schedule, maintenance and commissioning records and digital-twin points.

Status vocabulary used throughout this register:

| Status | Meaning |
|---|---|
| **Implemented and tested** | Implemented and covered by automated tests against independent references (hand calculations, published benchmarks, third-party parsers or protocol peers). |
| **Implemented but not fully validated** | Implemented and tested for consistency, but not yet compared with an independent reference or field use. |
| **Partially implemented** | Usable core exists; listed parts are missing. |
| **Simulated demonstration feature** | Produces simulated data only; always labelled as simulated in the UI and data. |
| **Planned** | Not implemented. No UI action pretends otherwise. |
| **Blocked by external dependency** | Requires a third-party SDK, licence, standard data or service not available to this build. |

## ELEC – Electrical Engineering

Schematic CAD with real connectivity, 46 IEC-style symbols and custom libraries, wiring and potentials, wire numbering, device tagging, coil/contact cross-references, cables and cores, terminals, cabinet layout (2D), equipment and catalogue, calculations, validation (22 VAL rules), reports. UI: main window, Engineering data, Calculations. CLI: `validate`, `number-wires`, `calc`.

## AUTO – Automation Engineering

Instrument index (ISA-5.1 style tags, ranges, signals, junction boxes, hazardous-zone and Ex-marking records), control loops, alarm database with span-based proposals, cause and effect matrices with logic review, control system architecture (nodes, networks, zones, protocols, redundancy), PLC configuration and I/O assignment with signal-kind/direction/occupancy checks, generated loop diagram sheets, rules AUT001–AUT007, five reports. UI: *Automation* window. Service: `AutomationService`.

## POWER – D&D PowerStudio

Network model (buses, utility feeders, transformers with vector groups, lines/cables, loads, generators, shunts, protection devices, harmonic sources), Newton–Raphson load flow, IEC 60909-0 short circuit (maximum/minimum 3-phase, 2-phase, line-to-earth), protection coordination with time-current chart, harmonic frequency scan and THDv, PowerStudio calculators (short circuit, transformer, motor starting, loads, PF correction, generator sizing, earthing, touch/step voltage, data centre, PV strings). UI: *PowerStudio* window, Calculations. CLI: `power`.

## 3D – 3D Engineering

Cross-platform scene model, 3D cabinet generated from the 2D layout, equipment and cable tray routes with fill check, separating-axis clash and exact clearance checks, measurement, mesh-clipped sections, exploded view, 2D↔3D synchronisation, glTF/GLB/STL export, HelixToolkit viewer with picking. UI: *3D layout* window. CLI: `model3d`.

## BIM – BIM Engineering

IFC4 export (xBIM) of 3D layouts with spatial structure, IFC electrical classes and engineering identities in `Pset_DnD_EngineeringObject`; IFC import of extruded rectangular bodies as context geometry. UI: 3D window (IFC… / Import IFC…). CLI: `model3d --ifc`.

## INFRA – Infrastructure Engineering

Industry templates (data center, solar PV, railway station, oil and gas, manufacturing, water, building, HVAC, power distribution, renewable), industry calculators, ten demonstration projects including DEMO-MFG-006, DEMO-RAIL-007, DEMO-DC-008, DEMO-OG-009 and DEMO-PV-010.

## TWIN – Digital Twin

Twin points bound to engineering objects; Modbus TCP and MQTT sources; built-in simulator; polling engine with history, trends and live alarm evaluation. UI: *Digital twin* window with a SIMULATED DATA banner.

## AI – Engineering Assistant

Local knowledge provider (offline) and Claude provider (official Anthropic SDK). Proposals are limited to corrections the application implements, always confirmed by the user, and every interaction and decision is stored in the project's AI audit trail. Project data is sent to an external provider only after explicit opt-in.

## DOCS – Documentation Engineering

Drawing sets (PDF), reports (PDF EN/FR, XLSX, CSV), DXF/SVG/PNG sheets, handover packages, JSON/XML interchange (interchange stays available without DOCS for data portability), help center and tutorials.

## CATALOG – Equipment Catalog

Manufacturer-neutral catalogue with verification status; demonstration products are flagged as fictional and reported by rule VAL022. No manufacturer specifications are invented.

## PROJECT – Project Management

Lifecycle phase, members and roles, tasks, milestones, issue register, approvals (role check, four-eyes principle), comments, document register, maintenance and commissioning records, revisions, audit trail, unified engineering-object view with conflict detection and confirmed synchronisation.

## INTEGRATION – External Integrations

Connector architecture with ERP BOM file drop, CMMS asset register, HMAC-signed webhook; planned connectors listed and not executable. See [INTEGRATIONS.md](INTEGRATIONS.md).

---

## Feature status register

| Module | Feature | Status | Evidence / notes |
|---|---|---|---|
| ELEC | 2D CAD engine, rendering, DXF export/import | Implemented and tested | Unit tests; DXF read by `ezdxf` with 0 errors |
| ELEC | Connectivity, wire numbering, tagging, cross-references | Implemented and tested | Unit tests |
| ELEC | Cable sizing, voltage drop, protection selection, TN length | Implemented and tested | Hand-computed references (IEC 60364-5-52 data subset: Cu, methods C/E) |
| ELEC | Aluminium/buried cable capacity tables, IEC 61439 verification | Planned | Data must come from licensed standards |
| ELEC | DWG import/export | Blocked by external dependency | Requires a licensed DWG library |
| AUTO | Instruments, loops, alarms, C&E, architecture, I/O assignment, loop diagrams | Implemented but not fully validated | Integration and UI tests; no field commissioning feedback yet |
| AUTO | Hazardous-area classification | Planned | Zones and Ex markings are recorded from user input; the software never derives or approves them |
| AUTO | Vendor PLC programming software exchange | Blocked by external dependency | Vendor tools and formats |
| POWER | Newton–Raphson load flow | Implemented and tested | IEEE 14-bus published solution reproduced (|V| ±0.001 pu, angle ±0.01°) |
| POWER | IEC 60909-0 short circuit (3-phase max/min, 2-phase, 1-phase) | Implemented and tested | Hand calculations, parallel paths, vector groups, KT/KG; cross-check with radial calculator |
| POWER | Asynchronous motor contribution, DC/far-from-source decay (Ib, Ik), thermal equivalent | Planned | Documented as assumption in every report |
| POWER | Protection coordination and TCC | Implemented and tested | IEC 60255 curve values; MCB bands from IEC 60898-1 conventional values (envelope, not manufacturer curves) |
| POWER | Harmonic frequency scan and THDv | Implemented and tested | Closed-form resonance and Z(h)·I(h) checks |
| POWER | Arc flash (IEEE 1584) | Blocked by external dependency | Requires the licensed IEEE 1584 model data |
| POWER | Dynamic motor starting, transient stability | Planned | |
| 3D | Scene model, cabinet generation, clashes, clearances, sections, explode | Implemented and tested | Analytical geometry tests; 5 000-object clash check 0.30 s |
| 3D | glTF 2.0/GLB, STL export | Implemented and tested | Khronos glTF-Validator 0 errors/0 warnings; trimesh extents |
| 3D | STEP/IGES exchange | Blocked by external dependency | Requires an OCCT-based kernel binding |
| 3D | WPF viewer (HelixToolkit) | Implemented but not fully validated | Compiles; must be smoke-tested on Windows |
| BIM | IFC4 export/import | Implemented and tested | Exact round trip; IfcOpenShell reads files and builds geometry for all elements |
| BIM | MEP routing, clash with imported building elements of arbitrary geometry | Planned | Only extruded rectangular bodies are imported |
| INFRA | Industry templates and calculators, five industry demos | Implemented but not fully validated | Calculator formulas tested; demo data fictional |
| TWIN | Modbus TCP client | Implemented and tested | Real sockets against an in-process Modbus server (FC 1–4, 6, 16, exceptions) |
| TWIN | MQTT subscriber | Implemented and tested | Embedded MQTTnet broker |
| TWIN | Simulator | Simulated demonstration feature | Every value flagged `Simulated`; UI banner |
| TWIN | OPC UA | Blocked by external dependency | Requires OPC UA stack integration and certificate management |
| AI | Local provider | Implemented and tested | |
| AI | Claude provider, confirmed proposals, audit trail | Implemented and tested | Mocked HTTP handler (request shape, refusals, malformed output); not called against the live API in CI |
| DOCS | PDF/XLSX/CSV/DXF/SVG/PNG, handover, interchange | Implemented and tested | `pypdf`, `openpyxl`, `ezdxf` |
| DOCS | Arabic localisation | Planned | EN/FR available |
| CATALOG | Neutral catalogue, demo flagging | Implemented and tested | |
| CATALOG | Verified manufacturer data feeds | Blocked by external dependency | Manufacturer agreements |
| PROJECT | Lifecycle, approvals, registers, engineering object conflicts | Implemented and tested | |
| PROJECT | Multi-user server with real-time collaboration | Planned | PostgreSQL store compiled, not executed in CI |
| INTEGRATION | ERP file drop, CMMS export, webhook | Implemented and tested | |
| INTEGRATION | SAP, PLM, Maximo API, SharePoint | Planned | Listed, not executable |
| Licensing | Signed licences, activation, floating seats, revocation, portal | Implemented and tested | 16 licensing tests incl. server (`WebApplicationFactory`) |
| Platform | WPF desktop shell | Implemented but not fully validated | Not executed in the Linux build environment |
| Platform | Windows installer | Partially implemented | Inno Setup script, not built here |
