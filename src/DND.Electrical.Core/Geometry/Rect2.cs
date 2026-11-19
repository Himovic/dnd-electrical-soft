namespace DND.Electrical.Core.Geometry;

/// <summary>Axis-aligned rectangle. <see cref="Empty"/> is the identity for <see cref="Union(Rect2)"/>.</summary>
public readonly record struct Rect2(double MinX, double MinY, double MaxX, double MaxY)
{
    public static readonly Rect2 Empty = new(double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);

    public bool IsEmpty => MinX > MaxX || MinY > MaxY;
    public double Width => IsEmpty ? 0 : MaxX - MinX;
    public double Height => IsEmpty ? 0 : MaxY - MinY;
    public Point2 Center => new((MinX + MaxX) / 2, (MinY + MaxY) / 2);
    public Point2 Min => new(MinX, MinY);
    public Point2 Max => new(MaxX, MaxY);

    public static Rect2 FromPoints(Point2 a, Point2 b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    public static Rect2 FromPoints(IEnumerable<Point2> points)
    {
        var r = Empty;
        foreach (var p in points) r = r.Union(p);
        return r;
    }

    public static Rect2 FromCenter(Point2 c, double halfWidth, double halfHeight) =>
        new(c.X - halfWidth, c.Y - halfHeight, c.X + halfWidth, c.Y + halfHeight);

    public static Rect2 FromSize(double x, double y, double width, double height) =>
        new(x, y, x + width, y + height);

    public Rect2 Union(Point2 p) =>
        IsEmpty ? new(p.X, p.Y, p.X, p.Y) : new(Math.Min(MinX, p.X), Math.Min(MinY, p.Y), Math.Max(MaxX, p.X), Math.Max(MaxY, p.Y));

    public Rect2 Union(Rect2 o)
    {
        if (o.IsEmpty) return this;
        if (IsEmpty) return o;
        return new(Math.Min(MinX, o.MinX), Math.Min(MinY, o.MinY), Math.Max(MaxX, o.MaxX), Math.Max(MaxY, o.MaxY));
    }

    public Rect2 Inflate(double d) => IsEmpty ? this : new(MinX - d, MinY - d, MaxX + d, MaxY + d);

    public bool Contains(Point2 p) => !IsEmpty && p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;

    public bool Contains(Rect2 o) => !IsEmpty && !o.IsEmpty &&
        o.MinX >= MinX && o.MaxX <= MaxX && o.MinY >= MinY && o.MaxY <= MaxY;

    public bool Intersects(Rect2 o) => !IsEmpty && !o.IsEmpty &&
        o.MinX <= MaxX && o.MaxX >= MinX && o.MinY <= MaxY && o.MaxY >= MinY;

    /// <summary>Strict overlap with positive area (touching edges do not count).</summary>
    public bool Overlaps(Rect2 o, double tolerance = 1e-9) => !IsEmpty && !o.IsEmpty &&
        o.MinX < MaxX - tolerance && o.MaxX > MinX + tolerance && o.MinY < MaxY - tolerance && o.MaxY > MinY + tolerance;

    public Rect2 Intersection(Rect2 o)
    {
        if (!Intersects(o)) return Empty;
        return new(Math.Max(MinX, o.MinX), Math.Max(MinY, o.MinY), Math.Min(MaxX, o.MaxX), Math.Min(MaxY, o.MaxY));
    }

    public double Area => Width * Height;

    public Rect2 Transform(Matrix2 m) => IsEmpty ? this : FromPoints(new[]
    {
        m.Transform(new Point2(MinX, MinY)), m.Transform(new Point2(MaxX, MinY)),
        m.Transform(new Point2(MaxX, MaxY)), m.Transform(new Point2(MinX, MaxY)),
    });

    public IEnumerable<Point2> Corners()
    {
        yield return new(MinX, MinY);
        yield return new(MaxX, MinY);
        yield return new(MaxX, MaxY);
        yield return new(MinX, MaxY);
    }
}
