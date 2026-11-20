using System.Text.Json;
using DND.Electrical.Application.Services;
using DND.Electrical.Domain;
using DND.Electrical.Licensing;
using DND.Electrical.Panel;
using DND.Electrical.ThreeD;

namespace DND.Electrical.IntegrationTests;

public class ThreeDTests
{
    public static IEnumerable<object[]> CabinetDemos => new[] { new object[] { "DEMO-IND-001" }, new object[] { "DEMO-CAB-004" } };

    [Theory]
    [MemberData(nameof(CabinetDemos))]
    public void Demo_cabinets_generate_clash_free_3d_models_consistent_with_2d(string number)
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create(number);
        var cab = p.Cabinets[0];
        Assert.DoesNotContain(CabinetLayoutService.Check(cab), i => i.Severity == "Error");
        var scene = new ThreeDService(env.Licensing).GenerateCabinet(p, cab);
        Assert.Equal(cab.Items.Count + 7, scene.Objects.Count); // 5 walls + door + plate
        Assert.DoesNotContain(SceneAnalysis.FindClashes(scene, cab.ClearanceMm), c => c.Kind == ClashKind.Hard);
        var b = scene.Bounds();
        Assert.Equal(cab.Width, b.Size.X, 6); Assert.Equal(cab.Depth, b.Size.Y, 6); Assert.Equal(cab.Height, b.Size.Z, 6);
        Assert.Empty(CabinetModel.Compare(scene, p));
        // Every device item keeps its engineering identity in 3D.
        foreach (var i in cab.Items.Where(i => i.DeviceId is not null))
            Assert.Contains(scene.Objects, o => o.ObjectId == i.DeviceId && o.SourceItemId == i.Id);
    }

    [Fact]
    public void Too_deep_device_collides_with_door_and_2d_change_is_detected_then_regenerated()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-CAB-004");
        var cab = p.Cabinets[0];
        var svc = new ThreeDService(env.Licensing);
        var scene = svc.GenerateCabinet(p, cab);
        var item = cab.Items.First(i => i.Kind == CabinetItemKind.Device && i.Face == CabinetFace.MountingPlate);

        item.Depth = cab.Depth; // 2D layout cannot see this; 3D must
        Assert.Contains(CabinetModel.Compare(scene, p), s => s.Kind == SyncIssueKind.ChangedInSource && s.SceneObjectId == item.Id);
        scene = svc.GenerateCabinet(p, cab);
        var clashes = svc.Clashes(p, scene);
        Assert.Contains(clashes, c => c.Kind == ClashKind.Hard && (c.A.Kind == SceneObjectKind.Door || c.B.Kind == SceneObjectKind.Door) && (c.A.Id == item.Id || c.B.Id == item.Id));
        Assert.Empty(CabinetModel.Compare(scene, p));
    }

    [Fact]
    public void Sync_reports_new_removed_and_deleted_objects_and_pushes_3d_moves_to_2d()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-IND-001");
        var cab = p.Cabinets[0];
        var scene = new ThreeDService(env.Licensing).GenerateCabinet(p, cab);
        var added = CabinetLayoutService.AddDuct(cab, 10, 10, 40, 40);
        var removed = cab.Items.First(i => i.DeviceId is not null && i.Face == CabinetFace.MountingPlate);
        cab.Items.Remove(removed);
        var issues = CabinetModel.Compare(scene, p);
        Assert.Contains(issues, s => s.Kind == SyncIssueKind.NotIn3D);
        Assert.Contains(issues, s => s.Kind == SyncIssueKind.RemovedFromSource && s.SceneObjectId == removed.Id);

        var dev = p.FindDevice(cab.Items.First(i => i.DeviceId is not null).DeviceId!.Value)!;
        p.Devices.Remove(dev);
        Assert.Contains(CabinetModel.Compare(scene, p), s => s.Kind == SyncIssueKind.EngineeringObjectDeleted && s.ObjectId == dev.Id);

        // 3D → 2D: move the duct object 50 mm to the right in 3D and write it back.
        CabinetModel.Regenerate(scene, cab, p);
        var o = scene.Find(added.Id)!;
        o.Position += new Vec3(50, 0, 0);
        Assert.True(CabinetModel.PushToLayout(o, p));
        Assert.Equal(60, added.X, 6);
        Assert.DoesNotContain(CabinetModel.Compare(scene, p), s => s.SceneObjectId == added.Id);
    }

    [Fact]
    public async Task Layouts_persist_and_appear_as_engineering_object_representations()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-IND-001");
        var scene = new ThreeDService(env.Licensing).GenerateCabinet(p, p.Cabinets[0]);
        scene.TrayRoutes.Add(new CableTrayRoute { Tag = "TR-01", Points = { new(0, 0, 2000), new(3000, 0, 2000) }, CableIds = { p.Cables[0].Id } });
        new ThreeDService(env.Licensing).Save(p, scene);
        var path = env.PathFor("3d.dndx");
        await env.Store.SaveAsync(p, path);
        var l = await env.Store.LoadAsync(path);
        var doc = Assert.Single(l.Layouts3D);
        Assert.Equal(p.Cabinets[0].Id, doc.CabinetId);
        var back = ThreeDService.Load(doc);
        Assert.Equal(scene.Objects.Count, back.Objects.Count);
        Assert.Equal("TR-01", back.TrayRoutes.Single().Tag);

        var item = l.Cabinets[0].Items.First(i => i.DeviceId is not null);
        var view = EngineeringObjectService.Describe(l, l.FindDevice(item.DeviceId!.Value)!);
        Assert.Contains(view.Representations, r => r.Kind == RepresentationKind.Model3D && r.Id == item.Id);

        var fill = SceneAnalysis.TrayFill(back.TrayRoutes[0], l);
        Assert.Equal(l.Cables[0].OuterDiameterMm is null ? 1 : 0, fill.CablesWithoutDiameter.Count);
    }

    [Fact]
    public void Glb_export_is_well_formed_with_metric_y_up_root_and_identities()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-CAB-004");
        var svc = new ThreeDService(env.Licensing);
        var scene = svc.GenerateCabinet(p, p.Cabinets[0]);
        var path = env.PathFor("cab.glb");
        svc.Export(scene, path);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal("glTF"u8.ToArray(), bytes[..4]);
        Assert.Equal(2u, BitConverter.ToUInt32(bytes, 4));
        Assert.Equal((uint)bytes.Length, BitConverter.ToUInt32(bytes, 8));
        var jsonLen = (int)BitConverter.ToUInt32(bytes, 12);
        Assert.Equal(0x4E4F534Au, BitConverter.ToUInt32(bytes, 16));
        using var json = JsonDocument.Parse(bytes.AsMemory(20, jsonLen));
        var root = json.RootElement;
        Assert.Equal("2.0", root.GetProperty("asset").GetProperty("version").GetString());
        var nodes = root.GetProperty("nodes");
        var sceneRoot = nodes[root.GetProperty("scenes")[0].GetProperty("nodes")[0].GetInt32()];
        Assert.Equal(0.001, sceneRoot.GetProperty("scale")[0].GetDouble(), 9);
        Assert.Equal(-Math.Sqrt(0.5), sceneRoot.GetProperty("rotation")[0].GetDouble(), 9);
        var withIds = nodes.EnumerateArray().Count(n => n.TryGetProperty("extras", out var e) && e.GetProperty("dndObjectId").ValueKind == JsonValueKind.String);
        Assert.Equal(p.Cabinets[0].Items.Count(i => i.DeviceId is not null), withIds);
        var binLen = (int)BitConverter.ToUInt32(bytes, 20 + jsonLen);
        Assert.Equal(root.GetProperty("buffers")[0].GetProperty("byteLength").GetInt32(), binLen);
        Assert.Equal(bytes.Length, 20 + jsonLen + 8 + binLen);

        svc.Export(scene, env.PathFor("cab.stl"));
        var stl = File.ReadAllBytes(env.PathFor("cab.stl"));
        Assert.Equal(84L + 50L * BitConverter.ToUInt32(stl, 80), (long)stl.Length);
        Assert.Throws<NotSupportedException>(() => svc.Export(scene, env.PathFor("cab.step")));
    }

    [Fact]
    public void Three_d_operations_require_the_3d_module()
    {
        using var env = new TestEnvironment();
        var limited = DND.Electrical.TestSupport.TestLicensing.Create(env.PathFor("lim"), EngineeringModule.ELEC);
        var p = env.Demos.Create("DEMO-IND-001");
        var svc = new ThreeDService(limited);
        var ex = Assert.Throws<LicenseException>(() => svc.GenerateCabinet(p, p.Cabinets[0]));
        Assert.Equal(EngineeringModule.ThreeD, ex.Module);
        Assert.Empty(p.Layouts3D);
    }

    [Fact]
    public void Clash_detection_scales_to_thousands_of_objects()
    {
        var s = new Scene3D();
        var rnd = new Random(42);
        for (var i = 0; i < 5000; i++)
            s.Objects.Add(new SceneObject3D { Kind = SceneObjectKind.Device, Size = new(18, 70, 90), Position = new(rnd.Next(0, 200000), rnd.Next(0, 2000), rnd.Next(0, 2000)) });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var clashes = SceneAnalysis.FindClashes(s);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 5000, $"5000-object clash check took {sw.ElapsedMilliseconds} ms");
        // Cross-check against brute force on the AABBs (all objects unrotated, so AABB overlap = OBB overlap).
        var boxes = s.Objects.Select(o => o.Box().Bounds()).ToArray();
        var brute = 0;
        for (var i = 0; i < boxes.Length; i++) for (var j = i + 1; j < boxes.Length; j++) if (boxes[i].Intersects(boxes[j], 0.01)) brute++;
        Assert.Equal(brute, clashes.Count);
    }
}
