using DND.Electrical.CAD.Entities;
using DND.Electrical.Core.Geometry;

namespace DND.Electrical.CAD.Dxf;

/// <summary>Entities that are exported as primitive geometry (e.g. wires, junctions).</summary>
public interface IExplodableEntity
{
    /// <summary>Primitive entities in world coordinates representing this entity.</summary>
    IEnumerable<CadEntity> Explode();
}

/// <summary>Entities exported as a DXF block definition + INSERT carrying attributes (engineering metadata).</summary>
public interface IDxfBlockSource
{
    DxfBlockReference GetDxfBlock();
}

public sealed record DxfAttribute(string Tag, string Value, Point2 Position, double Height, double Rotation, bool Visible);

/// <param name="BlockName">Block name (unique per distinct geometry).</param>
/// <param name="LocalEntities">Geometry in block coordinates (insertion point = origin).</param>
/// <param name="MirrorX">Mirror about the block's Y axis before rotation (DXF X scale = -1).</param>
public sealed record DxfBlockReference(
    string BlockName,
    IReadOnlyList<CadEntity> LocalEntities,
    Point2 Position,
    double Rotation,
    bool MirrorX,
    string Layer,
    IReadOnlyList<DxfAttribute> Attributes);
