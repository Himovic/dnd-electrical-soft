using System.Text.Json.Serialization;
using DND.Electrical.CAD.Dxf;
using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;

namespace DND.Electrical.Schematic.Entities;

/// <summary>Where a wire end is attached. Both fields null means the end is open (dangling).</summary>
public sealed class WireEnd
{
    public Guid? InstanceId { get; set; }
    public string? PinId { get; set; }
    public Guid? JunctionId { get; set; }

    [JsonIgnore] public bool IsConnected => (InstanceId is not null && PinId is not null) || JunctionId is not null;

    public static WireEnd Pin(Guid instanceId, string pinId) => new() { InstanceId = instanceId, PinId = pinId };
    public static WireEnd Junction(Guid junctionId) => new() { JunctionId = junctionId };
    public static WireEnd Open() => new();

    public WireEnd Clone() => (WireEnd)MemberwiseClone();

    public bool RefersTo(Guid entityId) => InstanceId == entityId || JunctionId == entityId;
}

/// <summary>
/// Electrical connection drawn as an orthogonal polyline between two connection points.
/// Connectivity is defined by <see cref="Start"/>/<see cref="End"/> references, not by geometry.
/// </summary>
public sealed class WireEntity : CadEntity, IExplodableEntity
{
    public WireEntity() => Layer = StandardLayers.Wires;

    public List<Point2> Points { get; set; } = new();
    public WireEnd Start { get; set; } = new();
    public WireEnd End { get; set; } = new();

    /// <summary>Persisted wire number (assigned automatically or manually).</summary>
    public string WireNumber { get; set; } = string.Empty;

    /// <summary>When true, automatic renumbering keeps <see cref="WireNumber"/>.</summary>
    public bool NumberLocked { get; set; }

    public string WireColor { get; set; } = string.Empty;
    public double? CrossSectionMm2 { get; set; }
    public string WireType { get; set; } = string.Empty;

    /// <summary>Physical length in metres when known (otherwise estimated from routing).</summary>
    public double? LengthM { get; set; }

    public Guid? CableId { get; set; }
    public int? CableCoreIndex { get; set; }
    public bool ShowNumber { get; set; } = true;

    [JsonIgnore] public string? PotentialName { get; set; }

    /// <summary>Cable core label displayed next to the wire (bound at runtime, e.g. "W3:BN").</summary>
    [JsonIgnore] public string? CableLabel { get; set; }

    public override string TypeName => "Wire";

    public Point2 StartPoint => Points.Count > 0 ? Points[0] : Point2.Zero;
    public Point2 EndPoint => Points.Count > 0 ? Points[^1] : Point2.Zero;

    public double DrawnLength
    {
        get
        {
            double l = 0;
            for (int i = 0; i + 1 < Points.Count; i++) l += Points[i].DistanceTo(Points[i + 1]);
            return l;
        }
    }

    public IEnumerable<(Point2 A, Point2 B, int Index)> Segments()
    {
        for (int i = 0; i + 1 < Points.Count; i++) yield return (Points[i], Points[i + 1], i);
    }

    public override Rect2 GetBounds() => Rect2.FromPoints(Points).Inflate(0.6);

    public override double DistanceTo(Point2 p) =>
        Points.Count < 2 ? (Points.Count == 1 ? p.DistanceTo(Points[0]) : double.MaxValue)
            : Segments().Min(s => GeometryMath.DistancePointToSegment(p, s.A, s.B));

    public override bool IsInside(Rect2 r) => Points.All(r.Contains);
    public override bool IntersectsRect(Rect2 r) => Segments().Any(s => GeometryMath.SegmentIntersectsRect(s.A, s.B, r));

    public override void Transform(Matrix2 m)
    {
        for (int i = 0; i < Points.Count; i++) Points[i] = m.Transform(Points[i]);
    }

    public override IEnumerable<SnapPoint> GetSnapPoints()
    {
        if (Points.Count == 0) yield break;
        yield return new SnapPoint(Points[0], SnapKind.Endpoint, this);
        yield return new SnapPoint(Points[^1], SnapKind.Endpoint, this);
        for (int i = 1; i + 1 < Points.Count; i++) yield return new SnapPoint(Points[i], SnapKind.Midpoint, this);
    }

    protected override void DeepCopyFrom(CadEntity source)
    {
        var s = (WireEntity)source;
        Points = new List<Point2>(s.Points);
        Start = s.Start.Clone();
        End = s.End.Clone();
    }

    /// <summary>Finds the segment containing <paramref name="p"/> (excluding the wire ends).</summary>
    public int FindSegment(Point2 p, double tolerance)
    {
        foreach (var (a, b, i) in Segments())
            if (GeometryMath.DistancePointToSegment(p, a, b) <= tolerance) return i;
        return -1;
    }

    public IEnumerable<CadEntity> Explode()
    {
        if (Points.Count < 2) yield break;
        yield return new PolylineEntity(Points) { Layer = Layer, Color = Color, LineWeight = LineWeight };
        var label = WireNumber;
        if (!string.IsNullOrEmpty(CableLabel)) label = string.IsNullOrEmpty(label) ? CableLabel : $"{label}  {CableLabel}";
        if (!ShowNumber || string.IsNullOrEmpty(label)) yield break;

        // Place the number next to the longest segment, reading direction kept upright.
        var (a, b, _) = Segments().OrderByDescending(s => s.A.DistanceTo(s.B)).First();
        var mid = Point2.Lerp(a, b, 0.5);
        var vertical = Math.Abs(b.X - a.X) < Math.Abs(b.Y - a.Y);
        yield return vertical
            ? new TextEntity(mid + new Point2(-0.8, 0), label, 2) { Layer = StandardLayers.WireNumbers, Rotation = 90, HAlign = TextHAlign.Center, VAlign = TextVAlign.Bottom }
            : new TextEntity(mid + new Point2(0, 0.8), label, 2) { Layer = StandardLayers.WireNumbers, HAlign = TextHAlign.Center, VAlign = TextVAlign.Bottom };
    }
}

/// <summary>Connection node where three or more wires meet (drawn as a dot).</summary>
public sealed class JunctionEntity : CadEntity, IExplodableEntity
{
    public JunctionEntity() => Layer = StandardLayers.Wires;
    public JunctionEntity(Point2 position) : this() => Position = position;

    public Point2 Position { get; set; }

    public override string TypeName => "Junction";
    public override Rect2 GetBounds() => Rect2.FromCenter(Position, 0.8, 0.8);
    public override double DistanceTo(Point2 p) => Math.Max(0, p.DistanceTo(Position) - 0.8);
    public override void Transform(Matrix2 m) => Position = m.Transform(Position);
    public override IEnumerable<SnapPoint> GetSnapPoints() { yield return new SnapPoint(Position, SnapKind.Pin, new JunctionRef(Id)); }

    public IEnumerable<CadEntity> Explode()
    {
        yield return new CircleEntity(Position, 0.7) { Layer = Layer, Filled = true, Color = Color };
    }
}

public static class SchematicEntityTypes
{
    private static int _registered;

    /// <summary>Registers schematic entity types for (de)serialization. Safe to call many times.</summary>
    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;
        CAD.Serialization.CadEntityTypeRegistry.Register<SymbolInstanceEntity>("SymbolInstance");
        CAD.Serialization.CadEntityTypeRegistry.Register<WireEntity>("Wire");
        CAD.Serialization.CadEntityTypeRegistry.Register<JunctionEntity>("Junction");
    }

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Init() => Register();
}
