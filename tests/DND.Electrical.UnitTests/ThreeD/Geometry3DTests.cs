using DND.Electrical.ThreeD;

namespace DND.Electrical.UnitTests.ThreeD;

public class Geometry3DTests
{
    private static Obb Box(double x, double y, double z, double sx, double sy, double sz, double rz = 0) =>
        new(new Vec3(x, y, z), new Vec3(sx, sy, sz) * 0.5, Rot3.FromEulerDegrees(new Vec3(0, 0, rz)));

    [Fact]
    public void Box_mesh_is_closed_with_outward_normals()
    {
        var m = MeshBuilder.Box(new Vec3(100, 50, 20));
        Assert.Equal(12, m.TriangleCount);
        Assert.Equal(100 * 50 * 20, m.Volume(), 6);
        // Every triangle's winding agrees with its stored normal.
        for (var i = 0; i < m.Indices.Count; i += 3)
        {
            var a = m.Positions[m.Indices[i]]; var b = m.Positions[m.Indices[i + 1]]; var c = m.Positions[m.Indices[i + 2]];
            Assert.True((b - a).Cross(c - a).Dot(m.Normals[m.Indices[i]]) > 0);
        }
    }

    [Fact]
    public void Cylinder_volume_converges_to_pi_r2_l()
    {
        var m = MeshBuilder.Cylinder(200, 20, 64);
        var exact = Math.PI * 10 * 10 * 200;
        // Inscribed 64-gon: area ratio = (n/2π)·sin(2π/n) ≈ 0.99839
        Assert.Equal(exact * 64 / (2 * Math.PI) * Math.Sin(2 * Math.PI / 64), m.Volume(), 3);
    }

    [Fact]
    public void Euler_rotation_and_quaternion_are_consistent()
    {
        var r = Rot3.FromEulerDegrees(new Vec3(0, 0, 90));
        var v = r.Apply(Vec3.UnitX);
        Assert.Equal(0, v.X, 9); Assert.Equal(1, v.Y, 9);
        var (x, y, z, w) = r.ToQuaternion();
        Assert.Equal(0, x, 9); Assert.Equal(0, y, 9);
        Assert.Equal(Math.Sin(Math.PI / 4), z, 9); Assert.Equal(Math.Cos(Math.PI / 4), w, 9);

        var arbitrary = Rot3.FromEulerDegrees(new Vec3(17, -33, 121));
        var back = Rot3.FromEulerDegrees(Scene3D.EulerFrom(arbitrary));
        for (var i = 0; i < 3; i++) Assert.True((arbitrary[i] - back[i]).Length < 1e-9);
        Assert.Equal(1, arbitrary.AxisX.Length, 12);
        Assert.Equal(0, arbitrary.AxisX.Dot(arbitrary.AxisY), 12);
    }

    [Fact]
    public void Separating_axis_test_handles_touching_overlapping_and_rotated_boxes()
    {
        var a = Box(0, 0, 0, 10, 10, 10);
        Assert.False(a.Intersects(Box(10, 0, 0, 10, 10, 10)));          // face contact
        Assert.True(a.Intersects(Box(9, 0, 0, 10, 10, 10)));            // 1 mm overlap
        Assert.Equal(1, a.Penetration(Box(9, 0, 0, 10, 10, 10)), 9);
        // A 45° rotated cube reaches 5·√2 = 7.07 mm from its centre: at 12 mm it overlaps (5 + 7.07 > 12), at 12.1 mm it does not.
        Assert.True(a.Intersects(Box(12, 0, 0, 10, 10, 10, 45)));
        Assert.False(a.Intersects(Box(12.1, 0, 0, 10, 10, 10, 45)));
        // Rotated boxes whose AABBs overlap but which are separated along a rotated axis.
        var thinDiag = new Obb(new Vec3(8, 8, 0), new Vec3(10, 0.5, 5), Rot3.FromEulerDegrees(new Vec3(0, 0, -45)));
        Assert.True(a.Bounds().Intersects(thinDiag.Bounds()));
        Assert.False(a.Intersects(thinDiag));
    }

    [Fact]
    public void Box_distance_is_exact_for_face_edge_and_corner_cases()
    {
        var a = Box(0, 0, 0, 10, 10, 10);
        Assert.Equal(10, a.DistanceTo(Box(20, 0, 0, 10, 10, 10)), 9);                      // face-face (5..15)
        Assert.Equal(Math.Sqrt(3 * 25), a.DistanceTo(Box(15, 15, 15, 10, 10, 10)), 9);    // corner-corner
        Assert.Equal(Math.Sqrt(50), a.DistanceTo(Box(15, 15, 0, 10, 10, 10)), 9);          // edge-edge
        // Rotated 45°: nearest point is the corner at 20 − 5√2.
        Assert.Equal(20 - 5 - 5 * Math.Sqrt(2), a.DistanceTo(Box(20, 0, 0, 10, 10, 10, 45)), 9);
        Assert.Equal(0, a.DistanceTo(Box(5, 0, 0, 10, 10, 10)));
    }

    [Fact]
    public void Segment_distance_matches_closed_form()
    {
        Assert.Equal(1, Obb.SegmentDistance(new(0, 0, 0), new(1, 0, 0), new(0.5, 1, -1), new(0.5, 1, 1)), 12);
        Assert.Equal(Math.Sqrt(2), Obb.SegmentDistance(new(0, 0, 0), new(1, 0, 0), new(2, 1, 0), new(3, 1, 0)), 12);
    }

    [Fact]
    public void Clash_detection_reports_hard_and_clearance_violations_only()
    {
        var s = new Scene3D();
        s.Objects.Add(new SceneObject3D { Name = "A", Kind = SceneObjectKind.Device, Size = new(10, 10, 10), Position = new(0, 0, 0) });
        s.Objects.Add(new SceneObject3D { Name = "B", Kind = SceneObjectKind.Device, Size = new(10, 10, 10), Position = new(9, 0, 0) });   // overlaps A by 1
        s.Objects.Add(new SceneObject3D { Name = "C", Kind = SceneObjectKind.Device, Size = new(10, 10, 10), Position = new(22, 0, 0) });  // 3 mm from B
        s.Objects.Add(new SceneObject3D { Name = "D", Kind = SceneObjectKind.Device, Size = new(10, 10, 10), Position = new(60, 0, 0) });  // far away
        var none = SceneAnalysis.FindClashes(s);
        var hard = Assert.Single(none);
        Assert.Equal(ClashKind.Hard, hard.Kind);
        Assert.Equal(new[] { "A", "B" }, new[] { hard.A.Name, hard.B.Name }.Order());

        var withClearance = SceneAnalysis.FindClashes(s, defaultClearanceMm: 5);
        var cl = Assert.Single(withClearance, c => c.Kind == ClashKind.Clearance);
        Assert.Equal(3, cl.Value, 9);
        Assert.DoesNotContain(withClearance, c => c.A.Name == "D" || c.B.Name == "D");
    }

    [Fact]
    public void Section_of_a_box_is_its_rectangular_outline()
    {
        var s = new Scene3D();
        s.Objects.Add(new SceneObject3D { Size = new(40, 20, 10), Position = new(0, 0, 0) });
        var segs = SceneAnalysis.Section(s, Vec3.UnitZ, 0);
        var perimeter = segs.Sum(x => (x.B - x.A).Length);
        // Each side face is split into two triangles, so the outline is cut into 8 segments of total length 2·(40+20).
        Assert.Equal(120, perimeter, 6);
        Assert.All(segs, x => { Assert.Equal(0, x.A.Z, 9); Assert.Equal(0, x.B.Z, 9); });
        Assert.Empty(SceneAnalysis.Section(s, Vec3.UnitZ, 6));
    }

    [Fact]
    public void Tray_route_segments_follow_points_and_joints_are_not_clashes()
    {
        var s = new Scene3D();
        s.TrayRoutes.Add(new CableTrayRoute { Tag = "TR1", Points = { new(0, 0, 3000), new(5000, 0, 3000), new(5000, 4000, 3000), new(5000, 4000, 1000) }, WidthMm = 300, HeightMm = 60 });
        var segs = s.TraySegments().ToList();
        Assert.Equal(3, segs.Count);
        Assert.Equal(11000, s.TrayRoutes[0].LengthMm, 9);
        var vertical = segs[2].Box();
        Assert.Equal(2000, vertical.Bounds().Size.Z, 6);
        Assert.Empty(SceneAnalysis.FindClashes(s));
        s.Objects.Add(new SceneObject3D { Name = "Duct", Kind = SceneObjectKind.Equipment, Size = new(400, 400, 400), Position = new(2500, 0, 3000) });
        Assert.Single(SceneAnalysis.FindClashes(s));
    }

    [Fact]
    public void Exploded_view_scales_offsets_without_modifying_layout()
    {
        var s = new Scene3D();
        var a = new SceneObject3D { Size = new(10, 10, 10), Position = new(-10, 0, 0) };
        var b = new SceneObject3D { Size = new(10, 10, 10), Position = new(10, 0, 0) };
        s.Objects.AddRange(new[] { a, b });
        var e = SceneAnalysis.Exploded(s, 1.0);
        Assert.Equal(new Vec3(-20, 0, 0), e[a.Id]);
        Assert.Equal(new Vec3(20, 0, 0), e[b.Id]);
        Assert.Equal(new Vec3(-10, 0, 0), a.Position);
    }

    [Fact]
    public void Scene_json_roundtrip_preserves_geometry_and_identity()
    {
        var s = new Scene3D { Name = "Room" };
        var id = Guid.NewGuid();
        s.Objects.Add(new SceneObject3D { ObjectId = id, Tag = "-Q1", Kind = SceneObjectKind.Device, Size = new(18, 70, 90), Position = new(1, 2, 3), RotationDeg = new(0, 0, 30) });
        s.TrayRoutes.Add(new CableTrayRoute { Points = { new(0, 0, 0), new(100, 0, 0) } });
        var back = Scene3D.FromJson(s.ToJson());
        Assert.Equal("Room", back.Name);
        Assert.Equal(id, back.Objects[0].ObjectId);
        Assert.Equal(new Vec3(1, 2, 3), back.Objects[0].Position);
        Assert.Equal(new Vec3(0, 0, 30), back.Objects[0].RotationDeg);
        Assert.Equal(SceneObjectKind.Device, back.Objects[0].Kind);
        Assert.Equal(2, back.TrayRoutes[0].Points.Count);
    }
}

public class MeshClipperTests
{
    [Fact]
    public void Clipping_a_box_halves_its_surface_and_keeps_the_correct_side()
    {
        var box = MeshBuilder.Box(new Vec3(10, 10, 10));
        var half = MeshClipper.Clip(box, Vec3.UnitX, 0);
        Assert.All(half.Positions, p => Assert.True(p.X <= 1e-9));
        // Remaining surface: one full face (100) + four half faces (4·50); the cut face is open.
        double Area(Mesh m) => m.Triangles().Sum(t => (t.b - t.a).Cross(t.c - t.a).Length / 2);
        Assert.Equal(300, Area(half), 9);
        Assert.Equal(600, Area(MeshClipper.Clip(box, Vec3.UnitX, 100)), 9);
        Assert.Empty(MeshClipper.Clip(box, Vec3.UnitX, -100).Positions);
    }
}
