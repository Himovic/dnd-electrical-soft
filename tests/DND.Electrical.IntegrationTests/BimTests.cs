using DND.Electrical.Application.Services;
using DND.Electrical.Bim;
using DND.Electrical.Licensing;
using DND.Electrical.ThreeD;

namespace DND.Electrical.IntegrationTests;

public class BimTests
{
    [Fact]
    public void Ifc_export_and_import_roundtrip_geometry_and_identities()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-CAB-004");
        var scene = new ThreeDService(env.Licensing).GenerateCabinet(p, p.Cabinets[0]);
        scene.Objects.Add(new SceneObject3D { Name = "Rotated equipment", Kind = SceneObjectKind.Equipment, Size = new(400, 300, 1200), Position = new(1500, 800, 600), RotationDeg = new(0, 0, 30) });
        var path = env.PathFor("cab.ifc");
        new BimService(env.Licensing).ExportIfc(p, scene, path);
        Assert.StartsWith("ISO-10303-21;", File.ReadAllText(path));

        var (doc, result) = new BimService(env.Licensing).ImportIfc(p, path);
        Assert.Equal(scene.Objects.Count, result.Imported);
        Assert.Equal(0, result.Skipped);
        var back = ThreeDService.Load(doc);
        foreach (var o in scene.Objects)
        {
            var i = back.Objects.Single(x => x.Name.EndsWith(o.Name, StringComparison.Ordinal) && x.Tag == o.Tag && (x.Position - o.Position).Length < 1e-6);
            Assert.True((i.Size - o.Size).Length < 1e-6, $"{o.Name}: size {i.Size} vs {o.Size}");
            Assert.Equal(o.ObjectId, i.ObjectId);
            for (var k = 0; k < 3; k++) Assert.True((i.Rotation[k] - o.Rotation[k]).Length < 1e-9);
        }
        Assert.Equal(scene.Bounds().Size.X, back.Bounds().Size.X, 6);
    }

    [Fact]
    public void Ifc_requires_the_bim_module()
    {
        using var env = new TestEnvironment();
        var limited = DND.Electrical.TestSupport.TestLicensing.Create(env.PathFor("lim"), EngineeringModule.ThreeD);
        var p = env.Demos.Create("DEMO-CAB-004");
        var scene = new ThreeDService(limited).GenerateCabinet(p, p.Cabinets[0]);
        Assert.Equal(EngineeringModule.BIM, Assert.Throws<LicenseException>(() => new BimService(limited).ExportIfc(p, scene, env.PathFor("x.ifc"))).Module);
        Assert.False(File.Exists(env.PathFor("x.ifc")));
    }
}
