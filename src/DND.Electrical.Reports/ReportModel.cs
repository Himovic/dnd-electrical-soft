namespace DND.Electrical.Reports;

public enum ColumnAlign { Left, Center, Right }

public sealed record ReportColumn(string Header, double Weight = 1, ColumnAlign Align = ColumnAlign.Left);

/// <summary>Tabular report content, independent of the output format.</summary>
public sealed class ReportTable
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public List<ReportColumn> Columns { get; } = new();
    public List<string[]> Rows { get; } = new();
    public List<string> Notes { get; } = new();

    public ReportTable Col(string header, double weight = 1, ColumnAlign align = ColumnAlign.Left)
    {
        Columns.Add(new ReportColumn(header, weight, align));
        return this;
    }

    public ReportTable Row(params object?[] values)
    {
        if (values.Length != Columns.Count) throw new ArgumentException($"Row has {values.Length} values but table '{Title}' has {Columns.Count} columns.");
        Rows.Add(values.Select(Format).ToArray());
        return this;
    }

    private static string Format(object? v) => v switch
    {
        null => string.Empty,
        double d => d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset dt => dt.ToString("yyyy-MM-dd HH:mm"),
        bool b => b ? "✓" : string.Empty,
        _ => v.ToString() ?? string.Empty,
    };
}

/// <summary>Customisation of generated documents.</summary>
public sealed class ReportOptions
{
    public string Language { get; set; } = "en";
    public string DateFormat { get; set; } = "yyyy-MM-dd";
    public string? HeaderText { get; set; }
    public string? FooterText { get; set; }
    public bool IncludeCover { get; set; } = true;
    public bool IncludePageNumbers { get; set; } = true;
    public string AccentColor { get; set; } = "#172B4D";
    public byte[]? CompanyLogoPng { get; set; }
    public bool IncludeDrawings { get; set; } = true;
    public HashSet<string> Sections { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Includes(string section) => Sections.Count == 0 || Sections.Contains(section);
}
