namespace DND.Electrical.Core.Geometry;

/// <summary>
/// 2D affine transform: x' = M11*x + M21*y + OffsetX ; y' = M12*x + M22*y + OffsetY.
/// Composition: <c>a * b</c> applies <c>a</c> first, then <c>b</c>.
/// </summary>
public readonly record struct Matrix2(double M11, double M12, double M21, double M22, double OffsetX, double OffsetY)
{
    public static readonly Matrix2 Identity = new(1, 0, 0, 1, 0, 0);

    public static Matrix2 Translation(double dx, double dy) => new(1, 0, 0, 1, dx, dy);
    public static Matrix2 Translation(Point2 d) => Translation(d.X, d.Y);
    public static Matrix2 Scaling(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    /// <summary>Counter-clockwise rotation about the origin. Exact for multiples of 90°.</summary>
    public static Matrix2 Rotation(double degrees)
    {
        var (s, c) = SinCosExact(degrees);
        return new(c, s, -s, c, 0, 0);
    }

    public static Matrix2 Rotation(double degrees, Point2 center) =>
        Translation(-center) * Rotation(degrees) * Translation(center);

    /// <summary>Mirror across the vertical axis x = axisX (flips X).</summary>
    public static Matrix2 MirrorX(double axisX = 0) => new(-1, 0, 0, 1, 2 * axisX, 0);

    /// <summary>Mirror across the horizontal axis y = axisY (flips Y).</summary>
    public static Matrix2 MirrorY(double axisY = 0) => new(1, 0, 0, -1, 0, 2 * axisY);

    public static Matrix2 operator *(Matrix2 a, Matrix2 b) => new(
        a.M11 * b.M11 + a.M12 * b.M21,
        a.M11 * b.M12 + a.M12 * b.M22,
        a.M21 * b.M11 + a.M22 * b.M21,
        a.M21 * b.M12 + a.M22 * b.M22,
        a.OffsetX * b.M11 + a.OffsetY * b.M21 + b.OffsetX,
        a.OffsetX * b.M12 + a.OffsetY * b.M22 + b.OffsetY);

    public Point2 Transform(Point2 p) => new(M11 * p.X + M21 * p.Y + OffsetX, M12 * p.X + M22 * p.Y + OffsetY);

    public Point2 TransformVector(Point2 v) => new(M11 * v.X + M21 * v.Y, M12 * v.X + M22 * v.Y);

    public double Determinant => M11 * M22 - M12 * M21;

    /// <summary>True when the transform reverses orientation (contains a mirror).</summary>
    public bool IsMirroring => Determinant < 0;

    /// <summary>Uniform scale factor (geometric mean of axis scales).</summary>
    public double ScaleFactor => Math.Sqrt(Math.Abs(Determinant));

    /// <summary>Rotation angle applied to the X axis, in degrees.</summary>
    public double RotationDegrees => Math.Atan2(M12, M11) * 180 / Math.PI;

    public Matrix2 Invert()
    {
        var det = Determinant;
        if (Math.Abs(det) < 1e-15) throw new InvalidOperationException("Matrix is not invertible.");
        var i11 = M22 / det;
        var i12 = -M12 / det;
        var i21 = -M21 / det;
        var i22 = M11 / det;
        return new(i11, i12, i21, i22,
            -(OffsetX * i11 + OffsetY * i21),
            -(OffsetX * i12 + OffsetY * i22));
    }

    private static (double sin, double cos) SinCosExact(double degrees)
    {
        var d = ((degrees % 360) + 360) % 360;
        if (Math.Abs(d) < 1e-12) return (0, 1);
        if (Math.Abs(d - 90) < 1e-12) return (1, 0);
        if (Math.Abs(d - 180) < 1e-12) return (0, -1);
        if (Math.Abs(d - 270) < 1e-12) return (-1, 0);
        var r = d * Math.PI / 180;
        return (Math.Sin(r), Math.Cos(r));
    }
}
