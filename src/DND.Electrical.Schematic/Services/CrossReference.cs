using DND.Electrical.Core.Geometry;
using DND.Electrical.Domain;
using DND.Electrical.Schematic.Entities;

namespace DND.Electrical.Schematic.Services;

/// <summary>Location of an object on the drawing set, printed as "/sheet.column".</summary>
public readonly record struct SheetLocation(int Sheet, int Column)
{
    public override string ToString() => $"/{Sheet}.{Column}";
    public static SheetLocation Of(Sheet sheet, Point2 p, ProjectSettings settings) =>
        new(sheet.Number, SheetLayout.For(sheet, settings).ColumnAt(p.X));
}

/// <summary>All placed representations of one device.</summary>
public sealed record DeviceOccurrence(Sheet Sheet, SymbolInstanceEntity Instance)
{
    public SheetLocation Location(ProjectSettings s) => SheetLocation.Of(Sheet, Instance.Position, s);
}

public static class ProjectQueries
{
    public static IEnumerable<(Sheet sheet, SymbolInstanceEntity instance)> SymbolInstances(this ElectricalProject project) =>
        project.OrderedSheets.SelectMany(s => s.Drawing.Entities.OfType<SymbolInstanceEntity>().Select(i => (s, i)));

    public static IEnumerable<(Sheet sheet, WireEntity wire)> WireEntities(this ElectricalProject project) =>
        project.OrderedSheets.SelectMany(s => s.Drawing.Entities.OfType<WireEntity>().Select(w => (s, w)));

    public static ILookup<Guid, DeviceOccurrence> OccurrencesByDevice(this ElectricalProject project) =>
        project.SymbolInstances().Where(x => x.instance.DeviceId is not null)
            .ToLookup(x => x.instance.DeviceId!.Value, x => new DeviceOccurrence(x.sheet, x.instance));

    public static (Sheet sheet, SymbolInstanceEntity instance)? FindInstance(this ElectricalProject project, Guid instanceId)
    {
        foreach (var s in project.Sheets)
            if (s.Drawing.Find(instanceId) is SymbolInstanceEntity si) return (s, si);
        return null;
    }
}
