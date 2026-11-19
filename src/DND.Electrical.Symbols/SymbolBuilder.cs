using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;

namespace DND.Electrical.Symbols;

/// <summary>Fluent helper used to author symbol artwork in code (millimetres, Y up, origin = insertion point).</summary>
public sealed class SymbolBuilder
{
    private readonly SymbolDefinition _s;
    private bool _tagPlaced;

    public SymbolBuilder(string id, string name, SymbolCategory category, string tagPrefix, SymbolRole role = SymbolRole.Standalone)
    {
        _s = new SymbolDefinition
        {
            Id = id, Name = name, Category = category, DefaultTagPrefix = tagPrefix, Role = role,
            Standard = "IEC 60617 style (original D&D artwork)",
        };
    }

    public SymbolBuilder Describe(string description, params string[] keywords)
    {
        _s.Description = description;
        _s.Keywords.AddRange(keywords);
        return this;
    }

    public SymbolBuilder Contact(ContactType t) { _s.ContactType = t; return this; }
    public SymbolBuilder Potential(string name) { _s.DefaultPotential = name; return this; }

    public SymbolBuilder Line(double x1, double y1, double x2, double y2, bool dashed = false)
    {
        _s.Graphics.Add(new LineEntity(new Point2(x1, y1), new Point2(x2, y2)) { Layer = StandardLayers.Symbols, LineType = dashed ? LineType.Dashed : LineType.ByLayer });
        return this;
    }

    public SymbolBuilder Poly(bool closed, params double[] xy) => Poly(closed, false, xy);

    public SymbolBuilder Poly(bool closed, bool filled, params double[] xy)
    {
        var pts = new List<Point2>();
        for (int i = 0; i + 1 < xy.Length; i += 2) pts.Add(new Point2(xy[i], xy[i + 1]));
        _s.Graphics.Add(new PolylineEntity(pts, closed) { Layer = StandardLayers.Symbols, Filled = filled });
        return this;
    }

    public SymbolBuilder Rect(double x1, double y1, double x2, double y2) =>
        Poly(true, x1, y1, x2, y1, x2, y2, x1, y2);

    public SymbolBuilder Circle(double cx, double cy, double r, bool filled = false)
    {
        _s.Graphics.Add(new CircleEntity(new Point2(cx, cy), r) { Layer = StandardLayers.Symbols, Filled = filled });
        return this;
    }

    public SymbolBuilder Arc(double cx, double cy, double r, double start, double sweep)
    {
        _s.Graphics.Add(new ArcEntity(new Point2(cx, cy), r, start, sweep) { Layer = StandardLayers.Symbols });
        return this;
    }

    public SymbolBuilder Text(double x, double y, string text, double height = 2.5, TextHAlign h = TextHAlign.Center, TextVAlign v = TextVAlign.Middle)
    {
        _s.Graphics.Add(new TextEntity(new Point2(x, y), text, height) { Layer = StandardLayers.Symbols, HAlign = h, VAlign = v });
        return this;
    }

    public SymbolBuilder Pin(string id, double x, double y, PinDirection dir, bool showLabel = true, string? label = null, string description = "")
    {
        _s.Pins.Add(new SymbolPin { Id = id, Label = label ?? id, Position = new Point2(x, y), Direction = dir, ShowLabel = showLabel, Description = description });
        return this;
    }

    public SymbolBuilder Internal(params string[] pins)
    {
        _s.InternalConnections.Add(pins.ToList());
        return this;
    }

    public SymbolBuilder Attr(string name, double x, double y, TextHAlign h = TextHAlign.Right, TextVAlign v = TextVAlign.Middle, bool visible = true, double height = 2.5, string def = "", string layer = StandardLayers.Tags)
    {
        _s.Attributes.Add(new SymbolAttributeDefinition { Name = name, Position = new Point2(x, y), HAlign = h, VAlign = v, Visible = visible, Height = height, DefaultValue = def, Layer = layer });
        if (name == SymbolAttributes.Tag) _tagPlaced = true;
        return this;
    }

    // ---- composite helpers -------------------------------------------------------------------

    /// <summary>Vertical normally-open contact between (x,10) and (x,-10).</summary>
    public SymbolBuilder NoContact(double x = 0) =>
        Line(x, 10, x, 4).Line(x, -10, x, -4).Line(x, -4, x - 4, 4.5);

    /// <summary>Vertical normally-closed contact between (x,10) and (x,-10).</summary>
    public SymbolBuilder NcContact(double x = 0) =>
        Line(x, 10, x, 4).Line(x, 4, x + 3, 4).Line(x, -10, x, -4).Line(x, -4, x + 3.5, 5);

    /// <summary>Mechanical linkage between poles (dashed).</summary>
    public SymbolBuilder Linkage(double fromX, double toX, double y = 0.5) => Line(fromX, y, toX, y, dashed: true);

    public SymbolDefinition Build()
    {
        if (!_tagPlaced && _s.Role is not (SymbolRole.PotentialSource or SymbolRole.Interruption))
        {
            var b = _s.Bounds;
            Attr(SymbolAttributes.Tag, b.MinX - 2.5, b.Center.Y + 2);
            Attr(SymbolAttributes.Function, b.MinX - 2.5, b.Center.Y - 2, height: 1.8, visible: true, layer: StandardLayers.Annotations);
        }
        var errors = _s.Validate();
        if (errors.Count > 0) throw new InvalidOperationException($"Built-in symbol {_s.Id} invalid: {string.Join("; ", errors)}");
        return _s;
    }
}
