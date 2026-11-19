using DND.Electrical.CAD.Entities;
using DND.Electrical.Core.Geometry;
using SkiaSharp;

namespace DND.Electrical.Rendering;

public sealed class RenderTheme
{
    public SKColor Background { get; init; } = SKColors.White;
    public SKColor GridDot { get; init; } = new(148, 163, 184);
    public SKColor Selection { get; init; } = new(37, 99, 235);
    public SKColor Preview { get; init; } = new(234, 88, 12);
    public SKColor Highlight { get; init; } = new(219, 39, 119);
    public SKColor SnapMarker { get; init; } = new(22, 163, 74);

    /// <summary>When set, light/white entity colours are drawn with this colour (dark backgrounds invert black).</summary>
    public bool InvertBlack { get; init; }

    public static readonly RenderTheme Light = new();

    public static readonly RenderTheme Dark = new()
    {
        Background = new SKColor(17, 24, 39),
        GridDot = new SKColor(71, 85, 105),
        Selection = new SKColor(96, 165, 250),
        Preview = new SKColor(251, 146, 60),
        Highlight = new SKColor(244, 114, 182),
        SnapMarker = new SKColor(74, 222, 128),
        InvertBlack = true,
    };

    public static readonly RenderTheme Print = new() { Background = SKColors.White };
}

public sealed class RenderOptions
{
    public RenderTheme Theme { get; init; } = RenderTheme.Light;
    public bool ShowGrid { get; init; }
    public double GridSpacing { get; init; } = 5;
    public IReadOnlySet<Guid>? Selected { get; init; }
    public IReadOnlySet<Guid>? Highlighted { get; init; }
    public IReadOnlyList<CadEntity>? Preview { get; init; }
    public (Rect2 rect, bool crossing)? SelectionWindow { get; init; }
    public (Point2 point, CAD.Model.SnapKind kind)? SnapMarker { get; init; }

    /// <summary>Print mode: skips non-printable layers and draws everything in print colours.</summary>
    public bool PrintMode { get; init; }

    /// <summary>Monochrome output (all geometry black) – used for some printed drawing sets.</summary>
    public bool Monochrome { get; init; }

    /// <summary>Resolves field placeholders in text (e.g. <c>{SHEET.NUMBER}</c>).</summary>
    public Func<string, string>? TextResolver { get; init; }

    public IImageProvider? Images { get; init; }

    /// <summary>Minimum stroke width in device pixels so thin lines stay visible on screen.</summary>
    public float MinStrokePixels { get; init; } = 1f;

    /// <summary>Device units per millimetre (pixels on screen, points in PDF).</summary>
    public double DeviceScale { get; init; } = 1;
}

/// <summary>Resolves image keys (e.g. <c>branding:logo</c>) to decoded images.</summary>
public interface IImageProvider
{
    SKImage? GetImage(string key);
}
