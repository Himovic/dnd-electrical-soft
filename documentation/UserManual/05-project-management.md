---
title: Project management
category: Projects
order: 5
version: 0.4.0
keywords: save, open, revision, backup, lock, archive, duplicate, attachments, status
---
# Project management

## Saving

Saving is **atomic**: the project is written to a temporary file which then replaces the original. Before every save the previous version is copied to `<project>.dndx.backups` (the last 10 are kept). Use **Project ▸ Version history** to restore one.

## Autosave and recovery

Every few minutes (Tools ▸ Settings) unsaved changes are written to a *recovery copy* — never to your file. After a crash the start page offers to recover it.

## Locking

Opening a project creates `<project>.dndx.lock`. If somebody else has it open you see who and since when, and can open it anyway only by explicitly forcing the lock.

## Revisions and status

**Project ▸ New revision** issues a revision (code, description, author). All sheets take the new revision code. The project status (Draft, In progress, For review, Approved, For construction, As built, Archived) appears in title blocks.

## Archives, duplicates and attachments

- **Export archive** creates a `.dndpkg` package (ZIP with manifest). Import validates the package before extracting it.
- **Duplicate** copies a project with a new identity and number.
- **Attachments** (datasheets, photos, PDFs up to 100 MB) are stored inside the project file.
- Every significant action is recorded in the **audit trail**.
