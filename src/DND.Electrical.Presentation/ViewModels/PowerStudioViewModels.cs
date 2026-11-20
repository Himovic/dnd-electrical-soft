using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DND.Electrical.Application.Services;
using DND.Electrical.Domain;
using DND.Electrical.Licensing;
using DND.Electrical.Power;
using DND.Electrical.Presentation.Services;

namespace DND.Electrical.Presentation.ViewModels;

/// <summary>One curve of the time-current chart; points are in chart pixels ("x,y x,y …").</summary>
public sealed record TccSeries(string Name, string Color, string Points, bool Dashed);
public sealed record TccMarker(string Label, double X);

/// <summary>
/// D&amp;D PowerStudio window: network data (buses, feeders, transformers, lines, loads, generators, shunts, protection),
/// load flow, IEC 60909 short circuit, protection coordination with time-current chart, harmonic frequency scan,
/// study reports. All studies run in the POWER-licensed application service.
/// </summary>
public sealed partial class PowerStudioViewModel : ObservableObject
{
    public const double ChartWidth = 640, ChartHeight = 420;
    private readonly ElectricalProject _p;
    private readonly PowerStudioService _svc;
    private readonly IDialogService _dialogs;
    private readonly Action _changed;
    private ShortCircuitResult? _sc;
    private bool _keepNetwork;

    public PowerStudioViewModel(ElectricalProject project, IEntitlements entitlements, IDialogService dialogs, Action changed)
    {
        _p = project; _svc = new PowerStudioService(entitlements); _dialogs = dialogs; _changed = changed;
        foreach (var d in project.PowerNetworks) Documents.Add(d);
        SelectedDocument = Documents.FirstOrDefault();
    }

    public string Title => $"D&D PowerStudio – {_p.Info.Number}";
    public ObservableCollection<PowerNetworkDocument> Documents { get; } = new();
    public ObservableCollection<Bus> Buses { get; } = new();
    public ObservableCollection<ExternalGrid> Grids { get; } = new();
    public ObservableCollection<Transformer> Transformers { get; } = new();
    public ObservableCollection<Line> Lines { get; } = new();
    public ObservableCollection<Load> Loads { get; } = new();
    public ObservableCollection<Generator> Generators { get; } = new();
    public ObservableCollection<Shunt> Shunts { get; } = new();
    public ObservableCollection<ProtectionDevice> Protection { get; } = new();
    public ObservableCollection<BusResult> BusResults { get; } = new();
    public ObservableCollection<BranchResult> BranchResults { get; } = new();
    public ObservableCollection<FaultResult> FaultResults { get; } = new();
    public ObservableCollection<string> Findings { get; } = new();
    public ObservableCollection<TccSeries> TccCurves { get; } = new();
    public ObservableCollection<TccMarker> TccMarkers { get; } = new();
    public ObservableCollection<string> HarmonicLines { get; } = new();

    [ObservableProperty] private PowerNetworkDocument? _selectedDocument;
    [ObservableProperty] private PowerNetwork? _network;
    [ObservableProperty] private object? _selectedElement;
    [ObservableProperty] private string _report = string.Empty;
    [ObservableProperty] private string _status = "Select or create a network.";

    partial void OnSelectedDocumentChanged(PowerNetworkDocument? value)
    {
        if (!_keepNetwork) Bind(value is null ? null : PowerStudioService.Load(value));
    }

    private void SelectWithoutReload(PowerNetworkDocument? doc)
    {
        _keepNetwork = true;
        try { SelectedDocument = doc; } finally { _keepNetwork = false; }
    }

    private void Bind(PowerNetwork? n)
    {
        Network = n;
        void Fill<T>(ObservableCollection<T> target, IEnumerable<T>? src) { target.Clear(); if (src is not null) foreach (var x in src) target.Add(x); }
        Fill(Buses, n?.Buses); Fill(Grids, n?.Grids); Fill(Transformers, n?.Transformers); Fill(Lines, n?.Lines);
        Fill(Loads, n?.Loads); Fill(Generators, n?.Generators); Fill(Shunts, n?.Shunts); Fill(Protection, n?.Protection);
        BusResults.Clear(); BranchResults.Clear(); FaultResults.Clear(); Findings.Clear(); TccCurves.Clear(); TccMarkers.Clear(); HarmonicLines.Clear();
        _sc = null; Report = string.Empty;
        Status = n is null ? "Select or create a network." : $"{n.Name}: {n.Buses.Count} buses, {n.Lines.Count + n.Transformers.Count + n.PuBranches.Count} branches.";
    }

    /// <summary>Copies the edited collections back into the network and stores it in the project.</summary>
    private bool Persist()
    {
        if (Network is null) { _dialogs.ShowError(Title, "Create or select a network first."); return false; }
        Network.Buses = Buses.ToList(); Network.Grids = Grids.ToList(); Network.Transformers = Transformers.ToList(); Network.Lines = Lines.ToList();
        Network.Loads = Loads.ToList(); Network.Generators = Generators.ToList(); Network.Shunts = Shunts.ToList(); Network.Protection = Protection.ToList();
        try
        {
            var doc = _svc.Save(_p, Network, SelectedDocument?.Id);
            if (!Documents.Contains(doc)) Documents.Add(doc);
            SelectWithoutReload(doc);
            _changed();
            return true;
        }
        catch (LicenseException ex) { _dialogs.ShowError(Title, ex.Message); return false; }
    }

    private void Adopt(PowerNetwork n)
    {
        SelectWithoutReload(null);
        Bind(n);
        if (Persist()) Status = $"{n.Name} stored in the project.";
    }

    [RelayCommand] private void NewNetwork()
    {
        var name = _dialogs.PromptText("New network", "Name", $"{_p.Info.Number} network");
        if (string.IsNullOrWhiteSpace(name)) return;
        var n = new PowerNetwork { Name = name.Trim() };
        n.Buses.Add(new Bus { Id = "MV", Name = "MV supply", Kv = 20 });
        n.Grids.Add(new ExternalGrid { Id = "Utility", Bus = "MV" });
        Adopt(n);
    }

    [RelayCommand] private void LoadDemoNetwork() => Adopt(SampleNetworks.DemoIndustrial());
    [RelayCommand] private void LoadIeee14() => Adopt(SampleNetworks.Ieee14());

    [RelayCommand] private void BuildFromProject()
    {
        var kva = _dialogs.PromptText("Build from project", "Transformer rating (kVA)", "630");
        if (kva is null || !double.TryParse(kva, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) || s <= 0) return;
        try
        {
            var (n, notes) = _svc.BuildFromProject(_p, s);
            Adopt(n);
            Findings.Clear(); foreach (var x in notes) Findings.Add("Placeholder: " + x);
        }
        catch (LicenseException ex) { _dialogs.ShowError(Title, ex.Message); }
    }

    [RelayCommand] private void Save() { if (Persist()) Status = "Network stored in the project."; }

    [RelayCommand] private void AddElement(string? kind)
    {
        if (Network is null) { _dialogs.ShowError(Title, "Create or select a network first."); return; }
        var bus = Buses.LastOrDefault()?.Id ?? "B1";
        switch (kind)
        {
            case "bus": Buses.Add(new Bus { Id = $"B{Buses.Count + 1}", Kv = 0.4 }); break;
            case "grid": Grids.Add(new ExternalGrid { Id = $"Grid{Grids.Count + 1}", Bus = bus }); break;
            case "transformer": Transformers.Add(new Transformer { Id = $"TR{Transformers.Count + 1}", HvBus = Buses.FirstOrDefault()?.Id ?? bus, LvBus = bus }); break;
            case "line": Lines.Add(new Line { Id = $"C{Lines.Count + 1}", From = Buses.FirstOrDefault()?.Id ?? bus, To = bus, ROhmPerKm = 0.193, XOhmPerKm = 0.08, LengthKm = 0.05 }); break;
            case "load": Loads.Add(new Load { Id = $"L{Loads.Count + 1}", Bus = bus, PMw = 0.1, QMvar = 0.05 }); break;
            case "generator": Generators.Add(new Generator { Id = $"G{Generators.Count + 1}", Bus = bus }); break;
            case "shunt": Shunts.Add(new Shunt { Id = $"PFC{Shunts.Count + 1}", Bus = bus, QMvar = 0.1 }); break;
            case "protection": Protection.Add(new ProtectionDevice { Id = $"Q{Protection.Count + 1}", Bus = bus }); break;
        }
    }

    [RelayCommand] private void DeleteSelected()
    {
        switch (SelectedElement)
        {
            case Bus b: Buses.Remove(b); break;
            case ExternalGrid g: Grids.Remove(g); break;
            case Transformer t: Transformers.Remove(t); break;
            case Line l: Lines.Remove(l); break;
            case Load l: Loads.Remove(l); break;
            case Generator g: Generators.Remove(g); break;
            case Shunt s: Shunts.Remove(s); break;
            case ProtectionDevice d: Protection.Remove(d); break;
        }
    }

    private bool Ready()
    {
        if (!Persist() || Network is null) return false;
        var errors = Network.Validate();
        if (errors.Count == 0) return true;
        Findings.Clear(); foreach (var e in errors) Findings.Add("Data error: " + e);
        Status = $"{errors.Count} data error(s) – see findings.";
        return false;
    }

    private void Run(Action study)
    {
        if (!Ready()) return;
        try { study(); }
        catch (LicenseException ex) { _dialogs.ShowError(Title, ex.Message); }
        catch (InvalidOperationException ex) { Findings.Clear(); Findings.Add(ex.Message); Status = "Study failed – see findings."; }
    }

    [RelayCommand] private void RunLoadFlow() => Run(() =>
    {
        var r = _svc.LoadFlow(Network!);
        BusResults.Clear(); foreach (var b in r.Buses) BusResults.Add(b);
        BranchResults.Clear(); foreach (var b in r.Branches) BranchResults.Add(b);
        Findings.Clear(); foreach (var m in r.Messages) Findings.Add(m);
        Report = PowerStudioService.LoadFlowReport(Network!, r);
        Status = r.Converged ? $"Load flow converged in {r.Iterations} iterations · losses {r.LossesMw * 1000:0.#} kW · {r.Messages.Count} finding(s)." : "Load flow did not converge – see findings.";
    });

    [RelayCommand] private void RunShortCircuit() => Run(() =>
    {
        _sc = _svc.ShortCircuit(Network!);
        FaultResults.Clear(); foreach (var f in _sc.Faults) FaultResults.Add(f);
        Findings.Clear(); foreach (var a in _sc.Assumptions) Findings.Add("Assumption: " + a);
        Report = PowerStudioService.ShortCircuitReport(Network!, _sc);
        var max = _sc.Faults.MaxBy(f => f.Ik3MaxKa)!;
        Status = $"Short circuit: highest I\"k3 {max.Ik3MaxKa:0.##} kA at {max.Bus}.";
    });

    [RelayCommand] private void RunCoordination() => Run(() =>
    {
        _sc ??= _svc.ShortCircuit(Network!);
        var findings = _svc.Coordination(Network!, _sc);
        Findings.Clear(); foreach (var f in findings) Findings.Add((f.Selective ? "✔ " : "✖ ") + f.Message);
        BuildChart(_sc);
        Status = findings.Count == 0 ? "No device has an upstream device assigned (set UpstreamId)." : $"{findings.Count(f => f.Selective)}/{findings.Count} pair(s) shown selective.";
    });

    [RelayCommand] private void RunHarmonics() => Run(() =>
    {
        var r = _svc.Harmonics(Network!);
        HarmonicLines.Clear();
        foreach (var res in r.Resonances.OrderBy(x => x.Order)) HarmonicLines.Add(FormattableString.Invariant($"Parallel resonance at bus {res.Bus}: h = {res.Order:0.0} ({res.Order * Network!.FrequencyHz:0} Hz), |Z| = {res.ZOhm:0.####} Ω"));
        foreach (var d in r.Distortion) HarmonicLines.Add(FormattableString.Invariant($"Bus {d.Bus}: THDv = {d.ThdPercent:0.##} % ({string.Join(", ", d.VoltagePercent.Select(kv => $"h{kv.Key} {kv.Value:0.##} %"))})"));
        if (HarmonicLines.Count == 0) HarmonicLines.Add("No resonance peak found up to h = 50 and no harmonic sources defined.");
        Findings.Clear(); foreach (var a in r.Assumptions) Findings.Add("Assumption: " + a);
        Status = $"Harmonic scan: {r.Resonances.Count} resonance peak(s).";
    });

    private void BuildChart(ShortCircuitResult sc)
    {
        TccCurves.Clear(); TccMarkers.Clear();
        var devices = Network!.Protection;
        if (devices.Count == 0) return;
        var iMin = Math.Max(1, devices.Min(d => d.PickupA) / 2);
        var iMax = Math.Max(iMin * 100, sc.Faults.Max(f => f.Ik3MaxKa) * 1000 * 2);
        double tMin = 0.005, tMax = 10000;
        double X(double i) => (Math.Log10(i) - Math.Log10(iMin)) / (Math.Log10(iMax) - Math.Log10(iMin)) * ChartWidth;
        double Y(double t) => (Math.Log10(tMax) - Math.Log10(Math.Max(tMin, t))) / (Math.Log10(tMax) - Math.Log10(tMin)) * ChartHeight;
        var colors = new[] { "#2563EB", "#DC2626", "#059669", "#D97706", "#7C3AED", "#DB2777" };
        var k = 0;
        foreach (var d in devices)
        {
            var col = colors[k++ % colors.Length];
            foreach (var max in new[] { true, false })
            {
                var pts = new List<string>();
                for (var s = 0; s <= 200; s++)
                {
                    var i = iMin * Math.Pow(iMax / iMin, s / 200.0);
                    var t = max ? ProtectionStudy.MaxTime(d, i) : ProtectionStudy.MinTime(d, i);
                    if (double.IsInfinity(t) || t > tMax) continue;
                    pts.Add(FormattableString.Invariant($"{X(i):0.#},{Y(t):0.#}"));
                }
                if (pts.Count > 1) TccCurves.Add(new TccSeries($"{d.Id} {(max ? "max" : "min")}", col, string.Join(' ', pts), !max));
            }
        }
        foreach (var f in sc.Faults.Where(f => devices.Any(d => string.Equals(d.Bus, f.Bus, StringComparison.OrdinalIgnoreCase))))
            TccMarkers.Add(new TccMarker(FormattableString.Invariant($"I\"k3 {f.Bus} {f.Ik3MaxKa:0.##} kA"), X(f.Ik3MaxKa * 1000)));
    }

    [RelayCommand] private void ExportReport()
    {
        if (Report.Length == 0) { _dialogs.ShowError(Title, "Run a load flow or short-circuit study first."); return; }
        var path = _dialogs.SaveFile("Export study report", "Text (*.txt)|*.txt", $"{_p.Info.Number}-powerstudio.txt");
        if (path is null) return;
        File.WriteAllText(path, Report);
        _dialogs.ShowInfo(Title, $"Report written: {path}");
    }

    [RelayCommand] private void ExportTcc()
    {
        if (Network is null || _sc is null) { _dialogs.ShowError(Title, "Run the coordination study first."); return; }
        var path = _dialogs.SaveFile("Export time-current chart", "SVG (*.svg)|*.svg", $"{_p.Info.Number}-tcc.svg");
        if (path is null) return;
        File.WriteAllText(path, ProtectionStudy.TccSvg(Network, _sc));
        _dialogs.ShowInfo(Title, $"Chart written: {path}");
    }

    [RelayCommand] private void ExportJson()
    {
        if (!Persist()) return;
        var path = _dialogs.SaveFile("Export network", "Network JSON (*.json)|*.json", $"{Network!.Name}.json");
        if (path is not null) File.WriteAllText(path, Network.ToJson());
    }

    [RelayCommand] private void ImportJson()
    {
        var path = _dialogs.OpenFile("Import network", "Network JSON (*.json)|*.json");
        if (path is null) return;
        try { Adopt(PowerNetwork.FromJson(File.ReadAllText(path))); }
        catch (System.Text.Json.JsonException ex) { _dialogs.ShowError(Title, "Invalid network file: " + ex.Message); }
    }
}
