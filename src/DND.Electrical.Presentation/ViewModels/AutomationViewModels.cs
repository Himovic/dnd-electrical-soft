using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DND.Electrical.Application.Services;
using DND.Electrical.Domain;
using DND.Electrical.Licensing;
using DND.Electrical.Presentation.Services;

namespace DND.Electrical.Presentation.ViewModels;

/// <summary>One cell of the cause and effect matrix (toggle).</summary>
public sealed partial class CeCell(CauseEffectMatrix matrix, CauseEffectCause cause, CauseEffectEffect effect, Action changed) : ObservableObject
{
    public string Mark => matrix.Links.FirstOrDefault(l => l.CauseId == cause.Id && l.EffectId == effect.Id)?.Mark ?? "";
    public bool Triggered { get => _triggered; set => SetProperty(ref _triggered, value); }
    private bool _triggered;
    public CauseEffectEffect Effect => effect;

    [RelayCommand]
    private void Toggle()
    {
        AutomationService.ToggleLink(matrix, cause.Id, effect.Id);
        OnPropertyChanged(nameof(Mark));
        changed();
    }
}

public sealed partial class CeRow(CauseEffectCause cause, IReadOnlyList<CeCell> cells) : ObservableObject
{
    public CauseEffectCause Cause => cause;
    public string Label => $"{cause.Tag} – {cause.Description} {cause.Condition}".Trim();
    public IReadOnlyList<CeCell> Cells => cells;
    [ObservableProperty] private bool _active;
}

/// <summary>
/// AUTO module window: instrument index, control loops, alarm database, cause and effect matrices (with logic
/// evaluation for review and commissioning checks), control system architecture and loop diagram generation.
/// </summary>
public sealed partial class AutomationViewModel : ObservableObject
{
    private readonly ElectricalProject _p;
    private readonly AutomationService _svc;
    private readonly IDialogService _dialogs;
    private readonly Action _changed;
    private readonly string _user;

    public AutomationViewModel(ElectricalProject project, IEntitlements entitlements, IDialogService dialogs, Action changed, string user)
    {
        _p = project; _svc = new AutomationService(entitlements); _dialogs = dialogs; _changed = changed; _user = user;
        Instruments = new(project.Instruments); Loops = new(project.Loops); Alarms = new(project.Alarms); Nodes = new(project.ScadaNodes);
        Matrices = new(project.CauseEffects);
        SelectedMatrix = Matrices.FirstOrDefault();
    }

    public string Title => $"Automation – {_p.Info.Number}";
    public ObservableCollection<Instrument> Instruments { get; }
    public ObservableCollection<ControlLoop> Loops { get; }
    public ObservableCollection<AlarmDefinition> Alarms { get; }
    public ObservableCollection<ScadaNode> Nodes { get; }
    public ObservableCollection<CauseEffectMatrix> Matrices { get; }
    public ObservableCollection<CeRow> MatrixRows { get; } = new();
    public ObservableCollection<CauseEffectEffect> MatrixEffects { get; } = new();
    public ObservableCollection<string> Messages { get; } = new();
    public IEnumerable<string> SpareChannels => _p.PlcStations.SelectMany(s => s.Channels.Where(c => c.IsSpare).Select(c => $"{s.Tag} {c.Address} ({c.Kind})"));

    [ObservableProperty] private Instrument? _selectedInstrument;
    [ObservableProperty] private ControlLoop? _selectedLoop;
    [ObservableProperty] private CauseEffectMatrix? _selectedMatrix;
    [ObservableProperty] private string _evaluation = string.Empty;

    public void Changed() => _changed();

    private bool Guard(Action a)
    {
        try { a(); return true; }
        catch (LicenseException ex) { _dialogs.ShowError(Title, ex.Message); return false; }
        catch (InvalidOperationException ex) { _dialogs.ShowError(Title, ex.Message); return false; }
    }

    partial void OnSelectedMatrixChanged(CauseEffectMatrix? value) => BuildMatrix();

    private void BuildMatrix()
    {
        MatrixRows.Clear(); MatrixEffects.Clear(); Evaluation = string.Empty;
        if (SelectedMatrix is not { } m) return;
        foreach (var e in m.Effects) MatrixEffects.Add(e);
        foreach (var c in m.Causes)
        {
            var row = new CeRow(c, m.Effects.Select(e => new CeCell(m, c, e, Changed)).ToList());
            row.PropertyChanged += (_, a) => { if (a.PropertyName == nameof(CeRow.Active)) Evaluate(); };
            MatrixRows.Add(row);
        }
    }

    /// <summary>Shows which effects the currently ticked causes trigger (logic review; nothing is sent to equipment).</summary>
    public void Evaluate()
    {
        if (SelectedMatrix is not { } m) return;
        var triggered = AutomationService.Evaluate(m, MatrixRows.Where(r => r.Active).Select(r => r.Cause.Id)).Select(e => e.Id).ToHashSet();
        foreach (var r in MatrixRows) foreach (var c in r.Cells) c.Triggered = r.Active && triggered.Contains(c.Effect.Id) && c.Mark.Length > 0;
        Evaluation = triggered.Count == 0 ? "No effect triggered." :
            "Triggered: " + string.Join(", ", m.Effects.Where(e => triggered.Contains(e.Id)).Select(e => $"{e.Tag} {e.Action}"));
    }

    [RelayCommand] private void AddInstrument()
    {
        var tag = _dialogs.PromptText("Add instrument", "Tag (e.g. PT-101)");
        if (string.IsNullOrWhiteSpace(tag)) return;
        if (_p.Instruments.Any(i => string.Equals(i.Tag, tag.Trim(), StringComparison.OrdinalIgnoreCase))) { _dialogs.ShowError(Title, $"Instrument {tag} already exists."); return; }
        var i = new Instrument { Tag = tag.Trim().ToUpperInvariant() };
        i.Variable = i.Tag[0] switch { 'P' => MeasuredVariable.Pressure, 'T' => MeasuredVariable.Temperature, 'L' => MeasuredVariable.Level, 'F' => MeasuredVariable.Flow, 'A' => MeasuredVariable.Analysis, _ => MeasuredVariable.Other };
        _p.Instruments.Add(i); Instruments.Add(i); SelectedInstrument = i; Changed();
    }

    [RelayCommand] private void AssignIo()
    {
        if (SelectedInstrument is not { } i) { _dialogs.ShowError(Title, "Select an instrument."); return; }
        var spares = _p.PlcStations.SelectMany(s => s.Channels.Where(c => c.IsSpare).Select(c => (s, c))).ToList();
        if (spares.Count == 0) { _dialogs.ShowError(Title, "No spare PLC channel in the project (add I/O modules in Engineering data → PLC)."); return; }
        var suggestion = spares.FirstOrDefault(x => (x.c.Kind is PlcSignalKind.AnalogInput or PlcSignalKind.AnalogOutput) == i.IsAnalog);
        var text = _dialogs.PromptText("Assign I/O", $"PLC station and address for {i.Tag} (spare channels: {spares.Count})", suggestion.c is null ? "" : $"{suggestion.s.Tag} {suggestion.c.Address}");
        if (text is null) return;
        var parts = text.Trim().Split(' ', 2, StringSplitOptions.TrimEntries);
        var target = parts.Length == 2 ? _p.PlcStations.Where(s => string.Equals(s.Tag, parts[0], StringComparison.OrdinalIgnoreCase)).SelectMany(s => s.Channels).FirstOrDefault(c => string.Equals(c.Address, parts[1], StringComparison.OrdinalIgnoreCase)) : null;
        if (target is null) { _dialogs.ShowError(Title, $"Channel '{text}' not found. Use '<station tag> <address>'."); return; }
        Guard(() =>
        {
            var error = _svc.AssignToChannel(_p, i, target, _user);
            if (error is not null) { _dialogs.ShowError(Title, error); return; }
            Messages.Insert(0, $"{i.Tag} assigned to {text}.");
            Changed();
        });
    }

    [RelayCommand] private void ProposeAlarms()
    {
        if (SelectedInstrument is not { } i) { _dialogs.ShowError(Title, "Select an analog instrument."); return; }
        var text = _dialogs.PromptText("Alarm proposal", "Fractions of span per the project alarm philosophy (LL,L,H,HH; blank to skip)", "0.05,0.10,0.90,0.95");
        if (text is null) return;
        var parts = text.Split(',');
        var types = new[] { AlarmType.LowLow, AlarmType.Low, AlarmType.High, AlarmType.HighHigh };
        var fractions = new Dictionary<AlarmType, double>();
        for (var k = 0; k < Math.Min(4, parts.Length); k++)
            if (double.TryParse(parts[k].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) fractions[types[k]] = f;
        Guard(() =>
        {
            var proposals = _svc.ProposeAlarms(i, fractions).Where(a => _p.Alarms.All(x => !string.Equals(x.Tag, a.Tag, StringComparison.OrdinalIgnoreCase))).ToList();
            if (proposals.Count == 0) { _dialogs.ShowInfo(Title, "No new alarm to add (tags already exist)."); return; }
            if (!_dialogs.Confirm(Title, "Add these alarms?\n\n" + string.Join("\n", proposals.Select(a => $"{a.Tag}: {a.Type} at {a.SetPoint} {a.Unit} ({a.Priority})")))) return;
            foreach (var a in proposals) { _p.Alarms.Add(a); Alarms.Add(a); }
            _p.Audit("Alarms added", $"{proposals.Count} alarm(s) for {i.Tag}", _user);
            Changed();
        });
    }

    [RelayCommand] private void AddLoop()
    {
        var tag = _dialogs.PromptText("Add control loop", "Loop tag (e.g. FIC-101)");
        if (string.IsNullOrWhiteSpace(tag)) return;
        var l = new ControlLoop { Tag = tag.Trim().ToUpperInvariant(), MeasurementId = SelectedInstrument?.Id };
        if (SelectedInstrument is { } i) { i.LoopId = l.Id; l.Unit = i.Unit; }
        _p.Loops.Add(l); Loops.Add(l); SelectedLoop = l; Changed();
    }

    [RelayCommand] private void GenerateLoopDiagram()
    {
        if (SelectedLoop is not { } l) { _dialogs.ShowError(Title, "Select a loop."); return; }
        Guard(() =>
        {
            var sheet = _svc.GenerateLoopDiagram(_p, l);
            Messages.Insert(0, $"Loop diagram generated on sheet {sheet.Number} '{sheet.Title}'.");
            Changed();
        });
    }

    [RelayCommand] private void AddAlarm()
    {
        var a = new AlarmDefinition { Tag = $"ALM-{_p.Alarms.Count + 1:000}", InstrumentId = SelectedInstrument?.Id, Unit = SelectedInstrument?.Unit ?? "" };
        _p.Alarms.Add(a); Alarms.Add(a); Changed();
    }

    [RelayCommand] private void NewMatrix()
    {
        var name = _dialogs.PromptText("New cause and effect matrix", "Name", "ESD cause and effect");
        if (string.IsNullOrWhiteSpace(name)) return;
        var m = new CauseEffectMatrix { Name = name.Trim() };
        _p.CauseEffects.Add(m); Matrices.Add(m); SelectedMatrix = m; Changed();
    }

    [RelayCommand] private void AddCause()
    {
        if (SelectedMatrix is not { } m) return;
        var text = _dialogs.PromptText("Add cause", "Tag; description; condition", SelectedInstrument is { } i ? $"{i.Tag} HH; {i.Description} high-high; " : "");
        if (string.IsNullOrWhiteSpace(text)) return;
        var p = text.Split(';', StringSplitOptions.TrimEntries);
        m.Causes.Add(new CauseEffectCause { Tag = p[0], Description = p.ElementAtOrDefault(1) ?? "", Condition = p.ElementAtOrDefault(2) ?? "", InstrumentId = SelectedInstrument?.Id });
        BuildMatrix(); Changed();
    }

    [RelayCommand] private void AddEffect()
    {
        if (SelectedMatrix is not { } m) return;
        var text = _dialogs.PromptText("Add effect", "Device or instrument tag; description; action", "; ; Stop");
        if (string.IsNullOrWhiteSpace(text)) return;
        var p = text.Split(';', StringSplitOptions.TrimEntries);
        var device = _p.FindDeviceByTag(p[0]);
        var inst = _p.Instruments.FirstOrDefault(i => string.Equals(i.Tag, p[0], StringComparison.OrdinalIgnoreCase));
        m.Effects.Add(new CauseEffectEffect { Tag = p[0], Description = p.ElementAtOrDefault(1) ?? "", Action = p.ElementAtOrDefault(2) is { Length: > 0 } a ? a : "Stop", DeviceId = device?.Id, InstrumentId = inst?.Id });
        if (device is null && inst is null) Messages.Insert(0, $"Effect {p[0]} is not linked to a device or instrument of the project.");
        BuildMatrix(); Changed();
    }

    [RelayCommand] private void AddNode()
    {
        var name = _dialogs.PromptText("Add network node", "Name");
        if (string.IsNullOrWhiteSpace(name)) return;
        var n = new ScadaNode { Name = name.Trim() };
        _p.ScadaNodes.Add(n); Nodes.Add(n); Changed();
    }

    [RelayCommand] private void ImportPlcStations()
    {
        Guard(() =>
        {
            var n = _svc.ImportPlcStations(_p, _p.ScadaNodes.FirstOrDefault(x => x.Kind == ScadaNodeKind.Switch)?.Id);
            foreach (var node in _p.ScadaNodes.Where(x => !Nodes.Contains(x))) Nodes.Add(node);
            Messages.Insert(0, $"{n} PLC station(s) added to the architecture.");
            if (n > 0) Changed();
        });
    }

    [RelayCommand] private void DeleteSelected(object? item)
    {
        switch (item)
        {
            case Instrument i when _dialogs.Confirm(Title, $"Delete instrument {i.Tag}?"):
                _p.Instruments.Remove(i); Instruments.Remove(i);
                foreach (var ch in _p.PlcStations.SelectMany(s => s.Channels).Where(c => c.Id == i.PlcChannelId)) { ch.Tag = ""; ch.Description = ""; }
                break;
            case ControlLoop l when _dialogs.Confirm(Title, $"Delete loop {l.Tag}?"): _p.Loops.Remove(l); Loops.Remove(l); break;
            case AlarmDefinition a: _p.Alarms.Remove(a); Alarms.Remove(a); break;
            case ScadaNode n: _p.ScadaNodes.Remove(n); Nodes.Remove(n); break;
            default: return;
        }
        Changed();
    }
}
