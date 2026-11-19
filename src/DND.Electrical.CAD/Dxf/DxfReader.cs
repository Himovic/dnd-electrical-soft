using System.Globalization;
using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;

namespace DND.Electrical.CAD.Dxf;

public sealed class DxfImportResult
{
    public List<CadEntity> Entities { get; } = new();
    public List<CadLayer> Layers { get; } = new();
    public Dictionary<string, int> SkippedEntityTypes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Warnings { get; } = new();
    public double UnitScale { get; set; } = 1;
    public int BlockReferencesExpanded { get; set; }
}

/// <summary>
/// ASCII DXF reader supporting LINE, LWPOLYLINE (incl. bulges), POLYLINE/VERTEX, CIRCLE, ARC, TEXT,
/// MTEXT (plain text), SOLID, INSERT (blocks expanded with transform) and layers. Binary DXF and DWG
/// are not supported. Unknown entities are counted and reported, never silently lost.
/// </summary>
public sealed class DxfReader
{
    private readonly List<(int code, string value)> _pairs = new();
    private int _pos;
    private readonly Dictionary<string, List<CadEntity>> _blocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Point2> _blockBase = new(StringComparer.OrdinalIgnoreCase);
    private readonly DxfImportResult _result = new();

    public static DxfImportResult Read(string text) => new DxfReader().Parse(text);

    public static DxfImportResult ReadFile(string path) => Read(File.ReadAllText(path));

    private DxfImportResult Parse(string text)
    {
        if (text.StartsWith("AutoCAD Binary DXF", StringComparison.Ordinal))
            throw new NotSupportedException("Binary DXF files are not supported. Save the file as ASCII DXF.");
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i + 1 < lines.Length; i += 2)
        {
            if (!int.TryParse(lines[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
                throw new FormatException($"Invalid DXF group code at line {i + 1}.");
            _pairs.Add((code, lines[i + 1].TrimEnd()));
        }

        while (_pos < _pairs.Count)
        {
            if (Is(0, "SECTION"))
            {
                _pos++;
                var name = _pairs[_pos].value.Trim();
                _pos++;
                switch (name)
                {
                    case "HEADER": ReadHeader(); break;
                    case "TABLES": ReadTables(); break;
                    case "BLOCKS": ReadBlocks(); break;
                    case "ENTITIES": ReadEntitySection(_result.Entities, "ENDSEC"); break;
                    default: SkipTo(0, "ENDSEC"); break;
                }
            }
            _pos++;
        }

        if (Math.Abs(_result.UnitScale - 1) > 1e-12)
        {
            var m = Matrix2.Scaling(_result.UnitScale, _result.UnitScale);
            foreach (var e in _result.Entities) e.Transform(m);
        }
        return _result;
    }

    private bool Is(int code, string value) =>
        _pos < _pairs.Count && _pairs[_pos].code == code && string.Equals(_pairs[_pos].value.Trim(), value, StringComparison.OrdinalIgnoreCase);

    private void SkipTo(int code, string value)
    {
        while (_pos < _pairs.Count && !Is(code, value)) _pos++;
    }

    private void ReadHeader()
    {
        while (_pos < _pairs.Count && !Is(0, "ENDSEC"))
        {
            if (_pairs[_pos].code == 9 && _pairs[_pos].value.Trim() == "$INSUNITS" && _pos + 1 < _pairs.Count)
            {
                var u = (int)D(_pairs[_pos + 1].value);
                _result.UnitScale = u switch { 1 => 25.4, 2 => 304.8, 4 => 1, 5 => 10, 6 => 1000, _ => 1 };
            }
            _pos++;
        }
    }

    private void ReadTables()
    {
        while (_pos < _pairs.Count && !Is(0, "ENDSEC"))
        {
            if (Is(0, "LAYER"))
            {
                _pos++;
                var layer = new CadLayer();
                var aci = 7;
                int? trueColor = null;
                while (_pos < _pairs.Count && _pairs[_pos].code != 0)
                {
                    var (c, v) = _pairs[_pos];
                    if (c == 2) layer.Name = v.Trim();
                    else if (c == 62) aci = (int)D(v);
                    else if (c == 420) trueColor = (int)D(v);
                    else if (c == 70) layer.Locked = ((int)D(v) & 4) != 0;
                    _pos++;
                }
                layer.Visible = aci >= 0;
                layer.Color = trueColor is { } tc ? CadColor.FromTrueColor(tc) : CadColor.FromAci(Math.Abs(aci));
                if (_result.Layers.All(l => !string.Equals(l.Name, layer.Name, StringComparison.OrdinalIgnoreCase))) _result.Layers.Add(layer);
                continue;
            }
            _pos++;
        }
    }

    private void ReadBlocks()
    {
        while (_pos < _pairs.Count && !Is(0, "ENDSEC"))
        {
            if (Is(0, "BLOCK"))
            {
                _pos++;
                string name = string.Empty;
                double bx = 0, by = 0;
                while (_pos < _pairs.Count && _pairs[_pos].code != 0)
                {
                    var (c, v) = _pairs[_pos];
                    if (c == 2) name = v.Trim();
                    else if (c == 10) bx = D(v);
                    else if (c == 20) by = D(v);
                    _pos++;
                }
                var list = new List<CadEntity>();
                ReadEntitySection(list, "ENDBLK");
                _blocks[name] = list;
                _blockBase[name] = new Point2(bx, by);
            }
            _pos++;
        }
    }

    private void ReadEntitySection(List<CadEntity> target, string terminator)
    {
        while (_pos < _pairs.Count && !Is(0, terminator) && !Is(0, "ENDSEC"))
        {
            if (_pairs[_pos].code != 0) { _pos++; continue; }
            var type = _pairs[_pos].value.Trim().ToUpperInvariant();
            _pos++;
            var props = new List<(int code, string value)>();
            while (_pos < _pairs.Count && _pairs[_pos].code != 0)
            {
                props.Add(_pairs[_pos]);
                _pos++;
            }

            switch (type)
            {
                case "LINE":
                    target.Add(Common(new LineEntity(P(props, 10), P(props, 11)), props));
                    break;
                case "CIRCLE":
                    target.Add(Common(new CircleEntity(P(props, 10), G(props, 40)), props));
                    break;
                case "ARC":
                    {
                        var s = G(props, 50); var e = G(props, 51);
                        target.Add(Common(new ArcEntity(P(props, 10), G(props, 40), s, GeometryMath.NormalizeAngle(e - s) is var sw && sw < 1e-9 ? 360 : sw), props));
                        break;
                    }
                case "TEXT":
                    target.Add(Common(ReadText(props), props));
                    break;
                case "MTEXT":
                    {
                        var t = new TextEntity(P(props, 10), CleanMText(string.Concat(props.Where(p => p.code is 3).Select(p => p.value)) + S(props, 1)), G(props, 40, 2.5))
                        {
                            Rotation = G(props, 50),
                        };
                        var attach = (int)G(props, 71, 1);
                        t.HAlign = ((attach - 1) % 3) switch { 1 => TextHAlign.Center, 2 => TextHAlign.Right, _ => TextHAlign.Left };
                        t.VAlign = ((attach - 1) / 3) switch { 0 => TextVAlign.Top, 1 => TextVAlign.Middle, _ => TextVAlign.Bottom };
                        target.Add(Common(t, props));
                        break;
                    }
                case "LWPOLYLINE":
                    target.Add(Common(ReadLwPolyline(props), props));
                    break;
                case "POLYLINE":
                    {
                        var closed = ((int)G(props, 70) & 1) != 0;
                        var pts = new List<(Point2 p, double bulge)>();
                        while (_pos < _pairs.Count && Is(0, "VERTEX"))
                        {
                            _pos++;
                            var vp = new List<(int, string)>();
                            while (_pos < _pairs.Count && _pairs[_pos].code != 0) { vp.Add(_pairs[_pos]); _pos++; }
                            pts.Add((P(vp, 10), G(vp, 42)));
                        }
                        if (Is(0, "SEQEND")) { _pos++; while (_pos < _pairs.Count && _pairs[_pos].code != 0) _pos++; }
                        target.Add(Common(new PolylineEntity(ExpandBulges(pts, closed), closed), props));
                        break;
                    }
                case "SOLID":
                    {
                        var p0 = P(props, 10); var p1 = P(props, 11); var p2 = P(props, 12); var p3 = P(props, 13);
                        var pts = p2.AlmostEquals(p3) ? new[] { p0, p1, p2 } : new[] { p0, p1, p3, p2 };
                        target.Add(Common(new PolylineEntity(pts, true) { Filled = true }, props));
                        break;
                    }
                case "INSERT":
                    ExpandInsert(props, target);
                    // Skip attached ATTRIB/SEQEND but import visible attribute values as text.
                    while (_pos < _pairs.Count && (Is(0, "ATTRIB") || Is(0, "SEQEND")))
                    {
                        var isAttrib = Is(0, "ATTRIB");
                        _pos++;
                        var ap = new List<(int, string)>();
                        while (_pos < _pairs.Count && _pairs[_pos].code != 0) { ap.Add(_pairs[_pos]); _pos++; }
                        if (isAttrib && ((int)G(ap, 70) & 1) == 0 && !string.IsNullOrEmpty(S(ap, 1)))
                            target.Add(Common(ReadText(ap), ap));
                    }
                    break;
                case "ATTDEF":
                case "POINT":
                case "VIEWPORT":
                    break;
                default:
                    _result.SkippedEntityTypes[type] = _result.SkippedEntityTypes.GetValueOrDefault(type) + 1;
                    break;
            }
        }
    }

    private void ExpandInsert(List<(int code, string value)> props, List<CadEntity> target)
    {
        var name = S(props, 2);
        if (!_blocks.TryGetValue(name, out var block))
        {
            _result.Warnings.Add($"Block '{name}' referenced by INSERT is not defined.");
            return;
        }
        var basePt = _blockBase.GetValueOrDefault(name);
        var sx = G(props, 41, 1); var sy = G(props, 42, 1);
        var m = Matrix2.Translation(-basePt) * Matrix2.Scaling(sx, sy) * Matrix2.Rotation(G(props, 50)) * Matrix2.Translation(P(props, 10));
        var layer = S(props, 8);
        foreach (var e in block)
        {
            var c = e.Clone();
            c.Transform(m);
            if (c.Layer == "0" && !string.IsNullOrEmpty(layer)) c.Layer = layer;
            target.Add(c);
        }
        _result.BlockReferencesExpanded++;
    }

    private static TextEntity ReadText(List<(int code, string value)> props)
    {
        var h = (int)G(props, 72);
        var v = (int)G(props, 73);
        var hasAlign = h != 0 || v != 0;
        var t = new TextEntity(hasAlign && props.Any(p => p.code == 11) ? P(props, 11) : P(props, 10), S(props, 1), G(props, 40, 2.5))
        {
            Rotation = G(props, 50),
            HAlign = h switch { 1 or 4 => TextHAlign.Center, 2 => TextHAlign.Right, _ => TextHAlign.Left },
            VAlign = v switch { 1 => TextVAlign.Bottom, 2 => TextVAlign.Middle, 3 => TextVAlign.Top, _ => TextVAlign.Baseline },
        };
        return t;
    }

    private static PolylineEntity ReadLwPolyline(List<(int code, string value)> props)
    {
        var closed = ((int)G(props, 70) & 1) != 0;
        var pts = new List<(Point2 p, double bulge)>();
        double? x = null;
        foreach (var (c, v) in props)
        {
            if (c == 10) x = D(v);
            else if (c == 20 && x is { } xv) { pts.Add((new Point2(xv, D(v)), 0)); x = null; }
            else if (c == 42 && pts.Count > 0) pts[^1] = (pts[^1].p, D(v));
        }
        return new PolylineEntity(ExpandBulges(pts, closed), closed);
    }

    /// <summary>Approximates bulged (arc) segments with line segments (max 10° per step).</summary>
    private static List<Point2> ExpandBulges(List<(Point2 p, double bulge)> pts, bool closed)
    {
        var result = new List<Point2>();
        for (int i = 0; i < pts.Count; i++)
        {
            var (p, bulge) = pts[i];
            result.Add(p);
            var hasNext = i + 1 < pts.Count || closed;
            if (!hasNext || Math.Abs(bulge) < 1e-9) continue;
            var q = pts[(i + 1) % pts.Count].p;
            var theta = 4 * Math.Atan(bulge);
            var chord = q - p;
            var c = chord.Length;
            if (c < 1e-12) continue;
            var r = c / (2 * Math.Sin(Math.Abs(theta) / 2));
            var mid = Point2.Lerp(p, q, 0.5);
            var nrm = new Point2(-chord.Y, chord.X) / c;
            var h = r * Math.Cos(theta / 2) * Math.Sign(bulge);
            var center = mid + nrm * h;
            var a0 = (p - center).AngleDegrees;
            var sweep = GeometryMath.RadToDeg(theta);
            var steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) / 10));
            for (int s = 1; s < steps; s++) result.Add(center + Point2.FromPolar(r, a0 + sweep * s / steps));
        }
        return result;
    }

    private static T Common<T>(T e, List<(int code, string value)> props) where T : CadEntity
    {
        var layer = S(props, 8);
        if (!string.IsNullOrEmpty(layer)) e.Layer = layer;
        var tc = props.FirstOrDefault(p => p.code == 420);
        if (tc.value is not null) e.Color = CadColor.FromTrueColor((int)D(tc.value));
        else
        {
            var aci = (int)G(props, 62, 256);
            if (aci is > 0 and < 256) e.Color = CadColor.FromAci(aci);
        }
        var lt = S(props, 6);
        if (lt.Contains("DASH", StringComparison.OrdinalIgnoreCase) || lt.Contains("HIDDEN", StringComparison.OrdinalIgnoreCase)) e.LineType = LineType.Dashed;
        return e;
    }

    private static string CleanMText(string s)
    {
        s = s.Replace("\\P", "\n");
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '{' || s[i] == '}') continue;
            if (s[i] == '\\' && i + 1 < s.Length && char.IsLetter(s[i + 1]))
            {
                var semi = s.IndexOf(';', i);
                if (semi > 0 && "fFcCHhWwTtQqAa".Contains(s[i + 1])) { i = semi; continue; }
                i++;
                continue;
            }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private static double D(string s) => double.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
    private static string S(List<(int code, string value)> p, int code) => p.FirstOrDefault(x => x.code == code).value?.Trim() ?? string.Empty;
    private static double G(List<(int code, string value)> p, int code, double def = 0)
    {
        foreach (var (c, v) in p) if (c == code) return D(v);
        return def;
    }
    private static Point2 P(List<(int code, string value)> p, int code) => new(G(p, code), G(p, code + 10));
}
