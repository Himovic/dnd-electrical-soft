using DND.Electrical.CAD.Editing;
using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;

namespace DND.Electrical.CAD.Tools;

/// <summary>
/// Base class for interactive tools. Tools receive snapped pointer input from <see cref="CadEditor"/>
/// and expose temporary <see cref="Preview"/> entities rendered as rubber-band feedback.
/// </summary>
public abstract class CadTool
{
    private CadEditor? _editor;
    protected readonly List<CadEntity> PreviewEntities = new();

    protected CadEditor Editor => _editor ?? throw new InvalidOperationException("Tool is not attached to an editor.");

    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public virtual string Prompt => DisplayName;

    /// <summary>Entities drawn as transient feedback (not part of the drawing).</summary>
    public IReadOnlyList<CadEntity> Preview => PreviewEntities;

    /// <summary>Optional rectangle drawn as selection window (screen feedback).</summary>
    public virtual (Rect2 rect, bool crossing)? SelectionWindow => null;

    /// <summary>Reference point for the ortho constraint (previous point), if any.</summary>
    public virtual Point2? OrthoReference => null;

    /// <summary>True while an operation is in progress (points collected but not committed).</summary>
    public virtual bool IsBusy => false;

    public virtual bool HandlesRightClick => false;

    internal void Attach(CadEditor editor) => _editor = editor;

    public virtual void Activate() { }

    public virtual void Deactivate() => PreviewEntities.Clear();

    public virtual void OnPointerDown(ToolInput input) { }
    public virtual void OnPointerMove(ToolInput input) { }
    public virtual void OnPointerUp(ToolInput input) { }
    public virtual bool OnKey(ToolKey key, KeyModifiers mods) => false;

    /// <summary>Point entered numerically (command line).</summary>
    public virtual void SubmitPoint(Point2 world) =>
        OnPointerDown(new ToolInput(Editor.Viewport.ToScreen(world), world, new SnapResult(world, SnapKind.None), PointerButton.Left, KeyModifiers.None, 1));

    /// <summary>Free text entered in the command line. Returns true when consumed.</summary>
    public virtual bool SubmitText(string text) => false;

    /// <summary>Completes a multi-point operation (Enter / right click). Returns false if nothing to finish.</summary>
    public virtual bool Finish() => false;

    public virtual void Cancel() => PreviewEntities.Clear();

    public virtual bool AllowSnapTo(CadEntity entity) => true;
    public virtual bool AllowSnapPoint(SnapPoint point) => true;

    protected T Styled<T>(T entity) where T : CadEntity
    {
        entity.Layer = Editor.ActiveLayer;
        entity.Color = Editor.ActiveColor;
        return entity;
    }
}
