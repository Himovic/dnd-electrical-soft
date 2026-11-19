using DND.Electrical.CAD.Entities;
using DND.Electrical.Core.Geometry;

namespace DND.Electrical.CAD.Model;

/// <summary>
/// Uniform-grid spatial hash used to accelerate hit testing and viewport culling.
/// Each entity is registered in every cell its bounding box overlaps. Updates are incremental.
/// </summary>
public sealed class SpatialIndex
{
    private readonly double _cell;
    private readonly Dictionary<(int, int), List<CadEntity>> _cells = new();
    private readonly Dictionary<CadEntity, Rect2> _registered = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<CadEntity> _oversized = new(ReferenceEqualityComparer.Instance);
    private const int MaxCellsPerEntity = 4096;

    public SpatialIndex(double cellSize = 25) => _cell = cellSize > 0 ? cellSize : 25;

    public int Count => _registered.Count;

    public void Clear()
    {
        _cells.Clear();
        _registered.Clear();
        _oversized.Clear();
    }

    public void Insert(CadEntity e)
    {
        var b = e.GetBounds();
        _registered[e] = b;
        if (b.IsEmpty) { _oversized.Add(e); return; }
        var (x0, y0, x1, y1) = CellRange(b);
        if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > MaxCellsPerEntity) { _oversized.Add(e); return; }
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            {
                if (!_cells.TryGetValue((x, y), out var list)) _cells[(x, y)] = list = new List<CadEntity>();
                list.Add(e);
            }
    }

    public void Remove(CadEntity e)
    {
        if (!_registered.Remove(e, out var b)) return;
        if (_oversized.Remove(e) || b.IsEmpty) return;
        var (x0, y0, x1, y1) = CellRange(b);
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                if (_cells.TryGetValue((x, y), out var list))
                {
                    list.Remove(e);
                    if (list.Count == 0) _cells.Remove((x, y));
                }
    }

    public void Update(CadEntity e)
    {
        Remove(e);
        Insert(e);
    }

    public IEnumerable<CadEntity> Query(Rect2 area)
    {
        var seen = new HashSet<CadEntity>(ReferenceEqualityComparer.Instance);
        foreach (var e in _oversized)
            if (seen.Add(e)) yield return e;
        if (area.IsEmpty) yield break;
        var (x0, y0, x1, y1) = CellRange(area);
        if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > _cells.Count)
        {
            // Large query: iterate the populated cells instead of the full range.
            foreach (var kv in _cells)
                if (kv.Key.Item1 >= x0 && kv.Key.Item1 <= x1 && kv.Key.Item2 >= y0 && kv.Key.Item2 <= y1)
                    foreach (var e in kv.Value)
                        if (seen.Add(e) && _registered[e].Intersects(area)) yield return e;
            yield break;
        }
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                if (_cells.TryGetValue((x, y), out var list))
                    foreach (var e in list)
                        if (seen.Add(e) && _registered[e].Intersects(area)) yield return e;
    }

    private (int, int, int, int) CellRange(Rect2 r) =>
        ((int)Math.Floor(r.MinX / _cell), (int)Math.Floor(r.MinY / _cell),
         (int)Math.Floor(r.MaxX / _cell), (int)Math.Floor(r.MaxY / _cell));
}
