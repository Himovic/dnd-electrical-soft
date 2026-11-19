using System.Globalization;
using ClosedXML.Excel;
using DND.Electrical.Domain;
using DND.Electrical.Reports;

namespace DND.Electrical.Application.Services;

public sealed record CatalogImportResult(int Added, int Updated, IReadOnlyList<string> Errors);

/// <summary>Imports manufacturer product catalogues from CSV or Excel into the project catalogue.</summary>
public static class CatalogImportService
{
    private static readonly string[] Required = { "Manufacturer", "PartNumber", "Description" };

    public static CatalogImportResult Import(ElectricalProject project, string path)
    {
        var rows = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".csv" => ReadCsv(path),
            ".xlsx" => ReadXlsx(path),
            _ => throw new NotSupportedException("Only .csv and .xlsx catalogues are supported."),
        };
        return Import(project, rows, Path.GetFileName(path));
    }

    public static CatalogImportResult Import(ElectricalProject project, List<string[]> rows, string source)
    {
        if (rows.Count < 1) return new CatalogImportResult(0, 0, new[] { "The file is empty." });
        var header = rows[0].Select(h => h.Trim().Replace(" ", string.Empty)).ToArray();
        int Col(string name) => Array.FindIndex(header, h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));
        var missing = Required.Where(r => Col(r) < 0).ToList();
        if (missing.Count > 0) return new CatalogImportResult(0, 0, new[] { $"Missing required column(s): {string.Join(", ", missing)}." });

        var errors = new List<string>();
        int added = 0, updated = 0;
        for (int r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            string Get(string name) { var c = Col(name); return c >= 0 && c < row.Length ? row[c].Trim() : string.Empty; }
            var part = Get("PartNumber");
            var manufacturer = Get("Manufacturer");
            if (part.Length == 0 && manufacturer.Length == 0) continue;
            if (part.Length == 0) { errors.Add($"Row {r + 1}: part number is empty."); continue; }
            decimal price = 0;
            if (Get("UnitPrice") is { Length: > 0 } ps && !decimal.TryParse(ps, NumberStyles.Number, CultureInfo.InvariantCulture, out price))
            { errors.Add($"Row {r + 1}: invalid price '{ps}'."); continue; }
            double D(string n) => double.TryParse(Get(n), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
            var existing = project.FindProduct(manufacturer, part);
            var p = existing ?? new CatalogProduct { Manufacturer = manufacturer, PartNumber = part };
            p.Description = Get("Description");
            p.Category = Get("Category");
            p.UnitPrice = price;
            p.Currency = Get("Currency") is { Length: > 0 } cur ? cur : project.Settings.Currency;
            p.Supplier = Get("Supplier");
            p.WidthMm = D("Width"); p.HeightMm = D("Height"); p.DepthMm = D("Depth");
            p.DatasheetUrl = Get("Datasheet");
            p.IsDemoData = false;
            p.Source = source;
            if (existing is null) { project.Catalog.Add(p); added++; } else updated++;
        }
        project.Audit("Catalogue import", $"{source}: {added} added, {updated} updated, {errors.Count} error(s)");
        return new CatalogImportResult(added, updated, errors);
    }

    private static List<string[]> ReadCsv(string path)
    {
        var text = File.ReadAllText(path);
        var sep = text.Split('\n')[0].Count(c => c == ';') > text.Split('\n')[0].Count(c => c == ',') ? ';' : ',';
        return CsvWriter.Parse(text, sep);
    }

    private static List<string[]> ReadXlsx(string path)
    {
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.First();
        var used = ws.RangeUsed();
        if (used is null) return new();
        return used.Rows().Select(r => r.Cells().Select(c => c.GetFormattedString()).ToArray()).ToList();
    }
}
