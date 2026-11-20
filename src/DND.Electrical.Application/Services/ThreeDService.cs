using DND.Electrical.Domain;
using DND.Electrical.Licensing;
using DND.Electrical.ThreeD;

namespace DND.Electrical.Application.Services;

/// <summary>
/// 3D module application service: stores scenes in the project, generates cabinet models from the 2D layouts,
/// runs clash and synchronisation checks and exports glTF/GLB/STL. All operations require the 3D licence module.
/// </summary>
public sealed class ThreeDService(IEntitlements entitlements)
{
    public static Scene3D Load(Layout3DDocument doc)
    {
        var s = Scene3D.FromJson(doc.SceneJson);
        s.Id = doc.Id; s.Name = doc.Name;
        return s;
    }

    public void Save(ElectricalProject p, Scene3D scene, string kind = "Cabinet", Guid? cabinetId = null)
    {
        entitlements.Demand(EngineeringModule.ThreeD);
        var doc = p.Layouts3D.FirstOrDefault(d => d.Id == scene.Id);
        if (doc is null) { doc = new Layout3DDocument { Id = scene.Id, Kind = kind, CabinetId = cabinetId }; p.Layouts3D.Add(doc); }
        doc.Name = scene.Name;
        doc.SceneJson = scene.ToJson();
        doc.Modified = DateTimeOffset.Now;
    }

    /// <summary>Creates (or regenerates) the 3D model of a cabinet from its 2D layout and stores it in the project.</summary>
    public Scene3D GenerateCabinet(ElectricalProject p, Cabinet c, string? user = null)
    {
        entitlements.Demand(EngineeringModule.ThreeD);
        var doc = p.Layouts3D.FirstOrDefault(d => d.CabinetId == c.Id);
        var scene = doc is null ? new Scene3D { Name = $"{c.Tag} 3D" } : Load(doc);
        var n = CabinetModel.Regenerate(scene, c, p);
        Save(p, scene, "Cabinet", c.Id);
        p.Audit("3D model generated", $"{c.Tag}: {n} objects from the 2D cabinet layout", user);
        return scene;
    }

    public IReadOnlyList<Clash> Clashes(ElectricalProject p, Scene3D scene)
    {
        entitlements.Demand(EngineeringModule.ThreeD);
        var cab = p.Layouts3D.FirstOrDefault(d => d.Id == scene.Id)?.CabinetId is { } cid ? p.Cabinets.FirstOrDefault(c => c.Id == cid) : null;
        return SceneAnalysis.FindClashes(scene, cab?.ClearanceMm ?? 0);
    }

    public IReadOnlyList<SyncIssue> Compare(ElectricalProject p, Scene3D scene)
    {
        entitlements.Demand(EngineeringModule.ThreeD);
        return CabinetModel.Compare(scene, p);
    }

    public void Export(Scene3D scene, string path, IReadOnlyDictionary<Guid, Vec3>? positions = null)
    {
        entitlements.Demand(EngineeringModule.ThreeD);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
        {
            switch (ext)
            {
                case ".glb": GltfExporter.Write(scene, fs, binary: true, positions); break;
                case ".gltf": GltfExporter.Write(scene, fs, binary: false, positions); break;
                case ".stl": StlExporter.Write(scene, fs); break;
                default: throw new NotSupportedException($"Unsupported 3D export format '{ext}'. Supported: .glb, .gltf, .stl.");
            }
        }
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>3D representations of an engineering object across all stored layouts.</summary>
    public static IEnumerable<Representation> Representations(ElectricalProject p, Device d)
    {
        foreach (var doc in p.Layouts3D)
        {
            Scene3D scene;
            try { scene = Load(doc); } catch (System.Text.Json.JsonException) { continue; }
            foreach (var o in scene.ForEngineeringObject(d.Id))
                yield return new Representation(RepresentationKind.Model3D, o.Id, $"{o.Kind} {o.Size.X:0}×{o.Size.Y:0}×{o.Size.Z:0} mm", doc.Name);
        }
    }
}
