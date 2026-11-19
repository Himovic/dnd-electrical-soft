---
title: Electrical connections
category: Drawing
order: 8
version: 0.4.0
keywords: wire, connection, junction, potential, interruption, cross sheet, continuity
---
# Electrical connections

## Drawing wires

Select **Wire** (`W`). Click a pin to start — pins are highlighted with a cross marker. Click free space to add bend points; click another pin, a junction or an existing wire to finish. Routes are orthogonal and follow the pins' exit direction. Enter ends a wire with an open end (reported by validation).

Clicking the **middle of an existing wire** inserts a **junction** and splits the wire (T-connection).

## Connectivity

A wire stores *what* it connects (instance and pin, or junction). Moving a symbol drags its wires along and keeps them orthogonal. Two symbols that merely touch are **not** connected.

Equipotential groups (**potentials**) are computed from wires, junctions, internal symbol connections, **potential feeds** with the same name (L1, N, PE, +24V, 0V…) and **interruption points** with the same name (cross-sheet connections). Interruption points display the location of their counterparts (e.g. `/5.3`).

## Wire properties

Wire number, colour, cross-section, type, physical length and cable core are edited in the Properties panel.

## Short circuits

If one potential connects two different named supplies (e.g. L1 and N) validation reports a short circuit.
