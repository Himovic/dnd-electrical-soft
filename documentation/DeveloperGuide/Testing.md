# Testing strategy

| Suite | Scope | Runs on |
|---|---|---|
| `tests/DND.Electrical.UnitTests` (86) | Geometry, CAD engine and tools (simulated pointer input), serialization, DXF round trip, symbols, connectivity, numbering, tagging, cross-references, schematic editor, calculations (hand-computed references), PLC, panel, cables, terminal diagrams | Any OS |
| `tests/DND.Electrical.IntegrationTests` (34) | SQLite persistence of all demo projects, derived SQL table, lifecycle (locks, backups, revisions), recovery, archives (incl. malicious archives), central store, JSON/XML, demo validation, handover package, PDF/XLSX/CSV/DXF/SVG/PNG export, help library, tutorials, plugins (end-to-end), catalogue import, assistant, permissions | Any OS |
| `tests/DND.Electrical.UIAutomationTests` (10) | UI workflows through the real view models: startup, project creation, symbol insertion, wiring, property editing with undo, numbering, validation navigation, save/close/reopen, unsaved-change prompt, help navigation and administration, tutorials, calculations, engineering data, symbol editor, command palette | Any OS |

```bash
dotnet test DND-Electrical-Engineering-Suite.sln
```

## Independent export verification

```bash
dotnet run --project src/DND.Electrical.Cli -- demo all --out out/demo
dotnet run --project src/DND.Electrical.Cli -- export out/demo/DEMO-IND-001.dndx --format handover --out out/handover
python3 scripts/verify-exports.py out/handover
```

The script opens every DXF with `ezdxf` (audit), every PDF with `pypdf` and the workbook with `openpyxl`.

## Windows smoke test (required before release)

The WPF shell cannot be executed on Linux. On Windows run `dotnet run --project src/DND.Electrical.App` and check:

1. Splash screen shows the logo, version and "Powered by D&D | Data&Development".
2. Start page lists the five demo projects; open DEMO-IND-001.
3. Sheet renders with title block and D&D logo; zoom (wheel), pan (middle button), grid toggle (F7).
4. Insert a symbol from the Symbol browser, draw a wire between two pins, rotate with R, undo/redo.
5. Edit the tag in Properties; the contact cross-reference updates.
6. F8 validation; double-click a message navigates to the object.
7. Reports ▸ Drawing set PDF and Reports… (PDF and XLSX) produce files.
8. Help (F1): article rendering, search, tutorials; About window shows the attribution.
9. Toggle dark theme; close with unsaved changes prompts to save.

Automating these checks with FlaUI/WinAppDriver is planned.
