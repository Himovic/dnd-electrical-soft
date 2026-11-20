using System.Globalization;
using DND.Electrical.Domain;
using DND.Electrical.Schematic.Connectivity;
using DND.Electrical.Schematic.Services;
using DND.Electrical.Symbols;

namespace DND.Electrical.Application.Services;

public enum RepresentationKind
{
    SchematicSymbol, Motor, CabinetItem, PlcChannel, Cable, Load, CatalogProduct, Model3D, BimElement, TwinPoint, Calculation, Maintenance, Commissioning, Instrument, NetworkElement,
}

public sealed record Representation(RepresentationKind Kind, Guid? Id, string Description, string Location);

public sealed record ConflictValue(RepresentationKind Source, string Value);

/// <summary>Two representations of the same engineering object disagree on a field.</summary>
public sealed record EngineeringConflict(Guid ObjectId, string ObjectTag, string Field, IReadOnlyList<ConflictValue> Values)
{
    public string Description => $"{ObjectTag}: {Field} differs – " + string.Join(" / ", Values.Select(v => $"{v.Source} = '{v.Value}'"));
}

public sealed record EngineeringObjectView(Device Device, IReadOnlyList<Representation> Representations, IReadOnlyList<EngineeringConflict> Conflicts)
{
    public bool HasConflicts => Conflicts.Count > 0;
}

/// <summary>
/// "One engineering object, multiple representations": resolves every representation of an object (device
/// identity) across schematics, equipment, cabinet, PLC, cables, loads, catalogue, 3D, BIM, digital twin,
/// calculations, maintenance and commissioning, and detects inconsistencies between them. Synchronisation is
/// never automatic: <see cref="Synchronize"/> is called only after the user chose the authoritative value.
/// </summary>
public static class EngineeringObjectService
{
    /// <summary>Extension point for modules that own additional representations (3D, BIM, twin, power network…).</summary>
    public static readonly List<Func<ElectricalProject, Device, IEnumerable<Representation>>> Providers = new();

    public static EngineeringObjectView Describe(ElectricalProject p, Device d, ConnectivityGraph? graph = null)
    {
        var reps = new List<Representation>();
        foreach (var (sheet, inst) in p.SymbolInstances().Where(x => x.instance.DeviceId == d.Id))
            reps.Add(new(RepresentationKind.SchematicSymbol, inst.Id, inst.Definition?.Name ?? inst.SymbolId, SheetLocation.Of(sheet, inst.Position, p.Settings).ToString()));
        foreach (var m in p.Motors.Where(m => m.DeviceId == d.Id))
            reps.Add(new(RepresentationKind.Motor, m.Id, $"{m.PowerKw} kW {m.VoltageV} V {m.StartingMethod}", "Equipment database"));
        foreach (var c in p.Cabinets)
            foreach (var i in c.Items.Where(i => i.DeviceId == d.Id))
                reps.Add(new(RepresentationKind.CabinetItem, i.Id, $"{i.Width}×{i.Height}×{i.Depth} mm {i.Face}", c.Tag));
        foreach (var st in p.PlcStations)
            foreach (var ch in st.Channels.Where(ch => ch.LinkedDeviceId == d.Id))
                reps.Add(new(RepresentationKind.PlcChannel, ch.Id, $"{ch.Address} {ch.Tag}", st.Tag));
        graph ??= ConnectivityGraph.Build(p);
        var instanceIds = p.SymbolInstances().Where(x => x.instance.DeviceId == d.Id).Select(x => x.instance.Id).ToHashSet();
        var cableIds = graph.Wires.Where(w => (w.Wire.Start.InstanceId is { } a && instanceIds.Contains(a)) || (w.Wire.End.InstanceId is { } b && instanceIds.Contains(b)))
            .Select(w => w.Wire.CableId).OfType<Guid>().Distinct();
        foreach (var cid in cableIds)
            if (p.FindCable(cid) is { } cable) reps.Add(new(RepresentationKind.Cable, cable.Id, cable.Designation, $"{cable.FromLocation} → {cable.ToLocation}"));
        foreach (var l in p.Loads.Where(l => string.Equals(l.ProtectionDeviceTag, d.Tag, StringComparison.OrdinalIgnoreCase) || string.Equals(l.Tag, d.Tag, StringComparison.OrdinalIgnoreCase)))
            reps.Add(new(RepresentationKind.Load, l.Id, $"{l.Description} {l.PowerKw} kW", l.Board));
        if (p.FindProduct(d.Manufacturer, d.PartNumber) is { } prod)
            reps.Add(new(RepresentationKind.CatalogProduct, prod.Id, $"{prod.Manufacturer} {prod.PartNumber}{(prod.IsDemoData ? " (fictional demo)" : "")}", "Catalogue"));
        foreach (var c in p.Calculations.Where(c => c.LinkedObjectId == d.Id))
            reps.Add(new(RepresentationKind.Calculation, c.Id, c.Title, c.Status.ToString()));
        foreach (var m in p.Maintenance.Where(m => m.ObjectId == d.Id))
            reps.Add(new(RepresentationKind.Maintenance, m.Id, $"{m.Kind}: {m.Description}", m.Date.ToString("d", CultureInfo.InvariantCulture)));
        foreach (var c in p.Commissioning.Where(c => c.ObjectId == d.Id))
            reps.Add(new(RepresentationKind.Commissioning, c.Id, $"{c.Test}: {c.Result}", c.Date?.ToString("d", CultureInfo.InvariantCulture) ?? "not tested"));
        reps.AddRange(ThreeDService.Representations(p, d));
        foreach (var provider in Providers) reps.AddRange(provider(p, d));
        return new EngineeringObjectView(d, reps, Conflicts(p, d));
    }

    public static IReadOnlyList<EngineeringConflict> DetectAll(ElectricalProject p) => p.Devices.SelectMany(d => Conflicts(p, d)).ToList();

    private static IReadOnlyList<EngineeringConflict> Conflicts(ElectricalProject p, Device d)
    {
        var result = new List<EngineeringConflict>();
        foreach (var m in p.Motors.Where(m => m.DeviceId == d.Id))
        {
            if (!string.Equals(m.Tag, d.Tag, StringComparison.OrdinalIgnoreCase))
                result.Add(new(d.Id, d.Tag, "Tag", new[] { new ConflictValue(RepresentationKind.SchematicSymbol, d.Tag), new ConflictValue(RepresentationKind.Motor, m.Tag) }));
            if (d.GetProperty(DeviceProperties.Rating) is { } rating && TryKw(rating, out var kw) && Math.Abs(kw - m.PowerKw) > 1e-6)
                result.Add(new(d.Id, d.Tag, "Rated power (kW)", new[] { new ConflictValue(RepresentationKind.SchematicSymbol, rating), new ConflictValue(RepresentationKind.Motor, m.PowerKw.ToString(CultureInfo.InvariantCulture)) }));
            if (m.PartNumber.Length > 0 && d.PartNumber.Length > 0 && !string.Equals(m.PartNumber, d.PartNumber, StringComparison.OrdinalIgnoreCase))
                result.Add(new(d.Id, d.Tag, "Part number", new[] { new ConflictValue(RepresentationKind.SchematicSymbol, d.PartNumber), new ConflictValue(RepresentationKind.Motor, m.PartNumber) }));
        }
        foreach (var c in p.Cabinets)
            foreach (var i in c.Items.Where(i => i.DeviceId == d.Id))
            {
                if (i.PartNumber.Length > 0 && d.PartNumber.Length > 0 && !string.Equals(i.PartNumber, d.PartNumber, StringComparison.OrdinalIgnoreCase))
                    result.Add(new(d.Id, d.Tag, "Part number", new[] { new ConflictValue(RepresentationKind.SchematicSymbol, d.PartNumber), new ConflictValue(RepresentationKind.CabinetItem, i.PartNumber) }));
                if (p.FindProduct(d.Manufacturer, d.PartNumber) is { WidthMm: > 0 } prod && (Math.Abs(prod.WidthMm - i.Width) > 0.5 || Math.Abs(prod.HeightMm - i.Height) > 0.5))
                    result.Add(new(d.Id, d.Tag, "Dimensions (mm)", new[] { new ConflictValue(RepresentationKind.CatalogProduct, $"{prod.WidthMm}×{prod.HeightMm}"), new ConflictValue(RepresentationKind.CabinetItem, $"{i.Width}×{i.Height}") }));
            }
        return result.GroupBy(r => (r.Field, string.Join("|", r.Values.Select(v => v.Value)))).Select(g => g.First()).ToList();
    }

    private static bool TryKw(string text, out double kw)
    {
        kw = 0;
        var t = text.Trim();
        if (!t.EndsWith("kW", StringComparison.OrdinalIgnoreCase)) return false;
        return double.TryParse(t[..^2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out kw);
    }

    /// <summary>Applies the value of <paramref name="authoritative"/> to the other representations of the conflict.</summary>
    public static void Synchronize(ElectricalProject p, EngineeringConflict conflict, RepresentationKind authoritative, string user)
    {
        var d = p.FindDevice(conflict.ObjectId) ?? throw new InvalidOperationException("Object not found.");
        var value = conflict.Values.First(v => v.Source == authoritative).Value;
        switch (conflict.Field)
        {
            case "Tag":
                d.Tag = value;
                foreach (var m in p.Motors.Where(m => m.DeviceId == d.Id)) m.Tag = value;
                break;
            case "Rated power (kW)":
                var kw = double.Parse(authoritative == RepresentationKind.Motor ? value : value[..^2].Trim(), CultureInfo.InvariantCulture);
                d.Properties[DeviceProperties.Rating] = $"{kw.ToString(CultureInfo.InvariantCulture)} kW";
                foreach (var m in p.Motors.Where(m => m.DeviceId == d.Id)) m.PowerKw = kw;
                break;
            case "Part number":
                d.PartNumber = value;
                foreach (var m in p.Motors.Where(m => m.DeviceId == d.Id)) m.PartNumber = value;
                foreach (var i in p.Cabinets.SelectMany(c => c.Items).Where(i => i.DeviceId == d.Id)) i.PartNumber = value;
                break;
            case "Dimensions (mm)":
                var parts = value.Split('×');
                var w = double.Parse(parts[0], CultureInfo.InvariantCulture); var h = double.Parse(parts[1], CultureInfo.InvariantCulture);
                foreach (var i in p.Cabinets.SelectMany(c => c.Items).Where(i => i.DeviceId == d.Id)) { i.Width = w; i.Height = h; }
                break;
            default:
                throw new NotSupportedException($"No synchronisation rule for '{conflict.Field}'.");
        }
        p.Audit("Synchronized", $"{conflict.ObjectTag}: {conflict.Field} set to '{value}' from {authoritative}", user);
    }

    /// <summary>Representations affected if the object changes (impact analysis before editing).</summary>
    public static IReadOnlyList<Representation> ImpactOfChange(ElectricalProject p, Device d) =>
        Describe(p, d).Representations.Where(r => r.Kind is not (RepresentationKind.Maintenance or RepresentationKind.Commissioning)).ToList();
}

/// <summary>Approval workflow with role checks and immutable decisions.</summary>
public static class ApprovalService
{
    public static ApprovalRecord Request(ElectricalProject p, string kind, Guid subjectId, string title, string requestedBy, string approver)
    {
        if (p.Approvals.Any(a => a.SubjectId == subjectId && a.Status == ApprovalStatus.Pending))
            throw new InvalidOperationException($"An approval for '{title}' is already pending.");
        var a = new ApprovalRecord { SubjectKind = kind, SubjectId = subjectId, SubjectTitle = title, Revision = p.Info.CurrentRevision, RequestedBy = requestedBy, Approver = approver };
        p.Approvals.Add(a);
        p.Audit("Approval requested", $"{kind} '{title}' → {approver}", requestedBy);
        return a;
    }

    public static void Decide(ElectricalProject p, ApprovalRecord a, string decidedBy, bool approve, string comment)
    {
        if (a.Status != ApprovalStatus.Pending) throw new InvalidOperationException("The approval has already been decided.");
        var member = p.Members.FirstOrDefault(m => string.Equals(m.Name, decidedBy, StringComparison.OrdinalIgnoreCase));
        if (member is null || member.Role is not (ProjectRole.Approver or ProjectRole.ProjectManager or ProjectRole.Administrator))
            throw new UnauthorizedAccessException($"{decidedBy} is not an approver on this project.");
        if (string.Equals(a.RequestedBy, decidedBy, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Requests cannot be approved by their requester (four-eyes principle).");
        a.Status = approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        a.Comment = comment;
        a.Approver = decidedBy;
        a.DecidedAt = DateTimeOffset.Now;
        p.Audit(approve ? "Approved" : "Rejected", $"{a.SubjectKind} '{a.SubjectTitle}' rev {a.Revision}: {comment}", decidedBy);
    }

    public static EngineeringIssue RaiseIssue(ElectricalProject p, string title, string description, IssueSeverity severity, string raisedBy, Guid? objectId = null)
    {
        var issue = new EngineeringIssue { Number = p.Issues.Count == 0 ? 1 : p.Issues.Max(i => i.Number) + 1, Title = title, Description = description, Severity = severity, RaisedBy = raisedBy, ObjectId = objectId };
        p.Issues.Add(issue);
        p.Audit("Issue raised", $"#{issue.Number} {title}", raisedBy);
        return issue;
    }
}
