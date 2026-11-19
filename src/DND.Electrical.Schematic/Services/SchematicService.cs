using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;
using DND.Electrical.Domain;
using DND.Electrical.Schematic.Entities;
using DND.Electrical.Symbols;

namespace DND.Electrical.Schematic.Services;

/// <summary>
/// Programmatic schematic construction: sheets, symbol placement, wiring and junctions.
/// Used by templates, demo projects, importers and the interactive editor.
/// </summary>
public sealed class SchematicService(SymbolResolver resolver, SchematicBinder binder)
{
    public SymbolResolver Resolver { get; } = resolver;
    public SchematicBinder Binder { get; } = binder;

    public Sheet AddSheet(ElectricalProject project, string title, SheetKind kind = SheetKind.Schematic, SheetFormat? format = null, string description = "")
    {
        var sheet = new Sheet
        {
            Number = project.NextSheetNumber(),
            Title = title,
            Description = description,
            Kind = kind,
            Format = format ?? SheetFormat.Parse(project.Settings.DefaultSheetFormat),
            Revision = project.Info.CurrentRevision,
        };
        SheetTemplate.Apply(sheet, project.Settings);
        project.Sheets.Add(sheet);
        return sheet;
    }

    public SymbolInstanceEntity PlaceSymbol(ElectricalProject project, Sheet sheet, string symbolId, Point2 position,
        string? tag = null, double rotation = 0, bool mirrored = false, IDictionary<string, string>? values = null, string? function = null)
    {
        var def = Resolver.Resolve(project, symbolId) ?? throw new KeyNotFoundException($"Symbol '{symbolId}' not found.");
        var inst = CreateInstance(def, position, rotation, mirrored);
        if (values is not null) foreach (var kv in values) inst.Values[kv.Key] = kv.Value;
        if (NeedsDevice(def))
        {
            var device = DeviceTaggingService.GetOrCreateDevice(project, def, tag);
            if (!string.IsNullOrEmpty(function) && string.IsNullOrEmpty(device.Function)) device.Function = function;
            inst.DeviceId = device.Id;
        }
        sheet.Drawing.Add(inst);
        return inst;
    }

    public static bool NeedsDevice(SymbolDefinition def) => def.Role is not (SymbolRole.PotentialSource or SymbolRole.Interruption);

    public static SymbolInstanceEntity CreateInstance(SymbolDefinition def, Point2 position, double rotation = 0, bool mirrored = false) => new()
    {
        SymbolId = def.Id,
        Definition = def,
        Position = position,
        Rotation = rotation,
        Mirrored = mirrored,
        Layer = StandardLayers.Symbols,
    };

    /// <summary>Wires two pins with an orthogonal route.</summary>
    public WireEntity Connect(Sheet sheet, SymbolInstanceEntity a, string pinA, SymbolInstanceEntity b, string pinB, IReadOnlyList<Point2>? via = null)
    {
        var pa = a.GetPinPosition(pinA) ?? throw new ArgumentException($"Pin '{pinA}' not found on {a.SymbolId}.");
        var pb = b.GetPinPosition(pinB) ?? throw new ArgumentException($"Pin '{pinB}' not found on {b.SymbolId}.");
        var da = a.Definition!.FindPin(pinA)!.Direction;
        var db = b.Definition!.FindPin(pinB)!.Direction;
        var pts = via is { Count: > 0 } ? new[] { pa }.Concat(via).Append(pb).ToList() : Route(pa, WorldDir(a, da), pb, WorldDir(b, db));
        var w = new WireEntity { Points = Simplify(pts), Start = WireEnd.Pin(a.Id, pinA), End = WireEnd.Pin(b.Id, pinB) };
        sheet.Drawing.Add(w);
        return w;
    }

    public WireEntity ConnectToJunction(Sheet sheet, SymbolInstanceEntity a, string pinA, JunctionEntity j)
    {
        var pa = a.GetPinPosition(pinA) ?? throw new ArgumentException($"Pin '{pinA}' not found.");
        var w = new WireEntity
        {
            Points = Simplify(Route(pa, WorldDir(a, a.Definition!.FindPin(pinA)!.Direction), j.Position, null)),
            Start = WireEnd.Pin(a.Id, pinA),
            End = WireEnd.Junction(j.Id),
        };
        sheet.Drawing.Add(w);
        return w;
    }

    public WireEntity ConnectJunctions(Sheet sheet, JunctionEntity a, JunctionEntity b)
    {
        var w = new WireEntity { Points = Simplify(Route(a.Position, null, b.Position, null)), Start = WireEnd.Junction(a.Id), End = WireEnd.Junction(b.Id) };
        sheet.Drawing.Add(w);
        return w;
    }

    public JunctionEntity AddJunction(Sheet sheet, Point2 position)
    {
        var j = new JunctionEntity(position);
        sheet.Drawing.Add(j);
        return j;
    }

    /// <summary>Splits <paramref name="wire"/> at <paramref name="at"/> and inserts a junction (T-connection).</summary>
    public static (WireEntity first, WireEntity second, JunctionEntity junction) SplitWire(CadDrawing drawing, WireEntity wire, Point2 at)
    {
        var (first, second, junction) = PrepareSplit(wire, at);
        drawing.Replace(first);
        drawing.Add(junction);
        drawing.Add(second);
        return (first, second, junction);
    }

    /// <summary>Computes a split without modifying the drawing (used by undoable editor actions).</summary>
    public static (WireEntity first, WireEntity second, JunctionEntity junction) PrepareSplit(WireEntity wire, Point2 at)
    {
        var seg = wire.FindSegment(at, 0.01);
        if (seg < 0) throw new ArgumentException("Point is not on the wire.");
        var p = GeometryMath.ClosestPointOnSegment(at, wire.Points[seg], wire.Points[seg + 1]);
        var junction = new JunctionEntity(p);
        var first = (WireEntity)wire.Clone(keepId: true);
        first.Points = Simplify(wire.Points.Take(seg + 1).Append(p).ToList());
        first.End = WireEnd.Junction(junction.Id);
        var second = (WireEntity)wire.Clone(keepId: false);
        second.Points = Simplify(new[] { p }.Concat(wire.Points.Skip(seg + 1)).ToList());
        second.Start = WireEnd.Junction(junction.Id);
        second.End = wire.End.Clone();
        second.CableId = null;
        second.CableCoreIndex = null;
        return (first, second, junction);
    }

    public static Point2 WorldDir(SymbolInstanceEntity inst, PinDirection d) =>
        inst.LocalToWorld.TransformVector(SymbolInstanceEntity.DirectionVector(d));

    /// <summary>Orthogonal route from a to b honouring the exit directions of pins when known.</summary>
    public static List<Point2> Route(Point2 a, Point2? dirA, Point2 b, Point2? dirB)
    {
        if (Math.Abs(a.X - b.X) < 1e-9 || Math.Abs(a.Y - b.Y) < 1e-9) return new() { a, b };
        var aVertical = dirA is { } da ? Math.Abs(da.Y) > 0.5 : Math.Abs(b.Y - a.Y) >= Math.Abs(b.X - a.X);
        var bVertical = dirB is { } db ? Math.Abs(db.Y) > 0.5 : aVertical;
        if (aVertical && bVertical)
        {
            var midY = Math.Round((a.Y + b.Y) / 2 / 2.5) * 2.5;
            return new() { a, new(a.X, midY), new(b.X, midY), b };
        }
        if (!aVertical && !bVertical)
        {
            var midX = Math.Round((a.X + b.X) / 2 / 2.5) * 2.5;
            return new() { a, new(midX, a.Y), new(midX, b.Y), b };
        }
        return aVertical ? new() { a, new(a.X, b.Y), b } : new() { a, new(b.X, a.Y), b };
    }

    /// <summary>Removes duplicate and collinear intermediate points.</summary>
    public static List<Point2> Simplify(IReadOnlyList<Point2> pts)
    {
        var r = new List<Point2>();
        foreach (var p in pts)
        {
            if (r.Count > 0 && r[^1].AlmostEquals(p, 1e-6)) continue;
            if (r.Count >= 2)
            {
                var a = r[^2]; var b = r[^1];
                if (Math.Abs((b - a).Cross(p - b)) < 1e-6 && (b - a).Dot(p - b) >= 0) { r[^1] = p; continue; }
            }
            r.Add(p);
        }
        if (r.Count == 1) r.Add(r[0]);
        return r;
    }
}
