using ClosedXML.Excel;
using DND.Electrical.Domain;
using DND.Electrical.Shared;

namespace DND.Electrical.Reports;

/// <summary>Writes report tables to an Excel workbook (one worksheet per table).</summary>
public static class XlsxReportWriter
{
    public static void Write(IReadOnlyList<ReportTable> tables, Stream output, ElectricalProject project, ReportOptions? options = null)
    {
        options ??= new ReportOptions();
        using var wb = new XLWorkbook();
        wb.Properties.Author = ProductInfo.Author;
        wb.Properties.Company = ProductInfo.Developer;
        wb.Properties.Title = $"{project.Info.Number} {project.Info.Name}";
        wb.Properties.Comments = $"{ProductInfo.ProductName} — {ProductInfo.PoweredBy}";
        var accent = XLColor.FromHtml(options.AccentColor);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            var name = SheetName(table.Title, used);
            var ws = wb.Worksheets.Add(name);
            ws.Cell(1, 1).Value = table.Title;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(2, 1).Value = $"{project.Info.Number} – {project.Info.Name} · {L.T("revision", options.Language)} {project.Info.CurrentRevision} · {L.T("generated", options.Language)} {DateTime.Now.ToString(options.DateFormat)}";
            ws.Cell(2, 1).Style.Font.FontColor = XLColor.Gray;
            const int headerRow = 4;
            for (int c = 0; c < table.Columns.Count; c++)
            {
                var cell = ws.Cell(headerRow, c + 1);
                cell.Value = table.Columns[c].Header;
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = accent;
            }
            for (int r = 0; r < table.Rows.Count; r++)
                for (int c = 0; c < table.Columns.Count; c++)
                {
                    var cell = ws.Cell(headerRow + 1 + r, c + 1);
                    var v = table.Rows[r][c];
                    if (table.Columns[c].Align == ColumnAlign.Right && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
                        cell.Value = d;
                    else cell.Value = v;
                }
            if (table.Rows.Count > 0)
            {
                var range = ws.Range(headerRow, 1, headerRow + table.Rows.Count, table.Columns.Count);
                range.SetAutoFilter();
                range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                range.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
            }
            var noteRow = headerRow + table.Rows.Count + 2;
            foreach (var n in table.Notes) ws.Cell(noteRow++, 1).Value = n;
            ws.Cell(noteRow + 1, 1).Value = $"{ProductInfo.ProductName} — {ProductInfo.PoweredBy}";
            ws.Cell(noteRow + 1, 1).Style.Font.FontColor = XLColor.Gray;
            ws.SheetView.FreezeRows(headerRow);
            for (int c = 0; c < table.Columns.Count; c++)
                ws.Column(c + 1).Width = Math.Clamp(Math.Max(table.Columns[c].Header.Length, table.Rows.Select(r => r[c].Length).DefaultIfEmpty(0).Max()) + 2, 6, 60);
        }
        if (tables.Count == 0) wb.Worksheets.Add("Empty");
        wb.SaveAs(output);
    }

    public static void Write(IReadOnlyList<ReportTable> tables, string path, ElectricalProject project, ReportOptions? options = null)
    {
        using var fs = File.Create(path);
        Write(tables, fs, project, options);
    }

    private static string SheetName(string title, HashSet<string> used)
    {
        var clean = new string(title.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray()).Trim();
        if (clean.Length > 28) clean = clean[..28];
        if (clean.Length == 0) clean = "Sheet";
        var name = clean;
        var i = 2;
        while (!used.Add(name)) name = $"{clean[..Math.Min(clean.Length, 25)]} {i++}";
        return name;
    }
}
