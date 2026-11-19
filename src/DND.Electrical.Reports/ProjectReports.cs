using DND.Electrical.Domain;
using DND.Electrical.PLC;
using DND.Electrical.Panel;
using DND.Electrical.Schematic.Connectivity;
using DND.Electrical.Schematic.Services;
using DND.Electrical.Shared;
using DND.Electrical.Symbols;
using DND.Electrical.Validation;
using DND.Electrical.Wiring;

namespace DND.Electrical.Reports;

/// <summary>Builds report tables from actual project data (never from static samples).</summary>
public sealed class ProjectReports(ElectricalProject project, ConnectivityGraph graph, ReportOptions? options = null)
{
    private readonly ReportOptions _o = options ?? new ReportOptions();
    private string T(string key) => L.T(key, _o.Language);

    public static IReadOnlyList<(string id, string title)> Catalogue(string language) => new[]
    {
        "summary", "sheets", "bom", "equipment", "motors", "loads", "cables", "cablecores", "wires", "terminals", "plcio", "xref", "cabinet", "calculations", "validation", "revisions",
    }.Select(id => (id, L.T(id, language))).ToList();

    public ReportTable Build(string id) => id switch
    {
        "summary" => Summary(),
        "sheets" => SheetIndex(),
        "bom" => BillOfMaterials(),
        "equipment" => EquipmentList(),
        "motors" => MotorList(),
        "loads" => LoadList(),
        "cables" => CableSchedule(),
        "cablecores" => CableCores(),
        "wires" => WireList(),
        "terminals" => TerminalDiagram(),
        "plcio" => PlcIoList(),
        "xref" => CrossReference(),
        "cabinet" => CabinetReport(),
        "calculations" => Calculations(),
        "validation" => ValidationReportTable(new ValidationEngine().Run(project, graph)),
        "revisions" => Revisions(),
        _ => throw new ArgumentException($"Unknown report '{id}'."),
    };

    public ReportTable Summary()
    {
        var i = project.Info;
        var t = new ReportTable { Id = "summary", Title = T("summary"), Subtitle = $"{i.Number} – {i.Name}" }.Col("Item", 1).Col("Value", 3);
        t.Row(T("project"), $"{i.Number} – {i.Name}").Row(T("description"), i.Description).Row(T("customer"), i.CustomerName)
            .Row(T("company"), i.CompanyName).Row(T("engineer"), i.Engineer).Row(T("revision"), i.CurrentRevision).Row(T("status"), i.Status)
            .Row("Industry", i.Industry).Row("Site", i.Site).Row("Reference", i.Reference)
            .Row("Sheets", project.Sheets.Count).Row("Devices", project.Devices.Count).Row("Wires", graph.Wires.Count)
            .Row("Potentials", graph.Potentials.Count).Row("Cables", project.Cables.Count).Row("Terminal strips", project.TerminalStrips.Count)
            .Row("PLC stations / channels", $"{project.PlcStations.Count} / {project.PlcStations.Sum(p => p.Channels.Count())}")
            .Row("Motors", project.Motors.Count).Row("Cabinets", project.Cabinets.Count).Row("Calculations", project.Calculations.Count)
            .Row("Supply", $"{project.Settings.NominalVoltage:0} V {project.Settings.FrequencyHz:0} Hz {project.Settings.EarthingSystem}")
            .Row("Standard profile", project.Settings.StandardProfileId)
            .Row("BOM total", $"{BomService.TotalCost(BomService.Build(project)):0.00} {project.Settings.Currency}");
        if (i.IsDemo) t.Notes.Add(T("demo"));
        return t;
    }

    public ReportTable SheetIndex()
    {
        var t = new ReportTable { Id = "sheets", Title = T("sheets") }.Col(T("sheet"), 0.6, ColumnAlign.Center).Col("Title", 3).Col("Type", 1).Col(T("revision"), 0.6, ColumnAlign.Center).Col("Format", 0.6).Col(T("description"), 3);
        foreach (var s in project.OrderedSheets) t.Row(s.Number, s.Title, s.Kind, s.Revision, s.FormatName, s.Description);
        return t;
    }

    public ReportTable BillOfMaterials()
    {
        var lines = BomService.Build(project);
        var t = new ReportTable { Id = "bom", Title = T("bom"), Subtitle = project.Info.Number }
            .Col("#", 0.4, ColumnAlign.Right).Col(T("quantity"), 0.6, ColumnAlign.Right).Col(T("unit"), 0.5).Col(T("manufacturer"), 1.3).Col(T("partnumber"), 1.5)
            .Col(T("description"), 2.6).Col(T("tag"), 1.8).Col(T("unitprice"), 0.9, ColumnAlign.Right).Col(T("total"), 0.9, ColumnAlign.Right).Col(T("status"), 0.9);
        var n = 1;
        foreach (var l in lines)
            t.Row(n++, l.Quantity, l.Unit, l.Manufacturer, l.PartNumber + (l.IsDemoData ? " *" : ""), l.Description, string.Join(", ", l.Tags), l.UnitPrice, l.Total, l.Status);
        t.Notes.Add($"{T("total")}: {BomService.TotalCost(lines):0.00} {project.Settings.Currency}");
        if (lines.Any(l => l.IsDemoData)) t.Notes.Add("* Fictional demonstration product – replace with verified catalogue data before procurement.");
        return t;
    }

    public ReportTable EquipmentList()
    {
        var occ = project.OccurrencesByDevice();
        var t = new ReportTable { Id = "equipment", Title = T("equipment") }
            .Col(T("tag"), 0.8).Col(T("function"), 2).Col("Category", 1).Col(T("location"), 0.8).Col(T("manufacturer"), 1.2).Col(T("partnumber"), 1.3).Col("Rating", 1).Col(T("sheet"), 1.2);
        foreach (var d in project.Devices.OrderBy(d => d.Tag, NaturalStringComparer.Instance))
            t.Row(d.Tag, d.Function, d.Category, d.Location, d.Manufacturer, d.PartNumber, d.GetProperty(DeviceProperties.Rating),
                string.Join(" ", occ[d.Id].Select(o => o.Location(project.Settings).ToString()).Distinct()));
        return t;
    }

    public ReportTable MotorList()
    {
        var t = new ReportTable { Id = "motors", Title = T("motors") }
            .Col(T("tag"), 0.7).Col(T("description"), 1.8).Col("kW", 0.6, ColumnAlign.Right).Col("V", 0.5, ColumnAlign.Right).Col("In [A]", 0.6, ColumnAlign.Right)
            .Col("cos φ", 0.5, ColumnAlign.Right).Col("η", 0.5, ColumnAlign.Right).Col("rpm", 0.6, ColumnAlign.Right).Col("Starting", 1.1).Col("Ist/In", 0.5, ColumnAlign.Right).Col("Protection", 0.8).Col(T("manufacturer"), 1);
        foreach (var m in project.Motors.OrderBy(m => m.Tag, NaturalStringComparer.Instance))
            t.Row(m.Tag, m.Description, m.PowerKw, m.VoltageV, m.RatedCurrentA, m.PowerFactor, m.Efficiency, m.SpeedRpm, m.StartingMethod, m.StartingCurrentRatio, m.ProtectionDeviceTag, m.Manufacturer);
        t.Notes.Add($"Installed motor power: {project.Motors.Sum(m => m.PowerKw):0.##} kW");
        return t;
    }

    public ReportTable LoadList()
    {
        var t = new ReportTable { Id = "loads", Title = T("loads") }
            .Col("Board", 0.7).Col("Circuit", 0.6).Col(T("tag"), 0.7).Col(T("description"), 2).Col("Category", 0.9).Col("Phase", 0.6).Col("P [kW]", 0.6, ColumnAlign.Right)
            .Col("cos φ", 0.5, ColumnAlign.Right).Col("ku", 0.4, ColumnAlign.Right).Col("Pd [kW]", 0.6, ColumnAlign.Right).Col("In [A]", 0.6, ColumnAlign.Right).Col("Cable", 0.8).Col("L [m]", 0.5, ColumnAlign.Right);
        foreach (var l in project.Loads.OrderBy(l => l.Board).ThenBy(l => l.Circuit, NaturalStringComparer.Instance))
        {
            var phases = l.Phase == PhaseAssignment.ThreePhase ? Math.Sqrt(3) : 1;
            var current = l.VoltageV > 0 && l.PowerFactor > 0 ? l.PowerKw * 1000 / (phases * l.VoltageV * l.PowerFactor) : 0;
            t.Row(l.Board, l.Circuit, l.Tag, l.Description, l.Category, l.Phase, l.PowerKw, l.PowerFactor, l.DemandFactor, Math.Round(l.PowerKw * l.DemandFactor, 3),
                Math.Round(current, 1), l.CableCrossSectionMm2 > 0 ? $"{l.CableCrossSectionMm2:0.##} mm²" : "", l.CableLengthM);
        }
        foreach (var g in project.Loads.GroupBy(l => l.Board))
            t.Notes.Add($"{g.Key}: installed {g.Sum(l => l.PowerKw):0.##} kW, demand {g.Sum(l => l.PowerKw * l.DemandFactor):0.##} kW");
        return t;
    }

    public ReportTable CableSchedule()
    {
        var t = new ReportTable { Id = "cables", Title = T("cables") }
            .Col(T("tag"), 0.6).Col("Type", 1).Col("Cores", 0.9).Col("L [m]", 0.5, ColumnAlign.Right).Col(T("from"), 1.2).Col(T("to"), 1.2).Col("Used", 0.5, ColumnAlign.Right).Col("Spare", 0.5, ColumnAlign.Right).Col("Route", 1);
        foreach (var c in WiringReports.CableSchedule(project)) t.Row(c.Tag, c.Type, c.Cores, c.LengthM, c.From, c.To, c.UsedCores, c.SpareCores, c.Route);
        t.Notes.Add($"Total cable length: {project.Cables.Sum(c => c.LengthM):0.#} m");
        return t;
    }

    public ReportTable CableCores()
    {
        var t = new ReportTable { Id = "cablecores", Title = T("cablecores") }.Col("Cable", 0.7).Col("Core", 0.4, ColumnAlign.Right).Col("Marking", 0.6).Col(T("wire"), 0.6).Col(T("from"), 1.5).Col(T("to"), 1.5);
        foreach (var r in WiringReports.CableCores(project, graph)) t.Row(r.Cable, r.Core, r.Marking, r.WireNumber, r.From, r.To);
        return t;
    }

    public ReportTable WireList()
    {
        var t = new ReportTable { Id = "wires", Title = T("wires") }
            .Col(T("wire"), 0.6).Col(T("from"), 1.5).Col(T("to"), 1.5).Col(T("potential"), 0.7).Col("Colour", 0.5).Col("mm²", 0.4, ColumnAlign.Right).Col("Cable", 0.7).Col(T("sheet"), 1.2);
        foreach (var r in WiringReports.ConnectionList(project, graph)) t.Row(r.WireNumber, r.From, r.To, r.Potential, r.Color, r.CrossSection, r.Cable, r.Sheet);
        return t;
    }

    public ReportTable TerminalDiagram()
    {
        var t = new ReportTable { Id = "terminals", Title = T("terminals") }
            .Col("Strip", 0.6).Col("Terminal", 0.6, ColumnAlign.Center).Col("Level", 0.4, ColumnAlign.Center).Col("Internal (panel side)", 1.6).Col("External (field side)", 1.6).Col(T("wire"), 0.7).Col("Jumper", 0.5).Col(T("sheet"), 0.6);
        foreach (var r in WiringReports.TerminalDiagram(project, graph)) t.Row(r.Strip, r.Terminal, r.Level, r.InternalTargets, r.ExternalTargets, r.WireNumbers, r.Jumper, r.Location);
        return t;
    }

    public ReportTable PlcIoList()
    {
        var t = new ReportTable { Id = "plcio", Title = T("plcio") }
            .Col("Station", 0.5).Col("Slot", 0.4, ColumnAlign.Center).Col("Module", 1.4).Col("Ch", 0.3, ColumnAlign.Center).Col("Type", 0.4).Col("Address", 0.9).Col("PLC tag", 1.1).Col(T("description"), 1.8).Col("Signal", 0.8).Col("Device", 0.6).Col(T("sheet"), 0.6);
        foreach (var r in PlcService.IoList(project))
            t.Row(r.Station, r.Slot, r.Module, r.Channel, r.Kind, r.Address, r.Spare ? "(spare)" : r.Tag, r.Description, r.SignalType, r.Device, r.SchematicLocation);
        return t;
    }

    public ReportTable CrossReference()
    {
        var t = new ReportTable { Id = "xref", Title = T("xref") }.Col(T("tag"), 0.7).Col("Symbol", 1.6).Col("Role", 0.8).Col("Pins", 1.4).Col(T("sheet"), 0.6).Col(T("function"), 1.6);
        var occ = project.OccurrencesByDevice();
        foreach (var d in project.Devices.OrderBy(d => d.Tag, NaturalStringComparer.Instance))
            foreach (var o in occ[d.Id].OrderBy(o => o.Sheet.Number).ThenBy(o => o.Instance.Position.X))
                t.Row(d.Tag, o.Instance.Definition?.Name ?? o.Instance.SymbolId, o.Instance.Definition?.Role,
                    string.Join(" ", o.Instance.Definition?.Pins.Select(p => o.Instance.GetPinLabel(p.Id)) ?? Array.Empty<string>()),
                    o.Location(project.Settings).ToString(), d.Function);
        return t;
    }

    public ReportTable CabinetReport()
    {
        var t = new ReportTable { Id = "cabinet", Title = T("cabinet") }.Col("Cabinet", 0.6).Col("Item", 1).Col("Kind", 0.8).Col(T("partnumber"), 1.2).Col("X", 0.5, ColumnAlign.Right).Col("Y", 0.5, ColumnAlign.Right).Col("W×H×D [mm]", 1.1);
        foreach (var c in project.Cabinets)
        {
            CabinetLayoutService.SyncNames(project, c);
            foreach (var i in c.Items.OrderBy(i => i.Kind).ThenByDescending(i => i.Y).ThenBy(i => i.X))
                t.Row(c.Tag, i.Name, i.Kind, i.PartNumber, i.X, i.Y, $"{i.Width:0}×{i.Height:0}×{i.Depth:0}");
            var u = CabinetLayoutService.Utilization(c);
            t.Notes.Add($"{c.Tag}: plate {c.PlateWidth:0}×{c.PlateHeight:0} mm, utilisation {u.Percent:0.#} %, free DIN rail {u.FreeRailLengthMm:0} mm, issues: {CabinetLayoutService.Check(c).Count}");
        }
        return t;
    }

    public ReportTable Calculations()
    {
        var t = new ReportTable { Id = "calculations", Title = T("calculations") }.Col("Calculation", 1.3).Col("Type", 0.9).Col(T("status"), 0.6).Col("Result", 2.4).Col("Standard / profile", 1.4).Col(T("date"), 0.8);
        foreach (var c in project.Calculations.OrderBy(c => c.Created)) t.Row(c.Title, c.CalculationType, c.Status, c.Summary, c.StandardProfileId, c.Created.ToString(_o.DateFormat));
        t.Notes.Add("Preliminary engineering calculations – verification by a qualified engineer is required. No certification of compliance is implied.");
        return t;
    }

    public ReportTable ValidationReportTable(ValidationReport report)
    {
        var t = new ReportTable { Id = "validation", Title = T("validation"), Subtitle = report.Summary }
            .Col("Rule", 0.5).Col(T("severity"), 0.7).Col(T("message"), 2.6).Col(T("sheet"), 0.5).Col("Object", 0.6).Col(T("suggestion"), 2);
        foreach (var i in report.Issues) t.Row(i.RuleId, i.Severity, i.Message, i.Location, i.ObjectRef, i.Suggestion);
        return t;
    }

    public ReportTable Revisions()
    {
        var t = new ReportTable { Id = "revisions", Title = T("revisions") }.Col(T("revision"), 0.5, ColumnAlign.Center).Col(T("date"), 0.8).Col("Author", 1).Col(T("status"), 0.7).Col(T("description"), 3);
        foreach (var r in project.Revisions.OrderBy(r => r.Date)) t.Row(r.Code, r.Date.ToString(_o.DateFormat), r.Author, r.Status, r.Description);
        return t;
    }
}
