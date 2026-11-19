using System.Globalization;
using System.Text;
using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;

namespace DND.Electrical.CAD.Dxf;

/// <summary>
/// Writes ASCII DXF (AutoCAD R12 / AC1009 dialect, the most widely supported interchange subset).
/// Units are millimetres. Symbols implementing <see cref="IDxfBlockSource"/> become BLOCK/INSERT with
/// ATTRIB entities so that tags and other engineering attributes remain machine-readable.
/// </summary>
public sealed class DxfWriter
{
    private readonly StringBuilder _sb = new();
    private readonly Func<string, string> _resolveText;

    public DxfWriter(Func<string, string>? textResolver = null) => _resolveText = textResolver ?? (s => s);

    public static string Write(CadDrawing drawing, Func<string, string>? textResolver = null, bool includeNonPrintable = false)
        => new DxfWriter(textResolver).WriteDrawing(drawing, includeNonPrintable);

    public string WriteDrawing(CadDrawing drawing, bool includeNonPrintable = false)
    {
        _sb.Clear();
        var printable = drawing.Layers.Where(l => includeNonPrintable || l.Printable).Select(l => l.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entities = drawing.Entities.Where(e => printable.Contains(e.Layer) || drawing.FindLayer(e.Layer) is null).ToList();

        var blocks = new Dictionary<string, DxfBlockReference>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entities.OfType<IDxfBlockSource>())
        {
            var b = e.GetDxfBlock();
            blocks.TryAdd(SafeName(b.BlockName), b);
        }

        // HEADER
        Pair(0, "SECTION"); Pair(2, "HEADER");
        Pair(9, "$ACADVER"); Pair(1, "AC1009");
        Pair(9, "$INSUNITS"); Pair(70, 4);
        Pair(9, "$MEASUREMENT"); Pair(70, 1);
        var ext = drawing.GetExtents();
        if (!ext.IsEmpty)
        {
            Pair(9, "$EXTMIN"); Pair(10, ext.MinX); Pair(20, ext.MinY); Pair(30, 0.0);
            Pair(9, "$EXTMAX"); Pair(10, ext.MaxX); Pair(20, ext.MaxY); Pair(30, 0.0);
        }
        Pair(0, "ENDSEC");

        // TABLES
        Pair(0, "SECTION"); Pair(2, "TABLES");
        Pair(0, "TABLE"); Pair(2, "LTYPE"); Pair(70, 2);
        LineTypeDef("CONTINUOUS", "Solid line", Array.Empty<double>());
        LineTypeDef("DASHED", "Dashed __ __ __", new[] { 5.0, -2.5 });
        Pair(0, "ENDTAB");
        Pair(0, "TABLE"); Pair(2, "LAYER"); Pair(70, drawing.Layers.Count);
        foreach (var l in drawing.Layers)
        {
            Pair(0, "LAYER"); Pair(2, SafeName(l.Name)); Pair(70, l.Locked ? 4 : 0);
            Pair(62, l.Visible ? l.Color.ToAci() : -l.Color.ToAci());
            Pair(6, l.LineType is LineType.Dashed or LineType.Hidden or LineType.DashDot ? "DASHED" : "CONTINUOUS");
        }
        Pair(0, "ENDTAB");
        Pair(0, "TABLE"); Pair(2, "STYLE"); Pair(70, 1);
        Pair(0, "STYLE"); Pair(2, "STANDARD"); Pair(70, 0); Pair(40, 0.0); Pair(41, 1.0); Pair(50, 0.0); Pair(71, 0); Pair(42, 2.5); Pair(3, "txt"); Pair(4, "");
        Pair(0, "ENDTAB");
        Pair(0, "ENDSEC");

        // BLOCKS
        Pair(0, "SECTION"); Pair(2, "BLOCKS");
        foreach (var (name, b) in blocks)
        {
            Pair(0, "BLOCK"); Pair(8, "0"); Pair(2, name); Pair(70, b.Attributes.Count > 0 ? 2 : 0);
            Pair(10, 0.0); Pair(20, 0.0); Pair(30, 0.0); Pair(3, name);
            foreach (var e in b.LocalEntities) WriteEntity(e, "0", drawing);
            foreach (var a in b.Attributes)
            {
                Pair(0, "ATTDEF"); Pair(8, "0");
                Pair(10, 0.0); Pair(20, 0.0); Pair(30, 0.0);
                Pair(40, a.Height); Pair(1, ""); Pair(3, a.Tag); Pair(2, SafeTag(a.Tag)); Pair(70, a.Visible ? 0 : 1);
            }
            Pair(0, "ENDBLK"); Pair(8, "0");
        }
        Pair(0, "ENDSEC");

        // ENTITIES
        Pair(0, "SECTION"); Pair(2, "ENTITIES");
        foreach (var e in entities) WriteEntity(e, null, drawing);
        Pair(0, "ENDSEC");
        Pair(0, "EOF");
        return _sb.ToString();
    }

    private void WriteEntity(CadEntity e, string? layerOverride, CadDrawing drawing)
    {
        var layer = SafeName(layerOverride ?? e.Layer);
        switch (e)
        {
            case IDxfBlockSource bs when layerOverride is null:
                WriteInsert(bs.GetDxfBlock(), e, drawing);
                return;
            case IExplodableEntity ex:
                foreach (var child in ex.Explode()) WriteEntity(child, layerOverride ?? e.Layer, drawing);
                return;
            case LineEntity l:
                Begin("LINE", layer, e);
                Pair(10, l.Start.X); Pair(20, l.Start.Y); Pair(30, 0.0);
                Pair(11, l.End.X); Pair(21, l.End.Y); Pair(31, 0.0);
                return;
            case PolylineEntity p when p.Points.Count >= 2:
                if (p.Filled && p.Closed && p.Points.Count is 3 or 4)
                {
                    Begin("SOLID", layer, e);
                    var pts = p.Points.Count == 3 ? new[] { p.Points[0], p.Points[1], p.Points[2], p.Points[2] }
                        : new[] { p.Points[0], p.Points[1], p.Points[3], p.Points[2] }; // DXF SOLID vertex order
                    for (int i = 0; i < 4; i++) { Pair(10 + i, pts[i].X); Pair(20 + i, pts[i].Y); Pair(30 + i, 0.0); }
                    return;
                }
                Begin("POLYLINE", layer, e);
                Pair(66, 1); Pair(10, 0.0); Pair(20, 0.0); Pair(30, 0.0); Pair(70, p.Closed ? 1 : 0);
                foreach (var v in p.Points)
                {
                    Pair(0, "VERTEX"); Pair(8, layer); Pair(10, v.X); Pair(20, v.Y); Pair(30, 0.0);
                }
                Pair(0, "SEQEND"); Pair(8, layer);
                return;
            case CircleEntity c:
                Begin("CIRCLE", layer, e);
                Pair(10, c.Center.X); Pair(20, c.Center.Y); Pair(30, 0.0); Pair(40, c.Radius);
                return;
            case ArcEntity a:
                if (a.SweepAngle >= 360 - 1e-9)
                {
                    Begin("CIRCLE", layer, e);
                    Pair(10, a.Center.X); Pair(20, a.Center.Y); Pair(30, 0.0); Pair(40, a.Radius);
                    return;
                }
                Begin("ARC", layer, e);
                Pair(10, a.Center.X); Pair(20, a.Center.Y); Pair(30, 0.0); Pair(40, a.Radius);
                Pair(50, GeometryMath.NormalizeAngle(a.StartAngle)); Pair(51, GeometryMath.NormalizeAngle(a.EndAngle));
                return;
            case TextEntity t:
                WriteText(layer, e, t.Position, _resolveText(t.Value), t.Height, t.Rotation, t.HAlign, t.VAlign);
                return;
            case DimensionEntity d:
                foreach (var child in ExplodeDimension(d)) WriteEntity(child, layerOverride ?? e.Layer, drawing);
                return;
            case LeaderEntity ld:
                for (int i = 0; i + 1 < ld.Points.Count; i++) WriteEntity(new LineEntity(ld.Points[i], ld.Points[i + 1]) { Color = e.Color }, layerOverride ?? e.Layer, drawing);
                if (ld.Points.Count > 0)
                {
                    foreach (var arrow in ArrowHead(ld.Points.Count > 1 ? ld.Points[1] : ld.Points[0] + new Point2(1, 0), ld.Points[0], ld.TextHeight * 0.8))
                        WriteEntity(arrow, layerOverride ?? e.Layer, drawing);
                    WriteText(layer, e, ld.Points[^1] + new Point2(ld.TextHeight * 0.3, ld.TextHeight * 0.3), ld.Text, ld.TextHeight, 0, TextHAlign.Left, TextVAlign.Baseline);
                }
                return;
            case GroupEntity g:
                foreach (var c in g.Children) WriteEntity(c, layerOverride, drawing);
                return;
            case ImageEntity img:
                // R12 has no raster images: export the frame so the layout is preserved.
                WriteEntity(new PolylineEntity(img.Rect.Corners(), true) { Color = e.Color }, layerOverride ?? e.Layer, drawing);
                return;
        }
    }

    private void WriteInsert(DxfBlockReference b, CadEntity e, CadDrawing drawing)
    {
        var layer = SafeName(b.Layer);
        Begin("INSERT", layer, e);
        if (b.Attributes.Count > 0) Pair(66, 1);
        Pair(2, SafeName(b.BlockName));
        Pair(10, b.Position.X); Pair(20, b.Position.Y); Pair(30, 0.0);
        Pair(41, b.MirrorX ? -1.0 : 1.0); Pair(42, 1.0); Pair(43, 1.0);
        Pair(50, GeometryMath.NormalizeAngle(b.Rotation));
        if (b.Attributes.Count == 0) return;
        foreach (var a in b.Attributes)
        {
            Pair(0, "ATTRIB"); Pair(8, layer);
            Pair(10, a.Position.X); Pair(20, a.Position.Y); Pair(30, 0.0);
            Pair(40, a.Height); Pair(1, Clean(a.Value)); Pair(2, SafeTag(a.Tag)); Pair(70, a.Visible ? 0 : 1);
            if (Math.Abs(a.Rotation) > 1e-9) Pair(50, a.Rotation);
        }
        Pair(0, "SEQEND"); Pair(8, layer);
    }

    private void WriteText(string layer, CadEntity e, Point2 pos, string value, double height, double rotation, TextHAlign h, TextVAlign v)
    {
        var lines = value.Replace("\r", "").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var offset = Point2.FromPolar(height * 1.4 * i, rotation - 90);
            var p = pos + offset;
            Begin("TEXT", layer, e);
            Pair(10, p.X); Pair(20, p.Y); Pair(30, 0.0);
            Pair(40, height); Pair(1, Clean(lines[i]));
            if (Math.Abs(rotation) > 1e-9) Pair(50, rotation);
            var h72 = h switch { TextHAlign.Center => 1, TextHAlign.Right => 2, _ => 0 };
            var v73 = v switch { TextVAlign.Bottom => 1, TextVAlign.Middle => 2, TextVAlign.Top => 3, _ => 0 };
            if (h72 != 0 || v73 != 0)
            {
                Pair(72, h72);
                Pair(11, p.X); Pair(21, p.Y); Pair(31, 0.0);
                Pair(73, v73);
            }
        }
    }

    public static IEnumerable<CadEntity> ExplodeDimension(DimensionEntity d)
    {
        var ds = d.DimStart; var de = d.DimEnd;
        var n = d.Normal * Math.Sign(d.Offset == 0 ? 1 : d.Offset);
        yield return new LineEntity(d.Start + n * 1, ds + n * 1.5) { Color = d.Color };
        yield return new LineEntity(d.End + n * 1, de + n * 1.5) { Color = d.Color };
        yield return new LineEntity(ds, de) { Color = d.Color };
        foreach (var a in ArrowHead(de, ds, d.TextHeight * 0.8)) yield return a;
        foreach (var a in ArrowHead(ds, de, d.TextHeight * 0.8)) yield return a;
        var mid = Point2.Lerp(ds, de, 0.5);
        var ang = (de - ds).AngleDegrees;
        if (ang > 90 || ang <= -90) ang += 180;
        var up = Point2.FromPolar(d.TextHeight * 0.4, ang + 90);
        yield return new TextEntity(mid + up, d.DisplayText, d.TextHeight)
        {
            Rotation = GeometryMath.NormalizeAngle(ang), HAlign = TextHAlign.Center, VAlign = TextVAlign.Bottom, Color = d.Color,
        };
    }

    /// <summary>Filled arrow head with tip at <paramref name="tip"/>, pointing from <paramref name="from"/>.</summary>
    public static IEnumerable<CadEntity> ArrowHead(Point2 from, Point2 tip, double size)
    {
        var dir = (tip - from).Normalized();
        if (dir.Length < 1e-9) yield break;
        var nrm = new Point2(-dir.Y, dir.X);
        var b = tip - dir * size;
        yield return new PolylineEntity(new[] { tip, b + nrm * size * 0.3, b - nrm * size * 0.3 }, true) { Filled = true };
    }

    private void Begin(string type, string layer, CadEntity e)
    {
        Pair(0, type);
        Pair(8, layer);
        if (e.Color is { } c) Pair(62, c.ToAci());
        if (e.LineType is LineType.Dashed or LineType.Hidden or LineType.DashDot) Pair(6, "DASHED");
    }

    private void LineTypeDef(string name, string desc, double[] pattern)
    {
        Pair(0, "LTYPE"); Pair(2, name); Pair(70, 0); Pair(3, desc); Pair(72, 65);
        Pair(73, pattern.Length); Pair(40, pattern.Sum(Math.Abs));
        foreach (var p in pattern) Pair(49, p);
    }

    private void Pair(int code, string value) => _sb.Append(code.ToString(CultureInfo.InvariantCulture)).Append('\n').Append(value).Append('\n');
    private void Pair(int code, int value) => Pair(code, value.ToString(CultureInfo.InvariantCulture));
    private void Pair(int code, double value) => Pair(code, Math.Round(value, 6).ToString("0.0#####", CultureInfo.InvariantCulture));

    private static string Clean(string s) => s.Replace("\n", " ").Replace("\r", "");

    /// <summary>DXF R12 table names: letters, digits, $, -, _ only.</summary>
    public static string SafeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name.ToUpperInvariant()) sb.Append(char.IsAsciiLetterOrDigit(ch) || ch is '$' or '-' or '_' ? ch : '_');
        return sb.Length == 0 ? "0" : sb.ToString();
    }

    private static string SafeTag(string tag) => SafeName(tag).Replace('-', '_');
}
