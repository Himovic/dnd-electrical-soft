using DND.Electrical.Domain;

namespace DND.Electrical.ThreeD;

public enum ClashKind { Hard, Clearance }

public sealed record Clash(SceneObject3D A, SceneObject3D B, ClashKind Kind, double Value)
{
    public string Description => Kind == ClashKind.Hard
        ? FormattableString.Invariant($"{Label(A)} intersects {Label(B)} (penetration {Value:0.##} mm)")
        : FormattableString.Invariant($"{Label(A)} – {Label(B)}: distance {Value:0.##} mm is below the required clearance");
    private static string Label(SceneObject3D o) => string.IsNullOrEmpty(o.Tag) ? o.Name : $"{o.Tag} ({o.Name})";
}

public sealed record SectionSegment(Guid ObjectId, Vec3 A, Vec3 B);

public sealed record TrayFillResult(CableTrayRoute Route, double CableAreaMm2, double TrayAreaMm2, double FillPercent, IReadOnlyList<string> CablesWithoutDiameter)
{
    public bool Exceeded => FillPercent > Route.MaxFillPercent;
    public bool Complete => CablesWithoutDiameter.Count == 0;
}

/// <summary>Collision and clearance detection, measurement, sections and exploded views on a 3D scene.</summary>
public static class SceneAnalysis
{
    /// <summary>
    /// Finds intersecting solids and clearance violations. Broad phase: sweep-and-prune on X over the bounding boxes;
    /// narrow phase: separating-axis test on the oriented boxes and exact box-box distance for clearances.
    /// Pairs that are structurally in contact (enclosure panels with each other, parent/child) are ignored.
    /// </summary>
    public static IReadOnlyList<Clash> FindClashes(Scene3D scene, double defaultClearanceMm = 0)
    {
        var solids = scene.AllSolids().Select(o => (o, box: o.Box())).Select(t => (t.o, t.box, bounds: t.box.Bounds())).ToList();
        var margin = solids.Count == 0 ? 0 : Math.Max(defaultClearanceMm, solids.Max(s => s.o.ClearanceMm));
        solids.Sort((a, b) => a.bounds.Min.X.CompareTo(b.bounds.Min.X));
        var result = new List<Clash>();
        for (var i = 0; i < solids.Count; i++)
        {
            var (a, boxA, ba) = solids[i];
            for (var j = i + 1; j < solids.Count; j++)
            {
                var (b, boxB, bb) = solids[j];
                if (bb.Min.X > ba.Max.X + margin) break;
                if (!ba.Inflate(margin).Intersects(bb)) continue;
                if (Ignored(a, b)) continue;
                var pen = boxA.Penetration(boxB);
                if (pen > 0.01)
                {
                    if (!HollowContact(a, b)) result.Add(new Clash(a, b, ClashKind.Hard, pen));
                    continue;
                }
                var required = Math.Max(Math.Max(a.ClearanceMm, b.ClearanceMm), RequiresDefault(a, b) ? defaultClearanceMm : 0);
                if (required <= 0) continue;
                var dist = boxA.DistanceTo(boxB);
                if (dist < required - 1e-6) result.Add(new Clash(a, b, ClashKind.Clearance, dist));
            }
        }
        return result;
    }

    /// <summary>
    /// Trays are open profiles: consecutive segments of one route overlap at their joints, and cables are laid inside
    /// trays. These overlaps of bounding solids are expected and are not reported as clashes.
    /// </summary>
    private static bool HollowContact(SceneObject3D a, SceneObject3D b)
    {
        bool tA = a.Shape == ShapeKind.Tray, tB = b.Shape == ShapeKind.Tray;
        if (tA && tB) return a.Source.Length > 0 && a.Source == b.Source;
        return (tA && b.Kind == SceneObjectKind.Cable) || (tB && a.Kind == SceneObjectKind.Cable);
    }

    private static bool RequiresDefault(SceneObject3D a, SceneObject3D b) => a.Kind == SceneObjectKind.Device && b.Kind == SceneObjectKind.Device;

    private static bool Ignored(SceneObject3D a, SceneObject3D b)
    {
        if (a.Kind == SceneObjectKind.EnclosurePanel && b.Kind == SceneObjectKind.EnclosurePanel) return true;
        if (a.ParentId == b.Id || b.ParentId == a.Id) return true;
        return false;
    }

    public static double Distance(Vec3 a, Vec3 b) => (b - a).Length;

    /// <summary>Minimum clear distance between two solids (0 when they touch or intersect).</summary>
    public static double Distance(SceneObject3D a, SceneObject3D b) => a.Box().DistanceTo(b.Box());

    /// <summary>
    /// Section through the scene with the plane n·p = d: returns the outline segments where the plane cuts each
    /// visible object's triangle mesh.
    /// </summary>
    public static IReadOnlyList<SectionSegment> Section(Scene3D scene, Vec3 normal, double d)
    {
        var n = normal.Normalized();
        var result = new List<SectionSegment>();
        foreach (var o in scene.Objects.Concat(scene.TraySegments()).Where(o => o.Visible))
        {
            var b = o.Box();
            var r = b.HalfSize.X * Math.Abs(b.Rotation.AxisX.Dot(n)) + b.HalfSize.Y * Math.Abs(b.Rotation.AxisY.Dot(n)) + b.HalfSize.Z * Math.Abs(b.Rotation.AxisZ.Dot(n));
            if (Math.Abs(n.Dot(b.Center) - d) > r) continue;
            foreach (var (p0, p1, p2) in o.WorldMesh().Triangles())
            {
                var pts = new List<Vec3>(2);
                void Edge(Vec3 u, Vec3 v)
                {
                    double du = n.Dot(u) - d, dv = n.Dot(v) - d;
                    if ((du < 0 && dv >= 0) || (du >= 0 && dv < 0)) pts.Add(u + (v - u) * (du / (du - dv)));
                }
                Edge(p0, p1); Edge(p1, p2); Edge(p2, p0);
                if (pts.Count == 2 && (pts[1] - pts[0]).Length > 1e-9) result.Add(new SectionSegment(o.Id, pts[0], pts[1]));
            }
        }
        return result;
    }

    /// <summary>Exploded view: each object's displacement from its centre to the scene centre is scaled by (1 + factor). Returns new positions only; the layout is not modified.</summary>
    public static IReadOnlyDictionary<Guid, Vec3> Exploded(Scene3D scene, double factor, Vec3? explodeCenter = null)
    {
        var c = explodeCenter ?? scene.Bounds().Center;
        return scene.Objects.ToDictionary(o => o.Id, o => c + (o.Position - c) * (1 + factor));
    }

    /// <summary>
    /// Cable tray fill: Σ(π/4·OD²) of the cables assigned to the route against the tray cross-section (width × height).
    /// Cables without an outer diameter are listed rather than estimated.
    /// </summary>
    public static TrayFillResult TrayFill(CableTrayRoute route, ElectricalProject project)
    {
        var missing = new List<string>(); double area = 0;
        foreach (var id in route.CableIds)
        {
            var c = project.Cables.FirstOrDefault(x => x.Id == id);
            if (c is null) continue;
            if (c.OuterDiameterMm is { } od and > 0) area += Math.PI / 4 * od * od;
            else missing.Add(c.Tag);
        }
        var trayArea = route.WidthMm * route.HeightMm;
        return new TrayFillResult(route, area, trayArea, trayArea <= 0 ? 0 : area / trayArea * 100, missing);
    }
}

/// <summary>Clips triangle meshes against a plane (keeps the half-space n·p ≤ d) for section views.</summary>
public static class MeshClipper
{
    public static Mesh Clip(Mesh mesh, Vec3 normal, double d)
    {
        var n = normal.Normalized();
        var result = new Mesh();
        for (var t = 0; t < mesh.Indices.Count; t += 3)
        {
            var poly = new List<(Vec3 p, Vec3 nrm)>(3);
            for (var k = 0; k < 3; k++) poly.Add((mesh.Positions[mesh.Indices[t + k]], mesh.Normals[mesh.Indices[t + k]]));
            var output = new List<(Vec3 p, Vec3 nrm)>(4);
            for (var i = 0; i < poly.Count; i++)
            {
                var cur = poly[i]; var next = poly[(i + 1) % poly.Count];
                double dc = n.Dot(cur.p) - d, dn = n.Dot(next.p) - d;
                if (dc <= 0) output.Add(cur);
                if ((dc <= 0) != (dn <= 0))
                {
                    var s = dc / (dc - dn);
                    output.Add((cur.p + (next.p - cur.p) * s, (cur.nrm + (next.nrm - cur.nrm) * s).Normalized()));
                }
            }
            if (output.Count < 3) continue;
            var start = result.Positions.Count;
            foreach (var (p, nrm) in output) { result.Positions.Add(p); result.Normals.Add(nrm); }
            for (var i = 1; i + 1 < output.Count; i++) result.Indices.AddRange(new[] { start, start + i, start + i + 1 });
        }
        return result;
    }
}
