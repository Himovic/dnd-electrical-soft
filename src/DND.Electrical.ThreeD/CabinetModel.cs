using DND.Electrical.Domain;

namespace DND.Electrical.ThreeD;

public enum SyncIssueKind { NotIn3D, RemovedFromSource, ChangedInSource, EngineeringObjectDeleted, TagChanged }

public sealed record SyncIssue(SyncIssueKind Kind, string Message, Guid? SceneObjectId, Guid? ObjectId);

/// <summary>
/// Generates a 3D cabinet model from the 2D cabinet layout (same items, same engineering-object identities) and keeps
/// both in step. Coordinates: origin at the front-bottom-left corner of the enclosure, X to the right, Y from the
/// door towards the back wall, Z up; 2D layout X/Y are measured from the bottom-left of the mounting plate.
/// </summary>
public static class CabinetModel
{
    public const double WallMm = 2, PlateMm = 3, PlateStandoffMm = 10;

    public static string SourceOf(Cabinet c) => $"cabinet:{c.Id}";

    public static double PlateFrontY(Cabinet c) => c.Depth - WallMm - PlateStandoffMm - PlateMm;
    public static double PlateOriginX(Cabinet c) => (c.Width - c.PlateWidth) / 2;
    public static double PlateOriginZ(Cabinet c) => (c.Height - c.PlateHeight) / 2;

    public static IReadOnlyList<SceneObject3D> Generate(Cabinet c, ElectricalProject? project = null)
    {
        var src = SourceOf(c);
        var list = new List<SceneObject3D>();
        double w = c.Width, h = c.Height, d = c.Depth, t = WallMm;
        SceneObject3D Panel(string name, double x0, double x1, double y0, double y1, double z0, double z1, SceneObjectKind kind = SceneObjectKind.EnclosurePanel) => new()
        {
            Id = Scene3D.DeterministicId(c.Id, list.Count + 1), Name = name, Tag = c.Tag, Kind = kind, Size = new Vec3(x1 - x0, y1 - y0, z1 - z0),
            Position = new Vec3((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), Color = "#CBD5E1", Opacity = kind == SceneObjectKind.Door ? 0.15 : 0.3, Source = src, Material = "Sheet steel",
        };
        list.Add(Panel("Back wall", 0, w, d - t, d, 0, h));
        list.Add(Panel("Left wall", 0, t, t, d - t, 0, h));
        list.Add(Panel("Right wall", w - t, w, t, d - t, 0, h));
        list.Add(Panel("Bottom", t, w - t, t, d - t, 0, t));
        list.Add(Panel("Roof", t, w - t, t, d - t, h - t, h));
        var door = Panel("Door", 0, w, 0, t, 0, h, SceneObjectKind.Door);
        list.Add(door);

        double px = PlateOriginX(c), pz = PlateOriginZ(c), pf = PlateFrontY(c);
        var plate = new SceneObject3D
        {
            Id = Scene3D.DeterministicId(c.Id, 100), Name = "Mounting plate", Tag = c.Tag, Kind = SceneObjectKind.MountingPlate,
            Size = new Vec3(c.PlateWidth, PlateMm, c.PlateHeight), Position = new Vec3(px + c.PlateWidth / 2, pf + PlateMm / 2, pz + c.PlateHeight / 2),
            Color = "#E5E7EB", Source = src, Material = "Galvanised steel",
        };
        list.Add(plate);

        var rails = c.Items.Where(i => i.Kind == CabinetItemKind.DinRail).ToDictionary(i => i.Id);
        foreach (var i in c.Items)
        {
            var dev = i.DeviceId is { } did ? project?.FindDevice(did) : null;
            var o = new SceneObject3D
            {
                Id = i.Id, ObjectId = i.DeviceId, Name = i.Name.Length > 0 ? i.Name : dev?.Function ?? i.Kind.ToString(), Tag = dev?.Tag ?? string.Empty,
                Kind = KindOf(i.Kind), Source = src, SourceItemId = i.Id, Color = ColorOf(i.Kind), Size = new Vec3(i.Width, i.Depth, i.Height),
            };
            if (i.Face == CabinetFace.Door)
            {
                // Door-mounted devices: body behind the door (inside the enclosure).
                o.Position = new Vec3(i.X + i.Width / 2, t + i.Depth / 2, i.Y + i.Height / 2);
                o.ParentId = door.Id;
            }
            else
            {
                var basePlane = pf;
                if (i.RailId is { } rid && rails.TryGetValue(rid, out var rail)) { basePlane = pf - rail.Depth; o.ParentId = rail.Id; }
                else o.ParentId = plate.Id;
                o.Position = new Vec3(px + i.X + i.Width / 2, basePlane - i.Depth / 2, pz + i.Y + i.Height / 2);
            }
            list.Add(o);
        }
        return list;
    }

    private static SceneObjectKind KindOf(CabinetItemKind k) => k switch
    {
        CabinetItemKind.DinRail => SceneObjectKind.DinRail,
        CabinetItemKind.CableDuct => SceneObjectKind.CableDuct,
        CabinetItemKind.TerminalStrip => SceneObjectKind.TerminalStrip,
        CabinetItemKind.Device => SceneObjectKind.Device,
        _ => SceneObjectKind.Accessory,
    };

    private static string ColorOf(CabinetItemKind k) => k switch
    {
        CabinetItemKind.DinRail => "#9CA3AF",
        CabinetItemKind.CableDuct => "#6B7280",
        CabinetItemKind.TerminalStrip => "#F59E0B",
        CabinetItemKind.Device => "#2563EB",
        _ => "#10B981",
    };

    /// <summary>Replaces the objects generated from <paramref name="c"/>; objects added by the user are kept.</summary>
    public static int Regenerate(Scene3D scene, Cabinet c, ElectricalProject project)
    {
        var src = SourceOf(c);
        scene.Objects.RemoveAll(o => o.Source == src);
        var generated = Generate(c, project);
        scene.Objects.AddRange(generated);
        return generated.Count;
    }

    /// <summary>Clash detection for a generated cabinet: hard clashes plus the cabinet's device clearance.</summary>
    public static IReadOnlyList<Clash> Check(Scene3D scene, Cabinet c) => SceneAnalysis.FindClashes(scene, c.ClearanceMm);

    /// <summary>Compares the 3D layout with the project (cabinet layouts and engineering objects). Nothing is changed.</summary>
    public static IReadOnlyList<SyncIssue> Compare(Scene3D scene, ElectricalProject p)
    {
        var issues = new List<SyncIssue>();
        foreach (var group in scene.Objects.Where(o => o.Source.StartsWith("cabinet:", StringComparison.Ordinal)).GroupBy(o => o.Source))
        {
            var cab = p.Cabinets.FirstOrDefault(c => SourceOf(c) == group.Key);
            if (cab is null)
            {
                issues.Add(new(SyncIssueKind.RemovedFromSource, $"Cabinet of {group.Count()} generated objects no longer exists.", null, null));
                continue;
            }
            var expected = Generate(cab, p).Where(o => o.SourceItemId is not null).ToDictionary(o => o.SourceItemId!.Value);
            foreach (var o in group.Where(o => o.SourceItemId is not null))
            {
                if (!expected.TryGetValue(o.SourceItemId!.Value, out var e))
                    issues.Add(new(SyncIssueKind.RemovedFromSource, $"{Label(o)} was removed from cabinet {cab.Tag}.", o.Id, o.ObjectId));
                else if ((e.Position - o.Position).Length > 0.5 || (e.Size - o.Size).Length > 0.5)
                    issues.Add(new(SyncIssueKind.ChangedInSource, $"{Label(o)} differs between the 2D layout of {cab.Tag} and the 3D model.", o.Id, o.ObjectId));
            }
            var present = group.Select(o => o.SourceItemId).OfType<Guid>().ToHashSet();
            foreach (var i in cab.Items.Where(i => !present.Contains(i.Id)))
                issues.Add(new(SyncIssueKind.NotIn3D, $"{(i.Name.Length > 0 ? i.Name : i.Kind.ToString())} in cabinet {cab.Tag} is not in the 3D model.", null, i.DeviceId));
        }
        foreach (var o in scene.Objects.Where(o => o.ObjectId is not null))
        {
            var dev = p.FindDevice(o.ObjectId!.Value);
            if (dev is null) issues.Add(new(SyncIssueKind.EngineeringObjectDeleted, $"{Label(o)} refers to a deleted engineering object.", o.Id, o.ObjectId));
            else if (o.Tag.Length > 0 && !string.Equals(o.Tag, dev.Tag, StringComparison.OrdinalIgnoreCase))
                issues.Add(new(SyncIssueKind.TagChanged, $"3D object tag {o.Tag} differs from {dev.Tag}.", o.Id, o.ObjectId));
        }
        return issues;
    }

    /// <summary>
    /// 3D → 2D: writes the position of a moved generated object back to its cabinet item (plate or door plane).
    /// Called only after the user confirmed the change.
    /// </summary>
    public static bool PushToLayout(SceneObject3D o, ElectricalProject p)
    {
        if (o.SourceItemId is not { } itemId) return false;
        var cab = p.Cabinets.FirstOrDefault(c => SourceOf(c) == o.Source);
        var item = cab?.Items.FirstOrDefault(i => i.Id == itemId);
        if (cab is null || item is null) return false;
        if (item.Face == CabinetFace.Door) { item.X = o.Position.X - o.Size.X / 2; item.Y = o.Position.Z - o.Size.Z / 2; }
        else { item.X = o.Position.X - o.Size.X / 2 - PlateOriginX(cab); item.Y = o.Position.Z - o.Size.Z / 2 - PlateOriginZ(cab); }
        p.Audit("3D → 2D", $"{Label(o)} position written to cabinet {cab.Tag} layout");
        return true;
    }

    private static string Label(SceneObject3D o) => o.Tag.Length > 0 ? o.Tag : o.Name;
}
