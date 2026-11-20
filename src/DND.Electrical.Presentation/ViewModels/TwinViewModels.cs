using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DND.Electrical.Application.Services;
using DND.Electrical.Domain;
using DND.Electrical.Licensing;
using DND.Electrical.Presentation.Services;
using DND.Electrical.Twin;

namespace DND.Electrical.Presentation.ViewModels;

public sealed record TwinRow(string Tag, string Value, string Unit, TwinQuality Quality, string Time, string Source, bool Simulated, string Error);

/// <summary>
/// Digital twin window: point configuration, live values with quality and timestamps, active alarms and a trend of
/// the selected point. A banner is shown whenever any displayed value comes from the simulator.
/// </summary>
public sealed partial class TwinViewModel : ObservableObject, IAsyncDisposable
{
    public const double TrendWidth = 600, TrendHeight = 160;
    private readonly ElectricalProject _p;
    private readonly TwinService _svc;
    private readonly IDialogService _dialogs;
    private readonly Action _changed;
    private TwinEngine? _engine;
    private CancellationTokenSource? _loop;
    private SynchronizationContext? _ui;

    public TwinViewModel(ElectricalProject project, IEntitlements entitlements, IDialogService dialogs, Action changed)
    {
        _p = project; _svc = new TwinService(entitlements); _dialogs = dialogs; _changed = changed;
        Points = new(project.TwinPoints);
    }

    public string Title => $"Digital twin – {_p.Info.Number}";
    public ObservableCollection<TwinPoint> Points { get; }
    public ObservableCollection<TwinRow> Values { get; } = new();
    public ObservableCollection<string> ActiveAlarms { get; } = new();
    public IReadOnlyList<TwinSourceKind> SourceKinds { get; } = Enum.GetValues<TwinSourceKind>();
    [ObservableProperty] private TwinPoint? _selectedPoint;
    [ObservableProperty] private bool _running;
    [ObservableProperty] private bool _showsSimulatedData;
    [ObservableProperty] private string _status = "Stopped.";
    [ObservableProperty] private string _trend = string.Empty;
    [ObservableProperty] private int _intervalSeconds = 2;

    public void Changed() => _changed();

    [RelayCommand] private void AddPoint()
    {
        var tag = _dialogs.PromptText("Add twin point", "Tag");
        if (string.IsNullOrWhiteSpace(tag)) return;
        var inst = _p.Instruments.FirstOrDefault(i => string.Equals(i.Tag, tag.Trim(), StringComparison.OrdinalIgnoreCase));
        var t = new TwinPoint { Tag = tag.Trim(), InstrumentId = inst?.Id, DeviceId = inst?.DeviceId ?? _p.FindDeviceByTag(tag.Trim())?.Id, Unit = inst?.Unit ?? "" };
        _p.TwinPoints.Add(t); Points.Add(t); SelectedPoint = t; Changed();
    }

    [RelayCommand] private void CreateSimulatedPoints()
    {
        try
        {
            var n = _svc.CreateSimulatedPoints(_p);
            foreach (var t in _p.TwinPoints.Where(t => !Points.Contains(t))) Points.Add(t);
            Status = n == 0 ? "Every analog instrument already has a twin point." : $"{n} simulated point(s) created – values will be marked SIMULATED.";
            if (n > 0) Changed();
        }
        catch (LicenseException ex) { _dialogs.ShowError(Title, ex.Message); }
    }

    [RelayCommand] private void DeletePoint()
    {
        if (SelectedPoint is not { } t) return;
        _p.TwinPoints.Remove(t); Points.Remove(t); Changed();
    }

    /// <summary>One acquisition cycle (also used by tests).</summary>
    public async Task PollOnceAsync(CancellationToken ct = default)
    {
        try { _engine ??= _svc.CreateEngine(_p); }
        catch (LicenseException ex) { _dialogs.ShowError(Title, ex.Message); return; }
        var values = await _engine.PollAsync(ct).ConfigureAwait(false);
        var alarms = _engine.EvaluateAlarms();
        void Apply()
        {
            Values.Clear();
            var byId = _p.TwinPoints.ToDictionary(t => t.Id);
            foreach (var v in values)
                if (byId.TryGetValue(v.PointId, out var t))
                    Values.Add(new TwinRow(t.Tag, v.Value is { } x ? x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "—", t.Unit, v.Quality, v.Timestamp.ToLocalTime().ToString("HH:mm:ss"), $"{t.Source} {t.Endpoint}".Trim(), v.Simulated, v.Error ?? ""));
            ActiveAlarms.Clear();
            foreach (var a in alarms) ActiveAlarms.Add($"{a.Alarm.Priority} {a.Alarm.Tag}: {a.Point.Tag} = {a.Value:0.###} {a.Point.Unit} ({a.Alarm.Type} {a.Alarm.SetPoint}){(a.Simulated ? " [SIMULATED]" : "")}");
            ShowsSimulatedData = values.Any(v => v.Simulated);
            Status = $"{values.Count(v => v.Quality == TwinQuality.Good)}/{values.Count} good · {alarms.Count} active alarm(s) · {DateTime.Now:HH:mm:ss}";
            UpdateTrend();
        }
        if (_ui is not null && SynchronizationContext.Current != _ui) _ui.Post(_ => Apply(), null); else Apply();
    }

    partial void OnSelectedPointChanged(TwinPoint? value) => UpdateTrend();

    private void UpdateTrend()
    {
        if (SelectedPoint is null || _engine is null) { Trend = string.Empty; return; }
        var h = _engine.History(SelectedPoint.Id).Where(v => v.Value is not null).ToList();
        if (h.Count < 2) { Trend = string.Empty; return; }
        double min = h.Min(v => v.Value!.Value), max = h.Max(v => v.Value!.Value);
        if (max - min < 1e-9) { min -= 1; max += 1; }
        Trend = string.Join(' ', h.Select((v, i) => FormattableString.Invariant($"{i * TrendWidth / (h.Count - 1):0.#},{TrendHeight - (v.Value!.Value - min) / (max - min) * TrendHeight:0.#}")));
    }

    [RelayCommand] private void Start()
    {
        if (Running) return;
        _ui = SynchronizationContext.Current;
        _loop = new CancellationTokenSource();
        Running = true;
        var token = _loop.Token;
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, IntervalSeconds)));
            try
            {
                do { await PollOnceAsync(token).ConfigureAwait(false); }
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    [RelayCommand] private void Stop()
    {
        _loop?.Cancel();
        Running = false;
        Status = "Stopped.";
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        if (_engine is not null) await _engine.DisposeAsync();
    }
}
