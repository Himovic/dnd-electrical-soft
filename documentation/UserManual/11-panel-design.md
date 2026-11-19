---
title: Control panel design
category: Engineering
order: 11
version: 0.4.0
keywords: cabinet, panel, enclosure, mounting plate, din rail, duct, layout, collision
---
# Control panel design

The cabinet layout is a 2D front view of the mounting plate.

1. **Engineering ▸ Engineering data ▸ Cabinet layout ▸ New cabinet**: enclosure width and height (the mounting plate is derived).
2. Add **DIN rails** and **cable ducts**.
3. **Place device** by tag: the device is placed at the first free position on a rail and centred on it; width and height come from its catalogue product.
4. Door-mounted items are stored with the *door* face and listed in the cabinet report; a door layout drawing is planned.

## Checks

The layout check reports items outside the mounting plate, collisions, clearance violations and devices placed twice. Utilisation and free rail length are shown in the cabinet report.

Cabinet items are linked to schematic devices: renaming a device updates the layout. 3D visualisation is planned.
