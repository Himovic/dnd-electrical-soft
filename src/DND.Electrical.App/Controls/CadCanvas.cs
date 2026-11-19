using System.Windows;
using System.Windows.Input;
using DND.Electrical.CAD.Editing;
using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;
using DND.Electrical.Rendering;
using DND.Electrical.Schematic.Editing;
using DND.Electrical.Schematic.Services;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace DND.Electrical.App.Controls;

/// <summary>
/// WPF host of the CAD engine: renders a <see cref="CadEditor"/> with SkiaSharp and forwards mouse and keyboard
/// input. All editing behaviour lives in the (UI-independent) editor and tools.
/// </summary>
public sealed class CadCanvas : SKElement
{
    public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(nameof(Editor), typeof(CadEditor), typeof(CadCanvas),
        new PropertyMetadata(null, (d, e) => ((CadCanvas)d).Attach(e.OldValue as CadEditor, e.NewValue as CadEditor)));

    /// <summary>Alternative to <see cref="Editor"/>: displays a drawing (read-only view with zoom/pan/select).</summary>
    public static readonly DependencyProperty DrawingProperty = DependencyProperty.Register(nameof(Drawing), typeof(CadDrawing), typeof(CadCanvas),
        new PropertyMetadata(null, (d, e) => { if (e.NewValue is CadDrawing dr) ((CadCanvas)d).Editor = new CadEditor(dr); }));

    public static readonly DependencyProperty DarkProperty = DependencyProperty.Register(nameof(Dark), typeof(bool), typeof(CadCanvas),
        new PropertyMetadata(false, (d, _) => ((CadCanvas)d).InvalidateVisual()));

    private readonly SkiaDrawingRenderer _renderer = new();
    private readonly ImageProvider _images = new();
    private Point? _panLast;
    private bool _fitted;

    public CadCanvas()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Cross;
        ClipToBounds = true;
        SizeChanged += (_, _) => Resize();
    }

    public CadEditor? Editor { get => (CadEditor?)GetValue(EditorProperty); set => SetValue(EditorProperty, value); }
    public CadDrawing? Drawing { get => (CadDrawing?)GetValue(DrawingProperty); set => SetValue(DrawingProperty, value); }
    public bool Dark { get => (bool)GetValue(DarkProperty); set => SetValue(DarkProperty, value); }

    /// <summary>Raised after a pointer operation completed (used for tutorials and status updates).</summary>
    public event EventHandler? OperationCompleted;

    private void Attach(CadEditor? old, CadEditor? editor)
    {
        if (old is not null) old.RedrawRequested -= OnRedraw;
        if (editor is null) return;
        editor.RedrawRequested += OnRedraw;
        _fitted = false;
        Resize();
        InvalidateVisual();
    }

    private void OnRedraw(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) InvalidateVisual();
        else Dispatcher.BeginInvoke(InvalidateVisual);
    }

    private void Resize()
    {
        if (Editor is not { } ed || ActualWidth <= 0 || ActualHeight <= 0) return;
        ed.Viewport.SetSize(ActualWidth, ActualHeight);
        if (!_fitted)
        {
            _fitted = true;
            if (ed is SchematicEditor se) ed.Viewport.ZoomToFit(new Rect2(0, 0, se.Sheet.Width, se.Sheet.Height), 16);
            else ed.ZoomExtents();
        }
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);
        var canvas = e.Surface.Canvas;
        var theme = Dark ? RenderTheme.Dark : RenderTheme.Light;
        canvas.Clear(theme.Background);
        if (Editor is not { } ed || ActualWidth <= 0) return;
        var dpi = e.Info.Width / ActualWidth;
        var vp = ed.Viewport;
        var m = vp.WorldToScreenMatrix * Matrix2.Scaling(dpi, dpi);
        Func<string, string>? resolver = ed is SchematicEditor se ? FieldResolver.CreateResolver(se.Project, se.Sheet) : null;
        if (ed is SchematicEditor s2 && s2.Sheet.Width > 0)
        {
            // Paper area behind the drawing.
            using var paper = new SKPaint { Color = Dark ? new SKColor(15, 23, 42) : SKColors.White };
            var a = vp.ToScreen(new Point2(0, s2.Sheet.Height)); var b = vp.ToScreen(new Point2(s2.Sheet.Width, 0));
            canvas.DrawRect((float)(a.X * dpi), (float)(a.Y * dpi), (float)((b.X - a.X) * dpi), (float)((b.Y - a.Y) * dpi), paper);
        }
        _renderer.Render(canvas, ed.Drawing, m, vp.VisibleWorld, new RenderOptions
        {
            Theme = theme,
            ShowGrid = ed.Snap.ShowGrid,
            GridSpacing = ed.Snap.GridSpacing,
            Selected = ed.Selection.Ids.ToHashSet(),
            Preview = ed.ActiveTool.Preview,
            SelectionWindow = ed.ActiveTool.SelectionWindow,
            SnapMarker = (ed.LastSnap.Point, ed.LastSnap.Kind),
            TextResolver = resolver,
            Images = _images,
            DeviceScale = vp.Scale * dpi,
            MinStrokePixels = (float)dpi,
        });
    }

    private Point2 P(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        return new Point2(p.X, p.Y);
    }

    private static KeyModifiers Mods()
    {
        var m = KeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) m |= KeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) m |= KeyModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) m |= KeyModifiers.Alt;
        return m;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (Editor is not { } ed) return;
        if (e.ChangedButton == MouseButton.Middle) { _panLast = e.GetPosition(this); CaptureMouse(); e.Handled = true; return; }
        var button = e.ChangedButton == MouseButton.Right ? PointerButton.Right : PointerButton.Left;
        CaptureMouse();
        ed.PointerDown(P(e), button, Mods(), e.ClickCount);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Editor is not { } ed) return;
        if (_panLast is { } last && e.MiddleButton == MouseButtonState.Pressed)
        {
            var p = e.GetPosition(this);
            ed.Viewport.Pan(p.X - last.X, p.Y - last.Y);
            _panLast = p;
            return;
        }
        ed.PointerMove(P(e), Mods());
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        ReleaseMouseCapture();
        if (Editor is not { } ed) return;
        if (e.ChangedButton == MouseButton.Middle) { _panLast = null; return; }
        ed.PointerUp(P(e), e.ChangedButton == MouseButton.Right ? PointerButton.Right : PointerButton.Left, Mods());
        OperationCompleted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Editor?.Wheel(P(e), e.Delta);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Editor is not { } ed || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        ToolKey? key = e.Key switch
        {
            Key.Escape => ToolKey.Escape,
            Key.Enter => ToolKey.Enter,
            Key.Delete => ToolKey.Delete,
            Key.Back => ToolKey.Backspace,
            Key.Space => ToolKey.Space,
            Key.C => ToolKey.C,
            Key.R => ToolKey.R,
            Key.M => ToolKey.M,
            Key.X => ToolKey.X,
            Key.F => ToolKey.F,
            _ => null,
        };
        if (key is { } k && ed.KeyDown(k, Mods()))
        {
            e.Handled = true;
            OperationCompleted?.Invoke(this, EventArgs.Empty);
        }
    }
}
