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

This build does not contain a licence-activation mechanism. Commercial licensing will be added in a later release; until then all features are available.

## Data locations

| Data | Location |
|---|---|
| Settings, recovery files, logs | `%LOCALAPPDATA%\DND\ElectricalSuite` |
| Custom symbol libraries | `%LOCALAPPDATA%\DND\ElectricalSuite\Symbols` |
| Custom help articles | `%LOCALAPPDATA%\DND\ElectricalSuite\Documentation` |
| Projects | Wherever you save them (`.dndx` files) |
