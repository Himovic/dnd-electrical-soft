using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;
using DND.Electrical.Shared;
using SkiaSharp;

namespace DND.Electrical.Rendering;

/// <summary>One page of a drawing set: a drawing area printed onto a paper size.</summary>
public sealed record DrawingPage(CadDrawing Drawing, Rect2 Area, double PaperWidthMm, double PaperHeightMm, Func<string, string>? TextResolver = null, string? Title = null);

public sealed class PdfMetadataInfo
{
    public string Title { get; init; } = ProductInfo.ProductName;
    public string Subject { get; init; } = string.Empty;
    public string Author { get; init; } = ProductInfo.Author;
    public string Keywords { get; init; } = "electrical, schematic";
}

/// <summary>Exports drawings to PNG, SVG and vector PDF using the shared renderer.</summary>
public static class DrawingExporter
{
    public const double PointsPerMm = 72.0 / 25.4;

    public static byte[] ToPng(CadDrawing drawing, Rect2 area, double pixelsPerMm = 6, Func<string, string>? textResolver = null, IImageProvider? images = null, bool transparent = false)
    {
        if (area.IsEmpty) area = drawing.GetExtents().Inflate(5);
        var w = Math.Max(1, (int)Math.Ceiling(area.Width * pixelsPerMm));
        var h = Math.Max(1, (int)Math.Ceiling(area.Height * pixelsPerMm));
        if ((long)w * h > 120_000_000) throw new InvalidOperationException("Requested image is too large.");
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(transparent ? SKColors.Transparent : SKColors.White);
        var m = new Matrix2(pixelsPerMm, 0, 0, -pixelsPerMm, -area.MinX * pixelsPerMm, area.MaxY * pixelsPerMm);
        new SkiaDrawingRenderer().Render(canvas, drawing, m, Rect2.Empty, new RenderOptions
        {
            PrintMode = true,
            TextResolver = textResolver,
            Images = images ?? new ImageProvider(),
            DeviceScale = pixelsPerMm,
            MinStrokePixels = 1,
        });
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static void ToSvg(CadDrawing drawing, Rect2 area, Stream output, Func<string, string>? textResolver = null, IImageProvider? images = null)
    {
        if (area.IsEmpty) area = drawing.GetExtents().Inflate(5);
        // SVG user units = millimetres scaled to points for broad viewer compatibility.
        var s = PointsPerMm;
        var bounds = new SKRect(0, 0, (float)(area.Width * s), (float)(area.Height * s));
        using (var managed = new SKManagedWStream(output, disposeManagedStream: false))
        using (var canvas = SKSvgCanvas.Create(bounds, managed))
        {
            var m = new Matrix2(s, 0, 0, -s, -area.MinX * s, area.MaxY * s);
            new SkiaDrawingRenderer().Render(canvas, drawing, m, Rect2.Empty, new RenderOptions
            {
                PrintMode = true,
                TextResolver = textResolver,
                Images = images ?? new ImageProvider(),
                DeviceScale = s,
                MinStrokePixels = 0.25f,
            });
        }
        output.Flush();
    }

    /// <summary>Writes a multi-page vector PDF; each page is scaled to fit its paper size.</summary>
    public static void ToPdf(IReadOnlyList<DrawingPage> pages, Stream output, PdfMetadataInfo? metadata = null, IImageProvider? images = null)
    {
        metadata ??= new PdfMetadataInfo();
        var meta = new SKDocumentPdfMetadata
        {
            Title = metadata.Title,
            Author = metadata.Author,
            Subject = metadata.Subject,
            Keywords = metadata.Keywords,
            Creator = ProductInfo.FullTitle,
            Producer = $"{ProductInfo.ProductName} — {ProductInfo.PoweredBy}",
            Creation = DateTime.Now,
            Modified = DateTime.Now,
            RasterDpi = 300,
        };
        images ??= new ImageProvider();
        using var managed = new SKManagedWStream(output, disposeManagedStream: false);
        using var doc = SKDocument.CreatePdf(managed, meta);
        var renderer = new SkiaDrawingRenderer();
        foreach (var page in pages)
        {
            var pw = (float)(page.PaperWidthMm * PointsPerMm);
            var ph = (float)(page.PaperHeightMm * PointsPerMm);
            var canvas = doc.BeginPage(pw, ph);
            var area = page.Area.IsEmpty ? page.Drawing.GetExtents().Inflate(5) : page.Area;
            var scale = Math.Min(page.PaperWidthMm / area.Width, page.PaperHeightMm / area.Height) * PointsPerMm;
            var offX = (pw - area.Width * scale) / 2;
            var offY = (ph - area.Height * scale) / 2;
            var m = new Matrix2(scale, 0, 0, -scale, offX - area.MinX * scale, offY + area.MaxY * scale);
            renderer.ClearCache();
            renderer.Render(canvas, page.Drawing, m, Rect2.Empty, new RenderOptions
            {
                PrintMode = true,
                TextResolver = page.TextResolver,
                Images = images,
                DeviceScale = scale,
                MinStrokePixels = 0.2f,
            });
            doc.EndPage();
        }
        doc.Close();
        output.Flush();
    }
}
