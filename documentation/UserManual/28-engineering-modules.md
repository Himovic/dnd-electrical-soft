---
title: Engineering modules (PowerStudio, Automation, 3D, BIM, Digital twin, Integrations)
category: Modules
order: 28
version: 0.5.0
keywords: powerstudio, load flow, short circuit, protection, harmonics, automation, instrument, loop, alarm, cause and effect, 3D, cabinet, ifc, bim, digital twin, modbus, mqtt, simulator, integration, webhook, assistant, claude
---
# Engineering modules

Each module opens from the ribbon and requires its licence module (Help → Licensing shows what is licensed). All modules work on the same project and the same engineering objects: selecting a device in 3D and choosing *Show in schematic* opens its symbol.

## D&D PowerStudio (POWER)

1. **Ribbon → PowerStudio.** Create a network, build one from the project's load list (placeholders are listed in the findings and must be replaced), import a network JSON, or open the demo or IEEE 14-bus networks.
2. Edit buses, utility feeders, transformers (vector group matters for earth faults), lines, loads, generators, shunts and protection devices in the tables.
3. Run **Load flow**, **Short circuit (IEC 60909)**, **Protection coordination** (set *UpstreamId* on each device) and **Harmonic scan**. Data errors (unknown buses, islands) are listed before any study runs.
4. Export the study report (text) and the time-current chart (SVG).

Assumptions are listed with every result. Motor contributions and arc-flash are not calculated.

## Automation (AUTO)

Instruments (tags, ranges, signals, junction box, hazardous zone and Ex marking from the certificate), control loops, alarms, cause and effect matrices and the control architecture. *Assign PLC I/O* checks signal type, direction and occupancy. *Propose alarms* uses fractions of span you enter and asks for confirmation. *Generate loop diagram* creates or regenerates a sheet. In the cause and effect tab, click cells to set marks and tick causes to review which effects the documented logic triggers. Validation rules AUT001–AUT007 check the data.

## 3D layout (3D) and IFC (BIM)

*Generate 3D from 2D layout* builds the cabinet from its 2D layout (same item and device identities). *Check clashes* reports intersections and clearance violations; *Check 2D/3D sync* lists differences; *Move…* can write a 3D move back to the 2D layout after confirmation. Add equipment and cable tray routes (tray fill uses cable outer diameters; cables without diameter are listed). Section, explode and export GLB/glTF/STL; with BIM, export IFC4 or import IFC context geometry.

## Digital twin (TWIN)

Add points bound to instruments or devices. Sources: Modbus TCP (`host:port/unit`, address `HR:100:f32`), MQTT (`host:port`, topic) or the simulator. **Values from the simulator are flagged and an orange SIMULATED DATA banner is shown.** Active alarms use the project's alarm set points and deadbands.

## Integrations (INTEGRATION)

ERP BOM file drop, CMMS asset register and a signed webhook. Planned connectors are listed but cannot be run.

## Assistant (AI)

*Assistant settings…* selects the local assistant or Claude, the model, whether a project summary may be shared (off by default) and the API key (stored encrypted for your Windows user). Accept or reject each proposal; corrections still ask for confirmation, and every interaction is recorded in the project.
