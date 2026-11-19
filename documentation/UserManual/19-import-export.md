---
title: Import and export
category: Documentation
order: 19
version: 0.4.0
keywords: dxf, svg, png, json, xml, csv, xlsx, archive, dwg
---
# Import and export

| Format | Import | Export |
|---|---|---|
| DXF (ASCII, R12 subset) | Lines, polylines (incl. bulges), circles, arcs, text, MTEXT, solids, block references | Sheets with layers; symbols as blocks with TAG and other attributes |
| SVG | — | Sheets |
| PNG | — | Sheets |
| PDF | — | Drawing sets and reports |
| XLSX / CSV | Product catalogues | Reports and lists |
| JSON | Complete projects | Complete projects |
| XML | — | Engineering data (devices, connections, cables, PLC I/O) |
| `.dndpkg` | Project archives | Project archives |
| Symbol libraries (JSON) | Yes | Yes |

DWG is **not** supported (it requires a licensed library). Binary DXF is not supported; save as ASCII DXF.
