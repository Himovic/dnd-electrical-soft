# Developer guide

## Prerequisites

- .NET 10 SDK (`global.json` pins the 10.0 feature band).
- Windows 10 2004+ to run the WPF application. All other projects build and test on Linux/macOS.
- Optional: `dotnet-ef` 10.x for migrations; Python with `ezdxf`, `pypdf`, `openpyxl` for `scripts/verify-exports.py`.

## Solution conventions

- Central package management (`Directory.Packages.props`); shared build settings in `Directory.Build.props` (nullable enabled, nullable warnings are errors).
- Product identity is read only from `DND.Electrical.Shared.ProductInfo`.
- Engineering code must not reference UI frameworks. View models must not reference WPF.
- Every user-visible operation that changes a drawing goes through `UndoManager` (use `ModifyEntitiesAction.Apply`, `AddEntitiesAction`, `RemoveEntitiesAction` or `DelegateAction`).

## Common tasks

### Add a symbol to the built-in library
Edit `src/DND.Electrical.Symbols/BuiltInSymbolLibrary.cs` with `SymbolBuilder` (millimetres, Y up, origin at the insertion point, pins on a 5 mm grid). Increment the library version, run the unit tests (`Built_in_library_is_valid_and_complete`) and regenerate `resources/Symbols/dnd-iec-basic.dndsym.json`:

```bash
dotnet run --project src/DND.Electrical.Cli -- symbols export resources/Symbols/dnd-iec-basic.dndsym.json
```

### Add a validation rule
Add a `DelegateRule` to `BuiltInRules.All` with a new id (`VAL0xx`), default severity, message, suggestion and, if a safe automatic correction exists, a fix id handled in `MainViewModel.ApplyFix`. Add tests and document the rule in `documentation/UserManual/17-validation.md`.

### Add a calculation
Implement a static calculator returning `CalculationResult` (inputs, steps with formulas, outputs, checks, assumptions, warnings, standard profile). Add hand-computed tests, a `CalculatorDefinition` in `CalculationsViewModel`, and document the method in `25-calculation-methodology.md`. Update `StandardProfile` clauses if a standard is involved.

### Change the database schema
```bash
cd src/DND.Electrical.Infrastructure
dotnet ef migrations add <Name> --output-dir Persistence/Migrations
```
Never edit an existing migration. Old projects are migrated on a temporary copy when opened.

### Add a CAD entity type
Derive from `CadEntity`, implement bounds, distance, transform, snap points and `DeepCopyFrom` for reference members, register it in `CadEntityTypeRegistry`, and implement `IExplodableEntity` (and `IDxfBlockSource` if it should export as a block).

### Write a plugin
Reference `DND.Electrical.Plugins` (contracts only, `Private=false`), implement `IPlugin`, ship `plugin.json` with the SHA-256 of the assembly and request minimal permissions. See `samples/DND.Electrical.Plugins.Sample`.

## Testing
See [Testing.md](Testing.md).

## Logging
Serilog writes daily rolling files to `%LOCALAPPDATA%\DND\ElectricalSuite\Logs` (14 days retained).
