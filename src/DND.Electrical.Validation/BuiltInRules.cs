using DND.Electrical.CAD.Model;
using DND.Electrical.Domain;
using DND.Electrical.Panel;
using DND.Electrical.PLC;
using DND.Electrical.Schematic.Entities;
using DND.Electrical.Schematic.Services;
using DND.Electrical.Symbols;

namespace DND.Electrical.Validation;

internal sealed class DelegateRule(string id, string title, string category, Severity severity, string description,
    Func<ValidationContext, DelegateRule, IEnumerable<ValidationIssue>> check) : IValidationRule
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    public string Category { get; } = category;
    public string Description { get; } = description;
    public Severity DefaultSeverity { get; } = severity;
    public IEnumerable<ValidationIssue> Check(ValidationContext context) => check(context, this);

    public ValidationIssue Issue(string message, string suggestion, Sheet? sheet = null, Guid? entity = null, string location = "", string obj = "", Severity? severity = null, string? fix = null) =>
        new(Id, severity ?? DefaultSeverity, message, suggestion, sheet?.Id, entity, location, obj, fix);
}

public static class BuiltInRules
{
    public const string FixNumberWires = "number-wires";
    public const string FixSyncTerminals = "sync-terminals";
    public const string FixRenumberDevices = "renumber-devices";

    public static readonly IReadOnlyList<IValidationRule> All = new IValidationRule[]
    {
        new DelegateRule("VAL001", "Missing component reference", "Components", Severity.Error,
            "Symbols that represent a device must be linked to an existing device.", (c, r) =>
                c.Project.SymbolInstances()
                    .Where(x => x.instance.Definition is { } d && SchematicService.NeedsDevice(d))
                    .Where(x => x.instance.DeviceId is null || c.Project.FindDevice(x.instance.DeviceId.Value) is null)
                    .Select(x => r.Issue($"Symbol {x.instance.Definition!.Name} has no device reference.", "Assign a device tag in the properties panel.",
                        x.sheet, x.instance.Id, c.Loc(x.sheet, x.instance.Position)))),

        new DelegateRule("VAL002", "Duplicate references", "Components", Severity.Error,
            "Device, cable, terminal strip and PLC tags must be unique.", (c, r) =>
                DeviceTaggingService.DuplicateTags(c.Project).Select(t => r.Issue($"Device tag {t} is used by several devices.", "Rename one of the devices or run automatic renumbering.", obj: t, fix: FixRenumberDevices))
                .Concat(c.Project.Cables.GroupBy(x => x.Tag, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
                    .Select(g => r.Issue($"Cable tag {g.Key} is used {g.Count()} times.", "Rename the cables.", obj: g.Key)))),

        new DelegateRule("VAL003", "Unconnected terminals", "Connections", Severity.Warning,
            "Device pins should be connected. Completely unused terminals are reported as information (spares).", (c, r) => UnconnectedPins(c, r)),

        new DelegateRule("VAL004", "Incomplete connections", "Connections", Severity.Error,
            "Wires must be connected at both ends.", (c, r) =>
                c.Graph.Wires.Where(w => !w.Wire.Start.IsConnected || !w.Wire.End.IsConnected)
                    .Select(w => r.Issue($"Wire {Num(w.Wire)} has an open end.", "Connect the wire end to a pin or junction, or delete the wire.",
                        w.Sheet, w.Wire.Id, c.Loc(w.Sheet, w.Wire.Start.IsConnected ? w.Wire.EndPoint : w.Wire.StartPoint), Num(w.Wire)))),

        new DelegateRule("VAL005", "Broken connection references", "Connections", Severity.Error,
            "Wire ends must reference existing symbols and junctions.", (c, r) =>
                c.Graph.BrokenReferences.Select(b => r.Issue($"Wire {Num(b.wire.Wire)} references a deleted object at its {(b.atStart ? "start" : "end")}.",
                    "Reconnect the wire end.", b.wire.Sheet, b.wire.Wire.Id, c.Loc(b.wire.Sheet, b.atStart ? b.wire.Wire.StartPoint : b.wire.Wire.EndPoint)))),

        new DelegateRule("VAL006", "Missing wire numbers", "Wiring", Severity.Warning,
            "Every wire should carry a wire number.", (c, r) =>
            {
                var missing = c.Graph.Wires.Where(w => string.IsNullOrWhiteSpace(w.Wire.WireNumber)).ToList();
                if (missing.Count == 0) return Array.Empty<ValidationIssue>();
                return missing.Take(50).Select(w => r.Issue("Wire without wire number.", "Run automatic wire numbering.", w.Sheet, w.Wire.Id, c.Loc(w.Sheet, w.Wire.StartPoint), fix: FixNumberWires))
                    .Concat(missing.Count > 50 ? new[] { r.Issue($"{missing.Count - 50} further wires without number.", "Run automatic wire numbering.", fix: FixNumberWires) } : Array.Empty<ValidationIssue>());
            }),

        new DelegateRule("VAL007", "Inconsistent wire numbers", "Wiring", Severity.Warning,
            "Wires of one potential share a number; one number must not be used on different potentials.", (c, r) => InconsistentNumbers(c, r)),

        new DelegateRule("VAL008", "Short circuit between supplies", "Connections", Severity.Error,
            "A potential must not connect different named supplies (e.g. L1 and N, +24V and 0V).", (c, r) =>
                c.Graph.Potentials.Where(p => p.HasNameConflict).Select(p =>
                {
                    var w = p.Wires.FirstOrDefault();
                    return r.Issue($"Supplies {string.Join(", ", p.SourceNames)} are connected together.", "Check the wiring between the supply symbols – this would be a short circuit.",
                        w?.Sheet, w?.Wire.Id, w is null ? "" : c.Loc(w.Sheet, w.Wire.StartPoint), string.Join("/", p.SourceNames));
                })),

        new DelegateRule("VAL009", "Invalid PLC addresses", "PLC", Severity.Error,
            "PLC addresses must be unique and follow the station's addressing profile.", (c, r) =>
                PlcService.Validate(c.Project).Select(i => r.Issue(i.Message, "Correct the address in the PLC editor or re-run address assignment.", entity: i.ChannelId,
                    severity: i.Severity switch { "Error" => Severity.Error, "Warning" => Severity.Warning, _ => Severity.Information }))),

        new DelegateRule("VAL010", "Missing equipment properties", "Equipment", Severity.Warning,
            "Motors and key devices need their rating data.", (c, r) =>
                c.Project.Motors.Where(m => m.PowerKw <= 0 || m.VoltageV <= 0 || m.RatedCurrentA <= 0)
                    .Select(m => r.Issue($"Motor {m.Tag} is missing power, voltage or rated current.", "Complete the motor data in the equipment database.", obj: m.Tag))
                .Concat(c.Project.Devices.Where(d => string.IsNullOrWhiteSpace(d.Function))
                    .Select(d => r.Issue($"Device {d.Tag} has no function text.", "Describe the device function (shown on schematics and reports).", obj: d.Tag, severity: Severity.Information)))),

        new DelegateRule("VAL011", "Missing cable assignments", "Cables", Severity.Warning,
            "Cables should carry wires; wires must not reference deleted cables.", (c, r) =>
                c.Project.Cables.Where(x => x.Cores.All(k => k.WireId is null))
                    .Select(x => r.Issue($"Cable {x.Tag} has no wires assigned.", "Assign wires to the cable cores or delete the cable.", obj: x.Tag))
                .Concat(c.Graph.Wires.Where(w => w.Wire.CableId is { } id && c.Project.FindCable(id) is null)
                    .Select(w => r.Issue($"Wire {Num(w.Wire)} references a deleted cable.", "Re-assign the wire to a cable.", w.Sheet, w.Wire.Id, severity: Severity.Error)))),

        new DelegateRule("VAL012", "Invalid terminal references", "Terminals", Severity.Error,
            "Terminals need a number that is unique on their strip.", (c, r) => TerminalIssues(c, r)),

        new DelegateRule("VAL013", "Inconsistent cross-references", "Components", Severity.Warning,
            "Contacts need a main function (coil); interruption points need a counterpart.", (c, r) => CrossReferenceIssues(c, r)),

        new DelegateRule("VAL014", "Missing manufacturer references", "Procurement", Severity.Information,
            "Devices should reference a manufacturer part number for the bill of materials.", (c, r) =>
                c.Project.Devices.Where(d => string.IsNullOrWhiteSpace(d.PartNumber) && !IsTerminalStrip(c, d))
                    .Select(d => r.Issue($"Device {d.Tag} has no part number.", "Select a catalogue product for the device.", obj: d.Tag))),

        new DelegateRule("VAL015", "Incomplete title blocks", "Documentation", Severity.Warning,
            "Project and sheet data shown in title blocks should be complete.", (c, r) => TitleBlockIssues(c, r)),

        new DelegateRule("VAL016", "Invalid project data", "Project", Severity.Error,
            "Sheet numbers must be unique and sheet sizes valid.", (c, r) =>
                c.Project.Sheets.GroupBy(s => s.Number).Where(g => g.Count() > 1)
                    .Select(g => r.Issue($"Sheet number {g.Key} is used {g.Count()} times.", "Renumber the sheets.", g.First()))
                .Concat(c.Project.Sheets.Where(s => s.Width <= 0 || s.Height <= 0).Select(s => r.Issue($"Sheet {s.Number} has an invalid size.", "Choose a standard sheet format.", s)))
                .Concat(string.IsNullOrWhiteSpace(c.Project.Info.CurrentRevision) ? new[] { r.Issue("The project has no current revision code.", "Set the revision in project properties.") } : Array.Empty<ValidationIssue>())),

        new DelegateRule("VAL017", "Inconsistent equipment relationships", "Equipment", Severity.Error,
            "Motors, cabinet items and PLC channels must reference existing devices.", (c, r) => RelationshipIssues(c, r)),

        new DelegateRule("VAL018", "Unnamed potential", "Connections", Severity.Error,
            "Potential feed symbols must carry a potential name.", (c, r) =>
                c.Project.SymbolInstances().Where(x => x.instance.Definition?.Role == SymbolRole.PotentialSource
                        && string.IsNullOrWhiteSpace(x.instance.GetValue(SymbolAttributes.Potential) ?? x.instance.Definition.DefaultPotential))
                    .Select(x => r.Issue("Potential feed without a name.", "Enter the potential name (e.g. L1, N, +24V).", x.sheet, x.instance.Id, c.Loc(x.sheet, x.instance.Position)))),

        new DelegateRule("VAL019", "Junction problems", "Connections", Severity.Warning,
            "Junctions join three or more wires.", (c, r) =>
                c.Graph.Potentials.SelectMany(p => p.Junctions).Concat(UnusedJunctions(c))
                    .Distinct()
                    .Select(j => (j, n: c.Graph.WireCountAtJunction(j.junction.Id)))
                    .Where(x => x.n < 3)
                    .Select(x => r.Issue(x.n < 2 ? "Junction with fewer than two wires." : "Junction joining only two wires (unnecessary).",
                        x.n < 2 ? "Connect or delete the junction." : "The junction can be removed.", x.j.sheet, x.j.junction.Id, c.Loc(x.j.sheet, x.j.junction.Position),
                        severity: x.n < 2 ? Severity.Warning : Severity.Information))),

        new DelegateRule("VAL020", "Cabinet layout conflicts", "Panel", Severity.Error,
            "Devices must fit on the mounting plate without collisions.", (c, r) =>
                c.Project.Cabinets.SelectMany(cab => CabinetLayoutService.Check(cab).Select(i =>
                    r.Issue($"{cab.Tag}: {i.Message}", "Move the item in the cabinet layout editor.", entity: i.ItemId, obj: cab.Tag,
                        severity: i.Severity == "Error" ? Severity.Error : Severity.Warning)))),

        new DelegateRule("VAL021", "Missing symbol definitions", "Symbols", Severity.Error,
            "Every placed symbol must resolve to a symbol definition.", (c, r) =>
                c.Project.SymbolInstances().Where(x => x.instance.Definition is null)
                    .Select(x => r.Issue($"Symbol '{x.instance.SymbolId}' not found in the project or libraries.", "Import the symbol library or replace the symbol.", x.sheet, x.instance.Id, c.Loc(x.sheet, x.instance.Position)))),

        new DelegateRule("VAL022", "Demonstration catalogue data", "Procurement", Severity.Information,
            "Fictional demonstration products must be replaced before procurement.", (c, r) =>
                c.Project.Catalog.Where(p => p.IsDemoData && c.Project.Devices.Any(d => string.Equals(d.PartNumber, p.PartNumber, StringComparison.OrdinalIgnoreCase)))
                    .Select(p => r.Issue($"Part {p.PartNumber} is fictional demonstration data.", "Replace with a verified manufacturer catalogue product before ordering.", obj: p.PartNumber))),
    };

    private static string Num(WireEntity w) => string.IsNullOrEmpty(w.WireNumber) ? "(unnumbered)" : w.WireNumber;

    private static bool IsTerminalStrip(ValidationContext c, Device d) => c.Project.FindTerminalStrip(d.Tag) is not null ||
        c.Project.SymbolInstances().Any(x => x.instance.DeviceId == d.Id && x.instance.Definition?.Role == SymbolRole.Terminal);

    private static IEnumerable<(Sheet sheet, JunctionEntity junction)> UnusedJunctions(ValidationContext c) =>
        c.Project.OrderedSheets.SelectMany(s => s.Drawing.Entities.OfType<JunctionEntity>().Select(j => (s, j)))
            .Where(x => c.Graph.WireCountAtJunction(x.j.Id) == 0);

    private static IEnumerable<ValidationIssue> UnconnectedPins(ValidationContext c, DelegateRule r)
    {
        foreach (var (sheet, inst) in c.Project.SymbolInstances())
        {
            var def = inst.Definition;
            if (def is null || def.Pins.Count == 0) continue;
            var counts = def.Pins.Select(p => (p, n: c.Graph.WireCountAt(inst.Id, p.Id))).ToList();
            if (counts.All(x => x.n == 0))
            {
                var spare = def.Role == SymbolRole.Terminal;
                yield return r.Issue(spare ? $"Terminal {Designation(c, inst)} is not connected (spare)." : $"{Designation(c, inst)} is not connected at all.",
                    spare ? "Mark as spare or remove the terminal." : "Wire the device or remove the symbol.", sheet, inst.Id, c.Loc(sheet, inst.Position), Designation(c, inst),
                    severity: spare ? Severity.Information : Severity.Warning);
                continue;
            }
            foreach (var (pin, _) in counts.Where(x => x.n == 0))
            {
                // Internally connected pin groups (terminals) only need one side connected.
                if (def.InternalConnections.Any(g => g.Contains(pin.Id) && g.Any(o => o != pin.Id && c.Graph.WireCountAt(inst.Id, o) > 0)) && def.Role != SymbolRole.Connector) continue;
                yield return r.Issue($"Pin {Designation(c, inst)}:{inst.GetPinLabel(pin.Id)} is not connected.", "Connect the pin or document it as unused.",
                    sheet, inst.Id, c.Loc(sheet, inst.Position), Designation(c, inst));
            }
        }
    }

    private static string Designation(ValidationContext c, SymbolInstanceEntity i) =>
        i.DeviceId is { } d && c.Project.FindDevice(d) is { } dev ? dev.Tag : i.Definition?.Name ?? i.SymbolId;

    private static IEnumerable<ValidationIssue> InconsistentNumbers(ValidationContext c, DelegateRule r)
    {
        if (c.Project.Settings.WireNumberingMode == WireNumberingMode.PerPotential)
            foreach (var p in c.Graph.Potentials.Where(p => p.WireNumbers.Count > 1))
            {
                var w = p.Wires[0];
                yield return r.Issue($"Potential {p} uses several wire numbers ({string.Join(", ", p.WireNumbers)}).", "Renumber the wires of this potential.",
                    w.Sheet, w.Wire.Id, c.Loc(w.Sheet, w.Wire.StartPoint), fix: FixNumberWires);
            }
        var byNumber = c.Graph.Potentials.SelectMany(p => p.WireNumbers.Select(n => (n, p))).GroupBy(x => x.n, StringComparer.OrdinalIgnoreCase);
        foreach (var g in byNumber.Where(g => g.Select(x => x.p).Distinct().Count() > 1))
            yield return r.Issue($"Wire number {g.Key} is used on {g.Select(x => x.p).Distinct().Count()} different potentials.",
                "Give each potential its own number.", severity: Severity.Error, obj: g.Key, fix: FixNumberWires);
    }

    private static IEnumerable<ValidationIssue> TerminalIssues(ValidationContext c, DelegateRule r)
    {
        var terminals = c.Project.SymbolInstances().Where(x => x.instance.Definition?.Role == SymbolRole.Terminal).ToList();
        foreach (var (sheet, inst) in terminals.Where(x => string.IsNullOrWhiteSpace(x.instance.GetValue(SymbolAttributes.TerminalNumber))))
            yield return r.Issue("Terminal without terminal number.", "Enter the terminal number.", sheet, inst.Id, c.Loc(sheet, inst.Position));
        foreach (var g in terminals.Where(x => x.instance.DeviceId is not null)
                     .GroupBy(x => (x.instance.DeviceId, n: x.instance.GetValue(SymbolAttributes.TerminalNumber) ?? ""))
                     .Where(g => g.Key.n.Length > 0 && g.Count() > 1))
        {
            var tag = c.Project.FindDevice(g.Key.DeviceId!.Value)?.Tag ?? "?";
            var first = g.First();
            yield return r.Issue($"Terminal {tag}:{g.Key.n} is placed {g.Count()} times.", "Renumber the duplicated terminal.", first.sheet, first.instance.Id, c.Loc(first.sheet, first.instance.Position), $"{tag}:{g.Key.n}");
        }
        var stripTags = c.Project.TerminalStrips.Select(t => t.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in terminals.Select(x => x.instance.DeviceId is { } d ? c.Project.FindDevice(d)?.Tag : null).OfType<string>().Distinct()
                     .Where(t => !stripTags.Contains(t)))
            yield return r.Issue($"Terminal strip {tag} is not defined in terminal management.", "Synchronise terminal strips.", obj: tag, severity: Severity.Information, fix: FixSyncTerminals);
    }

    private static IEnumerable<ValidationIssue> CrossReferenceIssues(ValidationContext c, DelegateRule r)
    {
        var byDevice = c.Project.SymbolInstances().Where(x => x.instance.DeviceId is not null).GroupBy(x => x.instance.DeviceId!.Value);
        foreach (var g in byDevice)
        {
            var device = c.Project.FindDevice(g.Key);
            if (device is null) continue;
            var mains = g.Count(x => x.instance.Definition?.Role == SymbolRole.Main);
            var children = g.Where(x => x.instance.Definition?.Role == SymbolRole.ChildContact).ToList();
            if (children.Count > 0 && mains == 0)
            {
                var f = children[0];
                yield return r.Issue($"Contacts of {device.Tag} have no main function (coil) in the project.", "Place the coil of the device or correct the contact's device tag.",
                    f.sheet, f.instance.Id, c.Loc(f.sheet, f.instance.Position), device.Tag);
            }
            if (mains > 1)
            {
                var f = g.First(x => x.instance.Definition?.Role == SymbolRole.Main);
                yield return r.Issue($"Device {device.Tag} has {mains} main symbols (coils).", "A device must have exactly one main function.",
                    f.sheet, f.instance.Id, c.Loc(f.sheet, f.instance.Position), device.Tag, Severity.Error);
            }
        }
        foreach (var g in c.Project.SymbolInstances().Where(x => x.instance.Definition?.Role == SymbolRole.Interruption)
                     .GroupBy(x => x.instance.GetValue(SymbolAttributes.Name) ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            var f = g.First();
            if (g.Key.Length == 0)
                foreach (var x in g) yield return r.Issue("Interruption point without a name.", "Name the interruption point.", x.sheet, x.instance.Id, c.Loc(x.sheet, x.instance.Position), severity: Severity.Error);
            else if (g.Count() == 1)
                yield return r.Issue($"Interruption point {g.Key} has no counterpart.", "Place the matching interruption point on the destination sheet.", f.sheet, f.instance.Id, c.Loc(f.sheet, f.instance.Position), g.Key);
        }
    }

    private static IEnumerable<ValidationIssue> TitleBlockIssues(ValidationContext c, DelegateRule r)
    {
        var i = c.Project.Info;
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(i.Name)) missing.Add("project name");
        if (string.IsNullOrWhiteSpace(i.Number)) missing.Add("project number");
        if (string.IsNullOrWhiteSpace(i.CompanyName)) missing.Add("company");
        if (string.IsNullOrWhiteSpace(i.CustomerName)) missing.Add("customer");
        if (string.IsNullOrWhiteSpace(i.Engineer)) missing.Add("engineer");
        if (missing.Count > 0) yield return r.Issue($"Title block data missing: {string.Join(", ", missing)}.", "Complete the project properties.");
        foreach (var s in c.Project.Sheets.Where(s => string.IsNullOrWhiteSpace(s.Title)))
            yield return r.Issue($"Sheet {s.Number} has no title.", "Enter a sheet title.", s);
        foreach (var s in c.Project.Sheets.Where(s => s.Kind != SheetKind.Cover && !s.Drawing.Entities.Any(e => e.Layer == StandardLayers.TitleBlock)))
            yield return r.Issue($"Sheet {s.Number} has no title block.", "Apply the sheet template.", s, severity: Severity.Information);
    }

    private static IEnumerable<ValidationIssue> RelationshipIssues(ValidationContext c, DelegateRule r)
    {
        foreach (var m in c.Project.Motors.Where(m => m.DeviceId is { } d && c.Project.FindDevice(d) is null))
            yield return r.Issue($"Motor {m.Tag} references a deleted device.", "Re-link the motor to its schematic device.", obj: m.Tag);
        foreach (var cab in c.Project.Cabinets)
            foreach (var it in cab.Items.Where(i => i.DeviceId is { } d && c.Project.FindDevice(d) is null))
                yield return r.Issue($"{cab.Tag}: layout item {it.Name} references a deleted device.", "Remove the item or re-link it.", entity: it.Id, obj: it.Name);
        foreach (var ch in c.Project.PlcStations.SelectMany(s => s.Channels).Where(ch => ch.LinkedDeviceId is { } d && c.Project.FindDevice(d) is null))
            yield return r.Issue($"PLC channel {ch.Address} references a deleted device.", "Re-link the channel.", entity: ch.Id, obj: ch.Address);
        var used = c.Project.SymbolInstances().Select(x => x.instance.DeviceId).OfType<Guid>().ToHashSet();
        foreach (var d in c.Project.Devices.Where(d => !used.Contains(d.Id)))
            yield return r.Issue($"Device {d.Tag} is not represented on any schematic.", "Place a symbol for the device or delete it.", obj: d.Tag, severity: Severity.Warning);
    }
}
