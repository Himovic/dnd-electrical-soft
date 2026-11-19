# Installation guide

## End users

1. Run `DND-Electrical-Engineering-Suite-Setup.exe`.
2. Accept the licence, choose the folder, optionally install the demonstration projects and a desktop shortcut.
3. Launch from the Start menu. `.dndx` files are associated with the application.

Requirements: Windows 10 2004 (build 19041) or later, 64-bit. The installer bundles the .NET runtime (self-contained publish).

Uninstalling keeps your projects and the settings folder `%LOCALAPPDATA%\DND\ElectricalSuite`.

## Building the installer

On Windows with the .NET 10 SDK and [Inno Setup 6](https://jrsoftware.org/isinfo.php):

```powershell
./scripts/publish-windows.ps1
```

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installers\DND-Electrical-Setup.iss
```

The setup is written to `artifacts/installer`.

## Command-line tool

```bash
dotnet publish src/DND.Electrical.Cli -c Release -o artifacts/cli
```

## Centralised deployment (preview)

`CentralProjectStore` supports PostgreSQL (`CentralProvider.PostgreSql`, connection string from configuration). The schema is created from the model on first use. Authentication and a server component are not part of this release.
