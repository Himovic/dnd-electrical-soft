---
title: Automatic numbering
category: Engineering
order: 9
version: 0.4.0
keywords: wire numbering, device tags, renumber, format
---
# Automatic numbering

## Device tags

New devices receive the next free tag using the project format (default `-{PREFIX}{N}`, e.g. `-K3`). Prefixes come from the symbol (Q breakers, K contactors/relays, F protection, M motors, S switches, B sensors, H lamps, X terminals, T transformers/drives, G supplies, Y valves). **Engineering ▸ Renumber devices** renumbers unlocked tags in order of appearance. Manually entered tags are locked.

## Wire numbers

**Engineering ▸ Number wires** assigns numbers:

- *Per potential* (default): all wires of a potential share one number.
- *Per wire*: each wire is numbered individually.
- Wires of named potentials take the potential name (L1, +24V…).

Format tokens: `{GSEQ}` project sequence, `{SHEET}`, `{COL}`, `{SEQ}` (sequence within the sheet), with optional zero padding (`{GSEQ:000}`). Existing numbers are kept unless you choose *renumber*; locked numbers are never changed. Numbering can be undone.
