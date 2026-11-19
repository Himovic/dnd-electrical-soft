namespace DND.Electrical.CAD.Editing;

/// <summary>Ordered set of selected entity identifiers.</summary>
public sealed class SelectionSet
{
    private readonly List<Guid> _ids = new();

    public IReadOnlyList<Guid> Ids => _ids;
    public int Count => _ids.Count;
    public bool IsEmpty => _ids.Count == 0;

    public event EventHandler? Changed;

    public bool Contains(Guid id) => _ids.Contains(id);

    public void Set(IEnumerable<Guid> ids)
    {
        var incoming = ids.ToList(); // materialise first: the input may be a query over this set
        _ids.Clear();
        foreach (var id in incoming) if (!_ids.Contains(id)) _ids.Add(id);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Add(IEnumerable<Guid> ids)
    {
        var changed = false;
        foreach (var id in ids.ToList())
            if (!_ids.Contains(id)) { _ids.Add(id); changed = true; }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Toggle(Guid id)
    {
        if (!_ids.Remove(id)) _ids.Add(id);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(Guid id)
    {
        if (_ids.Remove(id)) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (_ids.Count == 0) return;
        _ids.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
