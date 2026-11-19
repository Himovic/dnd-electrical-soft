namespace DND.Electrical.CAD.Model;

public enum LineType
{
    ByLayer = 0,
    Continuous,
    Dashed,
    DashDot,
    Dotted,
    Hidden,
}

public enum TextHAlign { Left, Center, Right }

public enum TextVAlign { Baseline, Bottom, Middle, Top }

/// <summary>Kinds of characteristic points used for object snapping.</summary>
public enum SnapKind
{
    None = 0,
    Grid,
    Nearest,
    Center,
    Midpoint,
    Endpoint,
    Pin,
}

public readonly record struct SnapPoint(Core.Geometry.Point2 Point, SnapKind Kind, object? Tag = null);
