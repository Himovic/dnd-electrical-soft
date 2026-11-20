using System.Text.Json.Serialization;

namespace DND.Electrical.ThreeD;

/// <summary>Double-precision 3D vector. Engineering convention: millimetres, X right, Y depth (away from the viewer / into the cabinet), Z up.</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 UnitX = new(1, 0, 0);
    public static readonly Vec3 UnitY = new(0, 1, 0);
    public static readonly Vec3 UnitZ = new(0, 0, 1);
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => a * s;
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);
    public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public Vec3 Cross(Vec3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    [JsonIgnore] public double Length => Math.Sqrt(Dot(this));
    public Vec3 Normalized() { var l = Length; return l < 1e-12 ? Zero : this / l; }
    public double this[int i] => i switch { 0 => X, 1 => Y, _ => Z };
    public static Vec3 Min(Vec3 a, Vec3 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    public static Vec3 Max(Vec3 a, Vec3 b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
    public override string ToString() => FormattableString.Invariant($"({X:0.###}, {Y:0.###}, {Z:0.###})");
}

/// <summary>Orthonormal 3×3 rotation matrix (columns are the local axes expressed in world coordinates).</summary>
public readonly record struct Rot3(Vec3 AxisX, Vec3 AxisY, Vec3 AxisZ)
{
    public static readonly Rot3 Identity = new(Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ);

    /// <summary>Rotation from Euler angles in degrees, applied X, then Y, then Z (extrinsic), i.e. R = Rz·Ry·Rx.</summary>
    public static Rot3 FromEulerDegrees(Vec3 deg)
    {
        double rx = deg.X * Math.PI / 180, ry = deg.Y * Math.PI / 180, rz = deg.Z * Math.PI / 180;
        double cx = Math.Cos(rx), sx = Math.Sin(rx), cy = Math.Cos(ry), sy = Math.Sin(ry), cz = Math.Cos(rz), sz = Math.Sin(rz);
        // Columns of Rz*Ry*Rx
        var c0 = new Vec3(cz * cy, sz * cy, -sy);
        var c1 = new Vec3(cz * sy * sx - sz * cx, sz * sy * sx + cz * cx, cy * sx);
        var c2 = new Vec3(cz * sy * cx + sz * sx, sz * sy * cx - cz * sx, cy * cx);
        return new Rot3(c0, c1, c2);
    }

    /// <summary>Rotation whose local X axis points along <paramref name="direction"/> with local Z as close to world Z as possible.</summary>
    public static Rot3 AlongDirection(Vec3 direction)
    {
        var x = direction.Normalized();
        var up = Math.Abs(x.Dot(Vec3.UnitZ)) > 0.999 ? Vec3.UnitY : Vec3.UnitZ;
        var y = up.Cross(x).Normalized();
        var z = x.Cross(y).Normalized();
        return new Rot3(x, y, z);
    }

    public Vec3 Apply(Vec3 v) => AxisX * v.X + AxisY * v.Y + AxisZ * v.Z;
    public Vec3 ApplyInverse(Vec3 v) => new(AxisX.Dot(v), AxisY.Dot(v), AxisZ.Dot(v));
    public Vec3 this[int i] => i switch { 0 => AxisX, 1 => AxisY, _ => AxisZ };

    /// <summary>Unit quaternion (x, y, z, w) for glTF.</summary>
    public (double x, double y, double z, double w) ToQuaternion()
    {
        double m00 = AxisX.X, m01 = AxisY.X, m02 = AxisZ.X, m10 = AxisX.Y, m11 = AxisY.Y, m12 = AxisZ.Y, m20 = AxisX.Z, m21 = AxisY.Z, m22 = AxisZ.Z;
        double tr = m00 + m11 + m22, x, y, z, w;
        if (tr > 0) { var s = Math.Sqrt(tr + 1) * 2; w = 0.25 * s; x = (m21 - m12) / s; y = (m02 - m20) / s; z = (m10 - m01) / s; }
        else if (m00 > m11 && m00 > m22) { var s = Math.Sqrt(1 + m00 - m11 - m22) * 2; w = (m21 - m12) / s; x = 0.25 * s; y = (m01 + m10) / s; z = (m02 + m20) / s; }
        else if (m11 > m22) { var s = Math.Sqrt(1 + m11 - m00 - m22) * 2; w = (m02 - m20) / s; x = (m01 + m10) / s; y = 0.25 * s; z = (m12 + m21) / s; }
        else { var s = Math.Sqrt(1 + m22 - m00 - m11) * 2; w = (m10 - m01) / s; x = (m02 + m20) / s; y = (m12 + m21) / s; z = 0.25 * s; }
        var n = Math.Sqrt(x * x + y * y + z * z + w * w);
        return (x / n, y / n, z / n, w / n);
    }
}

public readonly record struct Aabb(Vec3 Min, Vec3 Max)
{
    public Vec3 Center => (Min + Max) * 0.5;
    public Vec3 Size => Max - Min;
    public bool Intersects(Aabb o, double tol = 0) =>
        Min.X < o.Max.X - tol && o.Min.X < Max.X - tol && Min.Y < o.Max.Y - tol && o.Min.Y < Max.Y - tol && Min.Z < o.Max.Z - tol && o.Min.Z < Max.Z - tol;
    public Aabb Inflate(double d) => new(Min - new Vec3(d, d, d), Max + new Vec3(d, d, d));
    public static Aabb Of(IEnumerable<Vec3> pts)
    {
        var min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue); var max = -min;
        foreach (var p in pts) { min = Vec3.Min(min, p); max = Vec3.Max(max, p); }
        return new Aabb(min, max);
    }
    public static Aabb Union(Aabb a, Aabb b) => new(Vec3.Min(a.Min, b.Min), Vec3.Max(a.Max, b.Max));
}

/// <summary>Oriented bounding box: exact solid for box-shaped engineering objects.</summary>
public readonly record struct Obb(Vec3 Center, Vec3 HalfSize, Rot3 Rotation)
{
    public IEnumerable<Vec3> Corners()
    {
        for (var i = 0; i < 8; i++)
            yield return Center + Rotation.Apply(new Vec3((i & 1) == 0 ? -HalfSize.X : HalfSize.X, (i & 2) == 0 ? -HalfSize.Y : HalfSize.Y, (i & 4) == 0 ? -HalfSize.Z : HalfSize.Z));
    }

    public IEnumerable<(Vec3 a, Vec3 b)> Edges()
    {
        var c = Corners().ToArray();
        for (var i = 0; i < 8; i++)
            for (var bit = 1; bit < 8; bit <<= 1)
                if ((i & bit) == 0) yield return (c[i], c[i | bit]);
    }

    public Aabb Bounds() => Aabb.Of(Corners());
    public Obb Inflate(double d) => this with { HalfSize = HalfSize + new Vec3(d, d, d) };

    public Vec3 ClosestPoint(Vec3 p)
    {
        var local = Rotation.ApplyInverse(p - Center);
        var clamped = new Vec3(Math.Clamp(local.X, -HalfSize.X, HalfSize.X), Math.Clamp(local.Y, -HalfSize.Y, HalfSize.Y), Math.Clamp(local.Z, -HalfSize.Z, HalfSize.Z));
        return Center + Rotation.Apply(clamped);
    }

    public double DistanceTo(Vec3 p) => (ClosestPoint(p) - p).Length;

    /// <summary>
    /// Separating-axis test (15 axes). Boxes that only touch (penetration ≤ <paramref name="tolerance"/>) are not
    /// considered intersecting, so a device resting on a mounting plate is not a clash.
    /// </summary>
    public bool Intersects(Obb o, double tolerance = 0.01) => Penetration(o) > tolerance;

    /// <summary>Smallest overlap over all separating axes (≤ 0 means separated or touching).</summary>
    public double Penetration(Obb o)
    {
        var axes = new List<Vec3>(15);
        for (var i = 0; i < 3; i++) { axes.Add(Rotation[i]); axes.Add(o.Rotation[i]); }
        for (var i = 0; i < 3; i++)
            for (var j = 0; j < 3; j++)
            {
                var c = Rotation[i].Cross(o.Rotation[j]);
                if (c.Length > 1e-9) axes.Add(c.Normalized());
            }
        var d = o.Center - Center;
        var min = double.MaxValue;
        foreach (var axis in axes)
        {
            double ra = 0, rb = 0;
            for (var i = 0; i < 3; i++) { ra += HalfSize[i] * Math.Abs(Rotation[i].Dot(axis)); rb += o.HalfSize[i] * Math.Abs(o.Rotation[i].Dot(axis)); }
            var overlap = ra + rb - Math.Abs(d.Dot(axis));
            if (overlap < min) min = overlap;
            if (min <= 0) return min;
        }
        return min;
    }

    /// <summary>Exact minimum distance between two disjoint boxes (0 if they intersect).</summary>
    public double DistanceTo(Obb o)
    {
        if (Penetration(o) > 0) return 0;
        var best = double.MaxValue;
        foreach (var c in Corners()) best = Math.Min(best, o.DistanceTo(c));
        foreach (var c in o.Corners()) best = Math.Min(best, DistanceTo(c));
        var e2 = o.Edges().ToArray();
        foreach (var (a, b) in Edges())
            foreach (var (c, d) in e2) best = Math.Min(best, SegmentDistance(a, b, c, d));
        return best;
    }

    /// <summary>Minimum distance between segments p1q1 and p2q2 (Ericson, Real-Time Collision Detection §5.1.9).</summary>
    public static double SegmentDistance(Vec3 p1, Vec3 q1, Vec3 p2, Vec3 q2)
    {
        var d1 = q1 - p1; var d2 = q2 - p2; var r = p1 - p2;
        double a = d1.Dot(d1), e = d2.Dot(d2), f = d2.Dot(r), s, t;
        if (a <= 1e-12 && e <= 1e-12) return r.Length;
        if (a <= 1e-12) { s = 0; t = Math.Clamp(f / e, 0, 1); }
        else
        {
            var c = d1.Dot(r);
            if (e <= 1e-12) { t = 0; s = Math.Clamp(-c / a, 0, 1); }
            else
            {
                var b = d1.Dot(d2); var denom = a * e - b * b;
                s = denom > 1e-12 ? Math.Clamp((b * f - c * e) / denom, 0, 1) : 0;
                t = (b * s + f) / e;
                if (t < 0) { t = 0; s = Math.Clamp(-c / a, 0, 1); }
                else if (t > 1) { t = 1; s = Math.Clamp((b - c) / a, 0, 1); }
            }
        }
        return ((p1 + d1 * s) - (p2 + d2 * t)).Length;
    }
}

/// <summary>Indexed triangle mesh with per-vertex normals (local coordinates, mm).</summary>
public sealed class Mesh
{
    public List<Vec3> Positions { get; } = new();
    public List<Vec3> Normals { get; } = new();
    public List<int> Indices { get; } = new();
    public int TriangleCount => Indices.Count / 3;

    public void AddQuad(Vec3 a, Vec3 b, Vec3 c, Vec3 d)
    {
        var n = (b - a).Cross(c - a).Normalized();
        var i = Positions.Count;
        Positions.AddRange(new[] { a, b, c, d });
        Normals.AddRange(new[] { n, n, n, n });
        Indices.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
    }

    public void Append(Mesh other, Func<Vec3, Vec3> transformPoint, Func<Vec3, Vec3> transformNormal)
    {
        var offset = Positions.Count;
        Positions.AddRange(other.Positions.Select(transformPoint));
        Normals.AddRange(other.Normals.Select(transformNormal));
        Indices.AddRange(other.Indices.Select(i => i + offset));
    }

    public IEnumerable<(Vec3 a, Vec3 b, Vec3 c)> Triangles()
    {
        for (var i = 0; i < Indices.Count; i += 3) yield return (Positions[Indices[i]], Positions[Indices[i + 1]], Positions[Indices[i + 2]]);
    }

    /// <summary>Signed volume (divergence theorem); equals the solid volume for closed, outward-oriented meshes.</summary>
    public double Volume() => Triangles().Sum(t => t.a.Dot(t.b.Cross(t.c))) / 6.0;
}

public static class MeshBuilder
{
    /// <summary>Axis-aligned box centred on the origin, outward normals, counter-clockwise faces.</summary>
    public static Mesh Box(Vec3 size)
    {
        var h = size * 0.5;
        var m = new Mesh();
        Vec3 P(double x, double y, double z) => new(x * h.X, y * h.Y, z * h.Z);
        m.AddQuad(P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1)); // bottom (−Z)
        m.AddQuad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));     // top (+Z)
        m.AddQuad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1)); // front (−Y)
        m.AddQuad(P(1, 1, -1), P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1));     // back (+Y)
        m.AddQuad(P(-1, 1, -1), P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1)); // left (−X)
        m.AddQuad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1));     // right (+X)
        return m;
    }

    /// <summary>Open-top U-shaped cable tray along local X: base plus two side walls.</summary>
    public static Mesh Tray(double length, double width, double height, double wall)
    {
        var m = new Mesh();
        void Add(Mesh part, Vec3 offset) => m.Append(part, p => p + offset, n => n);
        Add(Box(new Vec3(length, width, wall)), new Vec3(0, 0, -height / 2 + wall / 2));
        Add(Box(new Vec3(length, wall, height)), new Vec3(0, -width / 2 + wall / 2, 0));
        Add(Box(new Vec3(length, wall, height)), new Vec3(0, width / 2 - wall / 2, 0));
        return m;
    }

    /// <summary>Closed cylinder along local X (cables, conduits).</summary>
    public static Mesh Cylinder(double length, double diameter, int segments = 16)
    {
        var m = new Mesh();
        var r = diameter / 2; var h = length / 2;
        for (var i = 0; i < segments; i++)
        {
            double a0 = 2 * Math.PI * i / segments, a1 = 2 * Math.PI * (i + 1) / segments;
            var p0 = new Vec3(0, Math.Cos(a0) * r, Math.Sin(a0) * r); var p1 = new Vec3(0, Math.Cos(a1) * r, Math.Sin(a1) * r);
            var idx = m.Positions.Count;
            var n0 = new Vec3(0, Math.Cos(a0), Math.Sin(a0)); var n1 = new Vec3(0, Math.Cos(a1), Math.Sin(a1));
            m.Positions.AddRange(new[] { p0 + new Vec3(-h, 0, 0), p1 + new Vec3(-h, 0, 0), p1 + new Vec3(h, 0, 0), p0 + new Vec3(h, 0, 0) });
            m.Normals.AddRange(new[] { n0, n1, n1, n0 });
            m.Indices.AddRange(new[] { idx, idx + 1, idx + 2, idx, idx + 2, idx + 3 });
            // caps
            var c = m.Positions.Count;
            m.Positions.AddRange(new[] { new Vec3(-h, 0, 0), p1 + new Vec3(-h, 0, 0), p0 + new Vec3(-h, 0, 0), new Vec3(h, 0, 0), p0 + new Vec3(h, 0, 0), p1 + new Vec3(h, 0, 0) });
            m.Normals.AddRange(new[] { -Vec3.UnitX, -Vec3.UnitX, -Vec3.UnitX, Vec3.UnitX, Vec3.UnitX, Vec3.UnitX });
            m.Indices.AddRange(new[] { c, c + 1, c + 2, c + 3, c + 4, c + 5 });
        }
        return m;
    }
}
