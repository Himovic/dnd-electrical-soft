namespace DND.Electrical.Core.Geometry;

public static class GeometryMath
{
    public const double Epsilon = 1e-9;

    public static double DegToRad(double d) => d * Math.PI / 180.0;
    public static double RadToDeg(double r) => r * 180.0 / Math.PI;

    /// <summary>Normalises an angle to [0, 360).</summary>
    public static double NormalizeAngle(double degrees)
    {
        var a = degrees % 360.0;
        if (a < 0) a += 360.0;
        return Math.Abs(a - 360.0) < 1e-12 ? 0 : a;
    }

    public static Point2 ClosestPointOnSegment(Point2 p, Point2 a, Point2 b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared;
        if (len2 < Epsilon) return a;
        var t = Math.Clamp((p - a).Dot(ab) / len2, 0, 1);
        return a + ab * t;
    }

    public static double DistancePointToSegment(Point2 p, Point2 a, Point2 b) =>
        p.DistanceTo(ClosestPointOnSegment(p, a, b));

    /// <summary>True when p lies on segment ab within tolerance (excluding nothing; endpoints count).</summary>
    public static bool IsPointOnSegment(Point2 p, Point2 a, Point2 b, double tolerance) =>
        DistancePointToSegment(p, a, b) <= tolerance;

    public static bool SegmentsIntersect(Point2 p1, Point2 p2, Point2 q1, Point2 q2)
    {
        double d1 = Orient(q1, q2, p1), d2 = Orient(q1, q2, p2), d3 = Orient(p1, p2, q1), d4 = Orient(p1, p2, q2);
        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
        if (Math.Abs(d1) < Epsilon && OnBox(q1, q2, p1)) return true;
        if (Math.Abs(d2) < Epsilon && OnBox(q1, q2, p2)) return true;
        if (Math.Abs(d3) < Epsilon && OnBox(p1, p2, q1)) return true;
        if (Math.Abs(d4) < Epsilon && OnBox(p1, p2, q2)) return true;
        return false;
    }

    public static bool SegmentIntersectsRect(Point2 a, Point2 b, Rect2 r)
    {
        if (r.Contains(a) || r.Contains(b)) return true;
        var c = r.Corners().ToArray();
        for (int i = 0; i < 4; i++)
            if (SegmentsIntersect(a, b, c[i], c[(i + 1) % 4])) return true;
        return false;
    }

    /// <summary>Is the angle <paramref name="a"/> inside the CCW sweep starting at <paramref name="start"/>?</summary>
    public static bool AngleInSweep(double a, double start, double sweep)
    {
        if (sweep >= 360) return true;
        if (sweep < 0) { start += sweep; sweep = -sweep; }
        var d = NormalizeAngle(a - start);
        return d <= sweep + 1e-9;
    }

    private static double Orient(Point2 a, Point2 b, Point2 c) => (b - a).Cross(c - a);

    private static bool OnBox(Point2 a, Point2 b, Point2 p) =>
        p.X >= Math.Min(a.X, b.X) - Epsilon && p.X <= Math.Max(a.X, b.X) + Epsilon &&
        p.Y >= Math.Min(a.Y, b.Y) - Epsilon && p.Y <= Math.Max(a.Y, b.Y) + Epsilon;

    /// <summary>Constrains <paramref name="p"/> so that the segment from <paramref name="from"/> is horizontal or vertical.</summary>
    public static Point2 Orthogonalize(Point2 from, Point2 p)
    {
        var d = p - from;
        return Math.Abs(d.X) >= Math.Abs(d.Y) ? new Point2(p.X, from.Y) : new Point2(from.X, p.Y);
    }
}
