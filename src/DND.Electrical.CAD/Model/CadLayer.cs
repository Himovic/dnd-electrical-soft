namespace DND.Electrical.CAD.Model;

/// <summary>Drawing layer. Entities reference layers by name (case-insensitive).</summary>
public sealed class CadLayer
{
    public string Name { get; set; } = "0";
    public CadColor Color { get; set; } = CadColor.Black;
    public double LineWeight { get; set; } = 0.25;
    public LineType LineType { get; set; } = LineType.Continuous;
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public bool Printable { get; set; } = true;
    public string Description { get; set; } = string.Empty;

    public CadLayer Clone() => (CadLayer)MemberwiseClone();

    public static CadLayer Create(string name, CadColor color, double lineWeight = 0.25, string description = "", LineType lineType = LineType.Continuous) =>
        new() { Name = name, Color = color, LineWeight = lineWeight, Description = description, LineType = lineType };
}

/// <summary>Standard layer names used by the electrical application.</summary>
public static class StandardLayers
{
    public const string Default = "0";
    public const string Frame = "FRAME";
    public const string TitleBlock = "TITLEBLOCK";
    public const string Symbols = "SYMBOLS";
    public const string Wires = "WIRES";
    public const string WireNumbers = "WIRENUMBERS";
    public const string Tags = "TAGS";
    public const string CrossReferences = "XREF";
    public const string Annotations = "ANNOTATIONS";
    public const string Dimensions = "DIMENSIONS";
    public const string Construction = "CONSTRUCTION";

    public static IEnumerable<CadLayer> CreateDefaults() => new[]
    {
        CadLayer.Create(Default, CadColor.Black, 0.25, "Default layer"),
        CadLayer.Create(Frame, CadColor.Navy, 0.5, "Drawing frame and grid references"),
        CadLayer.Create(TitleBlock, CadColor.Navy, 0.35, "Title block"),
        CadLayer.Create(Symbols, CadColor.Black, 0.35, "Electrical symbols"),
        CadLayer.Create(Wires, new CadColor(29, 78, 216), 0.35, "Electrical connections"),
        CadLayer.Create(WireNumbers, new CadColor(29, 78, 216), 0.18, "Wire numbers"),
        CadLayer.Create(Tags, new CadColor(185, 28, 28), 0.25, "Component tags and attributes"),
        CadLayer.Create(CrossReferences, new CadColor(21, 128, 61), 0.18, "Cross-references"),
        CadLayer.Create(Annotations, CadColor.Grey, 0.25, "Notes and annotations"),
        CadLayer.Create(Dimensions, CadColor.Grey, 0.18, "Dimensions"),
        CadLayer.Create(Construction, new CadColor(148, 163, 184), 0.13, "Construction geometry (not printed)", LineType.Dashed),
    };
}
