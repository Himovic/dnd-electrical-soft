---
title: PLC configuration
category: Engineering
order: 12
version: 0.4.0
keywords: plc, rack, module, io, address, tag, channel, siemens, rockwell, schneider
---
# PLC configuration

## Stations and modules

Create a PLC station (tag, name, addressing profile), then add generic modules to racks: CPU, power supply, DI 8/16, DO 8/16, relay outputs, AI 4/8, AO 4, communication, remote I/O adapter.

## Addressing profiles

| Profile | Example DI address |
|---|---|
| Generic IEC 61131-3 | `%IX0.0` |
| Siemens-style | `%I0.0` |
| Rockwell-style | `Local:1:I.Data.0` |
| Schneider-style | `%I0.1.0` |
| Mitsubishi-style | `X000` (octal) |
| Omron-style | `CIO 0.00` |
| CODESYS-based (ABB, WAGO, Phoenix Contact) | `%IX0.0` |

Profiles are *addressing templates* — they are not vendor catalogues and do not integrate with vendor programming software. Verify addresses against your hardware configuration.

## Linking

Place PLC channel symbols (DI, DO, AI, AO) on schematics and link them to channels: the symbol displays the address and PLC tag, and the I/O list shows the schematic location, field device and terminal.

Validation checks duplicate addresses and tags, address format, missing descriptions and unlinked symbols.
