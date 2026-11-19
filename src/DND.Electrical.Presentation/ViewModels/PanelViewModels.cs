using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DND.Electrical.CAD.Entities;
using DND.Electrical.Domain;
using DND.Electrical.Schematic.Editing;
using DND.Electrical.Schematic.Entities;
using DND.Electrical.Shared;
using DND.Electrical.Symbols;
using DND.Electrical.Validation;

namespace DND.Electrical.Presentation.ViewModels;

public enum ExplorerNodeKind { Folder, Sheet, Device, Cable, TerminalStrip, PlcStation, Motor, Cabinet, Calculation, Attachment, Revision }

public sealed partial class ExplorerNode : ObservableObject
{
    public ExplorerNode(string title, ExplorerNodeKind kind, object? item = null, string icon = "")
    {
        Title = title; Kind = kind; Item = item; Icon = icon;
    }

    public string Title { get; }
    public ExplorerNodeKind Kind { get; }
    public object? Item { get; }
    public string Icon { get; }
    public ObservableCollection<ExplorerNode> Children { get; } = new();
    [ObservableProperty] private bool _isExpanded;
}

/// <summary>Tree of the project content: sheets by kind, devices, cables, terminals, PLCs, motors, cabinets, calculations.</summary>
public sealed partial class ProjectExplorerViewModel : ObservableObject
{
    public ObservableCollection<ExplorerNode> Roots { get; } = new();
    [ObservableProperty] private string _filter = string.Empty;

    partial void OnFilterChanged(string value) => Refresh(_project);

    private ElectricalProject? _project;

    public void Refresh(ElectricalProject? project)
    {
        _project = project;
        Roots.Clear();
        if (project is null) return;
        bool Match(string s) => Filter.Length == 0 || s.Contains(Filter, StringComparison.OrdinalIgnoreCase);
        var root = new ExplorerNode($"{project.Info.Number} – {project.Info.Name}", ExplorerNodeKind.Folder, project, "📁") { IsExpanded = true };
        var sheets = new ExplorerNode($"Sheets ({project.Sheets.Count})", ExplorerNodeKind.Folder, null, "🗂") { IsExpanded = true };
        foreach (var g in project.OrderedSheets.GroupBy(s => s.Kind))
        {
            var folder = new ExplorerNode(g.Key.ToString(), ExplorerNodeKind.Folder) { IsExpanded = true };
            foreach (var s in g.Where(s => Match(s.DisplayName))) folder.Children.Add(new ExplorerNode(s.DisplayName, ExplorerNodeKind.Sheet, s, "📄"));
            if (folder.Children.Count > 0) sheets.Children.Add(folder);
        }
        root.Children.Add(sheets);
        Add(root, "Devices", project.Devices.OrderBy(d => d.Tag, NaturalStringComparer.Instance).Where(d => Match(d.DisplayName)).Select(d => new ExplorerNode(d.DisplayName, ExplorerNodeKind.Device, d, "⚙")));
        Add(root, "Cables", project.Cables.Where(c => Match(c.Tag)).Select(c => new ExplorerNode($"{c.Tag} {c.Designation}", ExplorerNodeKind.Cable, c, "〰")));
        Add(root, "Terminal strips", project.TerminalStrips.Where(t => Match(t.Tag)).Select(t => new ExplorerNode($"{t.Tag} ({t.Terminals.Count})", ExplorerNodeKind.TerminalStrip, t, "▤")));
        Add(root, "PLC", project.PlcStations.Where(s => Match(s.Tag + s.Name)).Select(s => new ExplorerNode($"{s.Tag} {s.Name}", ExplorerNodeKind.PlcStation, s, "▦")));
        Add(root, "Motors", project.Motors.Where(m => Match(m.Tag + m.Description)).Select(m => new ExplorerNode($"{m.Tag} {m.Description} ({m.PowerKw} kW)", ExplorerNodeKind.Motor, m, "Ⓜ")));
        Add(root, "Cabinets", project.Cabinets.Where(c => Match(c.Tag)).Select(c => new ExplorerNode($"{c.Tag} {c.Name}", ExplorerNodeKind.Cabinet, c, "▣")));
        Add(root, "Calculations", project.Calculations.Where(c => Match(c.Title)).Select(c => new ExplorerNode($"{c.Title} [{c.Status}]", ExplorerNodeKind.Calculation, c, "∑")));
        Add(root, "Revisions", project.Revisions.Select(r => new ExplorerNode($"{r.Code} – {r.Description}", ExplorerNodeKind.Revision, r, "⟲")));
        Add(root, "Attachments", project.Attachments.Select(a => new ExplorerNode(a.FileName, ExplorerNodeKind.Attachment, a, "📎")));
        Roots.Add(root);
    }

    private static void Add(ExplorerNode root, string title, IEnumerable<ExplorerNode> items)
    {
        var list = items.ToList();
        var folder = new ExplorerNode($"{title} ({list.Count})", ExplorerNodeKind.Folder);
        foreach (var i in list) folder.Children.Add(i);
        root.Children.Add(folder);
    }
}

/// <summary>Searchable symbol catalogue.</summary>
public sealed partial class SymbolBrowserViewModel : ObservableObject
{
    private readonly SymbolLibraryService _libraries;

    public SymbolBrowserViewModel(SymbolLibraryService libraries)
    {
        _libraries = libraries;
        Categories = new[] { "All" }.Concat(Enum.GetNames<SymbolCategory>()).ToList();
        Refresh();
        libraries.LibrariesChanged += (_, _) => Refresh();
    }

    public IReadOnlyList<string> Categories { get; }
    public ObservableCollection<SymbolDefinition> Results { get; } = new();
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _category = "All";
    [ObservableProperty] private SymbolDefinition? _selected;

    partial void OnSearchTextChanged(string value) => Refresh();
    partial void OnCategoryChanged(string value) => Refresh();

    public void Refresh()
    {
        Results.Clear();
        SymbolCategory? cat = Category == "All" ? null : Enum.Parse<SymbolCategory>(Category);
        foreach (var s in _libraries.Search(SearchText, cat)) Results.Add(s);
    }
}

/// <summary>Editable property row; <see cref="Apply"/> commits through undoable editor operations.</summary>
public sealed partial class PropertyItem : ObservableObject
{
    private readonly Action<string>? _apply;
    private string _committed;

    public PropertyItem(string category, string name, string value, Action<string>? apply, string? hint = null, IReadOnlyList<string>? options = null)
    {
        Category = category; Name = name; _value = value; _committed = value; _apply = apply; Hint = hint ?? string.Empty; Options = options;
    }

    public string Category { get; }
    public string Name { get; }
    public string Hint { get; }
    public IReadOnlyList<string>? Options { get; }
    public bool IsReadOnly => _apply is null;
    [ObservableProperty] private string _value;

    /// <summary>Commits the edited value (called by the view on focus loss / Enter).</summary>
    [RelayCommand]
    public void Commit()
    {
        if (_apply is null || Value == _committed) return;
        _apply(Value);
        _committed = Value;
    }
}

/// <summary>Validation results list with navigation and optional confirmed fixes.</summary>
public sealed partial class ValidationPanelViewModel : ObservableObject
{
    public ObservableCollection<ValidationIssue> Issues { get; } = new();
    [ObservableProperty] private string _summary = "Not validated yet.";
    [ObservableProperty] private ValidationIssue? _selected;
    [ObservableProperty] private bool _showInfos;

    public ValidationReport? Last { get; private set; }

    partial void OnShowInfosChanged(bool value) { if (Last is not null) Show(Last); }

    public void Show(ValidationReport report)
    {
        Last = report;
        Issues.Clear();
        foreach (var i in report.Issues.Where(i => ShowInfos || i.Severity != Severity.Information)) Issues.Add(i);
        Summary = report.Summary;
    }
}

/// <summary>Command line: history plus execution through the CAD command processor.</summary>
public sealed partial class CommandLineViewModel : ObservableObject
{
    public ObservableCollection<string> History { get; } = new();
    [ObservableProperty] private string _input = string.Empty;
    [ObservableProperty] private string _prompt = "Command:";

    public Func<string, string?>? Executor { get; set; }

    [RelayCommand]
    private void Execute()
    {
        var text = Input;
        Input = string.Empty;
        if (text.Length > 0) History.Add("> " + text);
        var result = Executor?.Invoke(text);
        if (!string.IsNullOrEmpty(result)) History.Add(result);
        while (History.Count > 300) History.RemoveAt(0);
    }
}

/// <summary>One open schematic sheet.</summary>
public sealed partial class SheetEditorViewModel : ObservableObject
{
    public SheetEditorViewModel(Sheet sheet, SchematicEditor editor)
    {
        Sheet = sheet;
        Editor = editor;
        editor.CursorMoved += (_, _) => CursorText = $"X {editor.CursorWorld.X:0.0}  Y {editor.CursorWorld.Y:0.0} mm";
        editor.ToolChanged += (_, _) => OnPropertyChanged(nameof(Prompt));
        editor.RedrawRequested += (_, _) => OnPropertyChanged(nameof(Prompt));
    }

    public Sheet Sheet { get; }
    public SchematicEditor Editor { get; }
    public string Title => $"{Sheet.Number}: {Sheet.Title}";
    public string Prompt => Editor.Prompt;
    [ObservableProperty] private string _cursorText = string.Empty;

    public void RefreshTitle() => OnPropertyChanged(nameof(Title));
}

public sealed record PaletteCommand(string Name, string Category, string Shortcut, IRelayCommand Command, object? Parameter = null);

public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private readonly IReadOnlyList<PaletteCommand> _all;

    public CommandPaletteViewModel(IReadOnlyList<PaletteCommand> commands)
    {
        _all = commands;
        Filter(string.Empty);
    }

    public ObservableCollection<PaletteCommand> Results { get; } = new();
    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private PaletteCommand? _selected;

    partial void OnQueryChanged(string value) => Filter(value);

    private void Filter(string q)
    {
        Results.Clear();
        foreach (var c in _all.Where(c => q.Length == 0 || c.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || c.Category.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(40))
            Results.Add(c);
        Selected = Results.FirstOrDefault();
    }

    [RelayCommand]
    private void Run(PaletteCommand? command)
    {
        command ??= Selected;
        if (command is not null && command.Command.CanExecute(command.Parameter)) command.Command.Execute(command.Parameter);
    }
}

public sealed record SearchResult(string Kind, string Title, string Detail, object Target);
