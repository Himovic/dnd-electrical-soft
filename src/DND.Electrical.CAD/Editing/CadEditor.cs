using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;
using DND.Electrical.CAD.Serialization;
using DND.Electrical.CAD.Tools;
using DND.Electrical.Core.Geometry;

namespace DND.Electrical.CAD.Editing;

public enum PointerButton { Left, Middle, Right }

[Flags]
public enum KeyModifiers { None = 0, Shift = 1, Control = 2, Alt = 4 }

public enum ToolKey { Escape, Enter, Delete, Backspace, Space, Tab, C, R, M, X, F }

public enum AlignMode { Left, Right, Top, Bottom, CenterHorizontal, CenterVertical }

/// <summary>Allows engineering modules to take part in generic editing operations.</summary>
public interface ICadEditorExtension
{
    /// <summary>Called before entities are deleted; may add dependent actions (executed in the same undo step).</summary>
    void OnDeleting(CadEditor editor, IReadOnlyList<CadEntity> entities, IList<IUndoableAction> additionalActions) { }

    /// <summary>Called after entities were transformed (move/rotate/mirror) inside an undo group.</summary>
    void OnTransformed(CadEditor editor, IReadOnlyList<CadEntity> entities, Matrix2 transform) { }

    /// <summary>
    /// Called after pasted/duplicated entities were added, inside an undo group.
    /// <paramref name="idMap"/> maps the original entity ids to the ids of the pasted copies.
    /// </summary>
    void OnPasted(CadEditor editor, IReadOnlyList<CadEntity> entities, IReadOnlyDictionary<Guid, Guid> idMap) { }

    /// <summary>Extra snap filtering (e.g. wire tool snapping to pins only).</summary>
    bool AllowSnap(CadEditor editor, CadEntity entity, SnapPoint point) => true;
}

/// <summary>
/// Editing session for one drawing: viewport, snapping, selection, active tool and undo.
/// UI-framework independent: hosts forward pointer/keyboard input and render <see cref="Drawing"/>
/// plus <see cref="ActiveTool"/> previews.
/// </summary>
public class CadEditor
{
    private static string? _clipboard;
    private CadTool _activeTool;

    public CadEditor(CadDrawing drawing, UndoManager? undo = null)
    {
        Drawing = drawing;
        Undo = undo ?? new UndoManager();
        _activeTool = new SelectTool();
        _activeTool.Attach(this);
        Selection.Changed += (_, _) => RequestRedraw();
        Drawing.Changed += (_, _) => RequestRedraw();
        Viewport.Changed += (_, _) => RequestRedraw();
    }

    public CadDrawing Drawing { get; }
    public UndoManager Undo { get; }
    public Viewport Viewport { get; } = new();
    public SnapSettings Snap { get; } = new();
    public SnapEngine SnapEngine { get; } = new();
    public SelectionSet Selection { get; } = new();
    public List<ICadEditorExtension> Extensions { get; } = new();

    public string ActiveLayer { get; set; } = StandardLayers.Annotations;

    /// <summary>
    /// Read-only sessions (e.g. no valid licence for the discipline) allow viewing, selecting, zooming and copying,
    /// but every modifying operation is refused at engine level.
    /// </summary>
    public bool IsReadOnly { get; set; }

    public string ReadOnlyReason { get; set; } = "This drawing is open read-only.";

    /// <summary>Returns true (and reports the reason) when a modification must be refused.</summary>
    protected bool RefuseIfReadOnly()
    {
        if (!IsReadOnly) return false;
        ShowStatus(ReadOnlyReason);
        return true;
    }
    public CadColor? ActiveColor { get; set; }
    public double TextHeight { get; set; } = 3.5;

    public Point2 CursorWorld { get; private set; }
    public SnapResult LastSnap { get; private set; }

    /// <summary>Optional UI callback to ask the user for text (prompt) — returns null when cancelled.</summary>
    public Func<string, string?>? TextInputProvider { get; set; }

    public CadTool ActiveTool => _activeTool;
    public string Prompt => _activeTool.Prompt;

    public event EventHandler? RedrawRequested;
    public event EventHandler? ToolChanged;
    public event EventHandler? CursorMoved;
    public event EventHandler<CadEntity>? EntityActivated;
    public event EventHandler<string>? StatusMessage;

    public void RequestRedraw() => RedrawRequested?.Invoke(this, EventArgs.Empty);
    public void ShowStatus(string message) => StatusMessage?.Invoke(this, message);
    internal void RaiseEntityActivated(CadEntity e) => EntityActivated?.Invoke(this, e);

    public IReadOnlyList<CadEntity> SelectedEntities =>
        Selection.Ids.Select(id => Drawing.Find(id)).OfType<CadEntity>().ToList();

    public void SetTool(CadTool tool)
    {
        if (IsReadOnly && tool is not (SelectTool or PanTool)) { ShowStatus(ReadOnlyReason); tool = new SelectTool(); }
        _activeTool.Deactivate();
        _activeTool = tool;
        tool.Attach(this);
        tool.Activate();
        ToolChanged?.Invoke(this, EventArgs.Empty);
        RequestRedraw();
    }

    public void CancelTool()
    {
        if (_activeTool is SelectTool st) { st.Cancel(); Selection.Clear(); return; }
        SetTool(new SelectTool());
    }

    // ---------------------------------------------------------------- input routing

    public SnapResult ComputeSnap(Point2 screen, Point2? orthoFrom)
    {
        var raw = Viewport.ToWorld(screen);
        var tool = _activeTool;
        return SnapEngine.Snap(raw, Drawing, Viewport, Snap, orthoFrom,
            e => tool.AllowSnapTo(e),
            sp => tool.AllowSnapPoint(sp));
    }

    private ToolInput MakeInput(Point2 screen, PointerButton button, KeyModifiers mods, int clicks)
    {
        var snap = ComputeSnap(screen, mods.HasFlag(KeyModifiers.Shift) ? null : _activeTool.OrthoReference);
        if (mods.HasFlag(KeyModifiers.Alt)) snap = new SnapResult(Viewport.ToWorld(screen), SnapKind.None);
        LastSnap = snap;
        CursorWorld = snap.Point;
        return new ToolInput(screen, Viewport.ToWorld(screen), snap, button, mods, clicks);
    }

    public void PointerDown(Point2 screen, PointerButton button, KeyModifiers mods = KeyModifiers.None, int clickCount = 1)
    {
        var input = MakeInput(screen, button, mods, clickCount);
        if (button == PointerButton.Right && _activeTool is not SelectTool && !_activeTool.HandlesRightClick)
        {
            if (!_activeTool.Finish()) CancelTool();
            RequestRedraw();
            return;
        }
        _activeTool.OnPointerDown(input);
        RequestRedraw();
    }

    public void PointerMove(Point2 screen, KeyModifiers mods = KeyModifiers.None)
    {
        var input = MakeInput(screen, PointerButton.Left, mods, 0);
        _activeTool.OnPointerMove(input);
        CursorMoved?.Invoke(this, EventArgs.Empty);
        RequestRedraw();
    }

    public void PointerUp(Point2 screen, PointerButton button, KeyModifiers mods = KeyModifiers.None)
    {
        var input = MakeInput(screen, button, mods, 1);
        _activeTool.OnPointerUp(input);
        RequestRedraw();
    }

    public void Wheel(Point2 screen, double delta)
    {
        Viewport.ZoomAt(screen, delta > 0 ? 1.2 : 1 / 1.2);
    }

    public bool KeyDown(ToolKey key, KeyModifiers mods = KeyModifiers.None)
    {
        if (_activeTool.OnKey(key, mods)) { RequestRedraw(); return true; }
        switch (key)
        {
            case ToolKey.Escape:
                CancelTool();
                return true;
            case ToolKey.Delete:
                DeleteSelection();
                return true;
            case ToolKey.F:
                ZoomExtents();
                return true;
        }
        return false;
    }

    /// <summary>Feeds a coordinate typed in the command line to the active tool.</summary>
    public void SubmitPoint(Point2 world)
    {
        CursorWorld = world;
        LastSnap = new SnapResult(world, SnapKind.None);
        _activeTool.SubmitPoint(world);
        RequestRedraw();
    }

    public bool SubmitText(string text)
    {
        var handled = _activeTool.SubmitText(text);
        RequestRedraw();
        return handled;
    }

    // ---------------------------------------------------------------- entity creation

    /// <summary>Adds an entity on the active layer (unless already set) as one undoable step.</summary>
    public void AddEntity(CadEntity entity, string? description = null) => AddEntities(new[] { entity }, description);

    public void AddEntities(IReadOnlyList<CadEntity> entities, string? description = null)
    {
        if (RefuseIfReadOnly()) return;
        foreach (var e in entities)
        {
            if (e.Layer == StandardLayers.Default && ActiveLayer != StandardLayers.Default) e.Layer = ActiveLayer;
            e.Color ??= ActiveColor;
        }
        Undo.Execute(new AddEntitiesAction(Drawing, entities, description));
    }

    // ---------------------------------------------------------------- selection operations

    public void SelectAll() =>
        Selection.Set(Drawing.Entities.Where(e => Drawing.IsLayerVisible(e.Layer) && !Drawing.IsLayerLocked(e.Layer)).Select(e => e.Id));

    public void SelectWindow(Rect2 window, bool crossing, bool add)
    {
        var hits = Drawing.Query(window)
            .Where(e => Drawing.IsLayerVisible(e.Layer) && !Drawing.IsLayerLocked(e.Layer))
            .Where(e => crossing ? e.IntersectsRect(window) : e.IsInside(window))
            .Select(e => e.Id)
            .ToList();
        if (add) Selection.Add(hits); else Selection.Set(hits);
    }

    public void DeleteSelection() => Delete(SelectedEntities);

    public void Delete(IReadOnlyList<CadEntity> entities)
    {
        if (RefuseIfReadOnly()) return;
        if (entities.Count == 0) return;
        var extra = new List<IUndoableAction>();
        foreach (var x in Extensions) x.OnDeleting(this, entities, extra);
        var remove = new RemoveEntitiesAction(Drawing, entities);
        using (Undo.BeginGroup(remove.Description))
        {
            foreach (var a in extra) Undo.Execute(a);
            Undo.Execute(remove);
        }
        Selection.Clear();
    }

    public void TransformSelection(Matrix2 m, string description) => Transform(SelectedEntities, m, description);

    public void Transform(IReadOnlyList<CadEntity> entities, Matrix2 m, string description)
    {
        if (RefuseIfReadOnly()) return;
        var movable = entities.Where(e => !Drawing.IsLayerLocked(e.Layer)).ToList();
        if (movable.Count == 0) return;
        using (Undo.BeginGroup(description))
        {
            Undo.Push(ModifyEntitiesAction.Apply(Drawing, movable, description, list =>
            {
                foreach (var e in list) e.Transform(m);
            }));
            var live = movable.Select(e => Drawing.Find(e.Id)!).ToList();
            foreach (var x in Extensions) x.OnTransformed(this, live, m);
        }
    }

    public void MoveSelection(Point2 delta) =>
        TransformSelection(Matrix2.Translation(delta), "Move");

    public Point2 SelectionCenter()
    {
        var r = Rect2.Empty;
        foreach (var e in SelectedEntities) r = r.Union(e.GetBounds());
        return r.IsEmpty ? Point2.Zero : r.Center.SnapToGrid(Snap.GridSpacing > 0 ? Snap.GridSpacing : 1);
    }

    public void RotateSelection(double degrees) =>
        TransformSelection(Matrix2.Rotation(degrees, SelectionCenter()), $"Rotate {degrees:0}°");

    public void MirrorSelection(bool horizontalFlip = true)
    {
        var c = SelectionCenter();
        TransformSelection(horizontalFlip ? Matrix2.MirrorX(c.X) : Matrix2.MirrorY(c.Y), "Mirror");
    }

    public void AlignSelection(AlignMode mode)
    {
        var items = SelectedEntities;
        if (items.Count < 2) return;
        var bounds = items.Select(e => e.GetBounds()).ToList();
        double target = mode switch
        {
            AlignMode.Left => bounds.Min(b => b.MinX),
            AlignMode.Right => bounds.Max(b => b.MaxX),
            AlignMode.Bottom => bounds.Min(b => b.MinY),
            AlignMode.Top => bounds.Max(b => b.MaxY),
            AlignMode.CenterHorizontal => bounds[0].Center.Y,
            _ => bounds[0].Center.X,
        };
        using (Undo.BeginGroup($"Align {mode}"))
        {
            for (int i = 0; i < items.Count; i++)
            {
                var b = bounds[i];
                var d = mode switch
                {
                    AlignMode.Left => new Point2(target - b.MinX, 0),
                    AlignMode.Right => new Point2(target - b.MaxX, 0),
                    AlignMode.Bottom => new Point2(0, target - b.MinY),
                    AlignMode.Top => new Point2(0, target - b.MaxY),
                    AlignMode.CenterHorizontal => new Point2(0, target - b.Center.Y),
                    _ => new Point2(target - b.Center.X, 0),
                };
                if (d.Length > 1e-9) Transform(new[] { items[i] }, Matrix2.Translation(d), "Align");
            }
        }
    }

    public string? CopySelection()
    {
        var items = SelectedEntities;
        if (items.Count == 0) return null;
        _clipboard = CadJson.SerializeEntities(items);
        return _clipboard;
    }

    public void CutSelection()
    {
        if (CopySelection() is null) return;
        DeleteSelection();
    }

    public bool CanPaste => _clipboard is not null;

    /// <summary>Pastes clipboard content offset by <paramref name="offset"/> (defaults to two grid steps).</summary>
    public IReadOnlyList<CadEntity> Paste(string? json = null, Point2? offset = null)
    {
        if (RefuseIfReadOnly()) return Array.Empty<CadEntity>();
        json ??= _clipboard;
        if (json is null) return Array.Empty<CadEntity>();
        var originals = CadJson.DeserializeEntities(json);
        var idMap = new Dictionary<Guid, Guid>();
        var items = new List<CadEntity>(originals.Count);
        foreach (var o in originals)
        {
            var copy = o.Clone(keepId: false);
            idMap[o.Id] = copy.Id;
            items.Add(copy);
        }
        if (items.Count == 0) return items;
        var d = offset ?? new Point2(Snap.GridSpacing * 2, -Snap.GridSpacing * 2);
        foreach (var e in items) e.Transform(Matrix2.Translation(d));
        using (Undo.BeginGroup("Paste"))
        {
            Undo.Execute(new AddEntitiesAction(Drawing, items, "Paste"));
            foreach (var x in Extensions) x.OnPasted(this, items, idMap);
        }
        Selection.Set(items.Select(e => e.Id));
        return items;
    }

    public IReadOnlyList<CadEntity> DuplicateSelection()
    {
        var json = SelectedEntities.Count == 0 ? null : CadJson.SerializeEntities(SelectedEntities);
        return json is null ? Array.Empty<CadEntity>() : Paste(json);
    }

    public GroupEntity? GroupSelection(string name = "")
    {
        if (RefuseIfReadOnly()) return null;
        var items = SelectedEntities.Where(e => e.GetType() == typeof(LineEntity) || e is PolylineEntity or CircleEntity or ArcEntity or TextEntity or DimensionEntity or LeaderEntity or GroupEntity or ImageEntity).ToList();
        if (items.Count < 2) return null;
        var order = items.OrderBy(Drawing.IndexOf).ToList();
        var group = new GroupEntity { Name = name, Layer = order[0].Layer, Children = order.Select(e => e.Clone(keepId: true)).ToList() };
        using (Undo.BeginGroup("Group"))
        {
            Undo.Execute(new RemoveEntitiesAction(Drawing, order));
            Undo.Execute(new AddEntitiesAction(Drawing, new[] { group }));
        }
        Selection.Set(new[] { group.Id });
        return group;
    }

    public void UngroupSelection()
    {
        if (RefuseIfReadOnly()) return;
        var groups = SelectedEntities.OfType<GroupEntity>().ToList();
        if (groups.Count == 0) return;
        var children = new List<CadEntity>();
        using (Undo.BeginGroup("Ungroup"))
        {
            foreach (var g in groups)
            {
                var items = g.Children.Select(c => c.Clone(keepId: true)).ToList();
                Undo.Execute(new RemoveEntitiesAction(Drawing, new[] { g }));
                Undo.Execute(new AddEntitiesAction(Drawing, items));
                children.AddRange(items);
            }
        }
        Selection.Set(children.Select(c => c.Id));
    }

    public void SetLayer(IReadOnlyList<CadEntity> entities, string layer)
    {
        if (RefuseIfReadOnly()) return;
        if (entities.Count == 0) return;
        Drawing.AddLayer(Drawing.FindLayer(layer) ?? CadLayer.Create(layer, CadColor.Black));
        Undo.Push(ModifyEntitiesAction.Apply(Drawing, entities, $"Move to layer {layer}", l =>
        {
            foreach (var e in l) e.Layer = layer;
        }));
    }

    /// <summary>Applies an arbitrary property change to entities as one undoable step.</summary>
    public void ModifyEntities(IReadOnlyList<CadEntity> entities, string description, Action<CadEntity> change)
    {
        if (RefuseIfReadOnly()) return;
        if (entities.Count == 0) return;
        Undo.Push(ModifyEntitiesAction.Apply(Drawing, entities, description, l =>
        {
            foreach (var e in l) change(e);
        }));
    }

    public void ZoomExtents()
    {
        var ext = Drawing.GetExtents();
        Viewport.ZoomToFit(ext.IsEmpty ? Rect2.FromSize(0, 0, 420, 297) : ext);
    }

    public bool UndoLast()
    {
        if (RefuseIfReadOnly()) return false;
        if (_activeTool.IsBusy) _activeTool.Cancel();
        var ok = Undo.Undo();
        PruneSelection();
        return ok;
    }

    public bool RedoLast()
    {
        if (RefuseIfReadOnly()) return false;
        var ok = Undo.Redo();
        PruneSelection();
        return ok;
    }

    private void PruneSelection() => Selection.Set(Selection.Ids.Where(id => Drawing.Find(id) is not null).ToList());
}

public readonly record struct ToolInput(
    Point2 Screen,
    Point2 RawWorld,
    SnapResult Snap,
    PointerButton Button,
    KeyModifiers Modifiers,
    int ClickCount)
{
    public Point2 World => Snap.Point;
}
