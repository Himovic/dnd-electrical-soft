using System.Text.Json.Serialization;
using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;

namespace DND.Electrical.CAD.Entities;

/// <summary>
/// Base class of every drawable object. Entities are plain data plus geometric behaviour;
/// rendering is performed by a separate renderer so the model stays UI-independent.
/// </summary>
public abstract class CadEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Layer { get; set; } = StandardLayers.Default;

    /// <summary>Explicit colour; <c>null</c> means "by layer".</summary>
    public CadColor? Color { get; set; }

    /// <summary>Line weight in mm; <c>null</c> means "by layer".</summary>
    public double? LineWeight { get; set; }

    public LineType LineType { get; set; } = LineType.ByLayer;

    /// <summary>Stable type discriminator used by serialization and the renderer.</summary>
    [JsonIgnore]
    public abstract string TypeName { get; }

    public abstract Rect2 GetBounds();

    /// <summary>Shortest distance from <paramref name="p"/> to the entity's visible geometry.</summary>
    public abstract double DistanceTo(Point2 p);

    public virtual bool HitTest(Point2 p, double tolerance) =>
        GetBounds().Inflate(tolerance).Contains(p) && DistanceTo(p) <= tolerance;

    /// <summary>Window selection: entity entirely inside the rectangle.</summary>
    public virtual bool IsInside(Rect2 r) => r.Contains(GetBounds());

    /// <summary>Crossing selection: entity touches the rectangle.</summary>
    public virtual bool IntersectsRect(Rect2 r) => r.Intersects(GetBounds());

    public abstract void Transform(Matrix2 m);

    public virtual IEnumerable<SnapPoint> GetSnapPoints() => Array.Empty<SnapPoint>();

    /// <summary>Deep copy. When <paramref name="keepId"/> is false a fresh identifier is assigned.</summary>
    public CadEntity Clone(bool keepId = false)
    {
        var copy = (CadEntity)MemberwiseClone();
        copy.DeepCopyFrom(this);
        if (!keepId) copy.Id = Guid.NewGuid();
        copy.OnCloned(keepId);
        return copy;
    }

    /// <summary>Override to deep-copy reference-type members (lists, dictionaries).</summary>
    protected virtual void DeepCopyFrom(CadEntity source) { }

    /// <summary>Hook for derived types that hold identifiers of their own (e.g. groups).</summary>
    protected virtual void OnCloned(bool keepId) { }
}
