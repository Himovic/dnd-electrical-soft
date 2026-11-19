# Project templates

Project templates are defined in code (`src/DND.Electrical.Application/Templates/ProjectTemplates.cs`) so that they always use the current symbol library and schema:

Blank · Industrial installation · Manufacturing plant · Oil and Gas · Water treatment · Power distribution · Building installation · HVAC systems · Industrial automation · Renewable energy.

Reusable circuit fragments (DOL motor feeder, start/stop rung, signal lamp rung, 24 V power supply) are provided by `CircuitBuilder` and used by the templates and the demonstration projects.
