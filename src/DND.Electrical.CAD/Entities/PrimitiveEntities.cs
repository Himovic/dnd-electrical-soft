using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;

namespace DND.Electrical.CAD.Entities;

public sealed class LineEntity : CadEntity
{
    public LineEntity() { }
    public LineEntity(Point2 start, Point2 end) { Start = start; End = end; }

    public Point2 Start { get; set; }
    public Point2 End { get; set; }

    public override string TypeName => "Line";
    public double Length => Start.DistanceTo(End);
    public override Rect2 GetBounds() => Rect2.FromPoints(Start, End);
    public override double DistanceTo(Point2 p) => GeometryMath.DistancePointToSegment(p, Start, End);
    public override bool IntersectsRect(Rect2 r) => GeometryMath.SegmentIntersectsRect(Start, End, r);

    public override void Transform(Matrix2 m)
    {
        Start = m.Transform(Start);
        End = m.Transform(End);
    }

    public override IEnumerable<SnapPoint> GetSnapPoints()
    {
        yield return new(Start, SnapKind.Endpoint);
        yield return new(End, SnapKind.Endpoint);
        yield return new(Point2.Lerp(Start, End, 0.5), SnapKind.Midpoint);
    }
}

public sealed class PolylineEntity : CadEntity
{
    public PolylineEntity() { }
    public PolylineEntity(IEnumerable<Point2> points, bool closed = false) { Points = points.ToList(); Closed = closed; }

    public List<Point2> Points { get; set; } = new();
    public bool Closed { get; set; }

    /// <summary>Fill the closed polygon (used for arrows and solid symbol parts).</summary>
    public bool Filled { get; set; }

    public override string TypeName => "Polyline";

    public static PolylineEntity Rectangle(Point2 a, Point2 b)
    {
        var r = Rect2.FromPoints(a, b);
        return new PolylineEntity(r.Corners(), closed: true);
    }

    public IEnumerable<(Point2 A, Point2 B)> Segments()
    {
        for (int i = 0; i + 1 < Points.Count; i++) yield return (Points[i], Points[i + 1]);
        if (Closed && Points.Count > 2) yield return (Points[^1], Points[0]);
    }

    public double Length => Segments().Sum(s => s.A.DistanceTo(s.B));

    public override Rect2 GetBounds() => Rect2.FromPoints(Points);

    public override double DistanceTo(Point2 p)
    {
        if (Points.Count == 0) return double.MaxValue;
        if (Points.Count == 1) return p.DistanceTo(Points[0]);
        var d = Segments().Min(s => GeometryMath.DistancePointToSegment(p, s.A, s.B));
        if (Filled && Closed && ContainsPoint(p)) return 0;
        return d;
    }

    public bool ContainsPoint(Point2 p)
    {
        bool inside = false;
        for (int i = 0, j = Points.Count - 1; i < Points.Count; j = i++)
        {
            var a = Points[i]; var b = Points[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    public override bool IntersectsRect(Rect2 r) => Segments().Any(s => GeometryMath.SegmentIntersectsRect(s.A, s.B, r))
        || (Points.Count == 1 && r.Contains(Points[0]));

    public override void Transform(Matrix2 m)
    {
        for (int i = 0; i < Points.Count; i++) Points[i] = m.Transform(Points[i]);
    }

    public override IEnumerable<SnapPoint> GetSnapPoints()
    {
        foreach (var p in Points) yield return new(p, SnapKind.Endpoint);
        foreach (var (a, b) in Segments()) yield return new(Point2.Lerp(a, b, 0.5), SnapKind.Midpoint);
    }

    protected override void DeepCopyFrom(CadEntity source) => Points = new List<Point2>(((PolylineEntity)source).Points);
}

public sealed class CircleEntity : CadEntity
{
    public CircleEntity() { }
    public CircleEntity(Point2 center, double radius) { Center = center; Radius = radius; }

    public Point2 Center { get; set; }
    public double Radius { get; set; }
    public bool Filled { get; set; }

    public override string TypeName => "Circle";
    public override Rect2 GetBounds() => Rect2.FromCenter(Center, Radius, Radius);

    public override double DistanceTo(Point2 p)
    {
        var d = p.DistanceTo(Center);
        return Filled && d <= Radius ? 0 : Math.Abs(d - Radius);
    }

    public override bool IntersectsRect(Rect2 r)
    {
        if (!r.Intersects(GetBounds())) return false;
        var cx = Math.Clamp(Center.X, r.MinX, r.MaxX);
        var cy = Math.Clamp(Center.Y, r.MinY, r.MaxY);
        var nearest = new Point2(cx, cy).DistanceTo(Center);
        var farthest = r.Corners().Max(c => c.DistanceTo(Center));
        return nearest <= Radius && farthest >= Radius || (Filled && nearest <= Radius);
    }

    public override void Transform(Matrix2 m)
    {
        Center = m.Transform(Center);
        Radius *= m.ScaleFactor;
    }

    public override IEnumerable<SnapPoint> GetSnapPoints()
    {
        yield return new(Center, SnapKind.Center);
        for (int a = 0; a < 360; a += 90) yield return new(Center + Point2.FromPolar(Radius, a), SnapKind.Endpoint);
    }
}

/// <summary>Circular arc, counter-clockwise from <see cref="StartAngle"/> through <see cref="SweepAngle"/> degrees.</summary>
public sealed class ArcEntity : CadEntity
{
    public ArcEntity() { }
    public ArcEntity(Point2 center, double radius, double startAngle, double sweepAngle)
    {
        Center = center; Radius = radius; StartAngle = startAngle; SweepAngle = sweepAngle;
    }

    public Point2 Center { get; set; }
    public double Radius { get; set; }
    public double StartAngle { get; set; }
    public double SweepAngle { get; set; } = 90;

    public override string TypeName => "Arc";
    public double EndAngle => StartAngle + SweepAngle;
    public Point2 StartPoint => Center + Point2.FromPolar(Radius, StartAngle);
    public Point2 EndPoint => Center + Point2.FromPolar(Radius, EndAngle);

    /// <summary>Arc through three points (start, any point on arc, end).</summary>
    public static ArcEntity? FromThreePoints(Point2 p1, Point2 p2, Point2 p3)
    {
        var d = 2 * (p1.X * (p2.Y - p3.Y) + p2.X * (p3.Y - p1.Y) + p3.X * (p1.Y - p2.Y));
        if (Math.Abs(d) < 1e-9) return null;
        var s1 = p1.LengthSquared; var s2 = p2.LengthSquared; var s3 = p3.LengthSquared;
        var c = new Point2(
            (s1 * (p2.Y - p3.Y) + s2 * (p3.Y - p1.Y) + s3 * (p1.Y - p2.Y)) / d,
            (s1 * (p3.X - p2.X) + s2 * (p1.X - p3.X) + s3 * (p2.X - p1.X)) / d);
        var r = c.DistanceTo(p1);
        var a1 = (p1 - c).AngleDegrees; var a2 = (p2 - c).AngleDegrees; var a3 = (p3 - c).AngleDegrees;
        var ccwSweep = GeometryMath.NormalizeAngle(a3 - a1);
        var mid = GeometryMath.NormalizeAngle(a2 - a1);
        return mid <= ccwSweep
            ? new ArcEntity(c, r, GeometryMath.NormalizeAngle(a1), ccwSweep)
            : new ArcEntity(c, r, GeometryMath.NormalizeAngle(a3), 360 - ccwSweep);
    }

    public override Rect2 GetBounds()
    {
        var r = Rect2.FromPoints(StartPoint, EndPoint);
        for (int a = 0; a < 360; a += 90)
            if (GeometryMath.AngleInSweep(a, StartAngle, SweepAngle)) r = r.Union(Center + Point2.FromPolar(Radius, a));
        return r;
    }

    public override double DistanceTo(Point2 p)
    {
        var ang = (p - Center).AngleDegrees;
        if (GeometryMath.AngleInSweep(ang, StartAngle, SweepAngle)) return Math.Abs(p.DistanceTo(Center) - Radius);
        return Math.Min(p.DistanceTo(StartPoint), p.DistanceTo(EndPoint));
    }

    public override void Transform(Matrix2 m)
    {
        var s = m.Transform(StartPoint);
        var e = m.Transform(EndPoint);
        Center = m.Transform(Center);
        Radius *= m.ScaleFactor;
        if (m.IsMirroring) (s, e) = (e, s);
        StartAngle = GeometryMath.NormalizeAngle((s - Center).AngleDegrees);
        var end = GeometryMath.NormalizeAngle((e - Center).AngleDegrees);
        var sweep = GeometryMath.NormalizeAngle(end - StartAngle);
        SweepAngle = sweep < 1e-9 && SweepAngle > 1 ? 360 : sweep;
    }

    public override IEnumerable<SnapPoint> GetSnapPoints()
    {
        yield return new(StartPoint, SnapKind.Endpoint);
        yield return new(EndPoint, SnapKind.Endpoint);
        yield return new(Center, SnapKind.Center);
        yield return new(Center + Point2.FromPolar(Radius, StartAngle + SweepAngle / 2), SnapKind.Midpoint);
    }
}

public sealed class TextEntity : CadEntity
{
    /// <summary>Average glyph advance relative to text height, used for bounds estimation.</summary>
    public const double WidthFactor = 0.62;

    public TextEntity() { }
    public TextEntity(Point2 position, string value, double height = 2.5) { Position = position; Value = value; Height = height; }

    public Point2 Position { get; set; }

    /// <summary>Text content. May contain field placeholders such as <c>{PROJECT.NAME}</c>.</summary>
    public string Value { get; set; } = string.Empty;

    public double Height { get; set; } = 2.5;
    public double Rotation { get; set; }
    public TextHAlign HAlign { get; set; } = TextHAlign.Left;
    public TextVAlign VAlign { get; set; } = TextVAlign.Baseline;
    public bool Bold { get; set; }

    public override string TypeName => "Text";

    public double EstimatedWidth => Math.Max(1, MaxLineLength) * Height * WidthFactor;
    private int MaxLineLength => Value.Split('\n').Max(l => l.Length);
    private int LineCount => Math.Max(1, Value.Split('\n').Length);

    /// <summary>Corners of the text box in world coordinates (honours rotation and alignment).</summary>
    public Point2[] GetBoxCorners()
    {
        var w = EstimatedWidth;
        var h = Height * (1 + (LineCount - 1) * 1.4);
        double x0 = HAlign switch { TextHAlign.Center => -w / 2, TextHAlign.Right => -w, _ => 0 };
        double y0 = VAlign switch
        {
            TextVAlign.Top => -h,
            TextVAlign.Middle => -h / 2,
            TextVAlign.Bottom => 0,
            _ => -Height * 0.2 - (LineCount - 1) * Height * 1.4,
        };
        var m = Matrix2.Rotation(Rotation) * Matrix2.Translation(Position);
        return new[]
        {
            m.Transform(new Point2(x0, y0)), m.Transform(new Point2(x0 + w, y0)),
            m.Transform(new Point2(x0 + w, y0 + h)), m.Transform(new Point2(x0, y0 + h)),
        };
    }

    public override Rect2 GetBounds() => Rect2.FromPoints(GetBoxCorners());

    public override double DistanceTo(Point2 p)
    {
        var c = GetBoxCorners();
        var poly = new PolylineEntity(c, true) { Filled = true };
        return poly.DistanceTo(p);
    }

    public override void Transform(Matrix2 m)
    {
        Position = m.Transform(Position);
        Height *= m.ScaleFactor;
        var dir = m.TransformVector(Point2.FromPolar(1, Rotation));
        var angle = GeometryMath.NormalizeAngle(dir.AngleDegrees);
        if (m.IsMirroring)
        {
            // Keep text readable after mirroring (equivalent of MIRRTEXT=0): flip alignment instead of glyphs.
            angle = GeometryMath.NormalizeAngle(angle + 180);
            HAlign = HAlign switch { TextHAlign.Left => TextHAlign.Right, TextHAlign.Right => TextHAlign.Left, _ => HAlign };
        }
        Rotation = NormalizeReadable(angle);
    }

    private double NormalizeReadable(double angle)
    {
        // Text rotated upside down (between 90° exclusive and 270° exclusive) is flipped to stay readable.
        if (angle > 90 + 1e-6 && angle < 270 - 1e-6)
        {
            HAlign = HAlign switch { TextHAlign.Left => TextHAlign.Right, TextHAlign.Right => TextHAlign.Left, _ => HAlign };
            return GeometryMath.NormalizeAngle(angle - 180);
        }
        return angle;
    }

    public override IEnumerable<SnapPoint> GetSnapPoints()
    {
        yield return new(Position, SnapKind.Endpoint);
    }
}

/// <summary>Aligned linear dimension between two points.</summary>
public sealed class DimensionEntity : CadEntity
{
    public Point2 Start { get; set; }
    public Point2 End { get; set; }

    /// <summary>Signed perpendicular offset of the dimension line from the measured segment.</summary>
    public double Offset { get; set; } = 10;
    public double TextHeight { get; set; } = 2.5;
    public string? TextOverride { get; set; }
    public int Precision { get; set; } = 0;
    public string Suffix { get; set; } = string.Empty;

    public override string TypeName => "Dimension";
    public double Measurement => Start.DistanceTo(End);

    public string DisplayText => TextOverride ?? Measurement.ToString("F" + Precision, System.Globalization.CultureInfo.InvariantCulture) + Suffix;

    public Point2 Normal
    {
        get
        {
            var d = (End - Start).Normalized();
            return new Point2(-d.Y, d.X);
        }
    }

    public Point2 DimStart => Start + Normal * Offset;
    public Point2 DimEnd => End + Normal * Offset;

    public override Rect2 GetBounds() =>
        Rect2.FromPoints(new[] { Start, End, DimStart, DimEnd }).Inflate(TextHeight * 1.5);

    public override double DistanceTo(Point2 p) => Math.Min(
        GeometryMath.DistancePointToSegment(p, DimStart, DimEnd),
        Math.Min(GeometryMath.DistancePointToSegment(p, Start, DimStart), GeometryMath.DistancePointToSegment(p, End, DimEnd)));

    public override void Transform(Matrix2 m)
    {
        Start = m.Transform(Start);
        End = m.Transform(End);
        Offset *= m.ScaleFactor * (m.IsMirroring ? -1 : 1);
        TextHeight *= m.ScaleFactor;
    }

    public override IEnumerable<SnapPoint> GetSnapPoints()
    {
        yield return new(Start, SnapKind.Endpoint);
        yield return new(End, SnapKind.Endpoint);
    }
}

/// <summary>Callout / leader: an arrow pointing at <c>Points[0]</c> with a text label at the last point.</summary>
public sealed class LeaderEntity : CadEntity
{
    public List<Point2> Points { get; set; } = new();
    public string Text { get; set; } = string.Empty;
    public double TextHeight { get; set; } = 2.5;

    public override string TypeName => "Leader";

    public override Rect2 GetBounds()
    {
        var r = Rect2.FromPoints(Points);
        if (Points.Count > 0)
        {
            var last = Points[^1];
            r = r.Union(new Point2(last.X + Text.Length * TextHeight * TextEntity.WidthFactor + TextHeight, last.Y + TextHeight * 1.2));
        }
        return r;
    }

    public override double DistanceTo(Point2 p)
    {
        if (Points.Count == 0) return double.MaxValue;
        var d = double.MaxValue;
        for (int i = 0; i + 1 < Points.Count; i++) d = Math.Min(d, GeometryMath.DistancePointToSegment(p, Points[i], Points[i + 1]));
        var last = Points[^1];
        var textBox = new Rect2(last.X, last.Y, last.X + Text.Length * TextHeight * TextEntity.WidthFactor + TextHeight, last.Y + TextHeight * 1.2);
        if (textBox.Contains(p)) d = 0;
        return Math.Min(d, p.DistanceTo(Points[0]));
    }

    public override void Transform(Matrix2 m)
    {
        for (int i = 0; i < Points.Count; i++) Points[i] = m.Transform(Points[i]);
        TextHeight *= m.ScaleFactor;
    }

    public override IEnumerable<SnapPoint> GetSnapPoints() => Points.Select(p => new SnapPoint(p, SnapKind.Endpoint));

    protected override void DeepCopyFrom(CadEntity source) => Points = new List<Point2>(((LeaderEntity)source).Points);
}

/// <summary>Raster image reference (e.g. company logo in a title block). The image is resolved by key at render time.</summary>
public sealed class ImageEntity : CadEntity
{
    public Point2 Position { get; set; }
    public double Width { get; set; } = 20;
    public double Height { get; set; } = 20;

    /// <summary>Key resolved by the renderer's image provider, e.g. <c>branding:logo</c> or <c>company:logo</c>.</summary>
    public string ImageKey { get; set; } = string.Empty;

    public override string TypeName => "Image";
    public Rect2 Rect => Rect2.FromSize(Position.X, Position.Y, Width, Height);
    public override Rect2 GetBounds() => Rect;
    public override double DistanceTo(Point2 p) => Rect.Contains(p) ? 0 : Rect.Corners().Min(c => c.DistanceTo(p));

    public override void Transform(Matrix2 m)
    {
        var r = Rect.Transform(m);
        Position = r.Min;
        Width = r.Width;
        Height = r.Height;
    }
}

/// <summary>Named group of entities that is selected and transformed as a unit.</summary>
public sealed class GroupEntity : CadEntity
{
    public string Name { get; set; } = string.Empty;
    public List<CadEntity> Children { get; set; } = new();

    public override string TypeName => "Group";

    public override Rect2 GetBounds()
    {
        var r = Rect2.Empty;
        foreach (var c in Children) r = r.Union(c.GetBounds());
        return r;
    }

    public override double DistanceTo(Point2 p) => Children.Count == 0 ? double.MaxValue : Children.Min(c => c.DistanceTo(p));
    public override bool IntersectsRect(Rect2 r) => Children.Any(c => c.IntersectsRect(r));

    public override void Transform(Matrix2 m)
    {
        foreach (var c in Children) c.Transform(m);
    }

    public override IEnumerable<SnapPoint> GetSnapPoints() => Children.SelectMany(c => c.GetSnapPoints());

    protected override void DeepCopyFrom(CadEntity source) =>
        Children = ((GroupEntity)source).Children.Select(c => c.Clone(keepId: true)).ToList();

    protected override void OnCloned(bool keepId)
    {
        if (keepId) return;
        foreach (var c in Children) c.Id = Guid.NewGuid();
    }
}
