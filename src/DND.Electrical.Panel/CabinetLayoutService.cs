using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;
using DND.Electrical.Domain;
using DND.Electrical.Shared;

namespace DND.Electrical.Panel;

public sealed record LayoutIssue(string Severity, string Message, Guid ItemId, Guid? OtherItemId = null);

public sealed record CabinetUtilization(double PlateAreaMm2, double OccupiedAreaMm2, double Percent, double FreeRailLengthMm);

/// <summary>2D control cabinet layout: placement, DIN rail mounting, collision/clearance checks and drawing generation.</summary>
public static class CabinetLayoutService
{
    public static Rect2 Bounds(CabinetItem i) => Rect2.FromSize(i.X, i.Y, i.Width, i.Height);

    public static CabinetItem AddRail(Cabinet c, double x, double y, double length) => Add(c, new CabinetItem
    {
        Kind = CabinetItemKind.DinRail, Name = "DIN rail TS35", X = x, Y = y, Width = length, Height = 35, Depth = 7.5, PartNumber = "DEMO-TS35",
    });

    public static CabinetItem AddDuct(Cabinet c, double x, double y, double width, double height) => Add(c, new CabinetItem
    {
        Kind = CabinetItemKind.CableDuct, Name = $"Cable duct {Math.Min(width, height):0}", X = x, Y = y, Width = width, Height = height, Depth = 60, PartNumber = "DEMO-DUCT",
    });

    public static CabinetItem Add(Cabinet c, CabinetItem item)
    {
        c.Items.Add(item);
        return item;
    }

    /// <summary>Places a device on a DIN rail at the first free position (left to right). Returns false when the rail is full.</summary>
    public static bool PlaceOnRail(Cabinet cabinet, CabinetItem rail, CabinetItem device, double gapMm = 0)
    {
        if (rail.Kind != CabinetItemKind.DinRail) throw new ArgumentException("Target is not a DIN rail.");
        var onRail = cabinet.Items.Where(i => i.RailId == rail.Id && i.Id != device.Id).OrderBy(i => i.X).ToList();
        var x = rail.X;
        foreach (var other in onRail)
        {
            if (x + device.Width + gapMm <= other.X) break;
            x = Math.Max(x, other.X + other.Width + gapMm);
        }
        if (x + device.Width > rail.X + rail.Width + 1e-9) return false;
        device.X = x;
        // Devices are centred on the rail axis.
        device.Y = rail.Y + rail.Height / 2 - device.Height / 2;
        device.RailId = rail.Id;
        if (!cabinet.Items.Contains(device)) cabinet.Items.Add(device);
        return true;
    }

    /// <summary>Detects overlaps, clearance violations and items outside the mounting plate.</summary>
    public static IReadOnlyList<LayoutIssue> Check(Cabinet cabinet)
    {
        var issues = new List<LayoutIssue>();
        var plate = new Rect2(0, 0, cabinet.PlateWidth, cabinet.PlateHeight);
        var items = cabinet.Items.Where(i => i.Face == CabinetFace.MountingPlate).ToList();
        foreach (var i in items)
            if (!plate.Contains(Bounds(i)))
                issues.Add(new("Error", $"{Label(i)} extends outside the mounting plate.", i.Id));

        // Items that legitimately overlap: a device on its rail.
        for (int a = 0; a < items.Count; a++)
            for (int b = a + 1; b < items.Count; b++)
            {
                var ia = items[a]; var ib = items[b];
                if (IsMountedOn(ia, ib) || IsMountedOn(ib, ia)) continue;
                var ra = Bounds(ia); var rb = Bounds(ib);
                if (ra.Overlaps(rb))
                {
                    issues.Add(new("Error", $"{Label(ia)} collides with {Label(ib)}.", ia.Id, ib.Id));
                    continue;
                }
                if (cabinet.ClearanceMm > 0 && ia.Kind == CabinetItemKind.Device && ib.Kind == CabinetItemKind.Device
                    && ra.Inflate(cabinet.ClearanceMm - 1e-6).Overlaps(rb))
                    issues.Add(new("Warning", $"{Label(ia)} and {Label(ib)} are closer than the {cabinet.ClearanceMm:0} mm clearance.", ia.Id, ib.Id));
            }
        // Door-mounted devices (pilot lights, push buttons, meters): must fit on the door and must not overlap.
        var door = new Rect2(0, 0, cabinet.Width, cabinet.Height);
        var doorItems = cabinet.Items.Where(i => i.Face == CabinetFace.Door).ToList();
        foreach (var i in doorItems)
            if (!door.Contains(Bounds(i))) issues.Add(new("Error", $"{Label(i)} extends outside the door.", i.Id));
        for (int a = 0; a < doorItems.Count; a++)
            for (int b = a + 1; b < doorItems.Count; b++)
                if (Bounds(doorItems[a]).Overlaps(Bounds(doorItems[b])))
                    issues.Add(new("Error", $"{Label(doorItems[a])} collides with {Label(doorItems[b])} on the door.", doorItems[a].Id, doorItems[b].Id));
        foreach (var dup in cabinet.Items.Where(i => i.DeviceId is not null).GroupBy(i => i.DeviceId).Where(g => g.Count() > 1))
            foreach (var i in dup.Skip(1)) issues.Add(new("Warning", $"Device placed more than once ({Label(i)}).", i.Id));
        return issues;
    }

    private static bool IsMountedOn(CabinetItem device, CabinetItem rail) => device.RailId == rail.Id && rail.Kind == CabinetItemKind.DinRail;

    private static string Label(CabinetItem i) => string.IsNullOrWhiteSpace(i.Name) ? i.Kind.ToString() : i.Name;

    public static CabinetUtilization Utilization(Cabinet cabinet)
    {
        var plateArea = cabinet.PlateWidth * cabinet.PlateHeight;
        var occupied = cabinet.Items.Where(i => i.Face == CabinetFace.MountingPlate && i.Kind != CabinetItemKind.DinRail && i.RailId is null).Sum(i => i.Width * i.Height)
                       + cabinet.Items.Where(i => i.Kind == CabinetItemKind.DinRail).Sum(r => r.Width * Math.Max(r.Height, cabinet.Items.Where(d => d.RailId == r.Id).Select(d => d.Height).DefaultIfEmpty(r.Height).Max()));
        var freeRail = cabinet.Items.Where(i => i.Kind == CabinetItemKind.DinRail)
            .Sum(r => r.Width - cabinet.Items.Where(d => d.RailId == r.Id).Sum(d => d.Width));
        return new CabinetUtilization(plateArea, occupied, plateArea > 0 ? occupied / plateArea * 100 : 0, freeRail);
    }

    /// <summary>Synchronises item names with device tags (after renaming in schematics).</summary>
    public static void SyncNames(ElectricalProject project, Cabinet cabinet)
    {
        foreach (var i in cabinet.Items.Where(i => i.DeviceId is not null))
            if (project.FindDevice(i.DeviceId!.Value) is { } d)
            {
                i.Name = d.Tag;
                if (string.IsNullOrEmpty(i.PartNumber)) i.PartNumber = d.PartNumber;
            }
    }

    /// <summary>Builds a 2D layout drawing (front view of the mounting plate) for display and export.</summary>
    public static CadDrawing BuildDrawing(ElectricalProject project, Cabinet cabinet)
    {
        SyncNames(project, cabinet);
        var d = new CadDrawing();
        d.AddLayer(CadLayer.Create("PANEL_ENCLOSURE", CadColor.Navy, 0.5));
        d.AddLayer(CadLayer.Create("PANEL_RAILS", CadColor.Grey, 0.25));
        d.AddLayer(CadLayer.Create("PANEL_DUCTS", new CadColor(148, 163, 184), 0.25));
        d.AddLayer(CadLayer.Create("PANEL_DEVICES", CadColor.Black, 0.35));
        var offX = (cabinet.Width - cabinet.PlateWidth) / 2;
        var offY = (cabinet.Height - cabinet.PlateHeight) / 2;
        var m = Matrix2.Translation(offX, offY);

        d.Add(new PolylineEntity(Rect2.FromSize(0, 0, cabinet.Width, cabinet.Height).Corners(), true) { Layer = "PANEL_ENCLOSURE", LineWeight = 0.7 });
        d.Add(new PolylineEntity(Rect2.FromSize(offX, offY, cabinet.PlateWidth, cabinet.PlateHeight).Corners(), true) { Layer = "PANEL_ENCLOSURE", LineType = LineType.Dashed });
        d.Add(new TextEntity(new Point2(cabinet.Width / 2, cabinet.Height + 15), $"{cabinet.Tag} {cabinet.Name} – {cabinet.Width:0}×{cabinet.Height:0}×{cabinet.Depth:0} mm ({cabinet.IpRating})", 12)
        { Layer = "PANEL_ENCLOSURE", HAlign = TextHAlign.Center, VAlign = TextVAlign.Bottom, Bold = true });

        foreach (var i in cabinet.Items.Where(i => i.Face == CabinetFace.MountingPlate).OrderBy(i => i.Kind switch { CabinetItemKind.CableDuct => 0, CabinetItemKind.DinRail => 1, _ => 2 }))
        {
            var r = Bounds(i).Transform(m);
            switch (i.Kind)
            {
                case CabinetItemKind.DinRail:
                    d.Add(new PolylineEntity(r.Corners(), true) { Layer = "PANEL_RAILS" });
                    d.Add(new LineEntity(new Point2(r.MinX, r.MinY + 5), new Point2(r.MaxX, r.MinY + 5)) { Layer = "PANEL_RAILS" });
                    d.Add(new LineEntity(new Point2(r.MinX, r.MaxY - 5), new Point2(r.MaxX, r.MaxY - 5)) { Layer = "PANEL_RAILS" });
                    break;
                case CabinetItemKind.CableDuct:
                    d.Add(new PolylineEntity(r.Corners(), true) { Layer = "PANEL_DUCTS" });
                    var horizontal = r.Width >= r.Height;
                    for (double t = 10; t < (horizontal ? r.Width : r.Height); t += 10)
                        d.Add(horizontal
                            ? new LineEntity(new Point2(r.MinX + t, r.MinY), new Point2(r.MinX + t, r.MinY + 6)) { Layer = "PANEL_DUCTS" }
                            : new LineEntity(new Point2(r.MinX, r.MinY + t), new Point2(r.MinX + 6, r.MinY + t)) { Layer = "PANEL_DUCTS" });
                    break;
                default:
                    d.Add(new PolylineEntity(r.Corners(), true) { Layer = "PANEL_DEVICES", Filled = false });
                    d.Add(new PolylineEntity(Rect2.FromSize(r.MinX + 1, r.MinY + 1, r.Width - 2, r.Height - 2).Corners(), true)
                    { Layer = "PANEL_DEVICES", Filled = true, Color = new CadColor(241, 245, 249) });
                    var h = Math.Clamp(Math.Min(r.Width / Math.Max(1, i.Name.Length) / 0.7, r.Height / 4), 2.5, 10);
                    d.Add(new TextEntity(r.Center, i.Name, h) { Layer = "PANEL_DEVICES", HAlign = TextHAlign.Center, VAlign = TextVAlign.Middle, Rotation = r.Width < r.Height * 0.6 && i.Name.Length > 3 ? 90 : 0 });
                    break;
            }
        }
        // Overall dimensions
        d.Add(new DimensionEntity { Start = new Point2(0, 0), End = new Point2(cabinet.Width, 0), Offset = -25, TextHeight = 10, Layer = StandardLayers.Dimensions });
        d.Add(new DimensionEntity { Start = new Point2(cabinet.Width, 0), End = new Point2(cabinet.Width, cabinet.Height), Offset = -25, TextHeight = 10, Layer = StandardLayers.Dimensions });
        return d;
    }
}
