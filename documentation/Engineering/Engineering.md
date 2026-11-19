# Engineering notes

## Conventions
- Drawing units: millimetres, A3 landscape by default, 10 reference columns, 5 mm grid.
- Device identification: `-{PREFIX}{N}` (configurable). Default prefixes follow common practice (Q, K, F, M, S, B, H, X, T, G, Y, E, A).
- Cross-reference notation: `/sheet.column`.
- Potential names on feed symbols connect project-wide; interruption points with the same name connect across sheets.
- Cable core markings follow IEC 60445 colour codes (BN, BK, GY, BU, GNYE).

## Calculations and standards
The calculation engine and its data sources are described in the in-app article *Calculation methodology* (`documentation/UserManual/25-calculation-methodology.md`). `StandardProfile` documents, per standard, which clauses are implemented, partial or not implemented:

| Profile | Implemented | Partial | Not implemented |
|---|---|---|---|
| IEC 60364 | Voltage drop (Annex G), temperature correction, 433.1 coordination | Current-carrying capacity (Cu, methods C/E), grouping (arrangement 1), TN disconnection | PE sizing (5-54) |
| IEC 60204-1 | 5 % voltage drop limit | – | Annex D capacities |
| IEC 60909-0 | κ peak factor | I"k3 max (radial LV) | Line-to-earth |
| IEC 61439 | – | – | Temperature-rise verification |
| NF C 15-100 | Voltage drop limits | Capacities (IEC data set) | National mapping of methods |

No compliance claim is made by the software.

## Demonstration data
All demo customers, products, part numbers (`DEMO-…`), prices and the manufacturer "D&D Demo Components (fictional)" are fictional and flagged (`IsDemoData`); validation rule VAL022 reminds users to replace them before procurement.
