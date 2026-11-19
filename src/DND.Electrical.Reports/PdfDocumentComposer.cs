using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;
using DND.Electrical.Domain;
using DND.Electrical.Rendering;
using DND.Electrical.Schematic.Services;
using DND.Electrical.Shared;
using SkiaSharp;

namespace DND.Electrical.Reports;

/// <summary>
/// Composes multi-section PDF documents: cover, drawing sheets (vector), tables and text sections,
/// with branded header/footer and page numbering. Layout units are PDF points (1/72 in), Y down.
/// </summary>
public sealed class PdfDocumentComposer
{
    private const float Margin = 36;
    private const float HeaderHeight = 46;
    private const float FooterHeight = 28;
    private static readonly SKSize A4 = new(595.28f, 841.89f);

    private readonly ElectricalProject _project;
    private readonly ReportOptions _options;
    private readonly IImageProvider _images;
    private readonly List<(SKSize size, Action<SKCanvas, int, int> draw, string? bookmark)> _pages = new();
    private readonly SKColor _accent;

    public PdfDocumentComposer(ElectricalProject project, ReportOptions? options = null, IImageProvider? images = null)
    {
        _project = project;
        _options = options ?? new ReportOptions();
        var provider = images as ImageProvider ?? new ImageProvider();
        if (_options.CompanyLogoPng is { Length: > 0 } logo) provider.Register(ImageProvider.CompanyLogoKey, () => logo);
        _images = images ?? provider;
        _accent = SKColor.TryParse(_options.AccentColor, out var c) ? c : new SKColor(23, 43, 77);
    }

    public int PageCount => _pages.Count;
    private string T(string key) => L.T(key, _options.Language);

    // ------------------------------------------------------------------ sections

    public PdfDocumentComposer AddCover(string documentTitle)
    {
        _pages.Add((A4, (c, n, total) => DrawCover(c, documentTitle), documentTitle));
        return this;
    }

    public PdfDocumentComposer AddSheets(IEnumerable<Sheet> sheets)
    {
        foreach (var s in sheets) AddSheet(s);
        return this;
    }

    public PdfDocumentComposer AddSheet(Sheet sheet)
    {
        var resolver = FieldResolver.CreateResolver(_project, sheet);
        var size = new SKSize((float)(sheet.Width * DrawingExporter.PointsPerMm), (float)(sheet.Height * DrawingExporter.PointsPerMm));
        _pages.Add((size, (c, n, total) => DrawDrawing(c, sheet.Drawing, new Rect2(0, 0, sheet.Width, sheet.Height), size, resolver), sheet.DisplayName));
        return this;
    }

    public PdfDocumentComposer AddDrawing(CadDrawing drawing, string title, double paperWidthMm = 420, double paperHeightMm = 297)
    {
        var size = new SKSize((float)(paperWidthMm * DrawingExporter.PointsPerMm), (float)(paperHeightMm * DrawingExporter.PointsPerMm));
        var area = drawing.GetExtents().Inflate(40);
        _pages.Add((size, (c, n, total) =>
        {
            DrawHeader(c, size, title, n, total);
            var content = new SKRect(Margin, Margin + HeaderHeight, size.Width - Margin, size.Height - Margin - FooterHeight);
            DrawDrawingInto(c, drawing, area, content, null);
            DrawFooter(c, size, n, total);
        }, title));
        return this;
    }

    public PdfDocumentComposer AddTable(ReportTable table)
    {
        var landscape = table.Columns.Count > 7;
        var size = landscape ? new SKSize(A4.Height, A4.Width) : A4;
        foreach (var page in PaginateTable(table, size)) _pages.Add((size, page, table.Title));
        return this;
    }

    public PdfDocumentComposer AddText(string title, string body)
    {
        var size = A4;
        using var font = FontProvider.CreateFont(7.0 / FontProvider.EmPerCapHeight * 1.0);
        var width = size.Width - 2 * Margin;
        var lines = new List<string>();
        using (var f = new SKFont(FontProvider.Regular, 8.5f) { LinearMetrics = true })
            foreach (var para in body.Replace("\r", "").Split('\n'))
                lines.AddRange(Wrap(para, f, width));
        const float lineH = 11.5f;
        var top = Margin + HeaderHeight + 26;
        var perPage = (int)((size.Height - top - Margin - FooterHeight) / lineH);
        for (int start = 0; start < Math.Max(1, lines.Count); start += perPage)
        {
            var chunk = lines.Skip(start).Take(perPage).ToList();
            var first = start == 0;
            _pages.Add((size, (c, n, total) =>
            {
                DrawHeader(c, size, title, n, total);
                using var f = new SKFont(FontProvider.Regular, 8.5f) { LinearMetrics = true };
                using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
                var y = top;
                if (first) DrawTitle(c, title, Margin, Margin + HeaderHeight + 14);
                foreach (var l in chunk) { c.DrawText(l, Margin, y, SKTextAlign.Left, f, paint); y += lineH; }
                DrawFooter(c, size, n, total);
            }, title));
        }
        return this;
    }

    public void Save(Stream output)
    {
        var meta = new SKDocumentPdfMetadata
        {
            Title = $"{_project.Info.Number} {_project.Info.Name}",
            Author = ProductInfo.Author,
            Subject = _project.Info.Description,
            Creator = ProductInfo.FullTitle,
            Producer = $"{ProductInfo.ProductName} — {ProductInfo.PoweredBy}",
            Keywords = "electrical engineering",
            Creation = DateTime.Now,
            Modified = DateTime.Now,
            RasterDpi = 300,
        };
        using var managed = new SKManagedWStream(output, disposeManagedStream: false);
        using var doc = SKDocument.CreatePdf(managed, meta);
        var total = _pages.Count;
        for (int i = 0; i < total; i++)
        {
            var (size, draw, _) = _pages[i];
            var canvas = doc.BeginPage(size.Width, size.Height);
            draw(canvas, i + 1, total);
            doc.EndPage();
        }
        doc.Close();
        output.Flush();
    }

    public void Save(string path)
    {
        using var fs = File.Create(path);
        Save(fs);
    }

    // ------------------------------------------------------------------ drawing helpers

    private void DrawDrawing(SKCanvas c, CadDrawing drawing, Rect2 area, SKSize page, Func<string, string> resolver) =>
        DrawDrawingInto(c, drawing, area, new SKRect(0, 0, page.Width, page.Height), resolver);

    private void DrawDrawingInto(SKCanvas c, CadDrawing drawing, Rect2 area, SKRect target, Func<string, string>? resolver)
    {
        var scale = Math.Min(target.Width / area.Width, target.Height / area.Height);
        var offX = target.Left + (target.Width - area.Width * scale) / 2;
        var offY = target.Top + (target.Height - area.Height * scale) / 2;
        var m = new Matrix2(scale, 0, 0, -scale, offX - area.MinX * scale, offY + area.MaxY * scale);
        new SkiaDrawingRenderer().Render(c, drawing, m, Rect2.Empty, new RenderOptions
        {
            PrintMode = true, TextResolver = resolver, Images = _images, DeviceScale = scale, MinStrokePixels = 0.25f,
        });
    }

    private void DrawCover(SKCanvas c, string documentTitle)
    {
        var i = _project.Info;
        var w = A4.Width;
        using var band = new SKPaint { Color = _accent, IsAntialias = true };
        c.DrawRect(0, 0, w, 10, band);
        if (_images.GetImage(ImageProvider.BrandingLogoKey) is { } logo)
            c.DrawImage(logo, new SKRect(Margin, 60, Margin + 90, 150), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (_options.CompanyLogoPng is { Length: > 0 } && _images.GetImage(ImageProvider.CompanyLogoKey) is { } clogo)
            c.DrawImage(clogo, new SKRect(w - Margin - 120, 60, w - Margin, 150), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

        Text(c, documentTitle.ToUpperInvariant(), Margin, 230, 13, _accent, bold: true);
        Text(c, i.Name, Margin, 268, 26, SKColors.Black, bold: true, maxWidth: w - 2 * Margin);
        Text(c, i.Number, Margin, 296, 15, new SKColor(100, 116, 139));
        if (!string.IsNullOrWhiteSpace(i.Description)) Text(c, i.Description, Margin, 322, 10, SKColors.Black, maxWidth: w - 2 * Margin);

        var y = 400f;
        void Row(string label, string value)
        {
            Text(c, label, Margin, y, 9, new SKColor(100, 116, 139));
            Text(c, value, Margin + 140, y, 10, SKColors.Black, maxWidth: w - 2 * Margin - 140);
            y += 22;
        }
        Row(T("customer"), i.CustomerName);
        Row(T("company"), i.CompanyName);
        Row("Site", i.Site);
        Row(T("engineer"), i.Engineer);
        Row(T("revision"), i.CurrentRevision);
        Row(T("status"), i.Status.ToString());
        Row(T("date"), DateTime.Now.ToString(_options.DateFormat));
        Row("Sheets", _project.Sheets.Count.ToString());

        if (i.IsDemo)
        {
            using var warn = new SKPaint { Color = new SKColor(254, 243, 199), IsAntialias = true };
            c.DrawRect(Margin, 640, w - 2 * Margin, 36, warn);
            Text(c, T("demo"), Margin + 10, 662, 9.5f, new SKColor(146, 64, 14), bold: true, maxWidth: w - 2 * Margin - 20);
        }

        var by = A4.Height - 110;
        using var line = new SKPaint { Color = new SKColor(203, 213, 225), StrokeWidth = 0.8f };
        c.DrawLine(Margin, by - 16, w - Margin, by - 16, line);
        Text(c, ProductInfo.ProductName, Margin, by, 10, _accent, bold: true);
        Text(c, $"Author & Developer: {ProductInfo.Author}", Margin, by + 16, 9, SKColors.Black);
        Text(c, ProductInfo.Copyright, Margin, by + 32, 9, SKColors.Black);
        Text(c, ProductInfo.PoweredBy, Margin, by + 48, 8, new SKColor(100, 116, 139));
    }

    private void DrawHeader(SKCanvas c, SKSize page, string title, int n, int total)
    {
        var top = Margin - 10;
        var logo = _images.GetImage(_options.CompanyLogoPng is { Length: > 0 } ? ImageProvider.CompanyLogoKey : ImageProvider.BrandingLogoKey);
        if (logo is not null) c.DrawImage(logo, new SKRect(Margin, top, Margin + 30, top + 30), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        Text(c, _options.HeaderText ?? _project.Info.CompanyName, Margin + 38, top + 12, 8, new SKColor(100, 116, 139));
        Text(c, $"{_project.Info.Number} – {_project.Info.Name}", Margin + 38, top + 25, 9.5f, SKColors.Black, bold: true, maxWidth: page.Width - 2 * Margin - 200);
        Text(c, $"{T("revision")} {_project.Info.CurrentRevision}", page.Width - Margin, top + 12, 8, new SKColor(100, 116, 139), align: SKTextAlign.Right);
        Text(c, title, page.Width - Margin, top + 25, 9, _accent, bold: true, align: SKTextAlign.Right);
        using var line = new SKPaint { Color = _accent, StrokeWidth = 1.2f };
        c.DrawLine(Margin, top + 34, page.Width - Margin, top + 34, line);
    }

    private void DrawFooter(SKCanvas c, SKSize page, int n, int total)
    {
        var y = page.Height - Margin + 8;
        using var line = new SKPaint { Color = new SKColor(203, 213, 225), StrokeWidth = 0.6f };
        c.DrawLine(Margin, y - 12, page.Width - Margin, y - 12, line);
        var grey = new SKColor(100, 116, 139);
        Text(c, _options.FooterText ?? $"{ProductInfo.ProductName} — {ProductInfo.PoweredBy}", Margin, y, 7, grey);
        Text(c, $"{T("generated")} {DateTime.Now.ToString(_options.DateFormat)}", page.Width / 2, y, 7, grey, align: SKTextAlign.Center);
        if (_options.IncludePageNumbers) Text(c, $"{T("page")} {n} {T("of")} {total}", page.Width - Margin, y, 7, grey, align: SKTextAlign.Right);
        if (_project.Info.IsDemo) Text(c, T("demo"), page.Width / 2, y + 10, 6, new SKColor(180, 83, 9), align: SKTextAlign.Center);
    }

    private void DrawTitle(SKCanvas c, string title, float x, float y) => Text(c, title, x, y, 13, _accent, bold: true);

    private static void Text(SKCanvas c, string text, float x, float y, float size, SKColor color, bool bold = false, SKTextAlign align = SKTextAlign.Left, float maxWidth = 0)
    {
        if (string.IsNullOrEmpty(text)) return;
        using var font = new SKFont(bold ? FontProvider.Bold : FontProvider.Regular, size) { LinearMetrics = true, Subpixel = true };
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        if (maxWidth > 0)
            while (font.MeasureText(text) > maxWidth && text.Length > 4) text = text[..^4] + "…";
        c.DrawText(text, x, y, align, font, paint);
    }

    // ------------------------------------------------------------------ tables

    private IEnumerable<Action<SKCanvas, int, int>> PaginateTable(ReportTable table, SKSize page)
    {
        const float fontSize = 7.2f, headerSize = 7.5f, lineH = 9.2f, pad = 3f;
        var contentW = page.Width - 2 * Margin;
        var totalWeight = table.Columns.Sum(c => c.Weight);
        var widths = table.Columns.Select(c => (float)(c.Weight / totalWeight * contentW)).ToArray();
        using var font = new SKFont(FontProvider.Regular, fontSize) { LinearMetrics = true };
        using var hfont = new SKFont(FontProvider.Bold, headerSize) { LinearMetrics = true };

        var headerLines = table.Columns.Select((c, i) => Wrap(c.Header, hfont, widths[i] - 2 * pad)).ToList();
        var headerH = headerLines.Max(l => l.Count) * lineH + 2 * pad;
        var rows = table.Rows.Select(r => r.Select((v, i) => Wrap(v, font, widths[i] - 2 * pad)).ToList()).ToList();
        var rowHeights = rows.Select(r => r.Max(l => Math.Max(1, l.Count)) * lineH + 2 * pad).ToList();

        var firstTop = Margin + HeaderHeight + 34;
        var otherTop = Margin + HeaderHeight + 6;
        var bottom = page.Height - Margin - FooterHeight;
        var pages = new List<(int from, int to)>();
        int start = 0;
        var top = firstTop;
        do
        {
            var y = top + headerH;
            var end = start;
            while (end < rows.Count && y + rowHeights[end] <= bottom) { y += rowHeights[end]; end++; }
            if (end == start && start < rows.Count) end = start + 1; // oversize row: force progress
            pages.Add((start, end));
            start = end;
            top = otherTop;
        } while (start < rows.Count);

        for (int p = 0; p < pages.Count; p++)
        {
            var (from, to) = pages[p];
            var isFirst = p == 0;
            var isLast = p == pages.Count - 1;
            yield return (c, n, total) =>
            {
                DrawHeader(c, page, table.Title, n, total);
                var y = isFirst ? firstTop : otherTop;
                if (isFirst)
                {
                    DrawTitle(c, table.Title, Margin, Margin + HeaderHeight + 14);
                    if (!string.IsNullOrEmpty(table.Subtitle)) Text(c, table.Subtitle, Margin, Margin + HeaderHeight + 26, 8, new SKColor(100, 116, 139));
                }
                using var hf = new SKFont(FontProvider.Bold, headerSize) { LinearMetrics = true };
                using var bf = new SKFont(FontProvider.Regular, fontSize) { LinearMetrics = true };
                using var headerFill = new SKPaint { Color = _accent };
                using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
                using var black = new SKPaint { Color = new SKColor(15, 23, 42), IsAntialias = true };
                using var zebra = new SKPaint { Color = new SKColor(241, 245, 249) };
                using var grid = new SKPaint { Color = new SKColor(203, 213, 225), StrokeWidth = 0.4f, Style = SKPaintStyle.Stroke };

                c.DrawRect(Margin, y, contentW, headerH, headerFill);
                var x = Margin;
                for (int i = 0; i < widths.Length; i++)
                {
                    DrawCell(c, headerLines[i], x, y, widths[i], table.Columns[i].Align, hf, white);
                    x += widths[i];
                }
                y += headerH;
                for (int r = from; r < to; r++)
                {
                    if ((r - from) % 2 == 1) c.DrawRect(Margin, y, contentW, rowHeights[r], zebra);
                    x = Margin;
                    for (int i = 0; i < widths.Length; i++)
                    {
                        DrawCell(c, rows[r][i], x, y, widths[i], table.Columns[i].Align, bf, black);
                        x += widths[i];
                    }
                    c.DrawLine(Margin, y + rowHeights[r], Margin + contentW, y + rowHeights[r], grid);
                    y += rowHeights[r];
                }
                if (rows.Count == 0) Text(c, "—", Margin + 4, y + 12, 8, new SKColor(100, 116, 139));
                if (isLast)
                {
                    y += 16;
                    foreach (var note in table.Notes)
                        foreach (var l in Wrap(note, bf, contentW))
                        {
                            if (y > page.Height - Margin - FooterHeight) break;
                            Text(c, l, Margin, y, 7.5f, new SKColor(51, 65, 85));
                            y += 10;
                        }
                }
                DrawFooter(c, page, n, total);
            };
        }

        void DrawCell(SKCanvas c, List<string> lines, float x, float y, float w, ColumnAlign align, SKFont f, SKPaint paint)
        {
            var ty = y + pad + lineH - 2.4f;
            foreach (var l in lines)
            {
                var tx = align switch { ColumnAlign.Right => x + w - pad, ColumnAlign.Center => x + w / 2, _ => x + pad };
                var a = align switch { ColumnAlign.Right => SKTextAlign.Right, ColumnAlign.Center => SKTextAlign.Center, _ => SKTextAlign.Left };
                c.DrawText(l, tx, ty, a, f, paint);
                ty += lineH;
            }
        }
    }

    /// <summary>Word-wraps text to a width (hard-breaking words longer than the width).</summary>
    public static List<string> Wrap(string text, SKFont font, float width)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text)) { result.Add(string.Empty); return result; }
        foreach (var para in text.Replace("\r", "").Split('\n'))
        {
            var line = string.Empty;
            foreach (var word in para.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (font.MeasureText(candidate) <= width) { line = candidate; continue; }
                if (line.Length > 0) result.Add(line);
                line = word;
                while (font.MeasureText(line) > width && line.Length > 1)
                {
                    var cut = line.Length - 1;
                    while (cut > 1 && font.MeasureText(line[..cut]) > width) cut--;
                    result.Add(line[..cut]);
                    line = line[cut..];
                }
            }
            result.Add(line);
        }
        return result;
    }
}
