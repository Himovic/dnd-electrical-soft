using DND.Electrical.ThreeD;
using Xbim.Common;
using Xbim.Common.Configuration;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xbim.Ifc4.ElectricalDomain;
using Xbim.Ifc4.GeometricConstraintResource;
using Xbim.Ifc4.GeometricModelResource;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.Kernel;
using Xbim.Ifc4.MeasureResource;
using Xbim.Ifc4.ProductExtension;
using Xbim.Ifc4.ProfileResource;
using Xbim.Ifc4.PropertyResource;
using Xbim.Ifc4.RepresentationResource;
using Xbim.Ifc4.SharedBldgElements;
using Xbim.IO;

namespace DND.Electrical.Bim;

/// <summary>IFC4 entity chosen for a scene object (classification is supplied by the application layer).</summary>
public enum IfcElementClass { Proxy, DistributionBoard, ProtectiveDevice, SwitchingDevice, Motor, CableCarrierSegment, CableSegment, Sensor }

public sealed record IfcExportOptions(string ProjectName, string SiteName = "Site", string BuildingName = "Building", string StoreyName = "Level 0",
    Func<SceneObject3D, IfcElementClass>? Classify = null);

public sealed record IfcImportResult(Scene3D Scene, int Imported, int Skipped, IReadOnlyList<string> Notes);

/// <summary>
/// IFC4 exchange through the xBIM toolkit. Export: spatial structure (project/site/building/storey), one element per
/// scene object with a swept-solid body (rectangle profile extruded along the local Z axis), placement from the
/// object position and rotation, and the engineering identity in the property set <c>Pset_DnD_EngineeringObject</c>.
/// Import: products whose body is an extruded rectangle are converted to scene boxes; others are counted and
/// reported (no geometry is invented).
/// </summary>
public static class IfcExchange
{
    public const string PsetName = "Pset_DnD_EngineeringObject";
    private static readonly object InitLock = new();
    private static bool _initialised;

    private static void EnsureInitialised()
    {
        lock (InitLock)
        {
            if (_initialised) return;
            XbimServices.Current.ConfigureServices(s => s.AddXbimToolkit(c => c.AddMemoryModel()));
            _initialised = true;
        }
    }

    private static XbimEditorCredentials Credentials => new()
    {
        ApplicationDevelopersName = "D&D | Data&Development",
        ApplicationFullName = DND.Electrical.Shared.ProductInfo.ProductName,
        ApplicationIdentifier = DND.Electrical.Shared.ProductInfo.ProductId,
        ApplicationVersion = DND.Electrical.Shared.ProductInfo.Version.ToString(),
        EditorsFamilyName = "User", EditorsGivenName = "Engineering", EditorsOrganisationName = "D&D Engineering Suite user",
    };

    public static void Export(Scene3D scene, string path, IfcExportOptions options)
    {
        EnsureInitialised();
        using var model = IfcStore.Create(Credentials, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel);
        using (var txn = model.BeginTransaction("Export"))
        {
            var i = model.Instances;
            var project = i.New<IfcProject>(p => p.Name = options.ProjectName);
            var mm = i.New<IfcSIUnit>(u => { u.UnitType = IfcUnitEnum.LENGTHUNIT; u.Name = IfcSIUnitName.METRE; u.Prefix = IfcSIPrefix.MILLI; });
            var area = i.New<IfcSIUnit>(u => { u.UnitType = IfcUnitEnum.AREAUNIT; u.Name = IfcSIUnitName.SQUARE_METRE; });
            var volume = i.New<IfcSIUnit>(u => { u.UnitType = IfcUnitEnum.VOLUMEUNIT; u.Name = IfcSIUnitName.CUBIC_METRE; });
            var angle = i.New<IfcSIUnit>(u => { u.UnitType = IfcUnitEnum.PLANEANGLEUNIT; u.Name = IfcSIUnitName.RADIAN; });
            project.UnitsInContext = i.New<IfcUnitAssignment>(a => a.Units.AddRange(new IfcUnit[] { mm, area, volume, angle }));
            var origin = Placement3D(i, Vec3.Zero, Rot3.Identity);
            var context = i.New<IfcGeometricRepresentationContext>(c =>
            {
                c.ContextType = "Model"; c.ContextIdentifier = "Model"; c.CoordinateSpaceDimension = 3; c.Precision = 1e-5; c.WorldCoordinateSystem = origin;
            });
            var body = i.New<IfcGeometricRepresentationSubContext>(c =>
            {
                c.ContextType = "Model"; c.ContextIdentifier = "Body"; c.ParentContext = context; c.TargetView = IfcGeometricProjectionEnum.MODEL_VIEW;
            });
            project.RepresentationContexts.Add(context);

            var sitePlacement = i.New<IfcLocalPlacement>(p => p.RelativePlacement = Placement3D(i, Vec3.Zero, Rot3.Identity));
            var site = i.New<IfcSite>(s => { s.Name = options.SiteName; s.CompositionType = IfcElementCompositionEnum.ELEMENT; s.ObjectPlacement = sitePlacement; });
            var buildingPlacement = i.New<IfcLocalPlacement>(p => { p.PlacementRelTo = sitePlacement; p.RelativePlacement = Placement3D(i, Vec3.Zero, Rot3.Identity); });
            var building = i.New<IfcBuilding>(b => { b.Name = options.BuildingName; b.CompositionType = IfcElementCompositionEnum.ELEMENT; b.ObjectPlacement = buildingPlacement; });
            var storeyPlacement = i.New<IfcLocalPlacement>(p => { p.PlacementRelTo = buildingPlacement; p.RelativePlacement = Placement3D(i, Vec3.Zero, Rot3.Identity); });
            var storey = i.New<IfcBuildingStorey>(s => { s.Name = options.StoreyName; s.CompositionType = IfcElementCompositionEnum.ELEMENT; s.Elevation = 0; s.ObjectPlacement = storeyPlacement; });
            i.New<IfcRelAggregates>(r => { r.RelatingObject = project; r.RelatedObjects.Add(site); });
            i.New<IfcRelAggregates>(r => { r.RelatingObject = site; r.RelatedObjects.Add(building); });
            i.New<IfcRelAggregates>(r => { r.RelatingObject = building; r.RelatedObjects.Add(storey); });
            var contained = i.New<IfcRelContainedInSpatialStructure>(r => r.RelatingStructure = storey);

            foreach (var o in scene.Objects.Concat(scene.TraySegments()).Where(o => o.Visible))
            {
                IfcProduct element = (options.Classify?.Invoke(o) ?? Default(o)) switch
                {
                    IfcElementClass.DistributionBoard => i.New<IfcElectricDistributionBoard>(),
                    IfcElementClass.ProtectiveDevice => i.New<IfcProtectiveDevice>(),
                    IfcElementClass.SwitchingDevice => i.New<IfcSwitchingDevice>(),
                    IfcElementClass.Motor => i.New<IfcElectricMotor>(),
                    IfcElementClass.CableCarrierSegment => i.New<IfcCableCarrierSegment>(),
                    IfcElementClass.CableSegment => i.New<IfcCableSegment>(),
                    IfcElementClass.Sensor => i.New<Xbim.Ifc4.BuildingControlsDomain.IfcSensor>(),
                    _ => i.New<IfcBuildingElementProxy>(),
                };
                element.Name = string.IsNullOrEmpty(o.Tag) ? o.Name : $"{o.Tag} {o.Name}";
                element.ObjectType = o.Kind.ToString();
                element.Description = o.Material.Length > 0 ? o.Material : null;
                element.GlobalId = Xbim.Ifc4.UtilityResource.IfcGloballyUniqueId.ConvertToBase64(o.Id);
                element.ObjectPlacement = i.New<IfcLocalPlacement>(p => { p.PlacementRelTo = storeyPlacement; p.RelativePlacement = Placement3D(i, o.Position, o.Rotation); });
                var solid = i.New<IfcExtrudedAreaSolid>(s =>
                {
                    s.SweptArea = i.New<IfcRectangleProfileDef>(r =>
                    {
                        r.ProfileType = IfcProfileTypeEnum.AREA; r.XDim = o.Size.X; r.YDim = o.Size.Y;
                        r.Position = i.New<IfcAxis2Placement2D>(a => a.Location = i.New<IfcCartesianPoint>(pt => pt.SetXY(0, 0)));
                    });
                    s.Position = Placement3D(i, new Vec3(0, 0, -o.Size.Z / 2), Rot3.Identity);
                    s.ExtrudedDirection = i.New<IfcDirection>(d => d.SetXYZ(0, 0, 1));
                    s.Depth = o.Size.Z;
                });
                var shape = i.New<IfcShapeRepresentation>(r => { r.ContextOfItems = body; r.RepresentationIdentifier = "Body"; r.RepresentationType = "SweptSolid"; r.Items.Add(solid); });
                element.Representation = i.New<IfcProductDefinitionShape>(d => d.Representations.Add(shape));
                contained.RelatedElements.Add(element);

                var props = new List<IfcProperty>
                {
                    Text(i, "Tag", o.Tag), Text(i, "SceneObjectId", o.Id.ToString()), Text(i, "Kind", o.Kind.ToString()), Text(i, "Source", o.Source),
                };
                if (o.ObjectId is { } oid) props.Add(Text(i, "EngineeringObjectId", oid.ToString()));
                var pset = i.New<IfcPropertySet>(ps => { ps.Name = PsetName; ps.HasProperties.AddRange(props); });
                i.New<IfcRelDefinesByProperties>(r => { r.RelatingPropertyDefinition = pset; r.RelatedObjects.Add(element); });
            }
            txn.Commit();
        }
        // xBIM selects the format from the extension: keep ".ifc" on the temporary file.
        var tmp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $".{Path.GetFileNameWithoutExtension(path)}.{Guid.NewGuid():N}.ifc");
        model.SaveAs(tmp, StorageType.Ifc);
        File.Move(tmp, path, overwrite: true);
    }

    private static IfcElementClass Default(SceneObject3D o) => o.Kind switch
    {
        SceneObjectKind.CableTray or SceneObjectKind.CableDuct or SceneObjectKind.DinRail => IfcElementClass.CableCarrierSegment,
        SceneObjectKind.Cable => IfcElementClass.CableSegment,
        SceneObjectKind.Instrument => IfcElementClass.Sensor,
        _ => IfcElementClass.Proxy,
    };

    private static IfcPropertySingleValue Text(IEntityCollection m, string name, string value) =>
        m.New<IfcPropertySingleValue>(p => { p.Name = name; p.NominalValue = new IfcLabel(value); });

    private static IfcAxis2Placement3D Placement3D(IEntityCollection m, Vec3 at, Rot3 r) => m.New<IfcAxis2Placement3D>(a =>
    {
        a.Location = m.New<IfcCartesianPoint>(p => p.SetXYZ(at.X, at.Y, at.Z));
        a.Axis = m.New<IfcDirection>(d => d.SetXYZ(r.AxisZ.X, r.AxisZ.Y, r.AxisZ.Z));
        a.RefDirection = m.New<IfcDirection>(d => d.SetXYZ(r.AxisX.X, r.AxisX.Y, r.AxisX.Z));
    });

    /// <summary>Reads an IFC (2x3 or 4) file: extruded rectangular bodies become context boxes in a new scene.</summary>
    public static IfcImportResult Import(string path)
    {
        EnsureInitialised();
        using var model = IfcStore.Open(path, Credentials, null, null, XbimDBAccess.Read, -1);
        var scale = model.ModelFactors.OneMilliMeter > 0 ? 1 / model.ModelFactors.OneMilliMeter : 1; // model units → mm
        var scene = new Scene3D { Name = Path.GetFileNameWithoutExtension(path) };
        var notes = new List<string>();
        int skipped = 0;
        foreach (var product in model.Instances.OfType<IIfcProduct>().Where(p => p is not IIfcSpatialElement && p.Representation is not null))
        {
            var solid = product.Representation!.Representations.SelectMany(r => r.Items).OfType<IIfcExtrudedAreaSolid>().FirstOrDefault(s => s.SweptArea is IIfcRectangleProfileDef);
            if (solid is null || product.ObjectPlacement is not IIfcLocalPlacement placement) { skipped++; continue; }
            var rect = (IIfcRectangleProfileDef)solid.SweptArea;
            var (pos, rot) = Resolve(placement, scale);
            var dir = solid.ExtrudedDirection;
            if (Math.Abs(dir.Z) < 0.999) { skipped++; notes.Add($"{product.Name}: extrusion not along local Z – skipped."); continue; }
            var solidOrigin = solid.Position is { } sp ? new Vec3((double)sp.Location.X, (double)sp.Location.Y, (double)sp.Location.Z) * scale : Vec3.Zero;
            var depth = (double)solid.Depth * scale;
            var centreLocal = solidOrigin + new Vec3(0, 0, depth / 2 * Math.Sign(dir.Z));
            var o = new SceneObject3D
            {
                Name = product.Name?.ToString() ?? product.GetType().Name, Kind = SceneObjectKind.Equipment, Source = $"ifc:{Path.GetFileName(path)}",
                Size = new Vec3((double)rect.XDim * scale, (double)rect.YDim * scale, depth), Position = pos + rot.Apply(centreLocal), RotationDeg = Scene3D.EulerFrom(rot), Color = "#94A3B8",
            };
            foreach (var rel in product.IsDefinedBy)
                if (rel.RelatingPropertyDefinition is IIfcPropertySet ps && ps.Name == PsetName)
                    foreach (var prop in ps.HasProperties.OfType<IIfcPropertySingleValue>())
                    {
                        var v = prop.NominalValue?.ToString() ?? string.Empty;
                        if (prop.Name == "EngineeringObjectId" && Guid.TryParse(v, out var g)) o.ObjectId = g;
                        if (prop.Name == "Tag") o.Tag = v;
                        if (prop.Name == "Kind" && Enum.TryParse<SceneObjectKind>(v, out var k)) o.Kind = k;
                    }
            scene.Objects.Add(o);
        }
        if (skipped > 0) notes.Add($"{skipped} product(s) without an extruded rectangular body were not converted (their geometry is not approximated).");
        return new IfcImportResult(scene, scene.Objects.Count, skipped, notes);
    }

    private static (Vec3 pos, Rot3 rot) Resolve(IIfcLocalPlacement placement, double scale)
    {
        var pos = Vec3.Zero; var rot = Rot3.Identity;
        for (IIfcObjectPlacement? p = placement; p is IIfcLocalPlacement lp; p = lp.PlacementRelTo)
        {
            if (lp.RelativePlacement is not IIfcAxis2Placement3D a) continue;
            var loc = new Vec3((double)a.Location.X, (double)a.Location.Y, (double)a.Location.Z) * scale;
            var z = a.Axis is { } ax ? new Vec3(ax.X, ax.Y, ax.Z).Normalized() : Vec3.UnitZ;
            var x = a.RefDirection is { } rd ? new Vec3(rd.X, rd.Y, rd.Z).Normalized() : Vec3.UnitX;
            x = (x - z * x.Dot(z)).Normalized();
            var local = new Rot3(x, z.Cross(x), z);
            pos = loc + local.Apply(pos);
            rot = new Rot3(local.Apply(rot.AxisX), local.Apply(rot.AxisY), local.Apply(rot.AxisZ));
        }
        return (pos, rot);
    }
}
