---
title: Schematic drawing
category: Drawing
order: 6
version: 0.4.0
keywords: draw, line, polyline, circle, arc, text, dimension, callout, zoom, pan, snap, grid, ortho, layers, sheet
---
# Schematic drawing

## Sheets

Sheets use A0–A4 formats (A3 landscape by default) with a frame, numbered reference columns and a title block. Fields such as `{PROJECT.NAME}` or `{SHEET.NUMBER}` are filled automatically.

## Drawing tools

| Tool | Command | Use |
|---|---|---|
| Line | `L` | Click start and end; continues from the last point |
| Polyline | `PL` | Click points, Enter to finish, `C` to close |
| Rectangle | `REC` | Two corners |
| Circle | `C` | Centre and radius (or type the radius) |
| Arc | `A` | Start, point on arc, end |
| Text | `T` | Insertion point, then type the text |
| Dimension | `DIM` | Two points and the dimension line position |
| Callout | `LE` | Arrow point, bend points, Enter, then the text |

## Precise input

Type coordinates in the command line: `100,50` (absolute), `@20,0` (relative), `@50<45` (polar).

## Snapping

- **Grid snap** (5 mm default), **object snap** (end points, midpoints, centres, pins) and **ortho** (horizontal/vertical).
- Hold **Shift** to disable ortho temporarily, **Alt** to disable snapping.

## Selecting and editing

Click to select; Shift adds, Ctrl toggles. Drag left→right for a *window* (objects fully inside), right→left for a *crossing* selection. Drag a selected object to move it. Rotate (R), mirror, align, group/ungroup, copy/paste and duplicate are in the Home and Drawing tabs. Every operation can be undone (Ctrl+Z).

## Layers

Entities live on layers (WIRES, SYMBOLS, TAGS, ANNOTATIONS…). Layers can be hidden, locked or excluded from printing.
