using DND.Electrical.CAD.Dxf;
using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;
using SkiaSharp;

namespace DND.Electrical.Rendering;

/// <summary>
/// Renders a <see cref="CadDrawing"/> onto any SkiaSharp canvas (screen, bitmap, PDF, SVG).
/// The canvas is expected to be in device units; the renderer applies the world→device matrix.
/// Module-defined entities are drawn through <see cref="IExplodableEntity"/>.
/// </summary>
public sealed class SkiaDrawingRenderer
{
    private readonly Dictionary<Guid, (CadEntity source, long revision, List<CadEntity> parts)> _explodeCache = new();

    /// <param name="worldToDevice">Transform from drawing millimetres to canvas device units.</param>
    /// <param name="visibleWorld">Visible world area for culling (Empty = draw everything).</param>
    public void Render(SKCanvas canvas, CadDrawing drawing, Matrix2 worldToDevice, Rect2 visibleWorld, RenderOptions options)
    {
        var theme = options.Theme;
        var deviceScale = Math.Abs(worldToDevice.ScaleFactor);
        var ctx = new Ctx(canvas, drawing, options, deviceScale);

        canvas.Save();
        canvas.SetMatrix(ToSk(worldToDevice));

        if (options.ShowGrid && !options.PrintMode) DrawGrid(ctx, visibleWorld);

        IEnumerable<CadEntity> entities = visibleWorld.IsEmpty
            ? drawing.Entities
            : drawing.Query(visibleWorld.Inflate(5)).OrderBy(drawing.IndexOf);

        foreach (var e in entities)
        {
            var layer = drawing.FindLayer(e.Layer);
            if (layer is { Visible: false }) continue;
            if (options.PrintMode && layer is { Printable: false }) continue;
            DrawEntity(ctx, e, ResolveColor(ctx, e, layer), null, drawing.Revision);
        }

        // Selection & highlight overlays
        if (options.Selected is { Count: > 0 } sel)
            foreach (var id in sel)
                if (drawing.Find(id) is { } e) DrawOverlay(ctx, e, theme.Selection, grips: true);
        if (options.Highlighted is { Count: > 0 } hl)
            foreach (var id in hl)
                if (drawing.Find(id) is { } e) DrawOverlay(ctx, e, theme.Highlight, grips: false);

        if (options.Preview is { Count: > 0 } preview)
            foreach (var e in preview) DrawEntity(ctx, e, theme.Preview, 1.5f, -1);

        if (options.SelectionWindow is { } win) DrawSelectionWindow(ctx, win.rect, win.crossing);
        if (options.SnapMarker is { } snap) DrawSnapMarker(ctx, snap.point, snap.kind);

        canvas.Restore();
    }

    public void ClearCache() => _explodeCache.Clear();

    private sealed record Ctx(SKCanvas Canvas, CadDrawing Drawing, RenderOptions Options, double DeviceScale)
    {
        public float Px(double pixels) => (float)(pixels / DeviceScale);
    }

    private static SKMatrix ToSk(Matrix2 m) => new(
        (float)m.M11, (float)m.M21, (float)m.OffsetX,
        (float)m.M12, (float)m.M22, (float)m.OffsetY,
        0, 0, 1);

    private static SKColor ResolveColor(Ctx ctx, CadEntity e, CadLayer? layer)
    {
        if (ctx.Options.Monochrome) return SKColors.Black;
        var c = e.Color ?? layer?.Color ?? CadColor.Black;
        var sk = new SKColor(c.R, c.G, c.B);
        if (ctx.Options.Theme.InvertBlack && c.R < 40 && c.G < 40 && c.B < 40) return new SKColor(226, 232, 240);
        return sk;
    }

    private float StrokeWidth(Ctx ctx, CadEntity e, float multiplier = 1f)
    {
        var lw = e.LineWeight ?? ctx.Drawing.FindLayer(e.Layer)?.LineWeight ?? 0.25;
        var min = ctx.Px(ctx.Options.MinStrokePixels);
        return Math.Max((float)lw * multiplier, min);
    }

    private SKPaint StrokePaint(Ctx ctx, CadEntity e, SKColor color, float? widthOverride)
    {
        var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeWidth = widthOverride is { } w ? Math.Max(ctx.Px(w), StrokeWidth(ctx, e)) : StrokeWidth(ctx, e),
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        var lt = e.LineType == LineType.ByLayer ? ctx.Drawing.FindLayer(e.Layer)?.LineType ?? LineType.Continuous : e.LineType;
        var dash = lt switch
        {
            LineType.Dashed => new[] { 3f, 1.5f },
            LineType.Hidden => new[] { 1.5f, 1f },
            LineType.DashDot => new[] { 4f, 1f, 0.5f, 1f },
            LineType.Dotted => new[] { 0.3f, 1f },
            _ => null,
        };
        if (dash is not null) paint.PathEffect = SKPathEffect.CreateDash(dash, 0);
        return paint;
    }

    private void DrawEntity(Ctx ctx, CadEntity e, SKColor color, float? widthPx, long revision)
    {
        var c = ctx.Canvas;
        switch (e)
        {
            case LineEntity l:
                {
                    using var p = StrokePaint(ctx, e, color, widthPx);
                    c.DrawLine((float)l.Start.X, (float)l.Start.Y, (float)l.End.X, (float)l.End.Y, p);
                    break;
                }
            case PolylineEntity pl when pl.Points.Count > 0:
                {
                    using var path = new SKPath();
                    path.MoveTo((float)pl.Points[0].X, (float)pl.Points[0].Y);
                    for (int i = 1; i < pl.Points.Count; i++) path.LineTo((float)pl.Points[i].X, (float)pl.Points[i].Y);
                    if (pl.Closed) path.Close();
                    using var p = StrokePaint(ctx, e, color, widthPx);
                    if (pl.Filled && pl.Closed)
                    {
                        using var fill = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
                        c.DrawPath(path, fill);
                    }
                    c.DrawPath(path, p);
                    break;
                }
            case CircleEntity ci:
                {
                    if (ci.Filled)
                    {
                        using var fill = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
                        c.DrawCircle((float)ci.Center.X, (float)ci.Center.Y, (float)ci.Radius, fill);
                    }
                    using var p = StrokePaint(ctx, e, color, widthPx);
                    c.DrawCircle((float)ci.Center.X, (float)ci.Center.Y, (float)ci.Radius, p);
                    break;
                }
            case ArcEntity a:
                {
                    using var p = StrokePaint(ctx, e, color, widthPx);
                    var rect = new SKRect((float)(a.Center.X - a.Radius), (float)(a.Center.Y - a.Radius), (float)(a.Center.X + a.Radius), (float)(a.Center.Y + a.Radius));
                    using var path = new SKPath();
                    // World Y-up: angles are CCW in world space, matching Skia's convention under the flipped matrix.
                    path.AddArc(rect, (float)a.StartAngle, (float)a.SweepAngle);
                    c.DrawPath(path, p);
                    break;
                }
            case TextEntity t:
                DrawText(ctx, t.Position, Resolve(ctx, t.Value), t.Height, t.Rotation, t.HAlign, t.VAlign, t.Bold, color);
                break;
            case DimensionEntity d:
                foreach (var part in DxfWriter.ExplodeDimension(d))
                {
                    part.Layer = d.Layer;
                    part.LineWeight = d.LineWeight;
                    DrawEntity(ctx, part, color, widthPx, -1);
                }
                break;
            case LeaderEntity ld when ld.Points.Count > 0:
                {
                    using var p = StrokePaint(ctx, e, color, widthPx);
                    for (int i = 0; i + 1 < ld.Points.Count; i++)
                        c.DrawLine((float)ld.Points[i].X, (float)ld.Points[i].Y, (float)ld.Points[i + 1].X, (float)ld.Points[i + 1].Y, p);
                    var from = ld.Points.Count > 1 ? ld.Points[1] : ld.Points[0] + new Point2(1, 0);
                    foreach (var arrow in DxfWriter.ArrowHead(from, ld.Points[0], ld.TextHeight * 0.8)) DrawEntity(ctx, arrow, color, widthPx, -1);
                    var last = ld.Points[^1];
                    DrawText(ctx, last + new Point2(ld.TextHeight * 0.3, ld.TextHeight * 0.3), Resolve(ctx, ld.Text), ld.TextHeight, 0, TextHAlign.Left, TextVAlign.Baseline, false, color);
                    break;
                }
            case ImageEntity img:
                DrawImage(ctx, img, color);
                break;
            case GroupEntity g:
                foreach (var child in g.Children) DrawEntity(ctx, child, color == default ? SKColors.Black : ResolveChildColor(ctx, child, color), widthPx, -1);
                break;
            case IExplodableEntity ex:
                foreach (var part in Explode(e, ex, revision))
                {
                    var partColor = widthPx is null && part.Color is { } pc && !ctx.Options.Monochrome
                        ? AdjustForTheme(ctx, new SKColor(pc.R, pc.G, pc.B))
                        : color;
                    if (part.Layer != e.Layer && widthPx is null && ctx.Drawing.FindLayer(part.Layer) is { } pl2)
                    {
                        if (!pl2.Visible || (ctx.Options.PrintMode && !pl2.Printable)) continue;
                        if (part.Color is null) partColor = ResolveColor(ctx, part, pl2);
                    }
                    DrawEntity(ctx, part, partColor, widthPx, -1);
                }
                break;
        }
    }

    private static SKColor AdjustForTheme(Ctx ctx, SKColor c) =>
        ctx.Options.Theme.InvertBlack && c.Red < 40 && c.Green < 40 && c.Blue < 40 ? new SKColor(226, 232, 240) : c;

    private static SKColor ResolveChildColor(Ctx ctx, CadEntity child, SKColor parent) =>
        child.Color is { } cc && !ctx.Options.Monochrome ? AdjustForTheme(ctx, new SKColor(cc.R, cc.G, cc.B)) : parent;

    private List<CadEntity> Explode(CadEntity e, IExplodableEntity ex, long revision)
    {
        if (revision >= 0 && _explodeCache.TryGetValue(e.Id, out var cached) && ReferenceEquals(cached.source, e) && cached.revision == revision)
            return cached.parts;
        var parts = ex.Explode().ToList();
        if (revision >= 0) _explodeCache[e.Id] = (e, revision, parts);
        return parts;
    }

    private static string Resolve(Ctx ctx, string text) => ctx.Options.TextResolver?.Invoke(text) ?? text;

    public static void DrawText(SKCanvas c, Point2 pos, string text, double height, double rotation,
        TextHAlign h, TextVAlign v, bool bold, SKColor color)
    {
        if (string.IsNullOrEmpty(text) || height <= 0) return;
        using var font = FontProvider.CreateFont(height, bold);
        using var paint = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
        var lines = text.Replace("\r", "").Split('\n');
        var metrics = font.Metrics;
        var cap = (float)height;
        var lineStep = (float)(height * 1.4);
        var totalExtra = lineStep * (lines.Length - 1);

        c.Save();
        c.Translate((float)pos.X, (float)pos.Y);
        if (Math.Abs(rotation) > 1e-9) c.RotateDegrees((float)rotation);
        c.Scale(1, -1); // glyph space is Y-down

        // Vertical anchor of the first baseline in glyph space (Y down).
        float baseline0 = v switch
        {
            TextVAlign.Top => cap,
            TextVAlign.Middle => cap / 2 - totalExtra / 2,
            TextVAlign.Bottom => -metrics.Descent - totalExtra,
            _ => 0,
        };

        for (int i = 0; i < lines.Length; i++)
        {
            var w = font.MeasureText(lines[i]);
            float x = h switch { TextHAlign.Center => -w / 2, TextHAlign.Right => -w, _ => 0 };
            c.DrawText(lines[i], x, baseline0 + i * lineStep, SKTextAlign.Left, font, paint);
        }
        c.Restore();
    }

    private static void DrawText(Ctx ctx, Point2 pos, string text, double height, double rotation, TextHAlign h, TextVAlign v, bool bold, SKColor color) =>
        DrawText(ctx.Canvas, pos, text, height, rotation, h, v, bold, color);

    private static void DrawImage(Ctx ctx, ImageEntity img, SKColor fallbackColor)
    {
        var image = ctx.Options.Images?.GetImage(img.ImageKey);
        var rect = img.Rect;
        if (image is null)
        {
            using var p = new SKPaint { Color = fallbackColor.WithAlpha(90), Style = SKPaintStyle.Stroke, StrokeWidth = ctx.Px(1), IsAntialias = true };
            ctx.Canvas.DrawRect(new SKRect((float)rect.MinX, (float)rect.MinY, (float)rect.MaxX, (float)rect.MaxY), p);
            return;
        }
        // Preserve aspect ratio, centred in the target rectangle.
        var ar = (double)image.Width / image.Height;
        double w = rect.Width, hgt = rect.Height;
        if (w / hgt > ar) w = hgt * ar; else hgt = w / ar;
        var x = rect.MinX + (rect.Width - w) / 2;
        var y = rect.MinY + (rect.Height - hgt) / 2;
        ctx.Canvas.Save();
        ctx.Canvas.Translate((float)x, (float)(y + hgt));
        ctx.Canvas.Scale(1, -1);
        using var paint = new SKPaint { IsAntialias = true };
        ctx.Canvas.DrawImage(image, new SKRect(0, 0, (float)w, (float)hgt), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        ctx.Canvas.Restore();
    }

    private void DrawOverlay(Ctx ctx, CadEntity e, SKColor color, bool grips)
    {
        DrawEntity(ctx, e, color.WithAlpha(200), 2.5f, ctx.Drawing.Revision);
        if (!grips) return;
        var s = ctx.Px(3.5);
        using var fill = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
        var count = 0;
        foreach (var sp in e.GetSnapPoints())
        {
            if (sp.Kind is not (SnapKind.Endpoint or SnapKind.Center or SnapKind.Pin)) continue;
            ctx.Canvas.DrawRect((float)sp.Point.X - s, (float)sp.Point.Y - s, 2 * s, 2 * s, fill);
            if (++count > 64) break;
        }
    }

    private static void DrawSelectionWindow(Ctx ctx, Rect2 r, bool crossing)
    {
        var rect = new SKRect((float)r.MinX, (float)r.MinY, (float)r.MaxX, (float)r.MaxY);
        var col = crossing ? new SKColor(22, 163, 74) : new SKColor(37, 99, 235);
        using var fill = new SKPaint { Color = col.WithAlpha(40), Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { Color = col, Style = SKPaintStyle.Stroke, StrokeWidth = ctx.Px(1), IsAntialias = true };
        if (crossing) stroke.PathEffect = SKPathEffect.CreateDash(new[] { ctx.Px(6), ctx.Px(4) }, 0);
        ctx.Canvas.DrawRect(rect, fill);
        ctx.Canvas.DrawRect(rect, stroke);
    }

    private static void DrawSnapMarker(Ctx ctx, Point2 p, SnapKind kind)
    {
        if (kind is SnapKind.None or SnapKind.Grid) return;
        var s = ctx.Px(6);
        using var paint = new SKPaint { Color = ctx.Options.Theme.SnapMarker, Style = SKPaintStyle.Stroke, StrokeWidth = ctx.Px(2), IsAntialias = true };
        float x = (float)p.X, y = (float)p.Y;
        switch (kind)
        {
            case SnapKind.Endpoint: ctx.Canvas.DrawRect(x - s, y - s, 2 * s, 2 * s, paint); break;
            case SnapKind.Center: ctx.Canvas.DrawCircle(x, y, s, paint); break;
            case SnapKind.Midpoint:
                using (var path = new SKPath())
                {
                    path.MoveTo(x - s, y - s); path.LineTo(x + s, y - s); path.LineTo(x, y + s); path.Close();
                    ctx.Canvas.DrawPath(path, paint);
                }
                break;
            case SnapKind.Pin:
                ctx.Canvas.DrawLine(x - s, y - s, x + s, y + s, paint);
                ctx.Canvas.DrawLine(x - s, y + s, x + s, y - s, paint);
                ctx.Canvas.DrawCircle(x, y, s * 1.2f, paint);
                break;
            default: ctx.Canvas.DrawCircle(x, y, s / 2, paint); break;
        }
    }

    private static void DrawGrid(Ctx ctx, Rect2 visible)
    {
        if (visible.IsEmpty) return;
        var spacing = ctx.Options.GridSpacing;
        if (spacing <= 0) return;
        while (spacing * ctx.DeviceScale < 8) spacing *= 2;
        var cols = visible.Width / spacing;
        var rows = visible.Height / spacing;
        if (cols * rows > 60000) return;
        using var paint = new SKPaint { Color = ctx.Options.Theme.GridDot, Style = SKPaintStyle.Fill, IsAntialias = false };
        var r = ctx.Px(0.9);
        var x0 = Math.Floor(visible.MinX / spacing) * spacing;
        var y0 = Math.Floor(visible.MinY / spacing) * spacing;
        var pts = new List<SKPoint>();
        for (var x = x0; x <= visible.MaxX; x += spacing)
            for (var y = y0; y <= visible.MaxY; y += spacing)
                pts.Add(new SKPoint((float)x, (float)y));
        paint.StrokeWidth = r * 2;
        paint.StrokeCap = SKStrokeCap.Square;
        ctx.Canvas.DrawPoints(SKPointMode.Points, pts.ToArray(), paint);
    }
}
