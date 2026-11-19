---
title: Troubleshooting
category: Help
order: 23
version: 0.4.0
keywords: problem, error, troubleshoot, locked, symbol missing, log
---
# Troubleshooting

| Symptom | Solution |
|---|---|
| "The project is being edited by …" | Someone else has the project open. If you are sure they don't (e.g. after a crash on another PC), choose *Open anyway*. |
| A symbol is drawn as a red box with `?` | The symbol is missing from the project cache and the libraries. Import the library that defines it. |
| "is not a D&D project file" | The file is not a `.dndx` database (perhaps truncated by a copy). Restore a backup. |
| Wire numbers do not change | Numbers are kept unless you choose *Renumber*; locked numbers never change. |
| Contact shows no `/x.y` reference | Its tag does not match a device that has a coil (main function). |
| DXF import shows skipped entities | HATCH, SPLINE, ELLIPSE and some others are not imported yet; the import report lists them. |

Logs are written to `%LOCALAPPDATA%\DND\ElectricalSuite\Logs`. Include the log when reporting a problem.
