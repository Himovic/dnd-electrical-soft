using System.Text.Json;
using System.Text.Json.Serialization;

namespace DND.Electrical.ThreeD;

public enum SceneObjectKind
{
    EnclosurePanel, Door, MountingPlate, DinRail, CableDuct, Device, TerminalStrip, Accessory, CableTray, Cable, Equipment, Room, Busbar, Instrument, ClearanceZone,
}

public enum ShapeKind { Box, Tray, Cylinder }

/// <summary>
/// A solid in a 3D layout. <see cref="ObjectId"/> is the engineering-object identity (device id) shared with the
/// schematic, cabinet, BOM, PLC and cable representations; <see cref="Source"/> marks objects generated from another
/// representation so they can be regenerated without touching user-added objects.
/// </summary>
public sealed class SceneObject3D
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ObjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public SceneObjectKind Kind { get; set; } = SceneObjectKind.Device;
    public ShapeKind Shape { get; set; } = ShapeKind.Box;
    /// <summary>Extents along the local axes (mm). For trays/cylinders X is the length.</summary>
    public Vec3 Size { get; set; } = new(100, 100, 100);
    /// <summary>Centre in world coordinates (mm).</summary>
    public Vec3 Position { get; set; }
    /// <summary>Euler angles in degrees (X, then Y, then Z).</summary>
    public Vec3 RotationDeg { get; set; }
    public string Color { get; set; } = "#9CA3AF";
    public double Opacity { get; set; } = 1;
    public bool Visible { get; set; } = true;
    /// <summary>Generated objects are owned by a source (e.g. "cabinet:{id}") and replaced on regeneration.</summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>Source item (e.g. cabinet item id) for change detection.</summary>
    public Guid? SourceItemId { get; set; }
    public Guid? ParentId { get; set; }
    /// <summary>Required free distance to other solids (mm), e.g. ventilation or operating clearance.</summary>
    public double ClearanceMm { get; set; }
    /// <summary>Wall thickness for trays (mm).</summary>
    public double WallMm { get; set; } = 1.5;
    public string Material { get; set; } = string.Empty;

    [JsonIgnore] public Rot3 Rotation => Rot3.FromEulerDegrees(RotationDeg);
    public Obb Box() => new(Position, Size * 0.5, Rotation);
    public bool IsSolid => Kind is not SceneObjectKind.ClearanceZone and not SceneObjectKind.Room;

    public Mesh LocalMesh() => Shape switch
    {
        ShapeKind.Tray => MeshBuilder.Tray(Size.X, Size.Y, Size.Z, WallMm),
        ShapeKind.Cylinder => MeshBuilder.Cylinder(Size.X, Math.Min(Size.Y, Size.Z)),
        _ => MeshBuilder.Box(Size),
    };

    public Mesh WorldMesh()
    {
        var m = new Mesh(); var r = Rotation;
        m.Append(LocalMesh(), p => Position + r.Apply(p), r.Apply);
        return m;
    }
}

/// <summary>Cable tray / ladder route through a sequence of 3D points.</summary>
public sealed class CableTrayRoute
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Tag { get; set; } = "TR-01";
    public List<Vec3> Points { get; set; } = new();
    public double WidthMm { get; set; } = 200;
    public double HeightMm { get; set; } = 60;
    /// <summary>Fill limit used for checking, as defined by the project standard (no default is claimed as normative).</summary>
    public double MaxFillPercent { get; set; } = 40;
    public List<Guid> CableIds { get; set; } = new();
    public double LengthMm => Points.Zip(Points.Skip(1), (a, b) => (b - a).Length).Sum();
}

public sealed class Scene3D
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "3D layout";
    public string Units => "mm";
    public List<SceneObject3D> Objects { get; set; } = new();
    public List<CableTrayRoute> TrayRoutes { get; set; } = new();

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = false, Converters = { new JsonStringEnumConverter() } };
    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static Scene3D FromJson(string json) => JsonSerializer.Deserialize<Scene3D>(json, Json) ?? new Scene3D();

    public SceneObject3D? Find(Guid id) => Objects.FirstOrDefault(o => o.Id == id);
    public IEnumerable<SceneObject3D> ForEngineeringObject(Guid objectId) => Objects.Where(o => o.ObjectId == objectId);

    public Aabb Bounds() => Objects.Count == 0 ? new Aabb(Vec3.Zero, Vec3.Zero) : Objects.Select(o => o.Box().Bounds()).Aggregate(Aabb.Union);

    /// <summary>Solids generated for cable tray routes (one tray segment per route leg).</summary>
    public IEnumerable<SceneObject3D> TraySegments()
    {
        foreach (var r in TrayRoutes)
            for (var i = 0; i + 1 < r.Points.Count; i++)
            {
                var a = r.Points[i]; var b = r.Points[i + 1]; var dir = b - a;
                if (dir.Length < 1e-6) continue;
                var rot = Rot3.AlongDirection(dir);
                yield return new SceneObject3D
                {
                    Id = DeterministicId(r.Id, i), Name = $"{r.Tag} segment {i + 1}", Tag = r.Tag, Kind = SceneObjectKind.CableTray, Shape = ShapeKind.Tray,
                    Size = new Vec3(dir.Length, r.WidthMm, r.HeightMm), Position = (a + b) * 0.5, RotationDeg = EulerFrom(rot), Color = "#A3A3A3", Source = $"tray:{r.Id}",
                };
            }
    }

    /// <summary>All solids including generated tray segments.</summary>
    public IEnumerable<SceneObject3D> AllSolids() => Objects.Concat(TraySegments()).Where(o => o.Visible && o.IsSolid);

    internal static Guid DeterministicId(Guid seed, int index)
    {
        var b = seed.ToByteArray();
        var i = BitConverter.GetBytes(index);
        for (var k = 0; k < 4; k++) b[12 + k] ^= i[k];
        return new Guid(b);
    }

    /// <summary>Euler angles (deg, X→Y→Z) that reproduce <paramref name="r"/>.</summary>
    public static Vec3 EulerFrom(Rot3 r)
    {
        // R = Rz Ry Rx; R[2,0] = -sin(y)
        var sy = -r.AxisX.Z;
        double x, y, z;
        if (Math.Abs(sy) < 0.999999)
        {
            y = Math.Asin(sy);
            x = Math.Atan2(r.AxisY.Z, r.AxisZ.Z);
            z = Math.Atan2(r.AxisX.Y, r.AxisX.X);
        }
        else
        {
            y = Math.Asin(Math.Clamp(sy, -1, 1));
            x = 0;
            z = Math.Atan2(-r.AxisY.X, r.AxisY.Y);
        }
        const double k = 180 / Math.PI;
        return new Vec3(x * k, y * k, z * k);
    }
}
