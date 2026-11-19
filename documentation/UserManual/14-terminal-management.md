---
title: Terminal management
category: Engineering
order: 14
version: 0.4.0
keywords: terminal, strip, jumper, terminal diagram
---
# Terminal management

Place terminal symbols (`IEC.TERMINAL`, `IEC.TERMINAL.PE`) with the strip tag (e.g. `-X1`) and a terminal number. **Engineering ▸ Sync terminals** creates the strip definitions (terminal number, type, level, jumper group). Levels and jumper groups are stored in the project and appear in the terminal diagram; an editor for them is planned (they can be set through the API).

The **terminal diagram** is generated from actual wiring: for each terminal it lists the panel-side (inner) and field-side (outer) connections, wire numbers and jumpers.
