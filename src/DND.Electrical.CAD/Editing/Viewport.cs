using DND.Electrical.Core.Geometry;

namespace DND.Electrical.CAD.Editing;

/// <summary>
/// World (mm, Y up) ↔ screen (pixels, Y down) transform with zoom and pan.
/// </summary>
public sealed class Viewport
{
    public const double MinScale = 0.02;
    public const double MaxScale = 400;

    /// <summary>Pixels per millimetre.</summary>
    public double Scale { get; private set; } = 3;

    /// <summary>Screen position of the world origin.</summary>
    public Point2 Origin { get; private set; } = new(40, 600);

    public double Width { get; private set; } = 800;
    public double Height { get; private set; } = 600;

    public event EventHandler? Changed;

    public void SetSize(double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        Width = width;
        Height = height;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Point2 ToScreen(Point2 w) => new(Origin.X + w.X * Scale, Origin.Y - w.Y * Scale);
    public Point2 ToWorld(Point2 s) => new((s.X - Origin.X) / Scale, (Origin.Y - s.Y) / Scale);
    public double ToWorldDistance(double pixels) => pixels / Scale;
    public double ToScreenDistance(double mm) => mm * Scale;

    public Rect2 VisibleWorld => Rect2.FromPoints(ToWorld(new Point2(0, 0)), ToWorld(new Point2(Width, Height)));

    /// <summary>World→screen as an affine matrix (useful for renderers).</summary>
    public Matrix2 WorldToScreenMatrix => new(Scale, 0, 0, -Scale, Origin.X, Origin.Y);

    public void ZoomAt(Point2 screen, double factor)
    {
        var world = ToWorld(screen);
        Scale = Math.Clamp(Scale * factor, MinScale, MaxScale);
        Origin = new Point2(screen.X - world.X * Scale, screen.Y + world.Y * Scale);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Pan(double dxPixels, double dyPixels)
    {
        Origin = new Point2(Origin.X + dxPixels, Origin.Y + dyPixels);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomToFit(Rect2 world, double marginPixels = 24)
    {
        if (world.IsEmpty || world.Width <= 0 && world.Height <= 0)
        {
            world = Rect2.FromCenter(world.IsEmpty ? Point2.Zero : world.Center, 50, 50);
        }
        var sx = (Width - 2 * marginPixels) / Math.Max(world.Width, 1e-6);
        var sy = (Height - 2 * marginPixels) / Math.Max(world.Height, 1e-6);
        Scale = Math.Clamp(Math.Min(sx, sy), MinScale, MaxScale);
        var c = world.Center;
        Origin = new Point2(Width / 2 - c.X * Scale, Height / 2 + c.Y * Scale);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetView(double scale, Point2 origin)
    {
        Scale = Math.Clamp(scale, MinScale, MaxScale);
        Origin = origin;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Zoom percentage relative to 1:1 at 96 DPI (3.78 px/mm).</summary>
    public double ZoomPercent => Scale / (96 / 25.4) * 100;
}
