using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;

namespace DND.Electrical.CAD.Editing;

public sealed class SnapSettings
{
    public bool GridSnap { get; set; } = true;
    public double GridSpacing { get; set; } = 5;
    public bool ShowGrid { get; set; } = true;
    public bool ObjectSnap { get; set; } = true;

    /// <summary>Object-snap capture radius in screen pixels.</summary>
    public double AperturePixels { get; set; } = 10;

    /// <summary>Constrain to horizontal/vertical relative to the previous point.</summary>
    public bool Ortho { get; set; } = true;
}

public readonly record struct SnapResult(Point2 Point, SnapKind Kind, CadEntity? Entity = null, object? Tag = null)
{
    public bool IsObjectSnap => Kind is SnapKind.Endpoint or SnapKind.Midpoint or SnapKind.Center or SnapKind.Pin;
}

/// <summary>Computes the snapped cursor position from object snaps, grid and ortho constraint.</summary>
public sealed class SnapEngine
{
    private static int Priority(SnapKind k) => k switch
    {
        SnapKind.Pin => 5,
        SnapKind.Endpoint => 4,
        SnapKind.Center => 3,
        SnapKind.Midpoint => 2,
        _ => 0,
    };

    public SnapResult Snap(Point2 raw, CadDrawing drawing, Viewport viewport, SnapSettings settings,
        Point2? orthoFrom = null, Func<CadEntity, bool>? entityFilter = null, Func<SnapPoint, bool>? pointFilter = null)
    {
        if (settings.ObjectSnap)
        {
            var aperture = viewport.ToWorldDistance(settings.AperturePixels);
            SnapResult? best = null;
            var bestScore = double.MaxValue;
            foreach (var e in drawing.Query(Rect2.FromCenter(raw, aperture * 4, aperture * 4)))
            {
                if (!drawing.IsLayerVisible(e.Layer)) continue;
                if (entityFilter is not null && !entityFilter(e)) continue;
                foreach (var sp in e.GetSnapPoints())
                {
                    if (pointFilter is not null && !pointFilter(sp)) continue;
                    var d = sp.Point.DistanceTo(raw);
                    if (d > aperture) continue;
                    // Score: distance, with a bonus for higher-priority snap kinds.
                    var score = d - Priority(sp.Kind) * aperture * 0.15;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = new SnapResult(sp.Point, sp.Kind, e, sp.Tag);
                    }
                }
            }
            if (best is { } b) return b;
        }

        var p = raw;
        if (settings.Ortho && orthoFrom is { } from) p = GeometryMath.Orthogonalize(from, p);
        if (settings.GridSnap && settings.GridSpacing > 0)
        {
            var g = p.SnapToGrid(settings.GridSpacing);
            if (settings.Ortho && orthoFrom is { } f)
            {
                // Keep the constrained axis exactly on the previous point.
                g = Math.Abs(p.Y - f.Y) < 1e-9 ? new Point2(g.X, f.Y) : Math.Abs(p.X - f.X) < 1e-9 ? new Point2(f.X, g.Y) : g;
            }
            return new SnapResult(g, SnapKind.Grid);
        }
        return new SnapResult(p, SnapKind.None);
    }
}
