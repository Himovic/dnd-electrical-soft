---
title: Advanced engineering workflows
category: Help
order: 24
version: 0.4.0
keywords: workflow, cli, automation, batch, ci
---
# Advanced engineering workflows

## Typical motor-control workflow

1. Power sheet: potential feeds → protection → contactor main contacts → overload → terminals → motor.
2. Control sheet: coil with the same tag; the contact mirror appears automatically.
3. Number wires, synchronise terminal strips, create the motor cable and assign the motor wires to its cores.
4. Run cable sizing and voltage drop calculations, store the results.
5. Place devices in the cabinet; check the layout.
6. Validate (F8), then generate the handover package.

## Command-line automation

The cross-platform `dnd-electrical` tool supports batch work:

```
dnd-electrical validate project.dndx
dnd-electrical number-wires project.dndx
dnd-electrical export project.dndx --format handover --out ./handover --lang fr
dnd-electrical calc cable --current 32 --length 60 --method E --insulation XLPE
```

`validate` returns exit code 1 when errors exist, so it can gate a CI pipeline.
