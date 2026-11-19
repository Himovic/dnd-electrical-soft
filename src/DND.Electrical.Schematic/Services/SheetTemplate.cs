using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;
using DND.Electrical.Domain;

namespace DND.Electrical.Schematic.Services;

/// <summary>
/// Geometry of the drawing frame: reference columns along the top, title block along the bottom.
/// Column numbers are used for cross-references ("/sheet.column").
/// </summary>
public sealed class SheetLayout
{
    public const double Margin = 5;
    public const double BindingMargin = 15;
    public const double ColumnBand = 6;
    public const double TitleBlockHeight = 32;

    public SheetLayout(double width, double height, int columns)
    {
        Width = width;
        Height = height;
        Columns = Math.Max(1, columns);
    }

    public double Width { get; }
    public double Height { get; }
    public int Columns { get; }

    public Rect2 Page => new(0, 0, Width, Height);
    public Rect2 Frame => new(BindingMargin, Margin, Width - Margin, Height - Margin);
    public Rect2 TitleBlock => new(Frame.MinX, Frame.MinY, Frame.MaxX, Frame.MinY + TitleBlockHeight);
    public Rect2 DrawingArea => new(Frame.MinX, TitleBlock.MaxY, Frame.MaxX, Frame.MaxY - ColumnBand);
    public double ColumnWidth => Frame.Width / Columns;

    public static SheetLayout For(Sheet sheet, ProjectSettings settings) => new(sheet.Width, sheet.Height, settings.SheetColumns);

    /// <summary>1-based column index for an X coordinate (clamped to the frame).</summary>
    public int ColumnAt(double x) => Math.Clamp((int)Math.Floor((x - Frame.MinX) / ColumnWidth) + 1, 1, Columns);

    public double ColumnCenter(int column) => Frame.MinX + (column - 0.5) * ColumnWidth;
}

/// <summary>Creates the frame, reference columns and title block of a sheet (with field placeholders).</summary>
public static class SheetTemplate
{
    public static void Apply(Sheet sheet, ProjectSettings settings)
    {
        var d = sheet.Drawing;
        foreach (var e in d.Entities.Where(e => e.Layer is StandardLayers.Frame or StandardLayers.TitleBlock).ToList()) d.Remove(e);
        var l = SheetLayout.For(sheet, settings);
        d.AddRange(BuildFrame(l, sheet.Kind));
    }

    public static IEnumerable<CadEntity> BuildFrame(SheetLayout l, SheetKind kind)
    {
        const string F = StandardLayers.Frame;
        const string T = StandardLayers.TitleBlock;
        var fr = l.Frame;
        yield return new PolylineEntity(fr.Corners(), true) { Layer = F, LineWeight = 0.5 };

        // Reference column band
        var bandY = fr.MaxY - SheetLayout.ColumnBand;
        yield return new LineEntity(new Point2(fr.MinX, bandY), new Point2(fr.MaxX, bandY)) { Layer = F, LineWeight = 0.25 };
        for (int c = 1; c <= l.Columns; c++)
        {
            var x0 = fr.MinX + (c - 1) * l.ColumnWidth;
            if (c > 1) yield return new LineEntity(new Point2(x0, bandY), new Point2(x0, fr.MaxY)) { Layer = F, LineWeight = 0.18 };
            yield return new TextEntity(new Point2(x0 + l.ColumnWidth / 2, bandY + SheetLayout.ColumnBand / 2), c.ToString(), 2.5)
            { Layer = F, HAlign = TextHAlign.Center, VAlign = TextVAlign.Middle };
        }

        // Title block (bottom strip): logo | company | project | customer | sheet title | sheet/rev/date
        var tb = l.TitleBlock;
        yield return new LineEntity(new Point2(tb.MinX, tb.MaxY), new Point2(tb.MaxX, tb.MaxY)) { Layer = T, LineWeight = 0.5 };
        var w = tb.Width;
        var cols = new[] { 0.0, 0.10, 0.30, 0.52, 0.68, 0.88, 1.0 }.Select(f => tb.MinX + f * w).ToArray();
        for (int i = 1; i < cols.Length - 1; i++)
            yield return new LineEntity(new Point2(cols[i], tb.MinY), new Point2(cols[i], tb.MaxY)) { Layer = T, LineWeight = 0.35 };

        IEnumerable<CadEntity> Field(int col, double row, string label, string value, double h = 2.6, bool bold = false)
        {
            var x = cols[col] + 1.5;
            var y = tb.MaxY - 4 - row * 8.5;
            yield return new TextEntity(new Point2(x, y), label, 1.6) { Layer = T, Color = CadColor.Grey };
            yield return new TextEntity(new Point2(x, y - 4.2), value, h) { Layer = T, Bold = bold };
        }

        // Company logo + powered-by
        yield return new ImageEntity { Position = new Point2(cols[0] + 2, tb.MinY + 8), Width = cols[1] - cols[0] - 4, Height = tb.Height - 11, ImageKey = "company:logo", Layer = T };
        yield return new TextEntity(new Point2((cols[0] + cols[1]) / 2, tb.MinY + 3), "{POWEREDBY}", 1.4) { Layer = T, HAlign = TextHAlign.Center, Color = CadColor.Grey };

        foreach (var e in Field(1, 0, "Company", "{COMPANY.NAME}", 3, bold: true)) yield return e;
        foreach (var e in Field(1, 1, "Address", "{COMPANY.ADDRESS}", 2)) yield return e;
        foreach (var e in Field(1, 2, "Engineer / Checked / Approved", "{ENGINEER} / {CHECKED} / {APPROVED}", 2)) yield return e;

        foreach (var e in Field(2, 0, "Project", "{PROJECT.NAME}", 3, bold: true)) yield return e;
        foreach (var e in Field(2, 1, "Project number", "{PROJECT.NUMBER}", 2.6)) yield return e;
        foreach (var e in Field(2, 2, "Reference / Site", "{PROJECT.REFERENCE} {PROJECT.SITE}", 2)) yield return e;

        foreach (var e in Field(3, 0, "Customer", "{CUSTOMER.NAME}", 2.0)) yield return e;
        foreach (var e in Field(3, 1, "Function / Location", "{SHEET.FUNCTION} {SHEET.LOCATION}", 2.4)) yield return e;
        foreach (var e in Field(3, 2, "Status", "{PROJECT.STATUS}", 2.2)) yield return e;

        foreach (var e in Field(4, 0, "Sheet title", "{SHEET.TITLE}", 3.2, bold: true)) yield return e;
        foreach (var e in Field(4, 1, "Description", "{SHEET.DESCRIPTION}", 2)) yield return e;
        yield return new TextEntity(new Point2(cols[4] + 1.5, tb.MinY + 2.5), "{PRODUCT}", 1.4) { Layer = T, Color = CadColor.Grey };

        foreach (var e in Field(5, 0, "Sheet", "{SHEET.NUMBER} / {SHEET.COUNT}", 3.2, bold: true)) yield return e;
        foreach (var e in Field(5, 1, "Revision", "{SHEET.REVISION}", 2.6)) yield return e;
        foreach (var e in Field(5, 2, "Date", "{DATE}", 2.2)) yield return e;

        // Demo watermark field (resolves to empty text for real projects)
        yield return new TextEntity(new Point2(fr.MinX + 3, bandY - 4), "{DEMO.BANNER}", 2.5) { Layer = T, Color = CadColor.Orange };
    }
}
