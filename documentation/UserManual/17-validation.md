---
title: Validation and verification
category: Engineering
order: 17
version: 0.4.0
keywords: validation, check, errors, warnings, rules
---
# Validation and verification

**Engineering ▸ Validate** (F8) runs the rule set and lists errors, warnings and information messages with the drawing location (`/sheet.column`) and a suggested correction. Double-click a message to open the sheet and select the object.

Rules include: missing or duplicate references, unconnected pins, open wire ends, broken references, missing or inconsistent wire numbers, short circuits between supplies, invalid PLC addresses, missing equipment data, cable assignments, terminal numbering, cross-reference consistency (contacts without coil, unmatched interruption points), missing part numbers, incomplete title blocks, invalid project data, broken equipment relationships, junction problems, cabinet collisions, missing symbol definitions and fictional catalogue data.

Rules can be disabled or their severity changed in Tools ▸ Settings (current project section). Validation **never modifies** the design; suggested fixes (such as numbering wires) run only when you confirm them. Each run is stored in the validation history.
