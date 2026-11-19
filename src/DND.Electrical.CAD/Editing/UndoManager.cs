namespace DND.Electrical.CAD.Editing;

/// <summary>An action that has been (or will be) applied and can be reverted.</summary>
public interface IUndoableAction
{
    string Description { get; }
    void Redo();
    void Undo();
}

public sealed class DelegateAction(string description, Action redo, Action undo) : IUndoableAction
{
    public string Description { get; } = description;
    public void Redo() => redo();
    public void Undo() => undo();
}

public sealed class CompositeAction(string description, IReadOnlyList<IUndoableAction> actions) : IUndoableAction
{
    public string Description { get; } = description;
    public IReadOnlyList<IUndoableAction> Actions { get; } = actions;

    public void Redo()
    {
        foreach (var a in Actions) a.Redo();
    }

    public void Undo()
    {
        for (int i = Actions.Count - 1; i >= 0; i--) Actions[i].Undo();
    }
}

/// <summary>
/// Linear undo/redo history with grouping (transactions) and a save point used for dirty tracking.
/// </summary>
public sealed class UndoManager
{
    private readonly List<IUndoableAction> _undo = new();
    private readonly Stack<IUndoableAction> _redo = new();
    private readonly Stack<(string description, List<IUndoableAction> actions)> _groups = new();
    private IUndoableAction? _savePoint;
    private bool _savePointValid = true;

    public UndoManager(int maxDepth = 500) => MaxDepth = maxDepth;

    public int MaxDepth { get; }
    public bool CanUndo => _undo.Count > 0 && _groups.Count == 0;
    public bool CanRedo => _redo.Count > 0 && _groups.Count == 0;
    public string? UndoDescription => _undo.Count > 0 ? _undo[^1].Description : null;
    public string? RedoDescription => _redo.Count > 0 ? _redo.Peek().Description : null;
    public IReadOnlyList<string> History => _undo.Select(a => a.Description).ToList();

    /// <summary>True when the state differs from the last <see cref="MarkSaved"/> call.</summary>
    public bool IsDirty => !_savePointValid || (_undo.Count > 0 ? _undo[^1] : null) != _savePoint;

    public event EventHandler? Changed;

    /// <summary>Applies the action and records it.</summary>
    public void Execute(IUndoableAction action)
    {
        action.Redo();
        Push(action);
    }

    /// <summary>Records an action that has already been applied.</summary>
    public void Push(IUndoableAction action)
    {
        if (_groups.Count > 0)
        {
            _groups.Peek().actions.Add(action);
            return;
        }
        _undo.Add(action);
        if (_undo.Count > MaxDepth)
        {
            if (_undo[0] == _savePoint) _savePointValid = false;
            _undo.RemoveAt(0);
        }
        if (_redo.Contains(_savePoint!)) _savePointValid = false;
        _redo.Clear();
        OnChanged();
    }

    public IDisposable BeginGroup(string description)
    {
        _groups.Push((description, new List<IUndoableAction>()));
        return new GroupScope(this);
    }

    private void EndGroup()
    {
        var (description, actions) = _groups.Pop();
        if (actions.Count == 0) return;
        Push(actions.Count == 1 ? actions[0] : new CompositeAction(description, actions));
    }

    public bool Undo()
    {
        if (!CanUndo) return false;
        var a = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        a.Undo();
        _redo.Push(a);
        OnChanged();
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo) return false;
        var a = _redo.Pop();
        a.Redo();
        _undo.Add(a);
        OnChanged();
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _savePoint = null;
        _savePointValid = true;
        OnChanged();
    }

    public void MarkSaved()
    {
        _savePoint = _undo.Count > 0 ? _undo[^1] : null;
        _savePointValid = true;
        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private sealed class GroupScope(UndoManager owner) : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            owner.EndGroup();
        }
    }
}
