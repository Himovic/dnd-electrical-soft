using System.Globalization;
using DND.Electrical.CAD.Editing;
using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;

namespace DND.Electrical.CAD.Tools;

/// <summary>Base for tools that collect a sequence of points.</summary>
public abstract class PointSequenceTool : CadTool
{
    protected readonly List<Point2> Points = new();
    protected Point2 Cursor;

    public override bool IsBusy => Points.Count > 0;
    public override Point2? OrthoReference => Points.Count > 0 ? Points[^1] : null;

    public override void OnPointerDown(ToolInput input)
    {
        if (input.Button != PointerButton.Left) return;
        Cursor = input.World;
        if (input.ClickCount >= 2 && Points.Count > 0 && Points[^1].AlmostEquals(input.World, 1e-6))
        {
            Finish();
            return;
        }
        Points.Add(input.World);
        OnPointAdded();
        UpdatePreview();
    }

    public override void OnPointerMove(ToolInput input)
    {
        Cursor = input.World;
        UpdatePreview();
    }

    public override bool OnKey(ToolKey key, KeyModifiers mods)
    {
        switch (key)
        {
            case ToolKey.Enter:
            case ToolKey.Space:
                return Finish() || IsBusy;
            case ToolKey.Escape when IsBusy:
                Cancel();
                return true;
            case ToolKey.Backspace when Points.Count > 0:
                Points.RemoveAt(Points.Count - 1);
                UpdatePreview();
                return true;
        }
        return false;
    }

    public override void Cancel()
    {
        base.Cancel();
        Points.Clear();
    }

    protected abstract void OnPointAdded();
    protected abstract void UpdatePreview();

    protected void Reset()
    {
        Points.Clear();
        PreviewEntities.Clear();
    }
}

public sealed class LineTool : PointSequenceTool
{
    public override string Id => "line";
    public override string DisplayName => "Line";
    public override string Prompt => Points.Count == 0 ? "Line: specify first point" : "Line: specify next point (Enter/right-click to finish)";

    protected override void OnPointAdded()
    {
        if (Points.Count < 2) return;
        var a = Points[^2]; var b = Points[^1];
        if (a.AlmostEquals(b, 1e-6)) { Points.RemoveAt(Points.Count - 1); return; }
        Editor.AddEntity(Styled(new LineEntity(a, b)));
        Points.RemoveRange(0, Points.Count - 1); // chain from the last point
    }

    protected override void UpdatePreview()
    {
        PreviewEntities.Clear();
        if (Points.Count == 1) PreviewEntities.Add(new LineEntity(Points[0], Cursor));
    }

    public override bool Finish()
    {
        var busy = IsBusy;
        Reset();
        return busy;
    }
}

public sealed class PolylineTool : PointSequenceTool
{
    public override string Id => "polyline";
    public override string DisplayName => "Polyline";
    public override string Prompt => Points.Count == 0 ? "Polyline: specify start point" : "Polyline: next point (Enter to finish, C to close)";

    protected override void OnPointAdded()
    {
        if (Points.Count >= 2 && Points[^1].AlmostEquals(Points[^2], 1e-6)) Points.RemoveAt(Points.Count - 1);
    }

    protected override void UpdatePreview()
    {
        PreviewEntities.Clear();
        if (Points.Count > 0) PreviewEntities.Add(new PolylineEntity(Points.Append(Cursor)));
    }

    public override bool OnKey(ToolKey key, KeyModifiers mods)
    {
        if (key == ToolKey.C && Points.Count >= 3) { Commit(closed: true); return true; }
        return base.OnKey(key, mods);
    }

    public override bool Finish()
    {
        if (Points.Count < 2) { var busy = IsBusy; Reset(); return busy; }
        Commit(closed: false);
        return true;
    }

    private void Commit(bool closed)
    {
        Editor.AddEntity(Styled(new PolylineEntity(Points, closed)));
        Reset();
    }
}

public sealed class RectangleTool : PointSequenceTool
{
    public override string Id => "rectangle";
    public override string DisplayName => "Rectangle";
    public override string Prompt => Points.Count == 0 ? "Rectangle: first corner" : "Rectangle: opposite corner";
    public override Point2? OrthoReference => null;

    protected override void OnPointAdded()
    {
        if (Points.Count < 2) return;
        if (Math.Abs(Points[0].X - Points[1].X) > 1e-9 && Math.Abs(Points[0].Y - Points[1].Y) > 1e-9)
            Editor.AddEntity(Styled(PolylineEntity.Rectangle(Points[0], Points[1])));
        Reset();
    }

    protected override void UpdatePreview()
    {
        PreviewEntities.Clear();
        if (Points.Count == 1) PreviewEntities.Add(PolylineEntity.Rectangle(Points[0], Cursor));
    }
}

public sealed class CircleTool : PointSequenceTool
{
    public override string Id => "circle";
    public override string DisplayName => "Circle";
    public override string Prompt => Points.Count == 0 ? "Circle: center point" : "Circle: point on circle or type radius";
    public override Point2? OrthoReference => null;

    protected override void OnPointAdded()
    {
        if (Points.Count < 2) return;
        var r = Points[0].DistanceTo(Points[1]);
        if (r > 1e-6) Editor.AddEntity(Styled(new CircleEntity(Points[0], r)));
        Reset();
    }

    protected override void UpdatePreview()
    {
        PreviewEntities.Clear();
        if (Points.Count == 1) PreviewEntities.Add(new CircleEntity(Points[0], Math.Max(0.01, Points[0].DistanceTo(Cursor))));
    }

    public override bool SubmitText(string text)
    {
        if (Points.Count == 1 && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) && r > 0)
        {
            Editor.AddEntity(Styled(new CircleEntity(Points[0], r)));
            Reset();
            return true;
        }
        return false;
    }
}

/// <summary>Arc through start point, a point on the arc, and end point.</summary>
public sealed class ArcTool : PointSequenceTool
{
    public override string Id => "arc";
    public override string DisplayName => "Arc";
    public override string Prompt => Points.Count switch
    {
        0 => "Arc: start point",
        1 => "Arc: second point on arc",
        _ => "Arc: end point",
    };
    public override Point2? OrthoReference => null;

    protected override void OnPointAdded()
    {
        if (Points.Count < 3) return;
        var arc = ArcEntity.FromThreePoints(Points[0], Points[1], Points[2]);
        if (arc is not null) Editor.AddEntity(Styled(arc));
        Reset();
    }

    protected override void UpdatePreview()
    {
        PreviewEntities.Clear();
        if (Points.Count == 1) PreviewEntities.Add(new LineEntity(Points[0], Cursor));
        if (Points.Count == 2 && ArcEntity.FromThreePoints(Points[0], Points[1], Cursor) is { } a) PreviewEntities.Add(a);
    }
}

public sealed class TextTool : CadTool
{
    private Point2? _position;
    private Point2 _cursor;

    public override string Id => "text";
    public override string DisplayName => "Text";
    public override string Prompt => _position is null ? "Text: specify insertion point" : "Text: type the text and press Enter";
    public override bool IsBusy => _position is not null;

    public override void OnPointerMove(ToolInput input)
    {
        _cursor = input.World;
        PreviewEntities.Clear();
        if (_position is null) PreviewEntities.Add(new TextEntity(_cursor, "Text", Editor.TextHeight));
    }

    public override void OnPointerDown(ToolInput input)
    {
        if (input.Button != PointerButton.Left) return;
        _position = input.World;
        PreviewEntities.Clear();
        if (Editor.TextInputProvider is { } ask)
        {
            var text = ask("Text");
            if (!string.IsNullOrEmpty(text)) Commit(text);
            _position = null;
        }
    }

    public override bool SubmitText(string text)
    {
        if (_position is null || string.IsNullOrEmpty(text)) return false;
        Commit(text);
        _position = null;
        return true;
    }

    private void Commit(string text) =>
        Editor.AddEntity(Styled(new TextEntity(_position!.Value, text, Editor.TextHeight)));

    public override void Cancel()
    {
        base.Cancel();
        _position = null;
    }
}

public sealed class DimensionTool : PointSequenceTool
{
    public override string Id => "dimension";
    public override string DisplayName => "Dimension";
    public override string Prompt => Points.Count switch
    {
        0 => "Dimension: first extension point",
        1 => "Dimension: second extension point",
        _ => "Dimension: dimension line location",
    };

    protected override void OnPointAdded()
    {
        if (Points.Count == 2 && Points[0].AlmostEquals(Points[1], 1e-6)) Points.RemoveAt(1);
        if (Points.Count < 3) return;
        var dim = MakeDim(Points[2]);
        dim.Layer = StandardLayers.Dimensions;
        Editor.AddEntity(dim);
        Reset();
    }

    private DimensionEntity MakeDim(Point2 location)
    {
        var dim = new DimensionEntity { Start = Points[0], End = Points[1], TextHeight = Editor.TextHeight };
        dim.Offset = (location - Points[0]).Dot(dim.Normal);
        return dim;
    }

    public override Point2? OrthoReference => Points.Count == 1 ? Points[0] : null;

    protected override void UpdatePreview()
    {
        PreviewEntities.Clear();
        if (Points.Count == 1) PreviewEntities.Add(new LineEntity(Points[0], Cursor));
        if (Points.Count == 2) PreviewEntities.Add(MakeDim(Cursor));
    }
}

public sealed class LeaderTool : PointSequenceTool
{
    private bool _awaitingText;

    public override string Id => "leader";
    public override string DisplayName => "Callout";
    public override string Prompt => _awaitingText ? "Callout: type the label text and press Enter"
        : Points.Count == 0 ? "Callout: arrow point" : "Callout: next point (Enter to finish)";

    protected override void OnPointAdded() { }

    protected override void UpdatePreview()
    {
        PreviewEntities.Clear();
        if (Points.Count > 0 && !_awaitingText)
            PreviewEntities.Add(new LeaderEntity { Points = Points.Append(Cursor).ToList(), Text = "…", TextHeight = Editor.TextHeight });
    }

    public override bool Finish()
    {
        if (Points.Count < 2) { var b = IsBusy; Reset(); return b; }
        if (Editor.TextInputProvider is { } ask)
        {
            var text = ask("Callout text");
            if (!string.IsNullOrEmpty(text)) Commit(text);
            else Reset();
            return true;
        }
        _awaitingText = true;
        return true;
    }

    public override bool SubmitText(string text)
    {
        if (!_awaitingText) return false;
        Commit(text);
        return true;
    }

    private void Commit(string text)
    {
        var leader = new LeaderEntity { Points = Points.ToList(), Text = text, TextHeight = Editor.TextHeight, Layer = StandardLayers.Annotations };
        Editor.AddEntity(leader);
        _awaitingText = false;
        Reset();
    }

    public override void Cancel()
    {
        base.Cancel();
        _awaitingText = false;
    }
}
