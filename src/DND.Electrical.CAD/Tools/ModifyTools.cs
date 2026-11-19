using System.Globalization;
using DND.Electrical.CAD.Editing;
using DND.Electrical.CAD.Entities;
using DND.Electrical.Core.Geometry;

namespace DND.Electrical.CAD.Tools;

/// <summary>Move (or copy) the current selection from a base point to a destination point.</summary>
public sealed class MoveTool(bool copy = false) : PointSequenceTool
{
    public override string Id => copy ? "copy" : "move";
    public override string DisplayName => copy ? "Copy" : "Move";
    public override string Prompt => Editor.Selection.IsEmpty ? $"{DisplayName}: select objects first"
        : Points.Count == 0 ? $"{DisplayName}: base point" : $"{DisplayName}: destination point";

    protected override void OnPointAdded()
    {
        if (Editor.Selection.IsEmpty) { Reset(); return; }
        if (Points.Count < 2) return;
        var d = Points[1] - Points[0];
        if (copy) Editor.Paste(Serialization.CadJson.SerializeEntities(Editor.SelectedEntities), d);
        else Editor.MoveSelection(d);
        Reset();
    }

    protected override void UpdatePreview()
    {
        PreviewEntities.Clear();
        if (Points.Count != 1) return;
        var m = Matrix2.Translation(Cursor - Points[0]);
        foreach (var e in Editor.SelectedEntities)
        {
            var c = e.Clone();
            c.Transform(m);
            PreviewEntities.Add(c);
        }
        PreviewEntities.Add(new LineEntity(Points[0], Cursor));
    }
}

/// <summary>Rotate the selection about a base point; angle from the cursor (snapped to 15°) or typed.</summary>
public sealed class RotateTool : PointSequenceTool
{
    public override string Id => "rotate";
    public override string DisplayName => "Rotate";
    public override string Prompt => Points.Count == 0 ? "Rotate: base point" : "Rotate: angle point or type angle in degrees";
    public override Point2? OrthoReference => null;

    private double AngleTo(Point2 p)
    {
        var a = (p - Points[0]).AngleDegrees;
        return Math.Round(a / 15) * 15;
    }

    protected override void OnPointAdded()
    {
        if (Editor.Selection.IsEmpty) { Reset(); return; }
        if (Points.Count < 2) return;
        Apply(AngleTo(Points[1]));
    }

    private void Apply(double angle)
    {
        if (Math.Abs(angle) > 1e-9) Editor.TransformSelection(Matrix2.Rotation(angle, Points[0]), $"Rotate {angle:0.#}°");
        Reset();
    }

    public override bool SubmitText(string text)
    {
        if (Points.Count == 1 && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var a))
        {
            Apply(a);
            return true;
        }
        return false;
    }

    protected override void UpdatePreview()
    {
        PreviewEntities.Clear();
        if (Points.Count != 1) return;
        var m = Matrix2.Rotation(AngleTo(Cursor), Points[0]);
        foreach (var e in Editor.SelectedEntities)
        {
            var c = e.Clone();
            c.Transform(m);
            PreviewEntities.Add(c);
        }
    }
}

/// <summary>Mirror the selection about an axis defined by two points.</summary>
public sealed class MirrorTool : PointSequenceTool
{
    public override string Id => "mirror";
    public override string DisplayName => "Mirror";
    public override string Prompt => Points.Count == 0 ? "Mirror: first point of axis" : "Mirror: second point of axis";

    public static Matrix2 MirrorAbout(Point2 a, Point2 b)
    {
        var angle = (b - a).AngleDegrees;
        return Matrix2.Translation(-a) * Matrix2.Rotation(-angle) * Matrix2.MirrorY() * Matrix2.Rotation(angle) * Matrix2.Translation(a);
    }

    protected override void OnPointAdded()
    {
        if (Editor.Selection.IsEmpty) { Reset(); return; }
        if (Points.Count < 2) return;
        if (!Points[0].AlmostEquals(Points[1], 1e-6))
            Editor.TransformSelection(MirrorAbout(Points[0], Points[1]), "Mirror");
        Reset();
    }

    protected override void UpdatePreview()
    {
        PreviewEntities.Clear();
        if (Points.Count != 1 || Points[0].AlmostEquals(Cursor, 1e-6)) return;
        var m = MirrorAbout(Points[0], Cursor);
        foreach (var e in Editor.SelectedEntities)
        {
            var c = e.Clone();
            c.Transform(m);
            PreviewEntities.Add(c);
        }
        PreviewEntities.Add(new LineEntity(Points[0], Cursor));
    }
}
