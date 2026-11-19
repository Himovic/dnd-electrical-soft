using System.Globalization;

namespace DND.Electrical.Core.Geometry;

/// <summary>2D point / vector in drawing units (millimetres). Y axis points up (CAD convention).</summary>
public readonly record struct Point2(double X, double Y)
{
    public static readonly Point2 Zero = new(0, 0);

    public static Point2 operator +(Point2 a, Point2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Point2 operator -(Point2 a, Point2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Point2 operator -(Point2 a) => new(-a.X, -a.Y);
    public static Point2 operator *(Point2 a, double s) => new(a.X * s, a.Y * s);
    public static Point2 operator *(double s, Point2 a) => new(a.X * s, a.Y * s);
    public static Point2 operator /(Point2 a, double s) => new(a.X / s, a.Y / s);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    public double DistanceTo(Point2 other) => (other - this).Length;

    public double Dot(Point2 other) => X * other.X + Y * other.Y;
    public double Cross(Point2 other) => X * other.Y - Y * other.X;

    public Point2 Normalized()
    {
        var len = Length;
        return len < GeometryMath.Epsilon ? Zero : this / len;
    }

    /// <summary>Angle of the vector from the positive X axis, in degrees (-180..180].</summary>
    public double AngleDegrees => Math.Atan2(Y, X) * 180.0 / Math.PI;

    public bool AlmostEquals(Point2 other, double tolerance = GeometryMath.Epsilon) =>
        Math.Abs(X - other.X) <= tolerance && Math.Abs(Y - other.Y) <= tolerance;

    public Point2 SnapToGrid(double spacing) =>
        spacing <= 0 ? this : new(Math.Round(X / spacing) * spacing, Math.Round(Y / spacing) * spacing);

    public static Point2 Lerp(Point2 a, Point2 b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    public static Point2 FromPolar(double length, double angleDegrees)
    {
        var r = angleDegrees * Math.PI / 180.0;
        return new(length * Math.Cos(r), length * Math.Sin(r));
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{X:0.###},{Y:0.###}");

    /// <summary>Parses "x,y" (invariant culture).</summary>
    public static bool TryParse(string? text, out Point2 point)
    {
        point = Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split(new[] { ',', ';' }, StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) return false;
        point = new Point2(x, y);
        return true;
    }
}
