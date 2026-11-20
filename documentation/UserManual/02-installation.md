---
title: Installation and activation
category: Getting started
order: 2
version: 0.4.0
keywords: install, setup, requirements, activation, license
---
# Installation and activation

## Requirements

| Item | Requirement |
|---|---|
| Operating system | Windows 10 (22H2) or Windows 11, 64-bit |
| Runtime | .NET 10 Desktop Runtime (included in the self-contained installer) |
| Memory | 8 GB recommended |
| Display | 1600×900 or larger recommended |

The command-line tool `dnd-electrical` also runs on Linux and macOS.

## Installing

1. Run the installer `DND-Electrical-Engineering-Suite-Setup.exe` (built with `installers/DND-Electrical-Setup.iss`).
2. Choose the installation folder and whether to install the demo projects.
3. Start the application from the Start menu.

## Activation

Open **Help ▸ Licensing**: install the evaluation licence, activate online with your licence key, or import a licence file (offline activation). Modules that are not licensed show an upgrade page; without ELEC, projects open read-only. Project data is never deleted when a licence expires. See the PDF user manual, chapter *Licensing and activation*.

## Data locations

| Data | Location |
|---|---|
| Settings, recovery files, logs | `%LOCALAPPDATA%\DND\ElectricalSuite` |
| Custom symbol libraries | `%LOCALAPPDATA%\DND\ElectricalSuite\Symbols` |
| Custom help articles | `%LOCALAPPDATA%\DND\ElectricalSuite\Documentation` |
| Projects | Wherever you save them (`.dndx` files) |
