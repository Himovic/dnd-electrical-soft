using SkiaSharp;

namespace DND.Electrical.Rendering;

/// <summary>
/// Supplies the bundled DejaVu Sans typeface (identical output on every platform) and the
/// ratio between CAD text height (cap height) and font em size.
/// </summary>
public static class FontProvider
{
    private static readonly Lazy<SKTypeface> RegularLazy = new(() => Load("Fonts.DejaVuSans.ttf"));
    private static readonly Lazy<SKTypeface> BoldLazy = new(() => Load("Fonts.DejaVuSans-Bold.ttf"));
    private static readonly Lazy<float> CapRatioLazy = new(ComputeCapRatio);

    public static SKTypeface Regular => RegularLazy.Value;
    public static SKTypeface Bold => BoldLazy.Value;

    /// <summary>Em size divided by cap height. Multiply CAD text height by this to get the font size.</summary>
    public static float EmPerCapHeight => CapRatioLazy.Value;

    public static SKFont CreateFont(double capHeight, bool bold = false) =>
        new(bold ? Bold : Regular, (float)(capHeight * EmPerCapHeight))
        {
            // Text is drawn at tiny world-unit sizes under a scaling matrix: hinting would round glyph
            // advances at that size and destroy spacing, so use unhinted linear metrics.
            Subpixel = true,
            LinearMetrics = true,
            Hinting = SKFontHinting.None,
            Edging = SKFontEdging.Antialias,
        };

    private static SKTypeface Load(string resource)
    {
        using var s = typeof(FontProvider).Assembly.GetManifestResourceStream(resource);
        if (s is not null)
        {
            var tf = SKTypeface.FromStream(s);
            if (tf is not null) return tf;
        }
        return SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default;
    }

    private static float ComputeCapRatio()
    {
        using var f = new SKFont(Regular, 100);
        var cap = f.Metrics.CapHeight;
        return cap > 1 ? 100f / cap : 1.4f;
    }
}

/// <summary>Image provider backed by in-memory PNG/JPEG bytes, with the D&amp;D logo built in.</summary>
public sealed class ImageProvider : IImageProvider
{
    private readonly Dictionary<string, SKImage?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<byte[]?>> _sources = new(StringComparer.OrdinalIgnoreCase);

    public const string BrandingLogoKey = "branding:logo";
    public const string CompanyLogoKey = "company:logo";

    public ImageProvider()
    {
        Register(BrandingLogoKey, LoadBrandingLogo);
    }

    public static byte[]? LoadBrandingLogo()
    {
        using var s = typeof(ImageProvider).Assembly.GetManifestResourceStream("Branding.dnd-logo.png");
        if (s is null) return null;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    public void Register(string key, Func<byte[]?> source)
    {
        _sources[key] = source;
        _cache.Remove(key);
    }

    public void RegisterFile(string key, string path) => Register(key, () => File.Exists(path) ? File.ReadAllBytes(path) : null);

    public SKImage? GetImage(string key)
    {
        // Projects without their own company logo show the product branding in the title block.
        if (string.Equals(key, CompanyLogoKey, StringComparison.OrdinalIgnoreCase) && !_sources.ContainsKey(CompanyLogoKey))
            key = BrandingLogoKey;
        if (_cache.TryGetValue(key, out var img)) return img;
        SKImage? result = null;
        if (_sources.TryGetValue(key, out var src) && src() is { Length: > 0 } bytes)
        {
            using var data = SKData.CreateCopy(bytes);
            result = SKImage.FromEncodedData(data);
        }
        _cache[key] = result;
        return result;
    }
}
