using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DND.Electrical.Application.Abstractions;
using DND.Electrical.Application.Demo;
using DND.Electrical.Application.Projects;
using DND.Electrical.Application.Services;
using DND.Electrical.CAD.Dxf;
using DND.Electrical.CAD.Editing;
using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;
using DND.Electrical.CAD.Tools;
using DND.Electrical.Core.Geometry;
using DND.Electrical.Documentation;
using DND.Electrical.Domain;
using DND.Electrical.Presentation.Services;
using DND.Electrical.Reports;
using DND.Electrical.Schematic.Editing;
using DND.Electrical.Schematic.Entities;
using DND.Electrical.Schematic.Services;
using DND.Electrical.Shared;
using DND.Electrical.Symbols;
using DND.Electrical.Validation;
using DND.Electrical.Wiring;

namespace DND.Electrical.Presentation.ViewModels;

/// <summary>Application shell: project lifecycle, documents, panels, editing, engineering automation and exports.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly CadCommandProcessor _commands = new();
    private readonly Dictionary<Guid, UndoManager> _undoBySheet = new();
    private bool _resettingSavePoints;

    public MainViewModel(AppServices services)
    {
        _s = services;
        Symbols = new SymbolBrowserViewModel(services.Symbols);
        CommandLine.Executor = ExecuteCommandLine;
        RegisterSchematicCommands();
        services.Projects.SessionChanged += (_, _) => OnSessionChanged();
        services.Tutorials.Changed += (_, _) => OnPropertyChanged(nameof(TutorialStep));
        services.Tutorials.Completed += (_, t) => { services.Settings.CompletedTutorials.Add(t.Id); SaveSettings(); Log($"Tutorial completed: {t.Title}"); };
        services.Events.Published += (_, e) => services.Tutorials.OnEvent(e);
        IsDarkTheme = services.Settings.Theme == "Dark";
        AdvancedMode = services.Settings.AdvancedMode;
        RefreshStartPage();
    }

    public AppServices Services => _s;
    public ProjectExplorerViewModel Explorer { get; } = new();
    public SymbolBrowserViewModel Symbols { get; }
    public ValidationPanelViewModel Validation { get; } = new();
    public CommandLineViewModel CommandLine { get; } = new();
    public ObservableCollection<PropertyItem> Properties { get; } = new();
    public ObservableCollection<string> Output { get; } = new();
    public ObservableCollection<SheetEditorViewModel> Documents { get; } = new();
    public ObservableCollection<string> RecentProjects { get; } = new();
    public ObservableCollection<RecoveryEntry> Recoverable { get; } = new();
    public IReadOnlyList<DemoProjectInfo> DemoProjects => DemoProjectFactory.Catalogue;
    public ObservableCollection<SearchResult> SearchResults { get; } = new();

    [ObservableProperty] private SheetEditorViewModel? _activeDocument;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private bool _isDarkTheme;
    [ObservableProperty] private bool _advancedMode;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private string _propertiesTitle = "No selection";

    public ProjectSession? Session => _s.Projects.Current;
    public bool IsProjectOpen => Session is not null;
    public bool ShowStartPage => Session is null || Documents.Count == 0;
    public string Title => Session is null ? ProductInfo.FullTitle : $"{Session.DisplayName} — {ProductInfo.ProductName}";
    public string PoweredBy => ProductInfo.PoweredBy;
    public TutorialStep? TutorialStep => _s.Tutorials.CurrentStep;
    public string TutorialTitle => _s.Tutorials.Active?.Title ?? string.Empty;
    public CadEditor? Editor => ActiveDocument?.Editor;

    /// <summary>
    /// Feature gate: shows the upgrade/activation page when the module is not licensed. Services enforce the
    /// same entitlements again at execution time (defence in depth).
    /// </summary>
    public bool Require(DND.Electrical.Licensing.EngineeringModule module)
    {
        if (_s.Licensing.IsEnabled(module)) return true;
        _s.Dialogs.ShowDialog(new ModuleLockedViewModel(module, _s.Licensing.Current, () => OpenLicensing()));
        return false;
    }

    [RelayCommand]
    public void OpenLicensing() => _s.Dialogs.Show(new LicensingViewModel(_s.Licensing, _s.Settings, _s.Dialogs, () => { SaveSettings(); OnLicenseChanged(); }, _s.Paths));

    public string LicenseStatusText => $"{_s.Licensing.Current.State} · {_s.Licensing.LicensedModules.Count} module(s)";

    private void OnLicenseChanged()
    {
        OnPropertyChanged(nameof(LicenseStatusText));
        foreach (var d in Documents) ApplyLicenseToEditor(d.Editor);
        Log("Licence: " + _s.Licensing.Current.Message);
    }

    private void ApplyLicenseToEditor(CadEditor editor)
    {
        editor.IsReadOnly = !_s.Licensing.IsEnabled(DND.Electrical.Licensing.EngineeringModule.ELEC);
        editor.ReadOnlyReason = "Read-only: the Electrical Engineering (ELEC) module is not licensed. " + _s.Licensing.Current.Message;
        if (editor.IsReadOnly && editor.ActiveTool is not SelectTool) editor.SetTool(new SelectTool());
    }

    /// <summary>Called once after startup: proposes activation when no usable licence is installed.</summary>
    public void CheckLicenseOnStartup()
    {
        Log("Licence: " + _s.Licensing.Current.Message);
        if (!_s.Licensing.Current.AllowsFeatures) OpenLicensing();
    }

    public void Log(string message)
    {
        Output.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        while (Output.Count > 500) Output.RemoveAt(0);
        StatusText = message;
    }

    private void SaveSettings()
    {
        try { _s.SettingsStore.Save(_s.Settings); } catch (IOException ex) { Log("Could not save settings: " + ex.Message); }
    }

    partial void OnIsDarkThemeChanged(bool value) { _s.Settings.Theme = value ? "Dark" : "Light"; SaveSettings(); }
    partial void OnAdvancedModeChanged(bool value) { _s.Settings.AdvancedMode = value; SaveSettings(); }

    partial void OnActiveDocumentChanged(SheetEditorViewModel? value)
    {
        OnPropertyChanged(nameof(Editor));
        OnPropertyChanged(nameof(ShowStartPage));
        UpdateProperties();
        if (value is not null) CommandLine.Prompt = value.Editor.Prompt;
    }

    // ================================================================== start page & lifecycle

    public void RefreshStartPage()
    {
        RecentProjects.Clear();
        foreach (var r in _s.Settings.RecentProjects.Where(File.Exists)) RecentProjects.Add(r);
        Recoverable.Clear();
        foreach (var r in _s.Projects.Recovery.FindRecoverable()) Recoverable.Add(r);
    }

    private void OnSessionChanged()
    {
        foreach (var d in Documents.ToList()) Documents.Remove(d);
        _undoBySheet.Clear();
        ActiveDocument = null;
        Explorer.Refresh(Session?.Project);
        Validation.Issues.Clear();
        Validation.Summary = "Not validated yet.";
        if (Session is not null) Session.Changed += (_, _) => OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Session));
        OnPropertyChanged(nameof(IsProjectOpen));
        foreach (var prop in GetType().GetProperties().Where(p => typeof(IRelayCommand).IsAssignableFrom(p.PropertyType)))
            (prop.GetValue(this) as IRelayCommand)?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ShowStartPage));
    }

    /// <summary>Asks about unsaved changes. Returns false when the user cancels.</summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (Session is not { IsDirty: true } s) return true;
        switch (_s.Dialogs.AskSaveChanges(s.DisplayName))
        {
            case SaveChangesAnswer.Cancel: return false;
            case SaveChangesAnswer.Save: return await SaveInternalAsync(saveAs: false);
            default: return true;
        }
    }

    [RelayCommand]
    private async Task NewProjectAsync()
    {
        if (!await ConfirmCloseAsync()) return;
        var vm = new NewProjectViewModel(_s.Settings.UserName);
        if (_s.Dialogs.ShowDialog(vm) != true) return;
        var session = _s.Projects.CreateNew(vm.SelectedTemplate, vm.ToInfo(), _s.Settings.UserName);
        session.MarkDirty();
        Log($"Project {session.Project.Info.Number} created from template '{vm.SelectedTemplate.Name}'.");
        _s.Events.Publish(AppEvents.ProjectCreated);
        OfferTutorial(AppEvents.ProjectCreated);
        OpenFirstSheet();
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        var path = _s.Dialogs.OpenFile("Open project", FileFilters.Project);
        if (path is not null) await OpenPathAsync(path);
    }

    [RelayCommand]
    public async Task OpenPathAsync(string? path)
    {
        if (string.IsNullOrEmpty(path) || !await ConfirmCloseAsync()) return;
        try
        {
            _s.Projects.Close();
            ProjectSession session;
            try { session = await _s.Projects.OpenAsync(path); }
            catch (ProjectLockedException ex)
            {
                if (!_s.Dialogs.Confirm("Project in use", ex.Message + "\n\nOpen anyway? Changes by the other user may be lost.")) return;
                session = await _s.Projects.OpenAsync(path, forceLock: true);
            }
            _s.Settings.AddRecent(path);
            SaveSettings();
            Log($"Opened {session.Project.Info.Number} ({session.Project.Sheets.Count} sheets).");
            OpenFirstSheet();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            _s.Dialogs.ShowError("Cannot open project", ex.Message);
        }
        RefreshStartPage();
    }

    /// <summary>Generates a demonstration project, saves it in the demo folder and opens it.</summary>
    [RelayCommand]
    public async Task OpenDemoAsync(string? number)
    {
        if (number is null || !await ConfirmCloseAsync()) return;
        Directory.CreateDirectory(_s.Paths.DemoProjects);
        var path = Path.Combine(_s.Paths.DemoProjects, number + ProductInfo.ProjectFileExtension);
        if (!File.Exists(path))
        {
            var project = _s.Demos.Create(number);
            await _s.Store.SaveAsync(project, path);
        }
        await OpenPathAsync(path);
    }

    [RelayCommand]
    private async Task RecoverAsync(RecoveryEntry? entry)
    {
        if (entry is null || !await ConfirmCloseAsync()) return;
        await _s.Projects.RecoverAsync(entry);
        Log($"Recovered {entry.ProjectName} from autosave of {entry.SavedAt:g}. Save to keep the recovered changes.");
        OpenFirstSheet();
        RefreshStartPage();
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private Task SaveAsync() => SaveInternalAsync(saveAs: false);

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private Task SaveAsAsync() => SaveInternalAsync(saveAs: true);

    private async Task<bool> SaveInternalAsync(bool saveAs)
    {
        var s = Session;
        if (s is null) return false;
        try
        {
            if (saveAs || s.FilePath is null)
            {
                var path = _s.Dialogs.SaveFile("Save project as", FileFilters.Project, SafeName(s.Project.Info.Number) + ProductInfo.ProjectFileExtension);
                if (path is null) return false;
                await _s.Projects.SaveAsAsync(path);
                _s.Settings.AddRecent(path);
                SaveSettings();
            }
            else await _s.Projects.SaveAsync();
            _resettingSavePoints = true;
            try { foreach (var u in _undoBySheet.Values) u.MarkSaved(); }
            finally { _resettingSavePoints = false; }
            Log($"Saved {s.FilePath}");
            OnPropertyChanged(nameof(Title));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ProjectLockedException)
        {
            _s.Dialogs.ShowError("Save failed", ex.Message + "\n\nYour changes are still in memory and in the autosave copy.");
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private async Task CloseProjectAsync()
    {
        if (!await ConfirmCloseAsync()) return;
        _s.Projects.Close();
        RefreshStartPage();
    }

    /// <summary>Called periodically by the shell.</summary>
    public async Task AutosaveAsync()
    {
        try
        {
            if (Session is { IsDirty: true })
            {
                await _s.Projects.AutosaveAsync();
                StatusText = $"Autosaved at {DateTime.Now:HH:mm}";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log("Autosave failed: " + ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private async Task ExportArchiveAsync()
    {
        var p = Session!.Project;
        var path = _s.Dialogs.SaveFile("Export project archive", FileFilters.Archive, SafeName(p.Info.Number) + ProductInfo.ProjectArchiveExtension);
        if (path is null) return;
        await ProjectArchive.ExportAsync(_s.Store, p, path);
        Log($"Archive written: {path}");
    }

    [RelayCommand]
    private async Task ImportArchiveAsync()
    {
        var pkg = _s.Dialogs.OpenFile("Import project archive", FileFilters.Archive);
        if (pkg is null) return;
        var target = _s.Dialogs.SaveFile("Save imported project as", FileFilters.Project, Path.GetFileNameWithoutExtension(pkg) + ProductInfo.ProjectFileExtension);
        if (target is null) return;
        try
        {
            await ProjectArchive.ImportAsync(pkg, target);
            await OpenPathAsync(target);
        }
        catch (InvalidDataException ex) { _s.Dialogs.ShowError("Invalid archive", ex.Message); }
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void CreateRevision()
    {
        var code = _s.Dialogs.PromptText("New revision", "Revision code", NextRevision(Session!.Project.Info.CurrentRevision));
        if (string.IsNullOrWhiteSpace(code)) return;
        var desc = _s.Dialogs.PromptText("New revision", "Description", "") ?? string.Empty;
        try
        {
            _s.Projects.CreateRevision(code.Trim(), desc, _s.Settings.UserName);
            RefreshAll();
            Log($"Revision {code} created.");
        }
        catch (InvalidOperationException ex) { _s.Dialogs.ShowError("Revision", ex.Message); }
    }

    private static string NextRevision(string current) =>
        current.Length == 1 && char.IsLetter(current[0]) && current[0] < 'Z' ? ((char)(current[0] + 1)).ToString() : current + "1";

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void ProjectProperties()
    {
        var vm = new ProjectPropertiesViewModel(Session!.Project);
        if (_s.Dialogs.ShowDialog(vm) == true)
        {
            vm.Apply();
            Session.MarkDirty();
            RefreshAll();
        }
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void AddAttachment()
    {
        var file = _s.Dialogs.OpenFile("Attach file", FileFilters.Any);
        if (file is null) return;
        try { _s.Projects.AddAttachment(file, string.Empty); Explorer.Refresh(Session!.Project); }
        catch (InvalidOperationException ex) { _s.Dialogs.ShowError("Attachment", ex.Message); }
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void VersionHistory()
    {
        if (Session?.FilePath is not { } path) { _s.Dialogs.ShowInfo("Version history", "Save the project first."); return; }
        var backups = _s.Projects.Backups.List(path);
        if (backups.Count == 0) { _s.Dialogs.ShowInfo("Version history", "No backups yet. A backup is created on every save."); return; }
        var list = string.Join("\n", backups.Select((b, i) => $"{i + 1}. {b.Created:g}  ({b.Size / 1024} KB)"));
        var choice = _s.Dialogs.PromptText("Restore a backup", list + "\n\nNumber of the version to restore (the current file is backed up first):", "");
        if (!int.TryParse(choice, out var n) || n < 1 || n > backups.Count) return;
        if (!_s.Dialogs.Confirm("Restore", $"Restore the version of {backups[n - 1].Created:g}? Unsaved changes are discarded.")) return;
        _s.Projects.Close(discardRecovery: false);
        _s.Projects.Backups.Restore(backups[n - 1], path);
        _ = OpenPathAsync(path);
    }

    // ================================================================== documents

    private void OpenFirstSheet()
    {
        var p = Session?.Project;
        if (p is null) return;
        var sheet = p.OrderedSheets.FirstOrDefault(s => s.Kind != SheetKind.Cover) ?? p.OrderedSheets.FirstOrDefault();
        if (sheet is not null) OpenSheet(sheet);
    }

    [RelayCommand]
    public void OpenSheet(Sheet? sheet)
    {
        if (sheet is null || Session is null) return;
        var existing = Documents.FirstOrDefault(d => d.Sheet == sheet);
        if (existing is not null) { ActiveDocument = existing; return; }
        if (!_undoBySheet.TryGetValue(sheet.Id, out var undo)) _undoBySheet[sheet.Id] = undo = new UndoManager();
        var editor = new SchematicEditor(Session.Project, sheet, _s.Schematic, undo);
        editor.Snap.GridSpacing = _s.Settings.GridSpacing;
        editor.Snap.ShowGrid = _s.Settings.ShowGrid;
        editor.Snap.Ortho = _s.Settings.OrthoMode;
        editor.TextInputProvider = label => _s.Dialogs.PromptText("Text", label);
        undo.Changed += (_, _) => { if (!_resettingSavePoints) Session?.MarkDirty(); };
        editor.ProjectChanged += (_, _) => Explorer.Refresh(Session?.Project);
        editor.Selection.Changed += (_, _) => { if (ActiveDocument?.Editor == editor) UpdateProperties(); };
        editor.EntityActivated += (_, e) => { UpdateProperties(); _s.Dialogs.Show(new EntityActivatedHint(e)); };
        editor.ToolChanged += (_, _) => CommandLine.Prompt = editor.Prompt;
        editor.StatusMessage += (_, m) => Log(m);
        ApplyLicenseToEditor(editor);
        var doc = new SheetEditorViewModel(sheet, editor);
        Documents.Add(doc);
        ActiveDocument = doc;
        _s.Events.Publish(AppEvents.SchematicEditorOpened);
        OfferTutorial(AppEvents.SchematicEditorOpened);
    }

    [RelayCommand]
    private void CloseDocument(SheetEditorViewModel? doc)
    {
        doc ??= ActiveDocument;
        if (doc is null) return;
        var idx = Documents.IndexOf(doc);
        Documents.Remove(doc);
        ActiveDocument = Documents.Count == 0 ? null : Documents[Math.Max(0, idx - 1)];
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void NewSheet()
    {
        if (!Require(DND.Electrical.Licensing.EngineeringModule.ELEC)) return;
        var p = Session!.Project;
        var title = _s.Dialogs.PromptText("New sheet", "Sheet title", $"Sheet {p.NextSheetNumber()}");
        if (string.IsNullOrWhiteSpace(title)) return;
        var sheet = _s.Schematic.AddSheet(p, title.Trim(), SheetKind.Schematic);
        Session.MarkDirty();
        Explorer.Refresh(p);
        OpenSheet(sheet);
        _s.Events.Publish(AppEvents.SheetCreated);
        Log($"Sheet {sheet.Number} created.");
    }

    [RelayCommand]
    private void RenameSheet(Sheet? sheet)
    {
        sheet ??= ActiveDocument?.Sheet;
        if (sheet is null) return;
        var title = _s.Dialogs.PromptText("Rename sheet", "Sheet title", sheet.Title);
        if (string.IsNullOrWhiteSpace(title)) return;
        sheet.Title = title.Trim();
        Session?.MarkDirty();
        Explorer.Refresh(Session?.Project);
        foreach (var d in Documents) d.RefreshTitle();
    }

    [RelayCommand]
    private void DeleteSheet(Sheet? sheet)
    {
        sheet ??= ActiveDocument?.Sheet;
        if (sheet is null || Session is null) return;
        if (!_s.Dialogs.Confirm("Delete sheet", $"Delete sheet {sheet.Number} \"{sheet.Title}\" and its content? This cannot be undone.")) return;
        if (Documents.FirstOrDefault(d => d.Sheet == sheet) is { } doc) CloseDocument(doc);
        Session.Project.Sheets.Remove(sheet);
        Session.Project.Audit("Sheet deleted", $"{sheet.Number} {sheet.Title}");
        Session.MarkDirty();
        _s.Schematic.Binder.BindProject(Session.Project);
        Explorer.Refresh(Session.Project);
    }

    public void ActivateExplorerNode(ExplorerNode? node)
    {
        switch (node?.Item)
        {
            case Sheet s: OpenSheet(s); break;
            case Device d: NavigateToDevice(d); break;
            case CalculationRecord c: _s.Dialogs.Show(new TextViewerViewModel(c.Title, CalculationService.ReportText(c))); break;
            case Cabinet or PlcStation or Cable or TerminalStrip or Motor: OpenEngineeringData(); break;
        }
    }

    public void NavigateToDevice(Device d)
    {
        var occ = Session?.Project.SymbolInstances().FirstOrDefault(x => x.instance.DeviceId == d.Id);
        if (occ is { } o) NavigateTo(o.sheet, o.instance.Id);
    }

    public void NavigateTo(Sheet sheet, Guid? entityId)
    {
        OpenSheet(sheet);
        if (entityId is { } id && ActiveDocument?.Editor is { } ed && ed.Drawing.Find(id) is { } e)
        {
            ed.Selection.Set(new[] { id });
            ed.Viewport.ZoomToFit(e.GetBounds().Inflate(40));
        }
    }

    // ================================================================== editing

    private bool HasEditor => Editor is not null;

    [RelayCommand] private void Undo() { if (Editor?.UndoLast() == true) Rebind(); }
    [RelayCommand] private void Redo() { if (Editor?.RedoLast() == true) Rebind(); }
    [RelayCommand] private void Copy() => Editor?.CopySelection();
    [RelayCommand] private void Cut() => Editor?.CutSelection();
    [RelayCommand] private void Paste() => Editor?.Paste();
    [RelayCommand] private void Duplicate() => Editor?.DuplicateSelection();
    [RelayCommand] private void Delete() => Editor?.DeleteSelection();
    [RelayCommand] private void SelectAll() => Editor?.SelectAll();
    [RelayCommand] private void Rotate() => Editor?.RotateSelection(90);
    [RelayCommand] private void Mirror() => Editor?.MirrorSelection();
    [RelayCommand] private void Group() => Editor?.GroupSelection();
    [RelayCommand] private void Ungroup() => Editor?.UngroupSelection();
    [RelayCommand] private void ZoomExtents() => Editor?.ZoomExtents();
    [RelayCommand] private void Align(string? mode) { if (Enum.TryParse<AlignMode>(mode, out var m)) Editor?.AlignSelection(m); }

    [RelayCommand]
    private void ToggleSnap(string? what)
    {
        if (Editor is not { } ed) return;
        switch (what)
        {
            case "grid": ed.Snap.ShowGrid = !ed.Snap.ShowGrid; _s.Settings.ShowGrid = ed.Snap.ShowGrid; break;
            case "snap": ed.Snap.GridSnap = !ed.Snap.GridSnap; break;
            case "osnap": ed.Snap.ObjectSnap = !ed.Snap.ObjectSnap; break;
            case "ortho": ed.Snap.Ortho = !ed.Snap.Ortho; _s.Settings.OrthoMode = ed.Snap.Ortho; break;
        }
        ed.RequestRedraw();
        StatusText = $"Grid {(ed.Snap.ShowGrid ? "on" : "off")} · Snap {(ed.Snap.GridSnap ? "on" : "off")} · Object snap {(ed.Snap.ObjectSnap ? "on" : "off")} · Ortho {(ed.Snap.Ortho ? "on" : "off")}";
    }

    /// <summary>Activates a drawing tool by id (select, line, polyline, rectangle, circle, arc, text, dimension, callout, wire, pan, move, copy, rotate, mirror).</summary>
    [RelayCommand]
    public void SetTool(string? id)
    {
        if (Editor is not { } ed) return;
        CadTool tool = id switch
        {
            "line" => new LineTool(),
            "polyline" => new PolylineTool(),
            "rectangle" => new RectangleTool(),
            "circle" => new CircleTool(),
            "arc" => new ArcTool(),
            "text" => new TextTool(),
            "dimension" => new DimensionTool(),
            "callout" => new LeaderTool(),
            "wire" => new WireTool(),
            "pan" => new PanTool(),
            "move" => new MoveTool(),
            "copy" => new MoveTool(copy: true),
            "rotate" => new RotateTool(),
            "mirror" => new MirrorTool(),
            _ => new SelectTool(),
        };
        ed.ActiveLayer = id is "wire" ? StandardLayers.Wires : StandardLayers.Annotations;
        ed.SetTool(tool);
        CommandLine.Prompt = tool.Prompt;
    }

    [RelayCommand]
    public void InsertSymbol(SymbolDefinition? def)
    {
        if (!Require(DND.Electrical.Licensing.EngineeringModule.ELEC)) return;
        def ??= Symbols.Selected;
        if (def is null || Editor is not SchematicEditor ed) return;
        ed.SetTool(new InsertSymbolTool(def));
        CommandLine.Prompt = ed.Prompt;
    }

    /// <summary>Called by the canvas after a tool completed an operation (for tutorials and status).</summary>
    public void NotifyEditorOperation()
    {
        if (Editor is not SchematicEditor ed) return;
        switch (ed.ActiveTool)
        {
            case InsertSymbolTool { LastInserted: { } inst }:
                _s.Events.Publish(AppEvents.SymbolInserted_(inst.Definition?.Category.ToString() ?? "General"));
                break;
            case WireTool { LastWire: not null }:
                _s.Events.Publish(AppEvents.WireDrawn);
                break;
        }
    }

    private string? ExecuteCommandLine(string text)
    {
        if (Editor is not { } ed) return "Open a sheet first.";
        var result = _commands.Execute(ed, text);
        CommandLine.Prompt = ed.Prompt;
        return result;
    }

    private void RegisterSchematicCommands()
    {
        _commands.Register(new CadCommand("WIRE", new[] { "W" }, "Draw wires", (e, _) => { SetTool("wire"); return e.Prompt; }));
        _commands.Register(new CadCommand("INSERT", new[] { "I" }, "INSERT <symbol id>", (e, a) =>
        {
            var def = a.Length > 0 ? _s.Symbols.Find(a[0]) : Symbols.Selected;
            if (def is null) return "Unknown symbol. Type SYMBOLS to search.";
            InsertSymbol(def);
            return e.Prompt;
        }));
        _commands.Register(new CadCommand("SYMBOLS", Array.Empty<string>(), "List symbols matching text", (_, a) =>
            string.Join(", ", _s.Symbols.Search(string.Join(' ', a)).Take(12).Select(s => s.Id))));
        _commands.Register(new CadCommand("NUMBER", new[] { "NW" }, "Number wires", (_, _) => { NumberWiresCommand.Execute(null); return null; }));
        _commands.Register(new CadCommand("VALIDATE", new[] { "VAL" }, "Validate project", (_, _) => { ValidateCommand.Execute(null); return Validation.Summary; }));
        _commands.Register(new CadCommand("HELP", new[] { "?" }, "List commands", (_, _) => string.Join(" · ", _commands.Commands.Select(c => c.Name))));
    }

    // ================================================================== properties

    public void UpdateProperties()
    {
        Properties.Clear();
        if (Editor is not SchematicEditor ed || Session is null) { PropertiesTitle = "No selection"; return; }
        var sel = ed.SelectedEntities;
        if (sel.Count == 0)
        {
            PropertiesTitle = $"Sheet {ed.Sheet.Number}";
            var sh = ed.Sheet;
            Add("Sheet", "Title", sh.Title, v => { sh.Title = v; Changed(); });
            Add("Sheet", "Description", sh.Description, v => { sh.Description = v; Changed(); });
            Add("Sheet", "Function (=)", sh.Function, v => { sh.Function = v; Changed(); });
            Add("Sheet", "Location (+)", sh.Location, v => { sh.Location = v; Changed(); });
            Add("Sheet", "Format", sh.FormatName, null);
            Add("Sheet", "Revision", sh.Revision, null);
            return;
        }
        if (sel.Count > 1) { PropertiesTitle = $"{sel.Count} objects selected"; AddCommon(ed, sel); return; }
        var e = sel[0];
        PropertiesTitle = e.TypeName;
        switch (e)
        {
            case SymbolInstanceEntity si:
                {
                    PropertiesTitle = si.Definition?.Name ?? si.SymbolId;
                    var dev = si.DeviceId is { } did ? Session.Project.FindDevice(did) : null;
                    if (dev is not null)
                    {
                        Add("Device", "Tag", dev.Tag, v => { if (!ed.RenameDevice(dev, v)) ed.AssignDevice(si, v); Rebind(); });
                        Add("Device", "Function", dev.Function, v => DeviceEdit(ed, dev, "Function", d => d.Function, (d, x) => d.Function = x, v));
                        Add("Device", "Location", dev.Location, v => DeviceEdit(ed, dev, "Location", d => d.Location, (d, x) => d.Location = x, v));
                        Add("Device", "Manufacturer", dev.Manufacturer, v => DeviceEdit(ed, dev, "Manufacturer", d => d.Manufacturer, (d, x) => d.Manufacturer = x, v));
                        Add("Device", "Part number", dev.PartNumber, v => DeviceEdit(ed, dev, "Part number", d => d.PartNumber, (d, x) => d.PartNumber = x, v),
                            options: Session.Project.Catalog.Select(c => c.PartNumber).Distinct().ToList());
                        Add("Device", "Rating", dev.GetProperty(DeviceProperties.Rating) ?? "", v => DeviceEdit(ed, dev, "Rating", d => d.GetProperty(DeviceProperties.Rating) ?? "", (d, x) => d.Properties[DeviceProperties.Rating] = x, v));
                    }
                    else if (si.Definition is { } d0 && SchematicService.NeedsDevice(d0))
                        Add("Device", "Tag", "", v => { ed.AssignDevice(si, v); Rebind(); });
                    foreach (var name in new[] { SymbolAttributes.Potential, SymbolAttributes.Name, SymbolAttributes.TerminalNumber })
                        if (si.Definition?.Attributes.Any(a => a.Name == name) == true)
                            Add("Symbol", name switch { SymbolAttributes.Potential => "Potential", SymbolAttributes.Name => "Name", _ => "Terminal number" },
                                si.GetValue(name) ?? si.Definition.DefaultPotential ?? "", v => { ed.ModifyEntities(new[] { si }, $"Set {name}", x => ((SymbolInstanceEntity)x).Values[name] = v); Rebind(); });
                    if (si.Definition is { } def)
                        foreach (var pin in def.Pins.Where(p => p.ShowLabel))
                            Add("Pin labels", $"Pin {pin.Id}", si.GetPinLabel(pin.Id), v => { ed.ModifyEntities(new[] { si }, "Pin label", x => ((SymbolInstanceEntity)x).PinLabels[pin.Id] = v); Rebind(); });
                    Add("Placement", "Rotation", $"{si.Rotation:0}", null);
                    Add("Placement", "Mirrored", si.Mirrored ? "Yes" : "No", null);
                    Add("Placement", "Symbol", $"{si.SymbolId} v{si.Definition?.Version}", null);
                    if (si.Definition?.Role == SymbolRole.PlcIo)
                    {
                        var kind = si.SymbolId switch
                        {
                            "IEC.PLC.DI" => PlcSignalKind.DigitalInput, "IEC.PLC.DO" => PlcSignalKind.DigitalOutput,
                            "IEC.PLC.AI" => PlcSignalKind.AnalogInput, _ => PlcSignalKind.AnalogOutput,
                        };
                        var channels = Session.Project.PlcStations.SelectMany(st => st.Channels.Where(c => c.Kind == kind).Select(c => (st, c))).ToList();
                        var current = si.PlcChannelId is { } chId ? channels.FirstOrDefault(x => x.c.Id == chId) : default;
                        Add("PLC", "Channel", current.c is null ? "" : $"{current.st.Tag} {current.c.Address}", v =>
                        {
                            var target = channels.FirstOrDefault(x => $"{x.st.Tag} {x.c.Address}" == v);
                            if (target.c is null) { _s.Dialogs.ShowError("PLC channel", "Choose a channel from the list (create stations and modules in Engineering data)."); return; }
                            var r = DND.Electrical.PLC.PlcService.LinkSymbol(Session.Project, target.c, si);
                            if (r.IsFailure) _s.Dialogs.ShowError("PLC channel", r.Error!);
                            Rebind();
                        }, options: channels.Select(x => $"{x.st.Tag} {x.c.Address}").ToList());
                        if (current.c is not null)
                        {
                            var chn = current.c;
                            Add("PLC", "PLC tag", chn.Tag, v => { chn.Tag = v; Rebind(); });
                            Add("PLC", "Signal description", chn.Description, v => { chn.Description = v; Rebind(); });
                        }
                    }
                    break;
                }
            case WireEntity w:
                {
                    PropertiesTitle = $"Wire {w.WireNumber}";
                    Add("Wire", "Wire number", w.WireNumber, v => { ed.ModifyEntities(new[] { w }, "Wire number", x => { var ww = (WireEntity)x; ww.WireNumber = v; ww.NumberLocked = v.Length > 0; }); Rebind(); });
                    Add("Wire", "Colour", w.WireColor, v => ed.ModifyEntities(new[] { w }, "Wire colour", x => ((WireEntity)x).WireColor = v), options: new[] { "BK", "BU", "BN", "GY", "RD", "WH", "OG", "VT", "GNYE", "DBU" });
                    Add("Wire", "Cross-section (mm²)", w.CrossSectionMm2?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "", v => ed.ModifyEntities(new[] { w }, "Cross-section", x => ((WireEntity)x).CrossSectionMm2 = ParseD(v)));
                    Add("Wire", "Type", w.WireType, v => ed.ModifyEntities(new[] { w }, "Wire type", x => ((WireEntity)x).WireType = v));
                    Add("Wire", "Length (m)", w.LengthM?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "", v => ed.ModifyEntities(new[] { w }, "Wire length", x => ((WireEntity)x).LengthM = ParseD(v)));
                    var cables = Session.Project.Cables.Select(c => c.Tag).Prepend("").ToList();
                    Add("Cable", "Cable", w.CableId is { } cid ? Session.Project.FindCable(cid)?.Tag ?? "" : "", v => AssignCable(ed, w, v), options: cables);
                    Add("Connectivity", "Potential", w.PotentialName ?? "(unnamed)", null);
                    Add("Connectivity", "Start", w.Start.IsConnected ? "connected" : "OPEN", null);
                    Add("Connectivity", "End", w.End.IsConnected ? "connected" : "OPEN", null);
                    break;
                }
            case TextEntity t:
                Add("Text", "Value", t.Value, v => ed.ModifyEntities(new[] { t }, "Edit text", x => ((TextEntity)x).Value = v));
                Add("Text", "Height (mm)", $"{t.Height}", v => { if (ParseD(v) is double h && h > 0) ed.ModifyEntities(new[] { t }, "Text height", x => ((TextEntity)x).Height = h); });
                AddCommon(ed, sel);
                break;
            default:
                AddCommon(ed, sel);
                break;
        }
    }

    private void AddCommon(SchematicEditor ed, IReadOnlyList<CadEntity> sel)
    {
        Add("General", "Layer", sel[0].Layer, v => ed.SetLayer(sel, v), options: ed.Drawing.Layers.Select(l => l.Name).ToList());
        Add("General", "Colour", sel[0].Color?.ToHex() ?? "ByLayer", v => ed.ModifyEntities(sel, "Colour", x => x.Color = CadColor.TryParse(v, out var c) ? c : null));
        Add("General", "Line weight (mm)", sel[0].LineWeight?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "ByLayer", v => ed.ModifyEntities(sel, "Line weight", x => x.LineWeight = ParseD(v)));
    }

    private void Add(string cat, string name, string value, Action<string>? apply, IReadOnlyList<string>? options = null) =>
        Properties.Add(new PropertyItem(cat, name, value, apply, null, options));

    private void DeviceEdit(SchematicEditor ed, Device dev, string what, Func<Device, string> get, Action<Device, string> set, string value)
    {
        var old = get(dev);
        ed.Undo.Execute(new DelegateAction($"{dev.Tag}: {what}", () => set(dev, value), () => set(dev, old)));
        if (what == "Part number" && Session!.Project.Catalog.FirstOrDefault(c => c.PartNumber == value) is { } prod) dev.Manufacturer = prod.Manufacturer;
        Rebind();
    }

    private void AssignCable(SchematicEditor ed, WireEntity w, string tag)
    {
        var p = Session!.Project;
        CableService.UnassignWire(p, w);
        if (tag.Length > 0 && p.Cables.FirstOrDefault(c => c.Tag == tag) is { } cable)
        {
            var r = CableService.AssignWire(cable, w);
            if (r.IsFailure) _s.Dialogs.ShowError("Cable", r.Error!);
        }
        Session.MarkDirty();
        Rebind();
    }

    private static double? ParseD(string s) =>
        double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;

    private void Changed()
    {
        Session?.MarkDirty();
        RefreshAll();
    }

    private void Rebind()
    {
        if (Session is null) return;
        _s.Schematic.Binder.BindProject(Session.Project);
        Session.MarkDirty();
        Editor?.RequestRedraw();
        Explorer.Refresh(Session.Project);
        UpdateProperties();
    }

    private void RefreshAll()
    {
        if (Session is null) return;
        _s.Schematic.Binder.BindProject(Session.Project);
        Explorer.Refresh(Session.Project);
        foreach (var d in Documents) { d.RefreshTitle(); d.Editor.RequestRedraw(); }
        OnPropertyChanged(nameof(Title));
    }

    // ================================================================== engineering automation

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void NumberWires(object? renumber)
    {
        if (!Require(DND.Electrical.Licensing.EngineeringModule.ELEC)) return;
        var all = renumber is true || renumber is "all";
        if (Editor is SchematicEditor ed)
        {
            var r = ed.NumberWires(all);
            Log($"Wire numbering: {r.Numbered} numbered, {r.Unchanged} unchanged.{(r.Warnings.Count > 0 ? " " + string.Join(" ", r.Warnings) : "")}");
        }
        else
        {
            var g = _s.Schematic.Binder.BindWithConnectivity(Session!.Project);
            var r = WireNumberingService.Apply(Session.Project, g, new WireNumberingOptions { RenumberExisting = all });
            Session.MarkDirty();
            Log($"Wire numbering: {r.Numbered} numbered.");
        }
        _s.Events.Publish(AppEvents.WiresNumbered);
        RefreshAll();
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void RenumberDevices()
    {
        if (!Require(DND.Electrical.Licensing.EngineeringModule.ELEC)) return;
        if (!_s.Dialogs.Confirm("Renumber devices", "Renumber all devices whose tag is not locked, in order of appearance?")) return;
        if (Editor is SchematicEditor ed)
        {
            var changes = ed.RenumberDevices();
            Log($"{changes.Count} device tag(s) changed.");
        }
        RefreshAll();
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void SyncTerminals()
    {
        var n = CableService.SyncTerminalStrips(Session!.Project);
        Session.MarkDirty();
        Explorer.Refresh(Session.Project);
        Log($"Terminal strips synchronised: {n} terminal(s) added.");
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    public void Validate()
    {
        if (!Require(DND.Electrical.Licensing.EngineeringModule.ELEC)) return;
        var p = Session!.Project;
        var g = _s.Schematic.Binder.BindWithConnectivity(p);
        var report = new ValidationEngine().Run(p, g);
        ValidationEngine.RecordRun(p, report);
        Validation.Show(report);
        Log("Validation: " + report.Summary);
        _s.Events.Publish(AppEvents.ProjectValidated);
    }

    [RelayCommand]
    private void NavigateToIssue(ValidationIssue? issue)
    {
        if (issue is null || Session is null) return;
        var sheet = issue.SheetId is { } sid ? Session.Project.FindSheet(sid) : null;
        if (sheet is not null) NavigateTo(sheet, issue.EntityId);
        else if (issue.ObjectRef.Length > 0 && Session.Project.FindDeviceByTag(issue.ObjectRef) is { } dev) NavigateToDevice(dev);
    }

    /// <summary>Applies a suggested correction only after explicit confirmation.</summary>
    [RelayCommand]
    public void ApplyFix(string? fixId)
    {
        if (fixId is null || Session is null) return;
        var text = fixId switch
        {
            BuiltInRules.FixNumberWires => "Number all wires that have no number?",
            BuiltInRules.FixSyncTerminals => "Create missing terminal strip definitions?",
            BuiltInRules.FixRenumberDevices => "Renumber unlocked devices?",
            _ => null,
        };
        if (text is null || !_s.Dialogs.Confirm("Apply correction", text)) return;
        switch (fixId)
        {
            case BuiltInRules.FixNumberWires: NumberWires(false); break;
            case BuiltInRules.FixSyncTerminals: SyncTerminals(); break;
            case BuiltInRules.FixRenumberDevices:
                if (Editor is SchematicEditor ed) ed.RenumberDevices();
                break;
        }
        Validate();
    }

    // ================================================================== exports

    private ReportOptions DefaultReportOptions() => new()
    {
        Language = _s.Settings.Language,
        DateFormat = Session?.Project.Settings.DateFormat ?? "yyyy-MM-dd",
        CompanyLogoPng = Session?.Project.Info.CompanyLogoAttachmentId is { } id ? Session.Project.Attachments.FirstOrDefault(a => a.Id == id)?.Content : null,
    };

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void ExportDrawingsPdf()
    {
        if (!Require(DND.Electrical.Licensing.EngineeringModule.DOCS)) return;
        var p = Session!.Project;
        var path = _s.Dialogs.SaveFile("Export drawing set", FileFilters.Pdf, SafeName($"{p.Info.Number}_{p.Info.CurrentRevision}_Drawings.pdf"));
        if (path is null) return;
        Run("Drawing set PDF", () => _s.Documentation.DrawingSetPdf(p, path, DefaultReportOptions()));
        _s.Events.Publish(AppEvents.PdfExported);
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void ExportReports()
    {
        if (!Require(DND.Electrical.Licensing.EngineeringModule.DOCS)) return;
        var vm = new ReportExportViewModel(_s.Settings.Language);
        if (_s.Dialogs.ShowDialog(vm) != true) return;
        var p = Session!.Project;
        var options = DefaultReportOptions();
        options.Language = vm.Language;
        options.IncludeCover = vm.IncludeCover;
        if (!string.IsNullOrWhiteSpace(vm.FooterText)) options.FooterText = vm.FooterText;
        var ids = vm.SelectedReportIds;
        var ext = vm.Format switch { "xlsx" => FileFilters.Xlsx, "csv" => FileFilters.Csv, _ => FileFilters.Pdf };
        var path = _s.Dialogs.SaveFile("Export reports", ext, SafeName($"{p.Info.Number}_Reports.{vm.Format}"));
        if (path is null) return;
        Run("Reports", () => vm.Format switch
        {
            "xlsx" => _s.Documentation.ReportsXlsx(p, path, ids, options),
            "csv" => _s.Documentation.ReportCsv(p, ids.FirstOrDefault() ?? "bom", path, options),
            _ => _s.Documentation.ReportsPdf(p, path, ids, options),
        });
        if (vm.Format == "pdf") _s.Events.Publish(AppEvents.PdfExported);
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void ExportSheet(string? format)
    {
        if (!Require(DND.Electrical.Licensing.EngineeringModule.DOCS)) return;
        if (ActiveDocument is not { } doc) { _s.Dialogs.ShowInfo("Export", "Open the sheet to export first."); return; }
        var p = Session!.Project;
        format ??= "dxf";
        var filter = format switch { "svg" => FileFilters.Svg, "png" => FileFilters.Png, _ => FileFilters.Dxf };
        var path = _s.Dialogs.SaveFile($"Export sheet as {format.ToUpperInvariant()}", filter, SafeName($"{p.Info.Number}_{doc.Sheet.Number:00}.{format}"));
        if (path is null) return;
        Run($"Sheet {format.ToUpperInvariant()}", () => format switch
        {
            "svg" => _s.Documentation.SheetSvg(p, doc.Sheet, path),
            "png" => _s.Documentation.SheetPng(p, doc.Sheet, path),
            _ => _s.Documentation.SheetDxf(p, doc.Sheet, path),
        });
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void ExportData(string? format)
    {
        var p = Session!.Project;
        var path = _s.Dialogs.SaveFile("Export project data", format == "xml" ? FileFilters.Xml : FileFilters.Json, SafeName($"{p.Info.Number}.{format ?? "json"}"));
        if (path is null) return;
        if (format == "xml") ProjectInterchange.ToEngineeringXml(p).Save(path);
        else File.WriteAllText(path, ProjectInterchange.ToJson(p));
        Log($"Exported {path}");
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void HandoverPackage()
    {
        if (!Require(DND.Electrical.Licensing.EngineeringModule.DOCS)) return;
        var p = Session!.Project;
        var path = _s.Dialogs.SaveFile("Handover package folder (a ZIP is created next to it)", FileFilters.Any, SafeName($"{p.Info.Number}_{p.Info.CurrentRevision}_Handover"));
        if (path is null) return;
        Run("Handover package", () =>
        {
            var results = _s.Documentation.HandoverPackage(p, path, DefaultReportOptions());
            return results.Last();
        });
    }

    [RelayCommand]
    private void ImportDxf()
    {
        if (!Require(DND.Electrical.Licensing.EngineeringModule.ELEC)) return;
        if (Editor is not { } ed) { _s.Dialogs.ShowInfo("Import DXF", "Open the target sheet first."); return; }
        var file = _s.Dialogs.OpenFile("Import DXF", FileFilters.Dxf);
        if (file is null) return;
        try
        {
            var r = DxfReader.ReadFile(file);
            foreach (var l in r.Layers) ed.Drawing.AddLayer(l);
            ed.AddEntities(r.Entities, $"Import {Path.GetFileName(file)}");
            ed.Selection.Set(r.Entities.Select(e => e.Id));
            Log($"DXF imported: {r.Entities.Count} entities, {r.BlockReferencesExpanded} block references expanded" +
                (r.SkippedEntityTypes.Count > 0 ? $"; skipped: {string.Join(", ", r.SkippedEntityTypes.Select(kv => $"{kv.Key}×{kv.Value}"))}" : "."));
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or IOException)
        {
            _s.Dialogs.ShowError("Import DXF", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void ImportCatalog()
    {
        if (!Require(DND.Electrical.Licensing.EngineeringModule.CATALOG)) return;
        var file = _s.Dialogs.OpenFile("Import product catalogue", FileFilters.Catalog);
        if (file is null) return;
        try
        {
            var r = CatalogImportService.Import(Session!.Project, file);
            Session.MarkDirty();
            _s.Dialogs.ShowInfo("Catalogue import", $"{r.Added} added, {r.Updated} updated." + (r.Errors.Count > 0 ? "\n\n" + string.Join("\n", r.Errors.Take(20)) : ""));
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidDataException)
        {
            _s.Dialogs.ShowError("Catalogue import", ex.Message);
        }
    }

    private void Run(string what, Func<Application.Exports.ExportResult> action)
    {
        try
        {
            var r = action();
            Log($"{what} written: {r.Path} ({r.Size / 1024.0:0.#} KB)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            _s.Dialogs.ShowError(what, ex.Message);
        }
    }

    private static string SafeName(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    // ================================================================== tool windows

    [RelayCommand] private void OpenHelp(string? articleId) => _s.Dialogs.Show(new HelpCenterViewModel(_s, articleId));
    [RelayCommand] private void OpenAbout() => _s.Dialogs.ShowDialog(new AboutViewModel());
    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void OpenCalculations()
    {
        if (Require(DND.Electrical.Licensing.EngineeringModule.ELEC))
            _s.Dialogs.Show(new CalculationsViewModel(Session!.Project, () => { Session.MarkDirty(); Explorer.Refresh(Session.Project); }, _s.Licensing));
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void OpenEngineeringData()
    {
        if (Require(DND.Electrical.Licensing.EngineeringModule.ELEC))
            _s.Dialogs.Show(new EngineeringDataViewModel(_s, Session!.Project, () => { Session.MarkDirty(); RefreshAll(); }));
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void OpenProjectManagement()
    {
        if (Require(DND.Electrical.Licensing.EngineeringModule.PROJECT))
            _s.Dialogs.Show(new ProjectManagementViewModel(Session!.Project, () => Session.MarkDirty(), _s.Dialogs, _s.Settings.UserName));
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void OpenTwin()
    {
        if (Require(DND.Electrical.Licensing.EngineeringModule.TWIN))
            _s.Dialogs.Show(new TwinViewModel(Session!.Project, _s.Licensing, _s.Dialogs, () => Session.MarkDirty()));
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void OpenAutomation()
    {
        if (Require(DND.Electrical.Licensing.EngineeringModule.AUTO))
            _s.Dialogs.Show(new AutomationViewModel(Session!.Project, _s.Licensing, _s.Dialogs, () => { Session.MarkDirty(); Explorer.Refresh(Session.Project); }, _s.Settings.UserName));
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void OpenPowerStudio()
    {
        if (Require(DND.Electrical.Licensing.EngineeringModule.POWER))
            _s.Dialogs.Show(new PowerStudioViewModel(Session!.Project, _s.Licensing, _s.Dialogs, () => Session.MarkDirty()));
    }

    /// <summary>3D layout window; focuses the engineering object selected in the schematic, if any.</summary>
    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void Open3D()
    {
        if (!Require(DND.Electrical.Licensing.EngineeringModule.ThreeD)) return;
        var focus = ActiveDocument?.Editor.SelectedEntities.OfType<DND.Electrical.Schematic.Entities.SymbolInstanceEntity>().Select(s => s.DeviceId).FirstOrDefault(d => d is not null);
        _s.Dialogs.Show(new ThreeDViewModel(Session!.Project, _s.Licensing, _s.Dialogs, () => { Session.MarkDirty(); Explorer.Refresh(Session.Project); },
            id => { if (Session?.Project.FindDevice(id) is { } d) NavigateToDevice(d); }, focus));
    }

    [RelayCommand]
    private void OpenAssistant()
    {
        if (Require(DND.Electrical.Licensing.EngineeringModule.AI)) _s.Dialogs.Show(new AssistantViewModel(_s.Assistant, () => Session?.Project, ApplyFix));
    }

    [RelayCommand]
    private void OpenSymbolEditor()
    {
        if (Require(DND.Electrical.Licensing.EngineeringModule.ELEC)) _s.Dialogs.Show(new SymbolEditorViewModel(_s.Symbols, _s.Permissions, Symbols.Selected));
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var vm = new SettingsViewModel(_s.Settings, Session?.Project.Settings);
        if (_s.Dialogs.ShowDialog(vm) != true) return;
        vm.Apply();
        SaveSettings();
        if (Session is not null) { Session.MarkDirty(); RefreshAll(); }
        IsDarkTheme = _s.Settings.Theme == "Dark";
        foreach (var d in Documents)
        {
            d.Editor.Snap.GridSpacing = _s.Settings.GridSpacing;
            d.Editor.RequestRedraw();
        }
    }

    [RelayCommand]
    private void ToggleTheme() => IsDarkTheme = !IsDarkTheme;

    /// <summary>Lists plugins and lets an administrator trust verified plugins (by assembly hash).</summary>
    [RelayCommand]
    private void ManagePlugins()
    {
        var pm = _s.Plugins;
        pm.TrustedHashes.UnionWith(_s.Settings.TrustedPluginHashes);
        var found = pm.DiscoverAndLoad();
        if (found.Count == 0) { _s.Dialogs.ShowInfo("Plugins", $"No plugins found in {_s.Paths.Plugins}."); return; }
        var text = string.Join("\n", found.Select(p => $"• {p.Manifest?.Name ?? Path.GetFileName(p.Folder)} {p.Manifest?.Version}: {p.State} – {p.Message}"));
        var untrusted = found.Where(p => p.State == DND.Electrical.Plugins.PluginState.Untrusted && p.ActualSha256 is not null).ToList();
        if (untrusted.Count == 0 || !_s.Permissions.Has(AppPermission.ManagePlugins)) { _s.Dialogs.ShowInfo("Plugins", text); return; }
        foreach (var p in untrusted)
        {
            if (!_s.Dialogs.Confirm("Trust plugin", $"{text}\n\nTrust \"{p.Manifest!.Name}\" by {p.Manifest.Author}?\nPermissions: {p.Manifest.Permissions}\nSHA-256: {p.ActualSha256}\n\nOnly trust plugins from sources you know.")) continue;
            _s.Settings.TrustedPluginHashes.Add(p.ActualSha256!);
            pm.TrustedHashes.Add(p.ActualSha256!);
        }
        SaveSettings();
        pm.UnloadAll();
        var reloaded = pm.DiscoverAndLoad();
        Log($"Plugins: {reloaded.Count(p => p.State == DND.Electrical.Plugins.PluginState.Loaded)} loaded.");
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private async Task DuplicateProjectAsync()
    {
        if (Session?.FilePath is not { } source || Session.IsDirty) { _s.Dialogs.ShowInfo("Duplicate project", "Save the project first."); return; }
        var number = _s.Dialogs.PromptText("Duplicate project", "Number of the new project", Session.Project.Info.Number + "-COPY");
        if (string.IsNullOrWhiteSpace(number)) return;
        var target = _s.Dialogs.SaveFile("Save duplicate as", FileFilters.Project, SafeName(number) + ProductInfo.ProjectFileExtension);
        if (target is null) return;
        await _s.Projects.DuplicateAsync(source, target, number.Trim());
        Log($"Duplicate created: {target}");
        if (_s.Dialogs.Confirm("Duplicate project", "Open the duplicate now?")) await OpenPathAsync(target);
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void SetCompanyLogo()
    {
        var file = _s.Dialogs.OpenFile("Company logo for title blocks and reports", FileFilters.Image);
        if (file is null) return;
        try
        {
            var a = _s.Projects.AddAttachment(file, "Company logo");
            Session!.Project.Info.CompanyLogoAttachmentId = a.Id;
            RefreshAll();
            Log("Company logo set.");
        }
        catch (InvalidOperationException ex) { _s.Dialogs.ShowError("Company logo", ex.Message); }
    }

    public IReadOnlyList<PaletteCommand> PaletteCommands() => new List<PaletteCommand>
    {
        new("New project", "File", "Ctrl+N", NewProjectCommand), new("Open project", "File", "Ctrl+O", OpenProjectCommand),
        new("Save", "File", "Ctrl+S", SaveCommand), new("Save as", "File", "Ctrl+Shift+S", SaveAsCommand),
        new("Close project", "File", "", CloseProjectCommand), new("Export archive", "File", "", ExportArchiveCommand), new("Import archive", "File", "", ImportArchiveCommand),
        new("New sheet", "Project", "", NewSheetCommand), new("Project properties", "Project", "", ProjectPropertiesCommand), new("New revision", "Project", "", CreateRevisionCommand),
        new("Version history", "Project", "", VersionHistoryCommand), new("Add attachment", "Project", "", AddAttachmentCommand),
        new("Wire tool", "Drawing", "W", SetToolCommand, "wire"), new("Line tool", "Drawing", "L", SetToolCommand, "line"), new("Polyline", "Drawing", "", SetToolCommand, "polyline"),
        new("Rectangle", "Drawing", "", SetToolCommand, "rectangle"), new("Circle", "Drawing", "", SetToolCommand, "circle"), new("Arc", "Drawing", "", SetToolCommand, "arc"),
        new("Text", "Drawing", "", SetToolCommand, "text"), new("Dimension", "Drawing", "", SetToolCommand, "dimension"), new("Callout", "Drawing", "", SetToolCommand, "callout"),
        new("Zoom extents", "View", "F", ZoomExtentsCommand), new("Toggle theme", "View", "", ToggleThemeCommand),
        new("Number wires", "Engineering", "", NumberWiresCommand), new("Renumber devices", "Engineering", "", RenumberDevicesCommand),
        new("Synchronise terminal strips", "Engineering", "", SyncTerminalsCommand), new("Validate project", "Engineering", "F8", ValidateCommand),
        new("Engineering data (BOM, cables, PLC, cabinets)", "Engineering", "", OpenEngineeringDataCommand), new("Calculations", "Calculation", "", OpenCalculationsCommand), new("Project management (tasks, issues, approvals, engineering objects)", "Project", "", OpenProjectManagementCommand), new("3D layout (cabinet 3D, trays, clashes, glTF export)", "3D", "", Open3DCommand), new("D&D PowerStudio (load flow, short circuit, protection, harmonics)", "Power", "", OpenPowerStudioCommand), new("Automation (instruments, loops, alarms, cause and effect, architecture)", "Automation", "", OpenAutomationCommand), new("Digital twin (live values: Modbus TCP, MQTT, simulator)", "Twin", "", OpenTwinCommand),
        new("Drawing set PDF", "Reports", "", ExportDrawingsPdfCommand), new("Reports…", "Reports", "", ExportReportsCommand), new("Handover package", "Reports", "", HandoverPackageCommand),
        new("Export sheet DXF", "Reports", "", ExportSheetCommand, "dxf"), new("Export sheet SVG", "Reports", "", ExportSheetCommand, "svg"), new("Export sheet PNG", "Reports", "", ExportSheetCommand, "png"),
        new("Export project JSON", "Reports", "", ExportDataCommand, "json"), new("Export engineering XML", "Reports", "", ExportDataCommand, "xml"),
        new("Import DXF into sheet", "Tools", "", ImportDxfCommand), new("Import product catalogue", "Tools", "", ImportCatalogCommand),
        new("Symbol editor", "Tools", "", OpenSymbolEditorCommand), new("Settings", "Tools", "", OpenSettingsCommand), new("Engineering assistant", "Tools", "", OpenAssistantCommand),
        new("Help center", "Help", "F1", OpenHelpCommand), new("About", "Help", "", OpenAboutCommand),
        new("Plugins", "Tools", "", ManagePluginsCommand), new("Licensing and subscription", "Help", "", OpenLicensingCommand), new("Duplicate project", "Project", "", DuplicateProjectCommand),
        new("Set company logo", "Project", "", SetCompanyLogoCommand),
    }.Concat(_s.Plugins.Commands.Select(c => new PaletteCommand(c.Title, c.Category, "", new RelayCommand(c.Execute)))).ToList();

    // ================================================================== search & tutorials

    partial void OnSearchQueryChanged(string value) => Search(value);

    public void Search(string q)
    {
        SearchResults.Clear();
        if (q.Trim().Length < 2) return;
        if (Session?.Project is { } p)
        {
            foreach (var s in p.OrderedSheets.Where(s => s.Title.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(10))
                SearchResults.Add(new SearchResult("Sheet", s.DisplayName, s.Description, s));
            foreach (var d in p.Devices.Where(d => d.Tag.Contains(q, StringComparison.OrdinalIgnoreCase) || d.Function.Contains(q, StringComparison.OrdinalIgnoreCase) || d.PartNumber.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(20))
                SearchResults.Add(new SearchResult("Device", d.Tag, $"{d.Function} {d.PartNumber}", d));
            foreach (var (sheet, w) in p.WireEntities().Where(x => x.wire.WireNumber.Equals(q, StringComparison.OrdinalIgnoreCase)).Take(20))
                SearchResults.Add(new SearchResult("Wire", w.WireNumber, $"Sheet {sheet.Number}", (sheet, w.Id)));
            foreach (var c in p.Cables.Where(c => c.Tag.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(10))
                SearchResults.Add(new SearchResult("Cable", c.Tag, c.Designation, c));
            foreach (var ch in p.PlcStations.SelectMany(s => s.Channels).Where(c => c.Tag.Contains(q, StringComparison.OrdinalIgnoreCase) || c.Address.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(10))
                SearchResults.Add(new SearchResult("PLC", $"{ch.Address} {ch.Tag}", ch.Description, ch));
        }
        foreach (var h in _s.Help.Search(q, 5)) SearchResults.Add(new SearchResult("Help", h.Article.Title, h.Snippet, h.Article));
    }

    [RelayCommand]
    public void OpenSearchResult(SearchResult? r)
    {
        switch (r?.Target)
        {
            case Sheet s: OpenSheet(s); break;
            case Device d: NavigateToDevice(d); break;
            case ValueTuple<Sheet, Guid> t: NavigateTo(t.Item1, t.Item2); break;
            case HelpArticle a: OpenHelp(a.Id); break;
            case PlcChannel ch when ch.SymbolInstanceId is { } sid && Session?.Project.FindInstance(sid) is { } x: NavigateTo(x.sheet, sid); break;
            case Cable or PlcChannel: OpenEngineeringData(); break;
        }
    }

    private void OfferTutorial(string appEvent)
    {
        var t = _s.Tutorials.OfferFor(appEvent, _s.Settings.CompletedTutorials);
        if (t is null) return;
        if (_s.Dialogs.Confirm("Interactive tutorial", $"Would you like a guided tour: \"{t.Title}\"? ({t.Steps.Count} steps)"))
        {
            _s.Tutorials.Start(t);
            OnPropertyChanged(nameof(TutorialTitle));
        }
        else
        {
            _s.Settings.CompletedTutorials.Add(t.Id);
            SaveSettings();
        }
    }

    [RelayCommand] private void TutorialNext() { _s.Tutorials.Next(); OnPropertyChanged(nameof(TutorialTitle)); }
    [RelayCommand] private void TutorialCancel() { _s.Tutorials.Cancel(); OnPropertyChanged(nameof(TutorialTitle)); }
}

/// <summary>Raised when an entity is double-clicked; the shell focuses the properties panel.</summary>
public sealed record EntityActivatedHint(CadEntity Entity);
