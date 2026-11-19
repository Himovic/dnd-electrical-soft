using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DND.Electrical.Application.Services;
using DND.Electrical.CAD.Editing;
using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;
using DND.Electrical.Calculations;
using DND.Electrical.Core.Geometry;
using DND.Electrical.Documentation;
using DND.Electrical.Domain;
using DND.Electrical.Panel;
using DND.Electrical.PLC;
using DND.Electrical.Reports;
using DND.Electrical.Schematic.Services;
using DND.Electrical.Symbols;
using DND.Electrical.Wiring;

namespace DND.Electrical.Presentation.ViewModels;

// ====================================================================== Help center

public sealed partial class HelpCategory : ObservableObject
{
    public HelpCategory(string name, IEnumerable<HelpArticle> articles) { Name = name; Articles = articles.ToList(); }
    public string Name { get; }
    public IReadOnlyList<HelpArticle> Articles { get; }
    [ObservableProperty] private bool _isExpanded = true;
}

public sealed partial class HelpCenterViewModel : ObservableObject
{
    private readonly AppServices _s;

    public HelpCenterViewModel(AppServices services, string? articleId = null)
    {
        _s = services;
        Reload();
        Open(articleId ?? "01-introduction");
    }

    public string Title => "Help & Documentation";
    public ObservableCollection<HelpCategory> Categories { get; } = new();
    public ObservableCollection<HelpSearchHit> SearchResults { get; } = new();
    public IReadOnlyList<TutorialDefinition> Tutorials => TutorialEngine.BuiltIn;
    public IReadOnlyList<Shortcut> Shortcuts => KeyboardShortcuts.All;
    public bool IsAdmin => _s.Permissions.Has(AppPermission.ManageDocumentation);
    public IReadOnlyList<DocumentationVersion> Versions => _s.Help.PublishedVersions();

    [ObservableProperty] private HelpArticle? _article;
    [ObservableProperty] private IReadOnlyList<Block> _blocks = Array.Empty<Block>();
    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private bool _editMode;
    [ObservableProperty] private string _editId = string.Empty;
    [ObservableProperty] private string _editTitle = string.Empty;
    [ObservableProperty] private string _editCategory = string.Empty;
    [ObservableProperty] private string _editKeywords = string.Empty;
    [ObservableProperty] private string _editBody = string.Empty;

    partial void OnQueryChanged(string value)
    {
        SearchResults.Clear();
        foreach (var h in _s.Help.Search(value)) SearchResults.Add(h);
    }

    partial void OnArticleChanged(HelpArticle? value) => Blocks = value is null ? Array.Empty<Block>() : Markdown.Parse(value.Body);

    private void Reload()
    {
        Categories.Clear();
        foreach (var c in _s.Help.Categories) Categories.Add(new HelpCategory(c, _s.Help.InCategory(c)));
    }

    /// <summary>Opens an article by id (also used for links inside articles).</summary>
    [RelayCommand]
    public void Open(string? id)
    {
        if (id is null) return;
        var clean = id.Split('#')[0];
        Article = _s.Help.Get(clean) ?? Article;
    }

    [RelayCommand] private void OpenArticle(HelpArticle? a) { if (a is not null) Article = a; }

    [RelayCommand]
    private void StartTutorial(TutorialDefinition? t)
    {
        if (t is null) return;
        _s.Tutorials.Start(t);
        _s.Dialogs.ShowInfo("Tutorial started", $"\"{t.Title}\" started. Follow the instructions in the tutorial bar at the top of the main window.");
    }

    public string? ResolveAsset(string reference) => _s.Help.ResolveAsset(reference);

    // ---- administration (Administrator role)

    [RelayCommand]
    private void BeginEdit(bool? asNew)
    {
        if (!IsAdmin) { _s.Dialogs.ShowError("Documentation", "Only administrators can edit documentation."); return; }
        if (asNew == true || Article is null)
        {
            EditId = "custom-" + DateTime.Now.ToString("yyyyMMddHHmm"); EditTitle = "New article"; EditCategory = "Company"; EditKeywords = ""; EditBody = "# New article\n\nWrite the content in Markdown.";
        }
        else
        {
            EditId = Article.Id; EditTitle = Article.Title; EditCategory = Article.Category; EditKeywords = string.Join(", ", Article.Keywords); EditBody = Article.Body;
        }
        EditMode = true;
    }

    [RelayCommand]
    private void SaveEdit()
    {
        try
        {
            var saved = _s.Help.Save(new HelpArticle
            {
                Id = EditId.Trim(), Title = EditTitle.Trim(), Category = EditCategory.Trim(), Body = EditBody,
                Keywords = EditKeywords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList(),
                Order = Article?.Id == EditId ? Article.Order : 500,
            });
            EditMode = false;
            Reload();
            Article = saved;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException) { _s.Dialogs.ShowError("Save article", ex.Message); }
    }

    [RelayCommand] private void CancelEdit() => EditMode = false;

    [RelayCommand]
    private void InsertAsset()
    {
        var file = _s.Dialogs.OpenFile("Add image, PDF manual or video", "Media|*.png;*.jpg;*.jpeg;*.gif;*.pdf;*.mp4;*.webm");
        if (file is null) return;
        try
        {
            var reference = _s.Help.AddAsset(file);
            EditBody += Path.GetExtension(file).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif"
                ? $"\n\n![{Path.GetFileNameWithoutExtension(file)}]({reference})\n"
                : $"\n\n[{Path.GetFileName(file)}]({reference})\n";
        }
        catch (ArgumentException ex) { _s.Dialogs.ShowError("Add asset", ex.Message); }
    }

    [RelayCommand]
    private void DeleteCustom()
    {
        if (Article is not { IsCustom: true } a || !_s.Dialogs.Confirm("Delete", $"Delete the custom version of \"{a.Title}\"?")) return;
        _s.Help.DeleteCustom(a.Id);
        Reload();
        Article = _s.Help.Get(a.Id) ?? _s.Help.Get("01-introduction");
    }

    [RelayCommand]
    private void Publish()
    {
        var v = _s.Help.Publish(_s.Settings.UserName);
        OnPropertyChanged(nameof(Versions));
        _s.Dialogs.ShowInfo("Documentation published", $"Documentation version {v.Version} published with {v.Articles} articles.");
    }
}

// ====================================================================== Calculations

public sealed partial class CalcField : ObservableObject
{
    public CalcField(string key, string label, string value, string unit = "", IReadOnlyList<string>? options = null)
    {
        Key = key; Label = label; _value = value; Unit = unit; Options = options;
    }

    public string Key { get; }
    public string Label { get; }
    public string Unit { get; }
    public IReadOnlyList<string>? Options { get; }
    public bool HasOptions => Options is { Count: > 0 };
    [ObservableProperty] private string _value;
}

public sealed record CalculatorDefinition(string Id, string Name, string Description, IReadOnlyList<CalcField> Fields, Func<Func<string, double>, Func<string, string>, CalculationResult> Run);

public sealed partial class CalculationsViewModel : ObservableObject
{
    private readonly ElectricalProject _project;
    private readonly Action _changed;
    private CalculationResult? _last;

    public CalculationsViewModel(ElectricalProject project, Action changed)
    {
        _project = project;
        _changed = changed;
        var v = project.Settings.NominalVoltage.ToString(CultureInfo.InvariantCulture);
        static CalcField F(string k, string l, string v, string u = "", params string[] o) => new(k, l, v, u, o.Length > 0 ? o : null);
        Calculators = new List<CalculatorDefinition>
        {
            new("current", "Current from power", "I = P/(k·U·cos φ·η)", new[] { F("sys", "System", "3", "", "1", "3"), F("p", "Power", "11", "kW"), F("u", "Voltage", v, "V"), F("pf", "cos φ", "0.85"), F("eff", "Efficiency", "0.9") },
                (d, s) => PowerCalculator.CurrentFromPower(Sys(s), d("p"), d("u"), d("pf"), d("eff"))),
            new("power", "Power (P, Q, S)", "S = k·U·I", new[] { F("sys", "System", "3", "", "1", "3"), F("u", "Voltage", v, "V"), F("i", "Current", "16", "A"), F("pf", "cos φ", "0.85") },
                (d, s) => PowerCalculator.PowerFromCurrent(Sys(s), d("u"), d("i"), d("pf"))),
            new("pf", "Power factor", "cos φ = P/S", new[] { F("p", "Active power", "50", "kW"), F("q", "Reactive power", "30", "kvar") },
                (d, _) => PowerCalculator.PowerTriangle(d("p"), d("q"))),
            new("vd", "Voltage drop", "IEC 60364-5-52 Annex G", new[] { F("sys", "System", "3", "", "1", "3"), F("u", "Voltage", v, "V"), F("i", "Current", "20", "A"), F("l", "Length", "50", "m"), F("s", "Section", "4", "mm²"), F("mat", "Material", "Copper", "", "Copper", "Aluminium"), F("pf", "cos φ", "0.85"), F("lim", "Limit", "5", "%") },
                (d, s) => VoltageDropCalculator.Calculate(new VoltageDropInput(Sys(s), d("u"), d("i"), d("l"), d("s"), Enum.Parse<Conductor>(s("mat")), d("pf"), LimitPercent: d("lim")))),
            new("cable", "Cable sizing", "Iz with k1·k2, 433.1 coordination, voltage drop", new[] { F("sys", "System", "3", "", "1", "3"), F("u", "Voltage", v, "V"), F("i", "Design current Ib", "32", "A"), F("l", "Length", "40", "m"), F("m", "Method", "C", "", "C", "E"), F("ins", "Insulation", "PVC", "", "PVC", "XLPE"), F("t", "Ambient", "30", "°C"), F("n", "Grouped circuits", "1"), F("pf", "cos φ", "0.85"), F("lim", "Max voltage drop", "5", "%"), F("dev", "Device", "CircuitBreaker", "", "CircuitBreaker", "FuseGG") },
                (d, s) => CableSizingCalculator.Calculate(new CableSizingInput(Sys(s), d("u"), d("i"), d("l"), Enum.Parse<InstallationMethod>(s("m")), Enum.Parse<CableInsulation>(s("ins")), d("t"), (int)d("n"), d("pf"), d("lim"), Enum.Parse<ProtectiveDevice>(s("dev"))))),
            new("sc", "Short-circuit current", "IEC 60909 simplified", new[] { F("ssc", "Network S\"kQ", "500", "MVA"), F("kva", "Transformer", "630", "kVA"), F("uk", "uk", "6", "%"), F("pk", "Load losses", "6.5", "kW"), F("u", "LV voltage", v, "V"), F("l", "Cable length", "30", "m"), F("s", "Cable section", "95", "mm²"), F("n", "Parallel", "1") },
                (d, _) => ShortCircuitCalculator.Calculate(new ShortCircuitInput(d("ssc"), d("kva"), d("uk"), d("pk"), d("u"), d("l"), d("s"), Conductor.Copper, (int)d("n")))),
            new("tr", "Transformer sizing", "S = P·ks/cos φ·(1+reserve)", new[] { F("p", "Connected load", Math.Max(1, project.Loads.Sum(l => l.PowerKw) + project.Motors.Sum(m => m.PowerKw)).ToString("0.#", CultureInfo.InvariantCulture), "kW"), F("ks", "Demand factor", "0.8"), F("pf", "cos φ", "0.9"), F("r", "Reserve", "20", "%") },
                (d, _) => TransformerSizingCalculator.Calculate(d("p"), d("ks"), d("pf"), d("r"))),
            new("motor", "Motor starting", "In and Ist by starting method", new[] { F("p", "Power", "11", "kW"), F("u", "Voltage", v, "V"), F("pf", "cos φ", "0.85"), F("eff", "Efficiency", "0.9"), F("k", "Ist/In", "7"), F("m", "Method", "DirectOnLine", "", Enum.GetNames<StartingMethod>()), F("kva", "Transformer (optional)", "0", "kVA"), F("uk", "uk", "6", "%") },
                (d, s) => MotorStartingCalculator.Calculate(d("p"), d("u"), d("pf"), d("eff"), d("k"), Enum.Parse<StartingMethod>(s("m")), transformerKva: d("kva") > 0 ? d("kva") : null, transformerUk: d("kva") > 0 ? d("uk") : null)),
            new("breaker", "Circuit-breaker selection", "Ib ≤ In ≤ Iz, Icu ≥ Ik", new[] { F("ib", "Ib", "14", "A"), F("iz", "Iz", "24", "A"), F("ik", "Prospective Ik", "6", "kA"), F("icu", "Icu", "10", "kA") },
                (d, _) => ProtectionCalculator.SelectBreaker(d("ib"), d("iz"), d("ik"), d("icu"))),
            new("tn", "TN maximum length", "Lmax = 0.8·U0·Sph/(ρ(1+m)Ia)", new[] { F("u0", "U0", "230", "V"), F("sph", "Phase section", "2.5", "mm²"), F("spe", "PE section", "2.5", "mm²"), F("ia", "Ia", "160", "A"), F("l", "Actual length", "40", "m") },
                (d, _) => ProtectionCalculator.TnMaximumLength(d("u0"), d("sph"), d("spe"), d("ia"), actualLengthM: d("l"))),
            new("loads", "Project load summary", "From the project load list and motors", new[] { F("u", "System voltage", v, "V"), F("ks", "Simultaneity", "0.9") },
                (d, _) => LoadCalculator.Summarize(project.Loads.Select(l => new LoadItem(l.Description, l.PowerKw, l.PowerFactor, l.DemandFactor, Category: l.Category.ToString()))
                    .Concat(project.Motors.Select(m => new LoadItem(m.Tag, m.PowerKw / Math.Max(0.1, m.Efficiency), m.PowerFactor, 1, Category: "Motor"))).ToList(), d("u"), d("ks"))),
            new("balance", "Phase balancing", "Single-phase loads of the project", Array.Empty<CalcField>(),
                (_, _) => LoadBalancingCalculator.Balance(project.Loads.Select(l => new LoadItem(l.Description, l.PowerKw, l.PowerFactor, l.DemandFactor, l.Phase == PhaseAssignment.ThreePhase ? "3P" : "1P")).ToList()).result),
        };
        _selected = Calculators[0];
    }

    public string Title => "Calculations";
    public IReadOnlyList<CalculatorDefinition> Calculators { get; }
    public ObservableCollection<CalculationRecord> Stored => new(_project.Calculations);
    [ObservableProperty] private CalculatorDefinition _selected;
    [ObservableProperty] private string _report = string.Empty;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _error = string.Empty;

    private static SupplySystem Sys(Func<string, string> s) => s("sys") == "1" ? SupplySystem.SinglePhase : SupplySystem.ThreePhase;

    [RelayCommand]
    public void Calculate()
    {
        Error = string.Empty;
        var fields = Selected.Fields.ToDictionary(f => f.Key);
        try
        {
            double D(string k) => double.TryParse(fields[k].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? v : throw new ArgumentException($"'{fields[k].Label}' is not a number (use '.' as decimal separator).");
            string S(string k) => fields[k].Value;
            _last = Selected.Run(D, S);
            Report = _last.ToReportText();
            Status = _last.Status.ToString();
            Summary = _last.Summary;
        }
        catch (ArgumentException ex)
        {
            _last = null;
            Error = ex.Message;
            Report = Status = Summary = string.Empty;
        }
    }

    [RelayCommand]
    private void Store()
    {
        if (_last is null) Calculate();
        if (_last is null) return;
        CalculationService.Store(_project, _last);
        OnPropertyChanged(nameof(Stored));
        _changed();
    }

    [RelayCommand]
    private void ShowStored(CalculationRecord? record)
    {
        if (record is null) return;
        Report = CalculationService.ReportText(record);
        Status = record.Status.ToString();
        Summary = record.Summary;
    }
}

// ====================================================================== Engineering data (lists, PLC, cables, cabinets)

public sealed partial class EngineeringDataViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly ElectricalProject _p;
    private readonly Action _changed;

    public EngineeringDataViewModel(AppServices services, ElectricalProject project, Action changed)
    {
        _s = services; _p = project; _changed = changed;
        Lists = ProjectReports.Catalogue(services.Settings.Language).Where(c => c.id is not ("validation")).ToList();
        _selectedList = Lists.First(l => l.id == "bom");
        _selectedCabinet = project.Cabinets.FirstOrDefault();
        Refresh();
    }

    public string Title => $"Engineering data – {_p.Info.Number}";
    public IReadOnlyList<(string id, string title)> Lists { get; }
    public IReadOnlyList<Cabinet> Cabinets => _p.Cabinets.ToList();
    public IReadOnlyList<PlcStation> Stations => _p.PlcStations.ToList();
    public IReadOnlyList<PlcModuleType> ModuleTypes => PlcModuleType.Generic;
    public IReadOnlyList<PlcProfile> Profiles => PlcProfile.BuiltIn;
    public IReadOnlyList<string> CabinetIssues => SelectedCabinet is null ? Array.Empty<string>() : CabinetLayoutService.Check(SelectedCabinet).Select(i => $"{i.Severity}: {i.Message}").ToList();

    [ObservableProperty] private (string id, string title) _selectedList;
    [ObservableProperty] private ReportTable? _table;
    [ObservableProperty] private Cabinet? _selectedCabinet;
    [ObservableProperty] private CadDrawing? _cabinetDrawing;
    [ObservableProperty] private PlcStation? _selectedStation;
    [ObservableProperty] private string _cabinetSummary = string.Empty;

    partial void OnSelectedListChanged((string id, string title) value) => Refresh();
    partial void OnSelectedCabinetChanged(Cabinet? value) => RefreshCabinet();

    [RelayCommand]
    public void Refresh()
    {
        var g = _s.Schematic.Binder.BindWithConnectivity(_p);
        Table = new ProjectReports(_p, g, new ReportOptions { Language = _s.Settings.Language }).Build(SelectedList.id);
        RefreshCabinet();
        OnPropertyChanged(nameof(Stations));
    }

    private void RefreshCabinet()
    {
        CabinetDrawing = SelectedCabinet is null ? null : CabinetLayoutService.BuildDrawing(_p, SelectedCabinet);
        if (SelectedCabinet is not null)
        {
            var u = CabinetLayoutService.Utilization(SelectedCabinet);
            CabinetSummary = $"{SelectedCabinet.Tag}: {SelectedCabinet.Items.Count} items · utilisation {u.Percent:0.#} % · free DIN rail {u.FreeRailLengthMm:0} mm";
        }
        OnPropertyChanged(nameof(CabinetIssues));
        OnPropertyChanged(nameof(Cabinets));
    }

    private string? Ask(string label, string def = "") => _s.Dialogs.PromptText(Title, label, def);

    private static double Num(string? s, double def) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

    private void Done()
    {
        _changed();
        Refresh();
    }

    [RelayCommand]
    private void AddCable()
    {
        var type = Ask("Cable type", "H07RN-F"); if (type is null) return;
        var cores = (int)Num(Ask("Number of cores", "4"), 4);
        var cs = Num(Ask("Cross-section (mm²)", "2.5"), 2.5);
        var len = Num(Ask("Length (m)", "10"), 10);
        var from = Ask("From", "+CAB1") ?? ""; var to = Ask("To", "") ?? "";
        var c = CableService.CreateCable(_p, type, Math.Max(1, cores), cs, len, from, to);
        _p.Audit("Cable", $"{c.Tag} created");
        SelectedList = Lists.First(l => l.id == "cables");
        Done();
    }

    [RelayCommand]
    private void AddPlcStation()
    {
        var tag = Ask("PLC station tag", $"-A{_p.PlcStations.Count + 1}"); if (string.IsNullOrWhiteSpace(tag)) return;
        var profile = Ask($"Addressing profile ({string.Join(", ", PlcProfile.BuiltIn.Select(p => p.Id))})", "generic-iec") ?? "generic-iec";
        SelectedStation = PlcService.CreateStation(_p, tag.Trim(), "PLC", PlcProfile.Get(profile).Id);
        PlcService.AddModule(SelectedStation, SelectedStation.Racks[0], "GEN-CPU");
        SelectedList = Lists.First(l => l.id == "plcio");
        Done();
    }

    [RelayCommand]
    private void AddPlcModule(PlcModuleType? type)
    {
        var st = SelectedStation ?? _p.PlcStations.FirstOrDefault();
        if (st is null || type is null) { _s.Dialogs.ShowInfo(Title, "Create a PLC station and choose a module type."); return; }
        PlcService.AddModule(st, st.Racks[0], type.Id);
        SelectedList = Lists.First(l => l.id == "plcio");
        Done();
    }

    [RelayCommand]
    private void AddCabinet()
    {
        var tag = Ask("Cabinet location tag", $"+CAB{_p.Cabinets.Count + 1}"); if (string.IsNullOrWhiteSpace(tag)) return;
        var w = Num(Ask("Width (mm)", "800"), 800); var h = Num(Ask("Height (mm)", "1200"), 1200);
        var cab = new Cabinet { Tag = tag.Trim(), Name = "Control cabinet", Width = w, Height = h, PlateWidth = w - 100, PlateHeight = h - 100 };
        _p.Cabinets.Add(cab);
        SelectedCabinet = cab;
        Done();
    }

    [RelayCommand]
    private void AddRail()
    {
        if (SelectedCabinet is not { } c) return;
        var y = Num(Ask("Rail Y position on plate (mm)", "500"), 500);
        CabinetLayoutService.AddRail(c, 20, y, c.PlateWidth - 40);
        Done();
    }

    [RelayCommand]
    private void AddDuct()
    {
        if (SelectedCabinet is not { } c) return;
        var y = Num(Ask("Duct Y position on plate (mm)", "600"), 600);
        CabinetLayoutService.AddDuct(c, 10, y, c.PlateWidth - 20, 60);
        Done();
    }

    [RelayCommand]
    private void PlaceDevice()
    {
        if (SelectedCabinet is not { } c) return;
        var tag = Ask("Device tag to place", ""); if (string.IsNullOrWhiteSpace(tag)) return;
        var dev = _p.FindDeviceByTag(tag.Trim());
        if (dev is null) { _s.Dialogs.ShowError(Title, $"Device {tag} not found."); return; }
        var prod = _p.FindProduct(dev.Manufacturer, dev.PartNumber);
        var item = new CabinetItem { DeviceId = dev.Id, Name = dev.Tag, PartNumber = dev.PartNumber, Width = prod?.WidthMm is > 0 ? prod.WidthMm : 45, Height = prod?.HeightMm is > 0 ? prod.HeightMm : 90, Depth = prod?.DepthMm is > 0 ? prod.DepthMm : 80 };
        foreach (var rail in c.Items.Where(i => i.Kind == CabinetItemKind.DinRail).OrderByDescending(i => i.Y))
            if (CabinetLayoutService.PlaceOnRail(c, rail, item, 2)) { Done(); return; }
        _s.Dialogs.ShowError(Title, "No DIN rail has enough free space. Add a rail first.");
    }

    [RelayCommand]
    private void SetProcurementStatus()
    {
        var part = Ask("Part number", ""); if (string.IsNullOrWhiteSpace(part)) return;
        var status = Ask($"Status ({string.Join(", ", Enum.GetNames<ProcurementStatus>())})", "Ordered");
        if (!Enum.TryParse<ProcurementStatus>(status, true, out var st)) { _s.Dialogs.ShowError(Title, "Unknown status."); return; }
        var entry = _p.Procurement.FirstOrDefault(e => string.Equals(e.PartNumber, part.Trim(), StringComparison.OrdinalIgnoreCase));
        if (entry is null) _p.Procurement.Add(entry = new ProcurementEntry { PartNumber = part.Trim() });
        entry.Status = st;
        Done();
    }

    [RelayCommand]
    private void AddMotor()
    {
        var tag = Ask("Motor device tag (e.g. -M1)", ""); if (string.IsNullOrWhiteSpace(tag)) return;
        var dev = _p.FindDeviceByTag(tag.Trim());
        var kw = Num(Ask("Rated power (kW)", "4"), 4);
        var r = PowerCalculator.CurrentFromPower(SupplySystem.ThreePhase, kw, _p.Settings.NominalVoltage, 0.85, 0.88);
        _p.Motors.Add(new Motor { DeviceId = dev?.Id, Tag = tag.Trim(), Description = dev?.Function ?? "", PowerKw = kw, VoltageV = _p.Settings.NominalVoltage, PowerFactor = 0.85, Efficiency = 0.88, RatedCurrentA = Math.Round(r.Output("Current I"), 2) });
        SelectedList = Lists.First(l => l.id == "motors");
        Done();
    }
}

// ====================================================================== Assistant

public sealed record ChatMessage(string Role, string Text);

public sealed partial class AssistantViewModel(IAssistantProvider provider, Func<ElectricalProject?> project, Action<string?> applyFix) : ObservableObject
{
    public string Title => $"Engineering assistant – {provider.Name}";
    public ObservableCollection<ChatMessage> Conversation { get; } = new()
    {
        new ChatMessage("Assistant", "Ask about symbols, calculations, connections, or ask me to check the project for errors. I never change drawings without your confirmation."),
    };
    public ObservableCollection<AssistantSuggestion> Suggestions { get; } = new();
    [ObservableProperty] private string _question = string.Empty;
    [ObservableProperty] private bool _busy;

    [RelayCommand]
    private async Task AskAsync()
    {
        var q = Question.Trim();
        if (q.Length == 0) return;
        Question = string.Empty;
        Conversation.Add(new ChatMessage("You", q));
        Busy = true;
        try
        {
            var a = await provider.AskAsync(q, project());
            Conversation.Add(new ChatMessage("Assistant", a.Text + (a.Sources.Count > 0 ? $"\n\nSources: {string.Join("; ", a.Sources)}" : "")));
            Suggestions.Clear();
            foreach (var s in a.Suggestions) Suggestions.Add(s);
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private void Apply(AssistantSuggestion? s) => applyFix(s?.FixId);
}

// ====================================================================== Symbol editor

public sealed partial class PinRow : ObservableObject
{
    [ObservableProperty] private string _id = "1";
    [ObservableProperty] private string _label = "1";
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private PinDirection _direction = PinDirection.Up;
}

/// <summary>Creates or edits symbols in custom libraries. Graphics are drawn with the CAD tools; pins are edited in a table.</summary>
public sealed partial class SymbolEditorViewModel : ObservableObject
{
    public const string PinLayer = "PINS";
    private readonly SymbolLibraryService _libraries;
    private readonly PermissionService _permissions;

    public SymbolEditorViewModel(SymbolLibraryService libraries, PermissionService permissions, SymbolDefinition? source)
    {
        _libraries = libraries;
        _permissions = permissions;
        Drawing = new CadDrawing();
        Drawing.AddLayer(CadLayer.Create(PinLayer, CadColor.Red, 0.2, "Pin markers (not saved as graphics)"));
        Editor = new CadEditor(Drawing);
        Editor.ActiveLayer = StandardLayers.Symbols;
        Editor.Snap.GridSpacing = 2.5;
        if (source is not null) Load(source);
        Pins.CollectionChanged += (_, _) => RefreshPinMarkers();
        RefreshPinMarkers();
    }

    public string Title => "Symbol editor";
    public CadDrawing Drawing { get; }
    public CadEditor Editor { get; }
    public ObservableCollection<PinRow> Pins { get; } = new();
    public IReadOnlyList<SymbolCategory> Categories { get; } = Enum.GetValues<SymbolCategory>();
    public IReadOnlyList<SymbolRole> Roles { get; } = Enum.GetValues<SymbolRole>();
    public IReadOnlyList<ContactType> ContactTypes { get; } = Enum.GetValues<ContactType>();
    public IReadOnlyList<PinDirection> Directions { get; } = Enum.GetValues<PinDirection>();
    [ObservableProperty] private string _symbolId = "CUSTOM.SYMBOL1";
    [ObservableProperty] private string _name = "New symbol";
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private SymbolCategory _category = SymbolCategory.General;
    [ObservableProperty] private SymbolRole _role = SymbolRole.Standalone;
    [ObservableProperty] private ContactType _contactType = ContactType.None;
    [ObservableProperty] private string _tagPrefix = "A";
    [ObservableProperty] private string _keywords = string.Empty;
    [ObservableProperty] private string _libraryId = "custom";
    [ObservableProperty] private string _message = string.Empty;

    private void Load(SymbolDefinition s)
    {
        var lib = _libraries.FindLibraryOf(s.Id);
        SymbolId = lib?.IsReadOnly == true ? s.Id.Replace("IEC.", "CUSTOM.") : s.Id;
        Name = s.Name; Description = s.Description; Category = s.Category; Role = s.Role; ContactType = s.ContactType;
        TagPrefix = s.DefaultTagPrefix; Keywords = string.Join(", ", s.Keywords);
        Drawing.AddRange(s.Graphics.Select(g => g.Clone()));
        foreach (var p in s.Pins) Pins.Add(new PinRow { Id = p.Id, Label = p.Label, X = p.Position.X, Y = p.Position.Y, Direction = p.Direction });
        Message = lib?.IsReadOnly == true ? $"Copied from read-only library '{lib.Name}'. Saving creates a custom symbol." : string.Empty;
    }

    [RelayCommand]
    private void AddPin()
    {
        var n = Pins.Count + 1;
        Pins.Add(new PinRow { Id = n.ToString(), Label = n.ToString(), X = 0, Y = Pins.Count % 2 == 0 ? 10 : -10, Direction = Pins.Count % 2 == 0 ? PinDirection.Up : PinDirection.Down });
    }

    [RelayCommand] private void RemovePin(PinRow? p) { if (p is not null) Pins.Remove(p); }

    [RelayCommand]
    public void RefreshPinMarkers()
    {
        foreach (var e in Drawing.Entities.Where(e => e.Layer == PinLayer).ToList()) Drawing.Remove(e);
        foreach (var p in Pins)
        {
            Drawing.Add(new CircleEntity(new Point2(p.X, p.Y), 0.8) { Layer = PinLayer });
            Drawing.Add(new TextEntity(new Point2(p.X + 1.2, p.Y + 0.6), p.Label, 1.6) { Layer = PinLayer });
        }
    }

    public SymbolDefinition Build() => new()
    {
        Id = SymbolId.Trim(), Name = Name.Trim(), Description = Description, Category = Category, Role = Role, ContactType = ContactType,
        DefaultTagPrefix = TagPrefix.Trim(), Standard = "Custom",
        Keywords = Keywords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList(),
        Graphics = Drawing.Entities.Where(e => e.Layer != PinLayer).Select(e => e.Clone()).ToList(),
        Pins = Pins.Select(p => new SymbolPin { Id = p.Id, Label = p.Label, Position = new Point2(p.X, p.Y), Direction = p.Direction }).ToList(),
        Attributes =
        {
            new SymbolAttributeDefinition { Name = SymbolAttributes.Tag, Position = new Point2(Math.Min(-3, Drawing.GetExtents().MinX - 2.5), 2) },
            new SymbolAttributeDefinition { Name = SymbolAttributes.Function, Position = new Point2(Math.Min(-3, Drawing.GetExtents().MinX - 2.5), -2), Height = 1.8, Layer = StandardLayers.Annotations },
        },
    };

    [RelayCommand]
    private void SaveSymbol() => Save();

    public bool Save()
    {
        if (!_permissions.Has(AppPermission.ManageLibraries)) { Message = "Your role cannot modify symbol libraries."; return false; }
        var def = Build();
        var errors = def.Validate();
        if (errors.Count > 0) { Message = string.Join(" ", errors); return false; }
        var existingLib = _libraries.FindLibraryOf(def.Id);
        if (existingLib is { IsReadOnly: true }) { Message = $"'{def.Id}' belongs to a read-only library. Choose another id."; return false; }
        var lib = _libraries.GetOrCreateCustomLibrary(LibraryId.Trim().Length == 0 ? "custom" : LibraryId.Trim());
        var saved = _libraries.SaveSymbol(lib, def);
        Message = $"Saved {saved.Id} version {saved.Version} in library '{lib.Name}'.";
        return true;
    }
}
