using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DND.Electrical.Presentation.ViewModels;
using HelixToolkit.Wpf;

namespace DND.Electrical.App.Controls;

/// <summary>
/// 3D viewer for <see cref="ThreeDViewModel"/> built on HelixToolkit.Wpf (orbit, pan, zoom, view cube). Meshes come
/// from the cross-platform 3D module in world millimetres with Z up; picking maps the hit model back to the scene
/// object so the selection is shared with the object list, measurement and the schematic.
/// </summary>
public sealed class Scene3DView : ContentControl
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(nameof(Model), typeof(ThreeDViewModel), typeof(Scene3DView), new PropertyMetadata(null, (d, e) => ((Scene3DView)d).Attach(e.OldValue as ThreeDViewModel, e.NewValue as ThreeDViewModel)));

    private readonly HelixViewport3D _viewport = new()
    {
        ModelUpDirection = new Vector3D(0, 0, 1), ShowViewCube = true, ShowCoordinateSystem = true, IsHeadLightEnabled = true,
        ZoomExtentsWhenLoaded = true, Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
    };
    private readonly ModelVisual3D _content = new();
    private readonly LinesVisual3D _section = new() { Color = Colors.OrangeRed, Thickness = 2 };
    private readonly Dictionary<GeometryModel3D, (Guid id, bool transparent)> _ids = new();
    private bool _firstBuild = true;

    public Scene3DView()
    {
        _viewport.Children.Add(new DefaultLights());
        _viewport.Children.Add(_content);
        _viewport.Children.Add(_section);
        _viewport.MouseLeftButtonUp += OnClick;
        Content = _viewport;
    }

    public ThreeDViewModel? Model { get => (ThreeDViewModel?)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }

    private void Attach(ThreeDViewModel? old, ThreeDViewModel? vm)
    {
        if (old is not null) old.PropertyChanged -= OnModelChanged;
        if (vm is not null) vm.PropertyChanged += OnModelChanged;
        _firstBuild = true;
        Build();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThreeDViewModel.RenderVersion)) Build();
        if (e.PropertyName == nameof(ThreeDViewModel.Scene)) _firstBuild = true;
    }

    private void Build()
    {
        _ids.Clear();
        var group = new Model3DGroup();
        var vm = Model;
        if (vm is not null)
        {
            // Opaque first, then transparent (enclosure panels) so that WPF blends them over the interior.
            foreach (var item in vm.RenderItems.OrderBy(i => i.Opacity < 1))
            {
                var mesh = new MeshGeometry3D();
                foreach (var p in item.Mesh.Positions) mesh.Positions.Add(new Point3D(p.X, p.Y, p.Z));
                foreach (var n in item.Mesh.Normals) mesh.Normals.Add(new Vector3D(n.X, n.Y, n.Z));
                foreach (var i in item.Mesh.Indices) mesh.TriangleIndices.Add(i);
                mesh.Freeze();
                var color = (Color)ColorConverter.ConvertFromString(item.Selected ? "#F97316" : item.Color);
                color.A = (byte)Math.Round(Math.Clamp(item.Opacity, 0.05, 1) * 255);
                var brush = new SolidColorBrush(color); brush.Freeze();
                var material = new MaterialGroup();
                material.Children.Add(new DiffuseMaterial(brush));
                if (item.Selected) material.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(90, 0xF9, 0x73, 0x16))));
                var model = new GeometryModel3D(mesh, material) { BackMaterial = item.Opacity < 1 ? material : null };
                _ids[model] = (item.SceneObjectId, item.Opacity < 1);
                group.Children.Add(model);
            }
            var pts = new Point3DCollection();
            foreach (var (a, b) in vm.SectionLines) { pts.Add(new Point3D(a.X, a.Y, a.Z)); pts.Add(new Point3D(b.X, b.Y, b.Z)); }
            _section.Points = pts;
        }
        _content.Content = group;
        if (_firstBuild && group.Children.Count > 0) { _viewport.ZoomExtents(); _firstBuild = false; }
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (Model is null) return;
        // Enclosure panels are see-through: prefer the first opaque object along the ray.
        Guid? opaque = null, any = null;
        VisualTreeHelper.HitTest(_viewport.Viewport, null, r =>
        {
            if (r is RayMeshGeometry3DHitTestResult m && m.ModelHit is GeometryModel3D g && _ids.TryGetValue(g, out var info))
            {
                any ??= info.id;
                if (!info.transparent) { opaque = info.id; return HitTestResultBehavior.Stop; }
            }
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(e.GetPosition(_viewport.Viewport)));
        var hit = opaque ?? any;
        if (hit is { } h) Model.Select(h);
    }
}
