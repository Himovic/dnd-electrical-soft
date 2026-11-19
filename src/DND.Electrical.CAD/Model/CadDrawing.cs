using System.Text.Json.Serialization;
using DND.Electrical.CAD.Entities;
using DND.Electrical.Core.Geometry;

namespace DND.Electrical.CAD.Model;

/// <summary>
/// A 2D drawing: ordered entity list (draw order) plus layers. All mutations go through
/// methods so that the spatial index and change notifications stay consistent.
/// </summary>
public sealed class CadDrawing
{
    private readonly Dictionary<Guid, CadEntity> _byId = new();
    private SpatialIndex? _index;

    public CadDrawing()
    {
        Layers = StandardLayers.CreateDefaults().ToList();
    }

    public List<CadLayer> Layers { get; set; }

    [JsonInclude]
    [JsonPropertyName("Entities")]
    internal List<CadEntity> EntityList { get; private set; } = new();

    [JsonIgnore]
    public IReadOnlyList<CadEntity> Entities => EntityList;

    /// <summary>Incremented on every change; used by renderers and caches.</summary>
    [JsonIgnore]
    public long Revision { get; private set; }

    public event EventHandler<DrawingChangedEventArgs>? Changed;

    public CadLayer GetLayer(string name) =>
        Layers.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? Layers.FirstOrDefault(l => l.Name == StandardLayers.Default)
        ?? AddLayer(CadLayer.Create(StandardLayers.Default, CadColor.Black));

    public CadLayer? FindLayer(string name) =>
        Layers.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

    public CadLayer AddLayer(CadLayer layer)
    {
        if (FindLayer(layer.Name) is { } existing) return existing;
        Layers.Add(layer);
        Touch(DrawingChangeKind.Layers, Array.Empty<CadEntity>());
        return layer;
    }

    public bool IsLayerVisible(string name) => FindLayer(name)?.Visible ?? true;
    public bool IsLayerLocked(string name) => FindLayer(name)?.Locked ?? false;

    public CadEntity? Find(Guid id)
    {
        EnsureIndex();
        return _byId.GetValueOrDefault(id);
    }

    public T? Find<T>(Guid id) where T : CadEntity => Find(id) as T;

    public bool Contains(CadEntity e) => Find(e.Id) is not null;

    public void Add(CadEntity entity) => Insert(EntityList.Count, entity);

    public void AddRange(IEnumerable<CadEntity> entities)
    {
        var list = entities.ToList();
        EnsureIndex();
        foreach (var e in list)
        {
            if (_byId.ContainsKey(e.Id)) throw new InvalidOperationException($"Entity {e.Id} already exists in drawing.");
            EntityList.Add(e);
            _byId[e.Id] = e;
            _index!.Insert(e);
        }
        Touch(DrawingChangeKind.Added, list);
    }

    public void Insert(int index, CadEntity entity)
    {
        EnsureIndex();
        if (_byId.ContainsKey(entity.Id)) throw new InvalidOperationException($"Entity {entity.Id} already exists in drawing.");
        EntityList.Insert(Math.Clamp(index, 0, EntityList.Count), entity);
        _byId[entity.Id] = entity;
        _index!.Insert(entity);
        Touch(DrawingChangeKind.Added, new[] { entity });
    }

    /// <summary>Removes the entity and returns its former draw-order index (or -1).</summary>
    public int Remove(CadEntity entity)
    {
        EnsureIndex();
        var idx = EntityList.FindIndex(e => e.Id == entity.Id);
        if (idx < 0) return -1;
        var actual = EntityList[idx];
        EntityList.RemoveAt(idx);
        _byId.Remove(actual.Id);
        _index!.Remove(actual);
        Touch(DrawingChangeKind.Removed, new[] { actual });
        return idx;
    }

    /// <summary>Replaces the entity having the same <see cref="CadEntity.Id"/> (used by undo snapshots).</summary>
    public void Replace(CadEntity replacement)
    {
        EnsureIndex();
        var idx = EntityList.FindIndex(e => e.Id == replacement.Id);
        if (idx < 0) throw new InvalidOperationException($"Entity {replacement.Id} not found.");
        _index!.Remove(EntityList[idx]);
        EntityList[idx] = replacement;
        _byId[replacement.Id] = replacement;
        _index.Insert(replacement);
        Touch(DrawingChangeKind.Modified, new[] { replacement });
    }

    /// <summary>Must be called after an entity's geometry was mutated in place.</summary>
    public void NotifyModified(IEnumerable<CadEntity> entities)
    {
        var list = entities.ToList();
        EnsureIndex();
        foreach (var e in list) _index!.Update(e);
        Touch(DrawingChangeKind.Modified, list);
    }

    public void NotifyModified(CadEntity entity) => NotifyModified(new[] { entity });

    public int IndexOf(CadEntity e) => EntityList.FindIndex(x => x.Id == e.Id);

    public void Clear()
    {
        var old = EntityList.ToList();
        EntityList.Clear();
        _byId.Clear();
        _index?.Clear();
        Touch(DrawingChangeKind.Removed, old);
    }

    public IEnumerable<CadEntity> Query(Rect2 area)
    {
        EnsureIndex();
        return _index!.Query(area);
    }

    /// <summary>Topmost visible, unlocked entity under <paramref name="p"/> (closest wins).</summary>
    public CadEntity? HitTest(Point2 p, double tolerance, Func<CadEntity, bool>? filter = null, bool includeLocked = false)
    {
        CadEntity? best = null;
        var bestD = double.MaxValue;
        var bestOrder = -1;
        foreach (var e in Query(Rect2.FromCenter(p, tolerance, tolerance)))
        {
            if (!IsLayerVisible(e.Layer) || (!includeLocked && IsLayerLocked(e.Layer))) continue;
            if (filter is not null && !filter(e)) continue;
            if (!e.HitTest(p, tolerance)) continue;
            var d = e.DistanceTo(p);
            var order = IndexOf(e);
            if (d < bestD - 1e-9 || (Math.Abs(d - bestD) <= 1e-9 && order > bestOrder))
            {
                best = e; bestD = d; bestOrder = order;
            }
        }
        return best;
    }

    public Rect2 GetExtents(bool visibleOnly = true)
    {
        var r = Rect2.Empty;
        foreach (var e in EntityList)
            if (!visibleOnly || IsLayerVisible(e.Layer)) r = r.Union(e.GetBounds());
        return r;
    }

    private void EnsureIndex()
    {
        if (_index is not null) return;
        _index = new SpatialIndex();
        _byId.Clear();
        foreach (var e in EntityList)
        {
            _byId[e.Id] = e;
            _index.Insert(e);
        }
    }

    private void Touch(DrawingChangeKind kind, IReadOnlyList<CadEntity> entities)
    {
        Revision++;
        Changed?.Invoke(this, new DrawingChangedEventArgs(kind, entities));
    }
}

public enum DrawingChangeKind { Added, Removed, Modified, Layers }

public sealed class DrawingChangedEventArgs(DrawingChangeKind kind, IReadOnlyList<CadEntity> entities) : EventArgs
{
    public DrawingChangeKind Kind { get; } = kind;
    public IReadOnlyList<CadEntity> Entities { get; } = entities;
}
