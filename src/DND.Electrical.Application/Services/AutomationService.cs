using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;
using DND.Electrical.Domain;
using DND.Electrical.Licensing;

namespace DND.Electrical.Application.Services;

/// <summary>
/// AUTO module: instrumentation, control loops, alarms, cause and effect matrices and control system architecture.
/// Operations that change the project require the AUTO licence module.
/// </summary>
public sealed class AutomationService(IEntitlements entitlements)
{
    /// <summary>Assigns an instrument to a PLC channel after checking signal kind and direction. Returns an error message or null.</summary>
    public string? AssignToChannel(ElectricalProject p, Instrument i, PlcChannel ch, string? user = null)
    {
        entitlements.Demand(EngineeringModule.AUTO);
        var analogChannel = ch.Kind is PlcSignalKind.AnalogInput or PlcSignalKind.AnalogOutput;
        var outputChannel = ch.Kind is PlcSignalKind.DigitalOutput or PlcSignalKind.AnalogOutput;
        var output = i.Function is InstrumentFunction.ControlValve or InstrumentFunction.OnOffValve or InstrumentFunction.Actuator;
        if (analogChannel != i.IsAnalog) return $"{i.Tag} ({i.Signal}) cannot be wired to {ch.Kind} {ch.Address}.";
        if (output != outputChannel) return $"{i.Tag} ({i.Function}) needs {(output ? "an output" : "an input")} channel; {ch.Address} is {ch.Kind}.";
        var holder = p.Instruments.FirstOrDefault(x => x.PlcChannelId == ch.Id && x.Id != i.Id);
        if (holder is not null) return $"{ch.Address} is already used by {holder.Tag}.";
        if (!ch.IsSpare && ch.LinkedDeviceId != i.DeviceId) return $"{ch.Address} is already used ({ch.Tag}).";
        foreach (var old in p.PlcStations.SelectMany(s => s.Channels).Where(c => c.Id == i.PlcChannelId && c.Id != ch.Id))
        { old.Tag = string.Empty; old.Description = string.Empty; old.LinkedDeviceId = null; }
        i.PlcChannelId = ch.Id;
        ch.Tag = i.Tag;
        ch.Description = i.Description;
        ch.SignalType = i.Signal switch { InstrumentSignal.Analog4To20mA or InstrumentSignal.Hart => "4–20 mA", InstrumentSignal.Voltage0To10V => "0–10 V", InstrumentSignal.Namur => "NAMUR", _ => "24 V DC" };
        if (i.DeviceId is not null) ch.LinkedDeviceId = i.DeviceId;
        p.Audit("I/O assigned", $"{i.Tag} → {ch.Address}", user);
        return null;
    }

    /// <summary>
    /// Proposes alarm definitions at the given fractions of the instrument span (values chosen by the user per the
    /// project alarm philosophy). Nothing is added to the project; the caller adds confirmed proposals.
    /// </summary>
    public IReadOnlyList<AlarmDefinition> ProposeAlarms(Instrument i, IReadOnlyDictionary<AlarmType, double> fractionOfSpan, AlarmPriority priority = AlarmPriority.Medium)
    {
        entitlements.Demand(EngineeringModule.AUTO);
        if (!i.IsAnalog || i.RangeMax <= i.RangeMin) throw new InvalidOperationException($"{i.Tag}: alarms from span require an analog instrument with a valid range.");
        var span = i.RangeMax - i.RangeMin;
        string Suffix(AlarmType t) => t switch { AlarmType.HighHigh => "HH", AlarmType.High => "H", AlarmType.Low => "L", AlarmType.LowLow => "LL", _ => t.ToString() };
        return fractionOfSpan.Select(kv => new AlarmDefinition
        {
            Tag = $"{i.Tag}-{Suffix(kv.Key)}", InstrumentId = i.Id, Type = kv.Key, SetPoint = Math.Round(i.RangeMin + kv.Value * span, 6), Unit = i.Unit,
            Priority = kv.Key is AlarmType.HighHigh or AlarmType.LowLow ? AlarmPriority.High : priority,
            Message = $"{i.Description} {Suffix(kv.Key)}".Trim(),
        }).ToList();
    }

    /// <summary>Effects triggered by the given active causes (any linked active cause triggers the effect).</summary>
    public static IReadOnlyList<CauseEffectEffect> Evaluate(CauseEffectMatrix m, IEnumerable<Guid> activeCauses)
    {
        var active = activeCauses.ToHashSet();
        return m.Effects.Where(e => m.Links.Any(l => l.EffectId == e.Id && active.Contains(l.CauseId) && !string.Equals(l.Mark, "N", StringComparison.OrdinalIgnoreCase))).ToList();
    }

    public static void ToggleLink(CauseEffectMatrix m, Guid cause, Guid effect, string mark = "X")
    {
        var link = m.Links.FirstOrDefault(l => l.CauseId == cause && l.EffectId == effect);
        if (link is null) m.Links.Add(new CauseEffectLink { CauseId = cause, EffectId = effect, Mark = mark });
        else m.Links.Remove(link);
    }

    /// <summary>Creates control network nodes for PLC stations that are not yet in the architecture.</summary>
    public int ImportPlcStations(ElectricalProject p, Guid? switchNodeId = null)
    {
        entitlements.Demand(EngineeringModule.AUTO);
        var n = 0;
        foreach (var s in p.PlcStations.Where(s => p.ScadaNodes.All(x => x.PlcStationId != s.Id)))
        {
            p.ScadaNodes.Add(new ScadaNode { Name = $"{s.Tag} {s.Name}".Trim(), Kind = ScadaNodeKind.Plc, IpAddress = s.IpAddress, PlcStationId = s.Id, ParentId = switchNodeId, Network = "Control LAN" });
            n++;
        }
        return n;
    }

    /// <summary>
    /// Generates a loop diagram sheet: field instrument → junction box → PLC channel, with tags, signal, range and
    /// cross-references. The drawing is regenerated from the data each time (no manual content is overwritten).
    /// </summary>
    public Sheet GenerateLoopDiagram(ElectricalProject p, ControlLoop loop)
    {
        entitlements.Demand(EngineeringModule.AUTO);
        var title = $"Loop diagram {loop.Tag}";
        var sheet = p.Sheets.FirstOrDefault(s => s.Title == title);
        if (sheet is null)
        {
            sheet = new Sheet
            {
                Title = title, Kind = SheetKind.Plc, Number = p.NextSheetNumber(), Description = loop.Description, Function = "=LOOP",
                Format = SheetFormat.Parse(p.Settings.DefaultSheetFormat), Revision = p.Info.CurrentRevision,
            };
            DND.Electrical.Schematic.Services.SheetTemplate.Apply(sheet, p.Settings);
            p.Sheets.Add(sheet);
        }
        var d = sheet.Drawing;
        foreach (var e in d.Entities.Where(e => e.Layer == StandardLayers.Annotations || e.Layer == "0").ToList()) d.Remove(e);
        var members = p.Instruments.Where(i => i.Id == loop.MeasurementId || i.Id == loop.FinalElementId || i.LoopId == loop.Id).Distinct().ToList();
        var channels = p.PlcStations.SelectMany(s => s.Channels.Select(c => (s, c))).ToDictionary(x => x.c.Id);
        void Box(double x, double y, double w, double h, string caption)
        {
            d.Add(new PolylineEntity(new[] { new Point2(x, y), new Point2(x + w, y), new Point2(x + w, y + h), new Point2(x, y + h) }, true) { Layer = StandardLayers.Annotations });
            d.Add(new TextEntity(new Point2(x + 2, y + h + 2), caption, 3) { Layer = StandardLayers.Annotations, Bold = true });
        }
        void Text(double x, double y, string t, double h = 2.5) => d.Add(new TextEntity(new Point2(x, y), t, h) { Layer = StandardLayers.Annotations });
        Text(20, 270, title, 5);
        Text(20, 262, $"{loop.Description} · {loop.Type}{(loop.SetPoint is { } sp ? $" · SP {sp:0.###} {loop.Unit}" : "")} · controller {loop.Controller}");
        Box(15, 60, 110, 180, "FIELD"); Box(145, 60, 90, 180, "JUNCTION BOX"); Box(255, 60, 140, 180, "CONTROL SYSTEM");
        var y = 210.0;
        foreach (var i in members)
        {
            d.Add(new CircleEntity(new Point2(45, y), 9) { Layer = StandardLayers.Annotations });
            Text(38, y + 1, i.Tag.Split('-')[0], 3);
            Text(38, y - 4, i.Tag.Contains('-') ? i.Tag[(i.Tag.IndexOf('-') + 1)..] : "", 3);
            Text(60, y + 2, i.Description, 2.2);
            Text(60, y - 2, $"{i.Signal}{(i.IsAnalog ? $" · {i.RangeMin:0.###}…{i.RangeMax:0.###} {i.Unit}" : "")}", 2.2);
            if (i.ExMarking.Length > 0) Text(60, y - 6, $"{i.HazardousZone} · {i.ExMarking}", 2.2);
            Text(150, y, i.JunctionBox.Length > 0 ? $"{i.JunctionBox} terminals +/−" : "(junction box not assigned)", 2.2);
            var io = i.PlcChannelId is { } id && channels.TryGetValue(id, out var ch) ? $"{ch.s.Tag} {ch.c.Address} ({ch.c.Kind})" : "(I/O not assigned)";
            Text(260, y, io, 2.5);
            d.Add(new LineEntity(new Point2(54, y), new Point2(145, y)) { Layer = StandardLayers.Annotations });
            d.Add(new LineEntity(new Point2(235, y), new Point2(255, y)) { Layer = StandardLayers.Annotations });
            y -= 40;
        }
        if (loop.FinalDeviceId is { } fd && p.FindDevice(fd) is { } dev)
            Text(20, 50, $"Final element (electrical): {dev.Tag} {dev.Function} – see power schematics.", 2.5);
        Text(20, 40, "Generated from the instrument, loop and I/O data. Regenerate after changes; do not edit by hand.", 2.2);
        p.Audit("Loop diagram generated", loop.Tag);
        return sheet;
    }
}
