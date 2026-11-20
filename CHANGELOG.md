# Changelog

All notable changes to **D&D Electrical & Automation Engineering Suite** (© 2026 D&D | Data&Development).

## [0.5.0] – unreleased (in development)

### Added
- Rebranding to D&D Electrical & Automation Engineering Suite (short name D&D Engineering Suite).
- Commercial modular licensing: ECDSA P-256 signed licences, modules (ELEC, AUTO, POWER, 3D, BIM, INFRA, TWIN, AI, DOCS, CATALOG, PROJECT, INTEGRATION), device binding, online/offline activation, grace period, revocation list, floating seat leases, clock-rollback detection, service-level enforcement, read-only mode without ELEC; licence server with administration portal; `dnd-license` issuing tool. See LICENSING.md.
- PROJECT module: lifecycle phase, members and roles, tasks, milestones, issue register, approvals (role check, four-eyes principle), comments, document register, maintenance and commissioning records.
- Unified engineering object: cross-representation view keyed by the device identity, conflict detection between representations and audited synchronisation on explicit confirmation only.
- 3D module: cross-platform scene model (oriented boxes, trays, cylinders), 3D cabinet model generated from the 2D layout, cable tray routes with fill check, separating-axis clash and exact clearance detection with sweep-and-prune broad phase, measurement, mesh-clipped sections, exploded view, 2D↔3D synchronisation (change detection, regeneration, write-back with confirmation), glTF 2.0/GLB and STL export (validated with the Khronos glTF-Validator and trimesh), HelixToolkit-based WPF viewer with picking, `model3d` CLI command.
- D&D PowerStudio (POWER module): network model (buses, utility feeders, transformers with vector groups, lines/cables, loads, generators, shunts, protection devices, harmonic sources); Newton–Raphson load flow with reactive limits (validated against the IEEE 14-bus published solution); IEC 60909-0 equivalent-source short circuit with bus impedance matrix (3-phase max/min, 2-phase, 1-phase via sequence networks, KT/KG factors, ip method B); protection coordination with IEC 60255 inverse curves, MCB bands and manufacturer points, grading margin check and SVG time-current chart; harmonic frequency scan and THDv from current injections; text reports; `power` CLI command; networks stored in the project.
- New calculators: power factor correction with resonance check, generator sizing (preliminary), earth rod (Dwight), earthing grid (IEEE Std 80 Sverak), tolerable touch/step voltages (IEEE Std 80), lighting by the lumen method.
- AUTO module: instrument index (ISA-5.1 style tags, ranges, signals, junction boxes, hazardous zone and Ex marking records), control loops, alarm database with span-based proposals (user-defined philosophy, confirmation required), cause and effect matrices with logic review, control system architecture (nodes, networks, zones, protocols, redundancy), PLC I/O assignment with signal/direction checks, generated loop diagram sheets; validation rules AUT001–AUT007; instrument, loop, alarm, cause and effect and architecture reports (EN/FR); Automation window.
- Industry templates: data center, solar PV plant, railway station, extended oil and gas.
- Calculators: UPS redundancy (N…2N+1), UPS battery autonomy (energy method), PUE, PV string sizing.
- Five new demonstration projects: DEMO-MFG-006 Manufacturing, DEMO-RAIL-007 Railway, DEMO-DC-008 Data Center, DEMO-OG-009 Oil & Gas, DEMO-PV-010 Solar (fictional data, each with schematics, loads, calculations, PowerStudio network, control architecture and project lifecycle data).
- TWIN module: Modbus TCP client (functions 0x01–0x04, 0x06, 0x10), MQTT subscriber (MQTTnet), simulator with mandatory SIMULATED flagging, twin engine with history and live alarm evaluation, Digital twin window.
- BIM module: IFC4 export/import through xBIM with engineering identities in a property set; verified with IfcOpenShell.
- AI module: Claude provider (official Anthropic SDK, structured output, server-side refusal fallbacks), opt-in project data sharing, encrypted API key storage, accept/reject per proposal, AI interaction audit trail stored in the project.
- INTEGRATION module: connector architecture with ERP BOM file drop, CMMS asset register and HMAC-signed webhook; planned connectors listed but not executable; `integrate` CLI command.
- 2D cabinet check now also covers door-mounted devices.

### Fixed
- DEMO-IND-001: the five door-mounted pilot devices were all placed at the same position (found by the new 3D clash detection).

## [0.4.0] – 2026-11-19

### Added
- Geometry kernel and 2D CAD engine: entities, layers, incremental spatial index, polymorphic JSON serialization, undo/redo with groups and save point, viewport, grid/object/ortho snapping, window/crossing selection, drawing and modify tools, command line with absolute/relative/polar input.
- SkiaSharp renderer for screen, PNG, vector PDF and SVG; bundled DejaVu font.
- DXF R12 export with symbol blocks and attributes; DXF import (incl. LWPOLYLINE bulges and block expansion).
- Symbol model with roles, pins, internal connections and attributes; library service with custom libraries, versioning, import/export; 46 original IEC-style symbols.
- Domain model for projects, sheets, devices, cables, terminals, PLC, motors, cabinets, catalogue, procurement, loads, calculations, attachments and audit trail.
- Schematic engine: symbol instances, wires, junctions; connectivity graph; wire numbering; device tagging; coil/contact cross-references; interruption points; title blocks with fields.
- Interactive schematic editor with insert-symbol and wire tools.
- Wiring, PLC, panel layout and calculation modules; 22-rule validation engine.
- Reports (EN/FR) to PDF, XLSX and CSV; handover packages; JSON and engineering XML exchange.
- EF Core persistence (SQLite per project, atomic save, migrations; central store foundation), backups, locks, autosave recovery, archives.
- Help center (Markdown), 27 user-manual articles, interactive tutorials, documentation administration.
- Plugin host with integrity and trust checks; sample plugin.
- Five demonstration projects; ten project templates.
- MVVM view models, WPF desktop application and cross-platform CLI.
- 131 automated tests (unit, integration, UI workflow).
