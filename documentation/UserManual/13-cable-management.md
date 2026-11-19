---
title: Cable management
category: Engineering
order: 13
version: 0.4.0
keywords: cable, core, schedule, multi-core, routing
---
# Cable management

Create cables in **Engineering data ▸ Add cable** (type, number of cores, cross-section, length, origin, destination). Cores receive IEC 60445 style markings (BN, BK, GY, BU, GNYE) or numbers.

Assign a wire to a cable with the **Cable** field of the wire's properties; the next free core is used (protective-earth cores last). The schematic shows the core next to the wire number (e.g. `-W1:BN`).

Reports: **cable schedule** (used and spare cores) and **cable connection report** (core → wire → from/to pins).
