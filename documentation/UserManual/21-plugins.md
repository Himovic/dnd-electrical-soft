---
title: Plugin management
category: Advanced
order: 21
version: 0.4.0
keywords: plugin, extension, security, permissions
---
# Plugin management

Plugins extend the suite (e.g. solar PV, lighting calculations). Each plugin is a folder in `%LOCALAPPDATA%\DND\ElectricalSuite\Plugins` containing `plugin.json` and its assembly.

The manifest declares id, name, version, compatible host versions, entry assembly and type, requested permissions and the SHA-256 hash of the assembly.

**Security:** a plugin is loaded only when it is compatible, its hash matches the manifest **and** an administrator has trusted that exact hash in **Tools ▸ Plugins** (the trust list is saved in the user settings). Plugins receive only the host services allowed by their granted permissions (project read, project write, UI commands, file export).
