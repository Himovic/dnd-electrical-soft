using System.Text.Json.Serialization;
using DND.Electrical.CAD.Model;

namespace DND.Electrical.Domain;

public sealed record SheetFormat(string Name, double Width, double Height)
{
    public static readonly SheetFormat A0 = new("A0", 1189, 841);
    public static readonly SheetFormat A1 = new("A1", 841, 594);
    public static readonly SheetFormat A2 = new("A2", 594, 420);
    public static readonly SheetFormat A3 = new("A3", 420, 297);
    public static readonly SheetFormat A4 = new("A4", 297, 210);
    public static readonly SheetFormat A4Portrait = new("A4P", 210, 297);

    public static IReadOnlyList<SheetFormat> Standard { get; } = new[] { A0, A1, A2, A3, A4, A4Portrait };

    public static SheetFormat Parse(string name) =>
        Standard.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) ?? A3;

    public static SheetFormat Custom(double width, double height) => new($"{width:0}x{height:0}", width, height);
}

/// <summary>A drawing page of the project (schematic, layout, cover…).</summary>
public sealed class Sheet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Number { get; set; } = 1;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public SheetKind Kind { get; set; } = SheetKind.Schematic;
    public string FormatName { get; set; } = "A3";
    public double Width { get; set; } = 420;
    public double Height { get; set; } = 297;
    public string Revision { get; set; } = "A";

    /// <summary>Functional assignment (e.g. "=MCC1") and location (e.g. "+CAB1") of the page content.</summary>
    public string Function { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;

    public CadDrawing Drawing { get; set; } = new();

    /// <summary>Additional title-block fields (key → value).</summary>
    public Dictionary<string, string> Fields { get; set; } = new();

    public DateTimeOffset Modified { get; set; } = DateTimeOffset.Now;

    [JsonIgnore]
    public SheetFormat Format
    {
        get => new(FormatName, Width, Height);
        set { FormatName = value.Name; Width = value.Width; Height = value.Height; }
    }

    [JsonIgnore]
    public string DisplayName => $"{Number:00} – {Title}";
}
