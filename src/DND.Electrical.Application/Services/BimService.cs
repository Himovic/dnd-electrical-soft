using DND.Electrical.Bim;
using DND.Electrical.Domain;
using DND.Electrical.Licensing;
using DND.Electrical.ThreeD;

namespace DND.Electrical.Application.Services;

/// <summary>BIM module: IFC4 export of 3D layouts with engineering identities and import of IFC context geometry. Requires BIM.</summary>
public sealed class BimService(IEntitlements entitlements)
{
    public void ExportIfc(ElectricalProject p, Scene3D scene, string path)
    {
        entitlements.Demand(EngineeringModule.BIM);
        var motors = p.Motors.Where(m => m.DeviceId is not null).Select(m => m.DeviceId!.Value).ToHashSet();
        IfcElementClass Classify(SceneObject3D o)
        {
            if (o.Kind is SceneObjectKind.CableTray or SceneObjectKind.CableDuct or SceneObjectKind.DinRail) return IfcElementClass.CableCarrierSegment;
            if (o.Kind == SceneObjectKind.Cable) return IfcElementClass.CableSegment;
            if (o.ObjectId is not { } id) return o.Kind == SceneObjectKind.Equipment ? IfcElementClass.DistributionBoard : IfcElementClass.Proxy;
            if (motors.Contains(id)) return IfcElementClass.Motor;
            if (p.Instruments.Any(i => i.DeviceId == id)) return IfcElementClass.Sensor;
            var prefix = p.FindDevice(id)?.Tag.TrimStart('-', '+', '=').FirstOrDefault() ?? ' ';
            // IEC 81346-2 letter codes: F protection, Q switching of energy flows, K signal processing (relays, contactors).
            return char.ToUpperInvariant(prefix) switch { 'F' => IfcElementClass.ProtectiveDevice, 'Q' => IfcElementClass.ProtectiveDevice, 'K' => IfcElementClass.SwitchingDevice, 'M' => IfcElementClass.Motor, _ => IfcElementClass.Proxy };
        }
        IfcExchange.Export(scene, path, new IfcExportOptions($"{p.Info.Number} {p.Info.Name}", p.Info.Site.Length > 0 ? p.Info.Site : "Site", p.Info.CustomerName.Length > 0 ? p.Info.CustomerName : "Building", Classify: Classify));
        p.Audit("IFC export", $"{scene.Name} → {Path.GetFileName(path)}");
    }

    /// <summary>Imports IFC geometry as a new context layout stored in the project.</summary>
    public (Layout3DDocument doc, IfcImportResult result) ImportIfc(ElectricalProject p, string path)
    {
        entitlements.Demand(EngineeringModule.BIM);
        var r = IfcExchange.Import(path);
        foreach (var o in r.Scene.Objects.Where(o => o.ObjectId is { } id && p.FindDevice(id) is null)) o.ObjectId = null; // identities from other projects are not kept
        var doc = new Layout3DDocument { Id = r.Scene.Id, Name = $"IFC: {r.Scene.Name}", Kind = "IFC context", SceneJson = r.Scene.ToJson() };
        p.Layouts3D.Add(doc);
        p.Audit("IFC import", $"{Path.GetFileName(path)}: {r.Imported} object(s), {r.Skipped} skipped");
        return (doc, r);
    }
}
