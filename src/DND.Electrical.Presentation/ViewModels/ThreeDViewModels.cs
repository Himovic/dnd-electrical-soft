using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DND.Electrical.Application.Services;
using DND.Electrical.Domain;
using DND.Electrical.Licensing;
using DND.Electrical.Presentation.Services;
using DND.Electrical.ThreeD;

namespace DND.Electrical.Presentation.ViewModels;

/// <summary>World-space triangle mesh ready for a renderer (mm, Z up).</summary>
public sealed record RenderItem(Guid SceneObjectId, Guid? ObjectId, Mesh Mesh, string Color, double Opacity, bool Selected);

public sealed record SceneObjectRow(SceneObject3D Object)
{
    public string Tag => Object.Tag;
    public string Name => Object.Name;
    public string Kind => Object.Kind.ToString();
    public string Size => FormattableString.Invariant($"{Object.Size.X:0.#}×{Object.Size.Y:0.#}×{Object.Size.Z:0.#}");
    public string Position => Object.Position.ToString();
    public string Source => Object.Source.Length == 0 ? "User" : Object.Source.Split(':')[0];
}

/// <summary>
/// D&amp;D 3D module: cabinet 3D models generated from the 2D layout, user equipment and cable tray routes, clash and
/// clearance checks, 2D↔3D synchronisation, measurement, section and exploded views, glTF/GLB/STL export.
/// </summary>
public sealed partial class ThreeDViewModel : ObservableObject
{
    private readonly ElectricalProject _p;
    private readonly ThreeDService _svc;
    private readonly IDialogService _dialogs;
    private readonly Action _changed;
    private readonly Action<Guid> _showInSchematic;

    public ThreeDViewModel(ElectricalProject project, IEntitlements entitlements, IDialogService dialogs, Action changed, Action<Guid> showInSchematic, Guid? focusObjectId = null)
    {
        _p = project; _svc = new ThreeDService(entitlements); _dialogs = dialogs; _changed = changed; _showInSchematic = showInSchematic;
        ReloadLayouts();
        if (focusObjectId is { } id)
        {
            var doc = project.Layouts3D.FirstOrDefault(d => ThreeDService.Load(d).ForEngineeringObject(id).Any());
            if (doc is not null) SelectedLayout = doc;
            SelectEngineeringObject(id);
        }
        else SelectedLayout = Layouts.FirstOrDefault();
    }

    public string Title => $"3D layout – {_p.Info.Number}";
    public ObservableCollection<Layout3DDocument> Layouts { get; } = new();
    public IReadOnlyList<Cabinet> Cabinets => _p.Cabinets;
    public ObservableCollection<SceneObjectRow> Objects { get; } = new();
    public ObservableCollection<string> Findings { get; } = new();
    public IReadOnlyList<string> SectionAxes { get; } = new[] { "X", "Y", "Z" };
    public IReadOnlyList<RenderItem> RenderItems { get; private set; } = Array.Empty<RenderItem>();
    public IReadOnlyList<(Vec3 a, Vec3 b)> SectionLines { get; private set; } = Array.Empty<(Vec3, Vec3)>();

    [ObservableProperty] private Layout3DDocument? _selectedLayout;
    [ObservableProperty] private Scene3D? _scene;
    [ObservableProperty] private SceneObjectRow? _selectedObject;
    [ObservableProperty] private SceneObjectRow? _measureTarget;
    [ObservableProperty] private Cabinet? _selectedCabinet;
    [ObservableProperty] private double _explode;
    [ObservableProperty] private bool _sectionEnabled;
    [ObservableProperty] private string _sectionAxis = "Y";
    [ObservableProperty] private double _sectionOffset;
    [ObservableProperty] private bool _showEnclosure = true;
    [ObservableProperty] private string _measurement = string.Empty;
    /// <summary>Incremented whenever the render model changes (the view rebuilds its scene graph).</summary>
    [ObservableProperty] private int _renderVersion;

    /// <summary>Synchronises the layout list with the project without resetting the current selection.</summary>
    private void ReloadLayouts()
    {
        foreach (var gone in Layouts.Where(l => !_p.Layouts3D.Contains(l)).ToList()) Layouts.Remove(gone);
        foreach (var d in _p.Layouts3D.Where(d => !Layouts.Contains(d))) Layouts.Add(d);
        SelectedCabinet ??= _p.Cabinets.FirstOrDefault();
    }

    partial void OnSelectedLayoutChanged(Layout3DDocument? value)
    {
        Scene = value is null ? null : ThreeDService.Load(value);
        RefreshObjects();
    }

    partial void OnExplodeChanged(double value) => Rebuild();
    partial void OnSectionEnabledChanged(bool value) => Rebuild();
    partial void OnSectionAxisChanged(string value) => Rebuild();
    partial void OnSectionOffsetChanged(double value) => Rebuild();
    partial void OnShowEnclosureChanged(bool value) => Rebuild();
    partial void OnSelectedObjectChanged(SceneObjectRow? value) { UpdateMeasurement(); Rebuild(); }
    partial void OnMeasureTargetChanged(SceneObjectRow? value) => UpdateMeasurement();

    private void RefreshObjects()
    {
        var sel = SelectedObject?.Object.Id;
        Objects.Clear();
        if (Scene is not null)
            foreach (var o in Scene.Objects.Concat(Scene.TraySegments()).OrderBy(o => o.Kind).ThenBy(o => o.Tag, StringComparer.OrdinalIgnoreCase)) Objects.Add(new SceneObjectRow(o));
        SelectedObject = Objects.FirstOrDefault(r => r.Object.Id == sel);
        Rebuild();
    }

    private Vec3 SectionNormal => SectionAxis switch { "X" => Vec3.UnitX, "Z" => Vec3.UnitZ, _ => Vec3.UnitY };

    private void Rebuild()
    {
        var items = new List<RenderItem>();
        var lines = new List<(Vec3, Vec3)>();
        if (Scene is { } s)
        {
            var exploded = Explode > 0 ? SceneAnalysis.Exploded(s, Explode) : null;
            foreach (var o in s.Objects.Concat(s.TraySegments()).Where(o => o.Visible))
            {
                if (!ShowEnclosure && o.Kind is SceneObjectKind.EnclosurePanel or SceneObjectKind.Door) continue;
                var shift = exploded is not null && exploded.TryGetValue(o.Id, out var pos) ? pos - o.Position : Vec3.Zero;
                var mesh = o.WorldMesh();
                if (shift != Vec3.Zero) { var m = new Mesh(); m.Append(mesh, p => p + shift, n => n); mesh = m; }
                if (SectionEnabled) mesh = MeshClipper.Clip(mesh, SectionNormal, SectionOffset);
                if (mesh.TriangleCount == 0) continue;
                items.Add(new RenderItem(o.Id, o.ObjectId, mesh, o.Color, o.Opacity, SelectedObject?.Object.Id == o.Id));
            }
            if (SectionEnabled && exploded is null)
                lines.AddRange(SceneAnalysis.Section(s, SectionNormal, SectionOffset).Select(x => (x.A, x.B)));
        }
        RenderItems = items; SectionLines = lines;
        RenderVersion++;
    }

    private void UpdateMeasurement()
    {
        if (SelectedObject is null || MeasureTarget is null || SelectedObject == MeasureTarget) { Measurement = string.Empty; return; }
        var a = SelectedObject.Object; var b = MeasureTarget.Object;
        Measurement = FormattableString.Invariant($"{Label(a)} ↔ {Label(b)}: clear distance {SceneAnalysis.Distance(a, b):0.##} mm · centre distance {SceneAnalysis.Distance(a.Position, b.Position):0.##} mm");
    }

    private static string Label(SceneObject3D o) => o.Tag.Length > 0 ? o.Tag : o.Name;

    /// <summary>Selection from the viewer (hit test) or from another representation.</summary>
    public void Select(Guid sceneObjectId) => SelectedObject = Objects.FirstOrDefault(r => r.Object.Id == sceneObjectId);

    public void SelectEngineeringObject(Guid objectId) => SelectedObject = Objects.FirstOrDefault(r => r.Object.ObjectId == objectId) ?? SelectedObject;

    private bool Guard(Action action)
    {
        try { action(); return true; }
        catch (LicenseException ex) { _dialogs.ShowError(Title, ex.Message); return false; }
    }

    private void Persist()
    {
        if (Scene is null) return;
        _svc.Save(_p, Scene, SelectedLayout?.Kind ?? "Plant", SelectedLayout?.CabinetId);
        _changed();
        ReloadLayouts();
        RefreshObjects();
    }

    [RelayCommand]
    private void GenerateFromCabinet()
    {
        if (SelectedCabinet is not { } c) { _dialogs.ShowError(Title, "The project has no cabinet layout to generate from."); return; }
        Guard(() =>
        {
            var scene = _svc.GenerateCabinet(_p, c);
            _changed();
            ReloadLayouts();
            var doc = Layouts.First(l => l.Id == scene.Id);
            if (SelectedLayout == doc) OnSelectedLayoutChanged(doc); else SelectedLayout = doc;
            CheckClashes();
        });
    }

    [RelayCommand]
    private void NewPlantLayout()
    {
        var name = _dialogs.PromptText("New 3D layout", "Name (room, plant area, tray network…)", "Plant area");
        if (string.IsNullOrWhiteSpace(name)) return;
        Guard(() =>
        {
            var scene = new Scene3D { Name = name.Trim() };
            _svc.Save(_p, scene, "Plant");
            _changed();
            ReloadLayouts();
            SelectedLayout = Layouts.First(l => l.Id == scene.Id);
        });
    }

    [RelayCommand]
    private void AddEquipment()
    {
        if (Scene is null) { _dialogs.ShowError(Title, "Create or select a layout first."); return; }
        var spec = _dialogs.PromptText("Add equipment", "Tag; width×depth×height mm; x,y,z of centre mm", "EQ1; 800×600×2000; 400,300,1000");
        if (spec is null) return;
        try
        {
            var parts = spec.Split(';', StringSplitOptions.TrimEntries);
            var size = ParseVec(parts[1].Replace('×', ',').Replace('x', ','));
            var pos = ParseVec(parts[2]);
            var device = _p.FindDeviceByTag(parts[0]);
            Scene.Objects.Add(new SceneObject3D { Tag = parts[0], Name = device?.Function ?? parts[0], ObjectId = device?.Id, Kind = SceneObjectKind.Equipment, Size = size, Position = pos, Color = "#0EA5E9" });
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException) { _dialogs.ShowError(Title, "Use the format: TAG; W×D×H; X,Y,Z (millimetres)."); return; }
        Guard(Persist);
    }

    [RelayCommand]
    private void AddTrayRoute()
    {
        if (Scene is null) { _dialogs.ShowError(Title, "Create or select a layout first."); return; }
        var spec = _dialogs.PromptText("Add cable tray route", "Tag; width×height mm; points x,y,z / x,y,z / …", "TR-01; 300×60; 0,0,2500 / 4000,0,2500 / 4000,3000,2500");
        if (spec is null) return;
        try
        {
            var parts = spec.Split(';', StringSplitOptions.TrimEntries);
            var wh = parts[1].Replace('×', 'x').Split('x');
            var route = new CableTrayRoute
            {
                Tag = parts[0], WidthMm = double.Parse(wh[0], CultureInfo.InvariantCulture), HeightMm = double.Parse(wh[1], CultureInfo.InvariantCulture),
                Points = parts[2].Split('/', StringSplitOptions.TrimEntries).Select(ParseVec).ToList(),
            };
            if (route.Points.Count < 2) throw new FormatException();
            route.CableIds = _p.Cables.Where(c => c.Route.Split(',', StringSplitOptions.TrimEntries).Contains(route.Tag, StringComparer.OrdinalIgnoreCase)).Select(c => c.Id).ToList();
            Scene.TrayRoutes.Add(route);
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException) { _dialogs.ShowError(Title, "Use the format: TAG; W×H; x,y,z / x,y,z (millimetres, at least two points)."); return; }
        Guard(Persist);
        TrayFill();
    }

    private static Vec3 ParseVec(string s)
    {
        var v = s.Split(',', StringSplitOptions.TrimEntries).Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        if (v.Length != 3) throw new FormatException();
        return new Vec3(v[0], v[1], v[2]);
    }

    [RelayCommand]
    private void MoveSelected()
    {
        if (SelectedObject?.Object is not { } o || Scene?.Find(o.Id) is null) { _dialogs.ShowError(Title, "Select an object of the layout (tray segments move with their route)."); return; }
        var text = _dialogs.PromptText("Move", "Offset dx,dy,dz (mm)", "0,0,0");
        if (text is null) return;
        try { o.Position += ParseVec(text); } catch (FormatException) { _dialogs.ShowError(Title, "Enter three numbers: dx,dy,dz."); return; }
        if (o.SourceItemId is not null && _dialogs.Confirm("2D/3D synchronisation", $"Also update the 2D cabinet layout position of {Label(o)}?"))
            CabinetModel.PushToLayout(o, _p);
        Guard(Persist);
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedObject?.Object is not { } o || Scene?.Find(o.Id) is null) return;
        if (o.Source.Length > 0) { _dialogs.ShowError(Title, "Generated objects are removed in their source (the 2D cabinet layout); regenerate afterwards."); return; }
        if (!_dialogs.Confirm(Title, $"Delete {Label(o)} from the 3D layout?")) return;
        Scene.Objects.Remove(o);
        Guard(Persist);
    }

    [RelayCommand]
    private void CheckClashes()
    {
        if (Scene is null) return;
        Guard(() =>
        {
            Findings.Clear();
            var clashes = _svc.Clashes(_p, Scene);
            foreach (var c in clashes) Findings.Add($"{(c.Kind == ClashKind.Hard ? "Clash" : "Clearance")}: {c.Description}");
            Findings.Add(clashes.Count == 0 ? "No clashes or clearance violations found." : $"{clashes.Count} finding(s).");
        });
    }

    [RelayCommand]
    private void CheckSync()
    {
        if (Scene is null) return;
        Guard(() =>
        {
            Findings.Clear();
            var issues = _svc.Compare(_p, Scene);
            foreach (var i in issues) Findings.Add($"{i.Kind}: {i.Message}");
            Findings.Add(issues.Count == 0 ? "2D and 3D representations are consistent." : $"{issues.Count} difference(s). Use 'Regenerate from 2D' to update the generated objects.");
        });
    }

    [RelayCommand]
    private void Regenerate()
    {
        var cabId = SelectedLayout?.CabinetId;
        var cab = cabId is null ? null : _p.Cabinets.FirstOrDefault(c => c.Id == cabId);
        if (cab is null) { _dialogs.ShowError(Title, "This layout was not generated from a cabinet."); return; }
        if (!_dialogs.Confirm(Title, $"Replace the generated objects of {cab.Tag} with the current 2D layout? Objects you added are kept.")) return;
        Guard(() => { _svc.GenerateCabinet(_p, cab); _changed(); OnSelectedLayoutChanged(SelectedLayout); CheckClashes(); });
    }

    [RelayCommand]
    private void TrayFill()
    {
        if (Scene is null) return;
        Findings.Clear();
        foreach (var r in Scene.TrayRoutes)
        {
            var f = SceneAnalysis.TrayFill(r, _p);
            Findings.Add(FormattableString.Invariant($"{r.Tag}: {r.LengthMm / 1000:0.##} m, {r.CableIds.Count} cable(s), fill {f.FillPercent:0.#} % of {r.WidthMm:0}×{r.HeightMm:0} mm (limit {r.MaxFillPercent:0} %){(f.Exceeded ? " – EXCEEDED" : "")}"));
            if (!f.Complete) Findings.Add($"  Fill incomplete – no outer diameter for: {string.Join(", ", f.CablesWithoutDiameter)}");
        }
        if (Scene.TrayRoutes.Count == 0) Findings.Add("No cable tray routes in this layout.");
    }

    [RelayCommand]
    private void ShowInSchematic()
    {
        if (SelectedObject?.Object.ObjectId is { } id) _showInSchematic(id);
        else _dialogs.ShowError(Title, "The selected 3D object is not linked to an engineering object.");
    }

    [RelayCommand]
    private void Export(string? format)
    {
        if (Scene is null) return;
        var ext = format is "gltf" or "stl" ? format : "glb";
        var path = _dialogs.SaveFile("Export 3D model", ext switch { "gltf" => "glTF 2.0 (*.gltf)|*.gltf", "stl" => "STL (*.stl)|*.stl", _ => "glTF binary (*.glb)|*.glb" }, $"{_p.Info.Number}-{Scene.Name}.{ext}");
        if (path is null) return;
        Guard(() =>
        {
            _svc.Export(Scene, path, Explode > 0 ? SceneAnalysis.Exploded(Scene, Explode) : null);
            _dialogs.ShowInfo(Title, $"3D model written: {path} ({new FileInfo(path).Length:N0} bytes).");
        });
    }
}
