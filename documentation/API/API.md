# API overview

The suite is a modular .NET library set; the main entry points for automation and integration are:

| Purpose | Type |
|---|---|
| Open/save projects | `IProjectStore` (`SqliteProjectStore`), `ProjectService` |
| Build schematics in code | `SchematicService` (`AddSheet`, `PlaceSymbol`, `Connect`, `AddJunction`), `CircuitBuilder` |
| Connectivity | `ConnectivityGraph.Build(project)` |
| Numbering | `WireNumberingService.Apply`, `DeviceTaggingService` |
| Validation | `new ValidationEngine().Run(project, graph)` |
| Reports | `ProjectReports`, `PdfDocumentComposer`, `XlsxReportWriter`, `CsvWriter`, `DocumentationService` |
| Calculations | `PowerCalculator`, `VoltageDropCalculator`, `CableSizingCalculator`, `ShortCircuitCalculator`, … |
| Interchange | `ProjectInterchange.ToJson/FromJson/ToEngineeringXml`, `DxfWriter`, `DxfReader` |
| Plugins | `IPlugin`, `IPluginHost`, `PluginManager` |

Example:

```csharp
var library = new SymbolLibraryService();
var resolver = new SymbolResolver(library);
var schematic = new SchematicService(resolver, new SchematicBinder(resolver));
var project = new ElectricalProject { Info = { Number = "P-1", Name = "Example" } };
var sheet = schematic.AddSheet(project, "Lighting");
var cb = schematic.PlaceSymbol(project, sheet, "IEC.CB.1P", new Point2(60, 220));
var lamp = schematic.PlaceSymbol(project, sheet, "IEC.LAMP", new Point2(60, 170));
schematic.Connect(sheet, cb, "2", lamp, "X1");
var graph = schematic.Binder.BindWithConnectivity(project);
WireNumberingService.Apply(project, graph);
await new SqliteProjectStore().SaveAsync(project, "example.dndx");
```

A REST API for centralised deployments is planned; it is **not implemented** in this release. The command-line tool (`dnd-electrical`) is the supported automation interface today.
