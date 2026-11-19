using System.Text.Json.Serialization;
using DND.Electrical.CAD.Dxf;
using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;
using DND.Electrical.Symbols;

namespace DND.Electrical.Schematic.Entities;

/// <summary>Reference to a connection point: a symbol pin or a junction.</summary>
public sealed record PinRef(Guid InstanceId, string PinId);

public sealed record JunctionRef(Guid JunctionId);

/// <summary>
/// Placed symbol. Geometry comes from the bound <see cref="SymbolDefinition"/>; engineering identity
/// from the linked <see cref="Domain.Device"/>. Transform order: mirror (about local Y axis) → rotate → translate.
/// </summary>
public sealed class SymbolInstanceEntity : CadEntity, IExplodableEntity, IDxfBlockSource
{
    public string SymbolId { get; set; } = string.Empty;
    public Guid? DeviceId { get; set; }
    public Point2 Position { get; set; }
    public double Rotation { get; set; }
    public bool Mirrored { get; set; }

    /// <summary>Per-instance pin label overrides (e.g. contact numbers 13/14 → 43/44).</summary>
    public Dictionary<string, string> PinLabels { get; set; } = new();

    /// <summary>Instance values for attributes such as POTENTIAL, NAME, TERMINAL, FUNCTION.</summary>
    public Dictionary<string, string> Values { get; set; } = new();

    /// <summary>Linked PLC channel for PLC I/O symbols.</summary>
    public Guid? PlcChannelId { get; set; }

    // ---- runtime (bound) state, not persisted -------------------------------------------------

    [JsonIgnore] public SymbolDefinition? Definition { get; set; }

    /// <summary>Attribute values to display (TAG, FUNCTION, RATING, ADDRESS, XREF…), computed by the binder.</summary>
    [JsonIgnore] public Dictionary<string, string> Display { get; set; } = new();

    /// <summary>Contact mirror lines shown under main (coil) symbols.</summary>
    [JsonIgnore] public List<string> ContactMirror { get; set; } = new();

    public override string TypeName => "SymbolInstance";

    public Matrix2 LocalToWorld =>
        (Mirrored ? Matrix2.MirrorX() : Matrix2.Identity) * Matrix2.Rotation(Rotation) * Matrix2.Translation(Position);

    public string GetPinLabel(string pinId) =>
        PinLabels.TryGetValue(pinId, out var l) ? l : Definition?.FindPin(pinId)?.Label ?? pinId;

    public Point2? GetPinPosition(string pinId) =>
        Definition?.FindPin(pinId) is { } pin ? LocalToWorld.Transform(pin.Position) : null;

    public IEnumerable<(SymbolPin pin, Point2 world)> PinPositions()
    {
        if (Definition is null) yield break;
        var m = LocalToWorld;
        foreach (var p in Definition.Pins) yield return (p, m.Transform(p.Position));
    }

    public string? GetValue(string name) => Values.TryGetValue(name, out var v) ? v : null;

    public Rect2 GraphicBounds => Definition is null
        ? Rect2.FromCenter(Position, 5, 5)
        : Definition.Bounds.Transform(LocalToWorld);

    public override Rect2 GetBounds()
    {
        var r = GraphicBounds;
        if (Definition is null) return r;
        foreach (var t in AttributeTexts()) r = r.Union(t.GetBounds());
        return r;
    }

    /// <summary>Symbols are picked anywhere inside their graphic extent (plus a small margin).</summary>
    public override double DistanceTo(Point2 p) => DistToRect(p, GraphicBounds.Inflate(0.5));

    private static double DistToRect(Point2 p, Rect2 r)
    {
        var dx = Math.Max(Math.Max(r.MinX - p.X, 0), p.X - r.MaxX);
        var dy = Math.Max(Math.Max(r.MinY - p.Y, 0), p.Y - r.MaxY);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public override bool IsInside(Rect2 r) => r.Contains(GraphicBounds);
    public override bool IntersectsRect(Rect2 r) => r.Intersects(GraphicBounds);

    public override void Transform(Matrix2 m)
    {
        var linear = LocalToWorld * m;
        Position = m.Transform(Position);
        var mirrored = Mirrored ^ m.IsMirroring;
        var v = linear.TransformVector(new Point2(1, 0));
        if (mirrored) v = -v;
        Mirrored = mirrored;
        var angle = GeometryMath.NormalizeAngle(v.AngleDegrees);
        var snapped = Math.Round(angle / 90) * 90;
        Rotation = Math.Abs(angle - snapped) < 1e-6 ? GeometryMath.NormalizeAngle(snapped) : angle;
    }

    public override IEnumerable<SnapPoint> GetSnapPoints()
    {
        yield return new SnapPoint(Position, SnapKind.Endpoint);
        foreach (var (pin, w) in PinPositions()) yield return new SnapPoint(w, SnapKind.Pin, new PinRef(Id, pin.Id));
    }

    protected override void DeepCopyFrom(CadEntity source)
    {
        var s = (SymbolInstanceEntity)source;
        PinLabels = new Dictionary<string, string>(s.PinLabels);
        Values = new Dictionary<string, string>(s.Values);
        Display = new Dictionary<string, string>(s.Display);
        ContactMirror = new List<string>(s.ContactMirror);
    }

    // ---- presentation --------------------------------------------------------------------------

    /// <summary>Attribute texts in world coordinates (kept horizontal for readability).</summary>
    public IEnumerable<TextEntity> AttributeTexts()
    {
        if (Definition is null) yield break;
        var m = LocalToWorld;
        foreach (var a in Definition.Attributes)
        {
            if (!a.Visible) continue;
            var value = Display.TryGetValue(a.Name, out var dv) ? dv : Values.TryGetValue(a.Name, out var iv) ? iv : a.DefaultValue;
            if (string.IsNullOrEmpty(value)) continue;
            yield return new TextEntity(m.Transform(a.Position), value, a.Height)
            {
                Layer = a.Layer,
                HAlign = AdjustAlign(a.HAlign, m),
                VAlign = AdjustVAlign(a.VAlign, m),
            };
        }
    }

    private static TextVAlign AdjustVAlign(TextVAlign v, Matrix2 m)
    {
        // Upside-down placement (e.g. a supply symbol rotated by 180°): keep text on the outer side.
        var flip = m.TransformVector(new Point2(0, 1)).Y < -0.5;
        if (!flip) return v;
        return v switch { TextVAlign.Bottom or TextVAlign.Baseline => TextVAlign.Top, TextVAlign.Top => TextVAlign.Bottom, _ => v };
    }

    private TextHAlign AdjustAlign(TextHAlign h, Matrix2 m)
    {
        // The anchor side flips when the symbol is mirrored or turned by 180°.
        var flip = m.TransformVector(new Point2(1, 0)).X < -0.5;
        if (!flip) return h;
        return h switch { TextHAlign.Left => TextHAlign.Right, TextHAlign.Right => TextHAlign.Left, _ => h };
    }

    public IEnumerable<CadEntity> Explode()
    {
        if (Definition is null)
        {
            var r = Rect2.FromCenter(Position, 5, 5);
            yield return new PolylineEntity(r.Corners(), true) { Layer = StandardLayers.Symbols, Color = CadColor.Red };
            yield return new TextEntity(Position, "?" + SymbolId, 2) { Layer = StandardLayers.Symbols, Color = CadColor.Red, HAlign = TextHAlign.Center, VAlign = TextVAlign.Middle };
            yield break;
        }
        var m = LocalToWorld;
        foreach (var g in Definition.Graphics)
        {
            var c = g.Clone(keepId: true);
            c.Transform(m);
            if (c.Color is null && Color is not null) c.Color = Color;
            yield return c;
        }
        foreach (var (pin, w) in PinPositions())
        {
            if (!pin.ShowLabel) continue;
            var label = GetPinLabel(pin.Id);
            if (string.IsNullOrEmpty(label)) continue;
            var dirWorld = m.TransformVector(DirectionVector(pin.Direction));
            var vertical = Math.Abs(dirWorld.Y) > 0.5;
            var pos = vertical ? w + new Point2(0.8, -dirWorld.Y * 2.2) : w + new Point2(-dirWorld.X * 1.5, 0.8);
            yield return new TextEntity(pos, label, 1.8)
            {
                Layer = StandardLayers.Symbols,
                HAlign = vertical ? TextHAlign.Left : (dirWorld.X > 0 ? TextHAlign.Right : TextHAlign.Left),
                VAlign = TextVAlign.Middle,
            };
        }
        foreach (var t in AttributeTexts()) yield return t;
        if (ContactMirror.Count > 0)
        {
            // Contact mirror to the right of the main symbol, clear of the current path.
            var b = Definition.Bounds.Transform(m);
            var y = b.Center.Y + (ContactMirror.Count - 1) * 1.3;
            foreach (var line in ContactMirror)
            {
                yield return new TextEntity(new Point2(b.MaxX + 2.5, y), line, 1.8)
                {
                    Layer = StandardLayers.CrossReferences, HAlign = TextHAlign.Left, VAlign = TextVAlign.Middle,
                };
                y -= 2.6;
            }
        }
    }

    public static Point2 DirectionVector(PinDirection d) => d switch
    {
        PinDirection.Up => new Point2(0, 1),
        PinDirection.Down => new Point2(0, -1),
        PinDirection.Left => new Point2(-1, 0),
        _ => new Point2(1, 0),
    };

    public DxfBlockReference GetDxfBlock()
    {
        var local = Definition?.Graphics.Select(g => g.Clone(keepId: true)).ToList() ?? new List<CadEntity>();
        var attrs = new List<DxfAttribute>();
        foreach (var t in AttributeTexts())
            attrs.Add(new DxfAttribute(t.Value == Display.GetValueOrDefault(SymbolAttributes.Tag) ? SymbolAttributes.Tag : AttributeNameFor(t), t.Value, t.Position, t.Height, 0, true));
        foreach (var (pin, w) in PinPositions())
            if (pin.ShowLabel) attrs.Add(new DxfAttribute("PIN_" + pin.Id, GetPinLabel(pin.Id), w + new Point2(0.8, 0), 1.8, 0, true));
        if (ContactMirror.Count > 0) attrs.Add(new DxfAttribute(SymbolAttributes.CrossReference, string.Join(" | ", ContactMirror), GraphicBounds.Min, 1.8, 0, true));
        attrs.Add(new DxfAttribute("SYMBOL_ID", SymbolId, Position, 1, 0, false));
        if (DeviceId is { } did) attrs.Add(new DxfAttribute("DEVICE_ID", did.ToString(), Position, 1, 0, false));
        return new DxfBlockReference($"DND_{SymbolId}_V{Definition?.Version ?? 0}", local, Position, Rotation, Mirrored, Layer, attrs);
    }

    private string AttributeNameFor(TextEntity t)
    {
        foreach (var kv in Display) if (kv.Value == t.Value) return kv.Key;
        foreach (var kv in Values) if (kv.Value == t.Value) return kv.Key;
        return "TEXT";
    }
}
