using DND.Electrical.CAD.Editing;
using DND.Electrical.CAD.Entities;
using DND.Electrical.Core.Geometry;

namespace DND.Electrical.CAD.Tools;

/// <summary>
/// Default tool: click to select, Shift/Ctrl to add/toggle, drag on empty space for window
/// (left→right, fully inside) or crossing (right→left) selection, drag a selected object to move it.
/// </summary>
public sealed class SelectTool : CadTool
{
    private const double DragThresholdPixels = 4;
    private enum State { Idle, PendingClick, Window, Moving }

    private State _state;
    private Point2 _downScreen;
    private Point2 _downWorld;
    private Point2 _currentScreen;
    private Point2 _currentWorld;
    private List<CadEntity> _moving = new();

    public override string Id => "select";
    public override string DisplayName => "Select";
    public override string Prompt => _state switch
    {
        State.Window => "Release to select (left→right: inside, right→left: crossing)",
        State.Moving => "Release to place the selection",
        _ => "Select objects (click, window or crossing). Drag a selected object to move it.",
    };

    public override bool IsBusy => _state is State.Moving or State.Window;

    public override (Rect2 rect, bool crossing)? SelectionWindow =>
        _state == State.Window ? (Rect2.FromPoints(_downWorld, Editor.Viewport.ToWorld(_currentScreen)), _currentScreen.X < _downScreen.X) : null;

    public override void OnPointerDown(ToolInput input)
    {
        if (input.Button != PointerButton.Left) return;
        _downScreen = _currentScreen = input.Screen;
        _downWorld = input.RawWorld;
        var tol = Editor.Viewport.ToWorldDistance(5);
        var hit = Editor.Drawing.HitTest(input.RawWorld, tol);

        if (hit is not null && input.ClickCount >= 2)
        {
            Editor.Selection.Set(new[] { hit.Id });
            Editor.RaiseEntityActivated(hit);
            _state = State.Idle;
            return;
        }

        var additive = input.Modifiers.HasFlag(KeyModifiers.Shift);
        var toggle = input.Modifiers.HasFlag(KeyModifiers.Control);
        if (hit is null)
        {
            if (!additive && !toggle) Editor.Selection.Clear();
            _state = State.Window;
            return;
        }

        if (toggle) Editor.Selection.Toggle(hit.Id);
        else if (additive) Editor.Selection.Add(new[] { hit.Id });
        else if (!Editor.Selection.Contains(hit.Id)) Editor.Selection.Set(new[] { hit.Id });

        _state = Editor.Selection.Contains(hit.Id) ? State.PendingClick : State.Idle;
        _downWorld = input.World;
    }

    public override void OnPointerMove(ToolInput input)
    {
        _currentScreen = input.Screen;
        _currentWorld = input.World;
        if (_state == State.PendingClick && input.Screen.DistanceTo(_downScreen) > DragThresholdPixels)
        {
            _state = State.Moving;
            _moving = Editor.SelectedEntities.Where(e => !Editor.Drawing.IsLayerLocked(e.Layer)).ToList();
        }
        if (_state == State.Moving) UpdateMovePreview();
    }

    private Point2 MoveDelta()
    {
        var d = _currentWorld - _downWorld;
        if (Editor.Snap.GridSnap && Editor.Snap.GridSpacing > 0) d = d.SnapToGrid(Editor.Snap.GridSpacing);
        return d;
    }

    private void UpdateMovePreview()
    {
        PreviewEntities.Clear();
        var m = Matrix2.Translation(MoveDelta());
        foreach (var e in _moving)
        {
            var c = e.Clone();
            c.Transform(m);
            PreviewEntities.Add(c);
        }
    }

    public override void OnPointerUp(ToolInput input)
    {
        switch (_state)
        {
            case State.Window:
                var w = Rect2.FromPoints(_downWorld, input.RawWorld);
                if (input.Screen.DistanceTo(_downScreen) > DragThresholdPixels)
                    Editor.SelectWindow(w, crossing: input.Screen.X < _downScreen.X, add: input.Modifiers.HasFlag(KeyModifiers.Shift));
                break;
            case State.Moving:
                _currentWorld = input.World;
                var d = MoveDelta();
                if (d.Length > 1e-9) Editor.Transform(_moving, Matrix2.Translation(d), "Move");
                break;
        }
        _state = State.Idle;
        _moving.Clear();
        PreviewEntities.Clear();
    }

    public override void Cancel()
    {
        base.Cancel();
        _state = State.Idle;
        _moving.Clear();
    }

    public override bool OnKey(ToolKey key, KeyModifiers mods)
    {
        if (key == ToolKey.Escape && IsBusy) { Cancel(); return true; }
        if (key == ToolKey.R && !Editor.Selection.IsEmpty && mods == KeyModifiers.None) { Editor.RotateSelection(90); return true; }
        return false;
    }
}

/// <summary>Drag to pan the view.</summary>
public sealed class PanTool : CadTool
{
    private Point2? _last;
    public override string Id => "pan";
    public override string DisplayName => "Pan";
    public override string Prompt => "Drag to pan the view. Press Esc to finish.";

    public override void OnPointerDown(ToolInput input) => _last = input.Screen;

    public override void OnPointerMove(ToolInput input)
    {
        if (_last is not { } l) return;
        Editor.Viewport.Pan(input.Screen.X - l.X, input.Screen.Y - l.Y);
        _last = input.Screen;
    }

    public override void OnPointerUp(ToolInput input) => _last = null;
}
