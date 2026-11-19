using DND.Electrical.Core.Geometry;
using DND.Electrical.Shared;

namespace DND.Electrical.UnitTests.Geometry;

public class GeometryTests
{
    [Fact]
    public void Rotation_90_is_exact()
    {
        var p = Matrix2.Rotation(90).Transform(new Point2(10, 0));
        Assert.Equal(0, p.X, 12);
        Assert.Equal(10, p.Y, 12);
    }

    [Fact]
    public void Rotation_about_center_keeps_center_fixed()
    {
        var c = new Point2(5, 5);
        Assert.True(Matrix2.Rotation(37, c).Transform(c).AlmostEquals(c, 1e-9));
    }

    [Fact]
    public void Composition_applies_left_first()
    {
        var m = Matrix2.Translation(10, 0) * Matrix2.Rotation(90);
        var p = m.Transform(Point2.Zero);
        Assert.True(p.AlmostEquals(new Point2(0, 10), 1e-9));
    }

    [Fact]
    public void Inverse_roundtrips()
    {
        var m = Matrix2.Rotation(33) * Matrix2.Scaling(2, 3) * Matrix2.Translation(4, -7);
        var p = new Point2(1.5, -2.25);
        Assert.True(m.Invert().Transform(m.Transform(p)).AlmostEquals(p, 1e-9));
    }

    [Fact]
    public void Mirror_reports_negative_determinant()
    {
        Assert.True(Matrix2.MirrorX(3).IsMirroring);
        Assert.Equal(6 - 1, Matrix2.MirrorX(3).Transform(new Point2(1, 0)).X, 9);
    }

    [Fact]
    public void Distance_point_to_segment()
    {
        Assert.Equal(5, GeometryMath.DistancePointToSegment(new Point2(5, 5), new Point2(0, 0), new Point2(10, 0)), 9);
        Assert.Equal(5, GeometryMath.DistancePointToSegment(new Point2(-3, 4), new Point2(0, 0), new Point2(10, 0)), 9);
    }

    [Fact]
    public void Segment_rect_intersection_detects_crossing_without_endpoints_inside()
    {
        var r = new Rect2(0, 0, 10, 10);
        Assert.True(GeometryMath.SegmentIntersectsRect(new Point2(-5, 5), new Point2(15, 5), r));
        Assert.False(GeometryMath.SegmentIntersectsRect(new Point2(-5, 15), new Point2(15, 15), r));
    }

    [Fact]
    public void Rect_union_with_empty_is_identity()
    {
        var r = new Rect2(1, 2, 3, 4);
        Assert.Equal(r, Rect2.Empty.Union(r));
        Assert.Equal(r, r.Union(Rect2.Empty));
    }

    [Theory]
    [InlineData(-90, 270)]
    [InlineData(360, 0)]
    [InlineData(725, 5)]
    public void Normalize_angle(double input, double expected) =>
        Assert.Equal(expected, GeometryMath.NormalizeAngle(input), 9);

    [Fact]
    public void Orthogonalize_picks_dominant_axis()
    {
        Assert.Equal(new Point2(10, 0), GeometryMath.Orthogonalize(Point2.Zero, new Point2(10, 3)));
        Assert.Equal(new Point2(0, 10), GeometryMath.Orthogonalize(Point2.Zero, new Point2(3, 10)));
    }

    [Fact]
    public void Natural_comparer_sorts_engineering_tags()
    {
        var tags = new[] { "-K10", "-K2", "-K1", "X1:10", "X1:9" }.OrderBy(t => t, NaturalStringComparer.Instance).ToArray();
        Assert.Equal(new[] { "-K1", "-K2", "-K10", "X1:9", "X1:10" }, tags);
    }

    [Fact]
    public void Point_parse_invariant()
    {
        Assert.True(Point2.TryParse("12.5, -3", out var p));
        Assert.Equal(new Point2(12.5, -3), p);
        Assert.False(Point2.TryParse("abc", out _));
    }
}
