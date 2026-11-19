# Changelog

All notable changes to **D&D Electrical Engineering Suite** (© 2026 D&D | Data&Development).

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
