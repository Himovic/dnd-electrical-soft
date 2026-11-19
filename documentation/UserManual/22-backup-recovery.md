---
title: Backup and recovery
category: Advanced
order: 22
version: 0.4.0
keywords: backup, recovery, autosave, crash, restore
---
# Backup and recovery

- **Backups**: before each save, the previous file is copied to `<project>.dndx.backups` with a timestamp (rolling, default 10).
- **Restore**: Project ▸ Version history ▸ Restore. The current file is backed up first.
- **Autosave**: unsaved changes are written to the recovery folder every few minutes.
- **After a crash**: the start page lists recoverable projects newer than their saved file. Recovering opens the copy; saving writes to the original location.
