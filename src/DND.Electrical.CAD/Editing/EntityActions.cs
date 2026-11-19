using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;

namespace DND.Electrical.CAD.Editing;

public sealed class AddEntitiesAction : IUndoableAction
{
    private readonly CadDrawing _drawing;
    private readonly List<CadEntity> _entities;

    public AddEntitiesAction(CadDrawing drawing, IEnumerable<CadEntity> entities, string? description = null)
    {
        _drawing = drawing;
        _entities = entities.ToList();
        Description = description ?? (_entities.Count == 1 ? $"Add {_entities[0].TypeName}" : $"Add {_entities.Count} objects");
    }

    public string Description { get; }
    public void Redo() => _drawing.AddRange(_entities);

    public void Undo()
    {
        for (int i = _entities.Count - 1; i >= 0; i--) _drawing.Remove(_entities[i]);
    }
}

public sealed class RemoveEntitiesAction : IUndoableAction
{
    private readonly CadDrawing _drawing;
    private readonly List<CadEntity> _entities;
    private readonly List<(int index, CadEntity entity)> _removed = new();

    public RemoveEntitiesAction(CadDrawing drawing, IEnumerable<CadEntity> entities, string? description = null)
    {
        _drawing = drawing;
        _entities = entities.ToList();
        Description = description ?? (_entities.Count == 1 ? $"Delete {_entities[0].TypeName}" : $"Delete {_entities.Count} objects");
    }

    public string Description { get; }

    public void Redo()
    {
        _removed.Clear();
        foreach (var e in _entities)
        {
            var idx = _drawing.Remove(e);
            if (idx >= 0) _removed.Add((idx, e));
        }
    }

    public void Undo()
    {
        for (int i = _removed.Count - 1; i >= 0; i--) _drawing.Insert(_removed[i].index, _removed[i].entity);
    }
}

/// <summary>
/// Generic modification: captures snapshots of entities before and after an in-place change
/// and swaps them on undo/redo. Works for any entity type, including module-defined ones.
/// </summary>
public sealed class ModifyEntitiesAction : IUndoableAction
{
    private readonly CadDrawing _drawing;
    private readonly List<CadEntity> _before;
    private List<CadEntity> _after = new();

    private ModifyEntitiesAction(CadDrawing drawing, List<CadEntity> before, string description)
    {
        _drawing = drawing;
        _before = before;
        Description = description;
    }

    public string Description { get; }

    /// <summary>Snapshots <paramref name="entities"/>, applies <paramref name="change"/>, snapshots again.</summary>
    public static ModifyEntitiesAction Apply(CadDrawing drawing, IEnumerable<CadEntity> entities, string description, Action<IReadOnlyList<CadEntity>> change)
    {
        var live = entities.Select(e => drawing.Find(e.Id) ?? e).ToList();
        var action = new ModifyEntitiesAction(drawing, live.Select(e => e.Clone(keepId: true)).ToList(), description);
        change(live);
        drawing.NotifyModified(live);
        action._after = live.Select(e => e.Clone(keepId: true)).ToList();
        return action;
    }

    public void Redo()
    {
        foreach (var e in _after) _drawing.Replace(e.Clone(keepId: true));
    }

    public void Undo()
    {
        foreach (var e in _before) _drawing.Replace(e.Clone(keepId: true));
    }
}
