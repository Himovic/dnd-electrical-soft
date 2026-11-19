---
title: Symbol management
category: Drawing
order: 7
version: 0.4.0
keywords: symbol, library, symbol editor, custom symbols, import, export, pins
---
# Symbol management

## Symbol Browser

The browser lists all symbols by category with a preview. Search by name, id or keyword (English and French keywords are included). Double-click a symbol to place it: **R** rotates, **X** mirrors, **Esc** stops.

## Built-in library

*D&D IEC Basic* contains original IEC 60617-style symbols: circuit breakers (1–4 poles), motor protection breakers, fuses, RCDs, switch-disconnectors, contactor coils and main contacts, auxiliary contacts (NO/NC/change-over), overload relays, motors, transformers, power supplies, VFDs, soft starters, push buttons, emergency stops, selector and limit switches, proximity/photoelectric/level sensors, temperature and pressure transmitters, lamps, horns, solenoid valves, heaters, socket outlets, terminals, connectors, potential feeds, interruption points and PLC I/O channels.

## Electrical behaviour

Each symbol declares a **role** (standalone device, main function such as a coil, child contact, terminal, potential feed, interruption point, PLC channel, connector), its **pins** (with exit direction), **internal connections** (e.g. both sides of a terminal) and a default **tag prefix**.

## Custom symbols

1. Tools ▸ Symbol editor.
2. Start from scratch or duplicate a built-in symbol (built-in libraries are read-only).
3. Draw graphics, add pins and attributes, set the role and tag prefix.
4. Save to a custom library. Each save increments the symbol version.

Libraries are JSON files (`*.dndsym.json`) that can be imported and exported. Projects keep a copy of every symbol they use, so they open correctly on other computers.
