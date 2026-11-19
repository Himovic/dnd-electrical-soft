using DND.Electrical.Domain;
using DND.Electrical.Schematic.Connectivity;
using DND.Electrical.Schematic.Entities;
using DND.Electrical.Schematic.Services;
using DND.Electrical.Shared;
using DND.Electrical.Symbols;

namespace DND.Electrical.Wiring;

/// <summary>One row of the connection (wire) list.</summary>
public sealed record ConnectionRow(
    string WireNumber,
    string From,
    string To,
    string Potential,
    string Color,
    string CrossSection,
    string WireType,
    string Cable,
    string Sheet,
    double LengthM,
    Guid WireId,
    Guid SheetId);

public sealed record TerminalRow(
    string Strip,
    string Terminal,
    int Level,
    string InternalTargets,
    string ExternalTargets,
    string WireNumbers,
    string Jumper,
    string Location,
    Guid InstanceId);

public sealed record CableRow(string Tag, string Type, string Cores, double CrossSection, double LengthM, string From, string To, int UsedCores, int SpareCores, string Route);

public sealed record CableCoreRow(string Cable, int Core, string Marking, string WireNumber, string From, string To);

/// <summary>Reports derived from the real connectivity graph: wire lists, terminal diagrams, cable schedules.</summary>
public static class WiringReports
{
    /// <summary>Length assumed for a schematic drawing millimetre when no physical length is set (0 = unknown).</summary>
    public const double UnknownLength = 0;

    public static IReadOnlyList<ConnectionRow> ConnectionList(ElectricalProject project, ConnectivityGraph graph)
    {
        var cables = project.Cables.ToDictionary(c => c.Id);
        var rows = new List<ConnectionRow>();
        foreach (var wr in graph.Wires)
        {
            var w = wr.Wire;
            var from = Describe(graph.PinsAtWireEnd(wr, true), w.Start);
            var to = Describe(graph.PinsAtWireEnd(wr, false), w.End);
            var pot = graph.PotentialOfWire(w.Id);
            string cable = string.Empty;
            if (w.CableId is { } cid && cables.TryGetValue(cid, out var c))
            {
                var core = c.Cores.FirstOrDefault(x => x.WireId == w.Id);
                cable = core is null ? c.Tag : $"{c.Tag}:{core.Marking}";
            }
            rows.Add(new ConnectionRow(
                w.WireNumber, from, to, pot?.Name ?? string.Empty, w.WireColor,
                w.CrossSectionMm2 is { } cs ? cs.ToString("0.##") : string.Empty, w.WireType, cable,
                $"{wr.Sheet.Number} {wr.Sheet.Title}", w.LengthM ?? UnknownLength, w.Id, wr.Sheet.Id));
        }
        return rows.OrderBy(r => r.WireNumber, NaturalStringComparer.Instance).ThenBy(r => r.From, NaturalStringComparer.Instance).ToList();
    }

    private static string Describe(IReadOnlyList<PinNode> pins, WireEnd end)
    {
        if (pins.Count > 0) return string.Join(", ", pins.Select(p => p.Designation));
        return end.IsConnected ? "(junction)" : "(open)";
    }

    /// <summary>
    /// Terminal diagram rows: for each placed terminal, the devices wired to its inner side (pin 1,
    /// panel side) and outer side (pin 2, field side), computed from actual wiring.
    /// </summary>
    public static IReadOnlyList<TerminalRow> TerminalDiagram(ElectricalProject project, ConnectivityGraph graph, string? stripTag = null)
    {
        var rows = new List<TerminalRow>();
        foreach (var (sheet, inst) in project.SymbolInstances())
        {
            if (inst.Definition?.Role != SymbolRole.Terminal) continue;
            var device = inst.DeviceId is { } did ? project.FindDevice(did) : null;
            var tag = device?.Tag ?? "?";
            if (stripTag is not null && !string.Equals(tag, stripTag, StringComparison.OrdinalIgnoreCase)) continue;
            var number = inst.GetValue(SymbolAttributes.TerminalNumber) ?? "?";
            var pins = inst.Definition.Pins;
            string Targets(int idx)
            {
                if (idx >= pins.Count) return string.Empty;
                var pn = graph.GetPin(inst.Id, pins[idx].Id);
                return pn is null ? string.Empty : string.Join(", ", graph.WiredNeighbours(pn).Select(n => n.Designation));
            }
            var wires = graph.Wires.Where(w => w.Wire.Start.InstanceId == inst.Id || w.Wire.End.InstanceId == inst.Id)
                .Select(w => w.Wire.WireNumber).Where(n => n.Length > 0).Distinct();
            var strip = project.FindTerminalStrip(tag);
            var def = strip?.Terminals.FirstOrDefault(t => t.Number == number);
            rows.Add(new TerminalRow(tag, number, def?.Level ?? 1, Targets(0), Targets(1), string.Join(", ", wires),
                def?.JumperGroup ?? string.Empty, SheetLocation.Of(sheet, inst.Position, project.Settings).ToString(), inst.Id));
        }
        return rows.OrderBy(r => r.Strip, NaturalStringComparer.Instance).ThenBy(r => r.Terminal, NaturalStringComparer.Instance).ToList();
    }

    public static IReadOnlyList<CableRow> CableSchedule(ElectricalProject project) =>
        project.Cables.OrderBy(c => c.Tag, NaturalStringComparer.Instance).Select(c =>
        {
            var used = c.Cores.Count(x => x.WireId is not null);
            return new CableRow(c.Tag, c.CableType, $"{c.CoreCount}×{c.CrossSectionMm2:0.##} mm²", c.CrossSectionMm2, c.LengthM,
                c.FromLocation, c.ToLocation, used, c.CoreCount - used, c.Route);
        }).ToList();

    public static IReadOnlyList<CableCoreRow> CableCores(ElectricalProject project, ConnectivityGraph graph)
    {
        var wires = graph.Wires.ToDictionary(w => w.Wire.Id);
        var rows = new List<CableCoreRow>();
        foreach (var c in project.Cables.OrderBy(c => c.Tag, NaturalStringComparer.Instance))
            foreach (var core in c.Cores.OrderBy(x => x.Index))
            {
                if (core.WireId is { } wid && wires.TryGetValue(wid, out var wr))
                    rows.Add(new CableCoreRow(c.Tag, core.Index, core.Marking, wr.Wire.WireNumber,
                        string.Join(", ", graph.PinsAtWireEnd(wr, true).Select(p => p.Designation)),
                        string.Join(", ", graph.PinsAtWireEnd(wr, false).Select(p => p.Designation))));
                else rows.Add(new CableCoreRow(c.Tag, core.Index, core.Marking, "spare", string.Empty, string.Empty));
            }
        return rows;
    }
}

/// <summary>Cable and terminal management operations.</summary>
public static class CableService
{
    public static Cable CreateCable(ElectricalProject project, string type, int cores, double crossSection, double lengthM, string from, string to)
    {
        var used = project.Cables.Select(c => c.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var n = 1;
        while (used.Contains($"-W{n}")) n++;
        var cable = new Cable
        {
            Tag = $"-W{n}", CableType = type, CoreCount = cores, CrossSectionMm2 = crossSection, LengthM = lengthM,
            FromLocation = from, ToLocation = to,
        };
        cable.EnsureCores();
        project.Cables.Add(cable);
        return cable;
    }

    /// <summary>Assigns a wire to a free core of the cable (or to the given core index).</summary>
    public static Result AssignWire(Cable cable, WireEntity wire, int? coreIndex = null)
    {
        cable.EnsureCores();
        var existing = cable.Cores.FirstOrDefault(c => c.WireId == wire.Id);
        if (existing is not null) return Result.Ok();
        var core = coreIndex is { } idx ? cable.Cores.FirstOrDefault(c => c.Index == idx) : cable.Cores.FirstOrDefault(c => c.WireId is null && c.Marking != "GNYE");
        core ??= cable.Cores.FirstOrDefault(c => c.WireId is null);
        if (core is null) return Result.Fail($"Cable {cable.Tag} has no free core.");
        if (core.WireId is not null) return Result.Fail($"Core {core.Index} of {cable.Tag} is already used.");
        core.WireId = wire.Id;
        wire.CableId = cable.Id;
        wire.CableCoreIndex = core.Index;
        if (wire.CrossSectionMm2 is null) wire.CrossSectionMm2 = cable.CrossSectionMm2;
        return Result.Ok();
    }

    public static void UnassignWire(ElectricalProject project, WireEntity wire)
    {
        foreach (var c in project.Cables)
            foreach (var core in c.Cores.Where(x => x.WireId == wire.Id)) core.WireId = null;
        wire.CableId = null;
        wire.CableCoreIndex = null;
    }

    /// <summary>
    /// Synchronises terminal strip definitions with the terminals placed on schematics: creates strips
    /// and terminal entries for placed terminals (existing jumpers/levels are preserved).
    /// </summary>
    public static int SyncTerminalStrips(ElectricalProject project)
    {
        var added = 0;
        foreach (var (_, inst) in project.SymbolInstances())
        {
            if (inst.Definition?.Role != SymbolRole.Terminal || inst.DeviceId is not { } did) continue;
            var dev = project.FindDevice(did);
            if (dev is null) continue;
            var strip = project.FindTerminalStrip(dev.Tag);
            if (strip is null)
            {
                strip = new TerminalStrip { Tag = dev.Tag, Location = dev.Location, Description = dev.Function };
                project.TerminalStrips.Add(strip);
            }
            var number = inst.GetValue(SymbolAttributes.TerminalNumber) ?? string.Empty;
            if (number.Length == 0 || strip.Terminals.Any(t => t.Number == number)) continue;
            strip.Terminals.Add(new TerminalDefinition
            {
                Number = number,
                Type = inst.SymbolId == "IEC.TERMINAL.PE" ? TerminalType.ProtectiveEarth : TerminalType.FeedThrough,
                SortIndex = strip.Terminals.Count,
            });
            added++;
        }
        foreach (var s in project.TerminalStrips)
            s.Terminals = s.Terminals.OrderBy(t => t.Number, NaturalStringComparer.Instance).ToList();
        return added;
    }
}
