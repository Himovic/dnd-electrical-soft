using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DND.Electrical.Application.Abstractions;
using DND.Electrical.Application.Templates;
using DND.Electrical.Domain;
using DND.Electrical.Reports;
using DND.Electrical.Shared;
using FluentValidation;

namespace DND.Electrical.Presentation.ViewModels;

/// <summary>Base for modal dialogs: the view closes with <see cref="DialogResult"/>.</summary>
public abstract partial class DialogViewModel : ObservableObject
{
    [ObservableProperty] private bool? _dialogResult;
    [ObservableProperty] private string _error = string.Empty;

    public abstract string Title { get; }

    [RelayCommand]
    private void Ok()
    {
        Error = Validate() ?? string.Empty;
        if (Error.Length == 0) DialogResult = true;
    }

    [RelayCommand]
    private void Cancel() => DialogResult = false;

    /// <summary>Returns an error message or null when the input is valid.</summary>
    protected virtual string? Validate() => null;
}

public sealed partial class NewProjectViewModel : DialogViewModel
{
    public NewProjectViewModel(string engineer)
    {
        _engineer = engineer;
        _selectedTemplate = Templates[1];
    }

    public override string Title => "New project";
    public IReadOnlyList<ProjectTemplate> Templates => ProjectTemplate.All;
    [ObservableProperty] private ProjectTemplate _selectedTemplate;
    [ObservableProperty] private string _number = $"P-{DateTime.Now:yyyyMMdd}";
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _customer = string.Empty;
    [ObservableProperty] private string _company = string.Empty;
    [ObservableProperty] private string _engineer;
    [ObservableProperty] private string _site = string.Empty;

    public ProjectInfo ToInfo() => new()
    {
        Number = Number.Trim(), Name = Name.Trim(), Description = Description, CustomerName = Customer, CompanyName = Company,
        Engineer = Engineer, Site = Site, Status = ProjectStatus.Draft,
    };

    protected override string? Validate()
    {
        var r = new Validator().Validate(this);
        return r.IsValid ? null : string.Join("\n", r.Errors.Select(e => e.ErrorMessage));
    }

    private sealed class Validator : AbstractValidator<NewProjectViewModel>
    {
        public Validator()
        {
            RuleFor(x => x.Number).NotEmpty().WithMessage("Project number is required.")
                .MaximumLength(60).Must(n => n.All(c => !Path.GetInvalidFileNameChars().Contains(c))).WithMessage("Project number contains invalid characters.");
            RuleFor(x => x.Name).NotEmpty().WithMessage("Project name is required.").MaximumLength(200);
            RuleFor(x => x.SelectedTemplate).NotNull();
        }
    }
}

public sealed partial class ProjectPropertiesViewModel : DialogViewModel
{
    private readonly ElectricalProject _project;

    public ProjectPropertiesViewModel(ElectricalProject project)
    {
        _project = project;
        Info = project.Info.Clone();
        Settings = project.Settings.Clone();
    }

    public override string Title => "Project properties";
    public ProjectInfo Info { get; }
    public ProjectSettings Settings { get; }
    public IReadOnlyList<ProjectStatus> Statuses { get; } = Enum.GetValues<ProjectStatus>();
    public IReadOnlyList<ProjectIndustry> Industries { get; } = Enum.GetValues<ProjectIndustry>();

    protected override string? Validate() =>
        string.IsNullOrWhiteSpace(Info.Number) || string.IsNullOrWhiteSpace(Info.Name) ? "Project number and name are required." : null;

    public void Apply()
    {
        _project.Info = Info;
        _project.Settings = Settings;
        _project.Audit("Properties", "Project properties changed.");
    }
}

public sealed partial class RuleSettingRow : ObservableObject
{
    public RuleSettingRow(string id, string title, string defaultSeverity, bool enabled, string severity)
    {
        Id = id; Title = title; DefaultSeverity = defaultSeverity; _enabled = enabled; _severity = severity;
    }

    public string Id { get; }
    public string Title { get; }
    public string DefaultSeverity { get; }
    public IReadOnlyList<string> Severities { get; } = new[] { "Default", "Error", "Warning", "Information" };
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _severity;
}

public sealed partial class SettingsViewModel : DialogViewModel
{
    private readonly UserSettings _settings;
    private readonly ProjectSettings? _project;

    public SettingsViewModel(UserSettings settings, ProjectSettings? project)
    {
        _settings = settings;
        _project = project;
        _theme = settings.Theme; _accent = settings.AccentColor; _language = settings.Language; _advanced = settings.AdvancedMode;
        _autosave = settings.AutosaveMinutes; _backups = settings.BackupsToKeep; _grid = settings.GridSpacing; _userName = settings.UserName;
        _wireFormat = project?.WireNumberFormat ?? string.Empty; _tagFormat = project?.DeviceTagFormat ?? string.Empty;
        _perPotential = project?.WireNumberingMode != WireNumberingMode.PerWire;
        _voltage = project?.NominalVoltage ?? 400; _profile = project?.StandardProfileId ?? "IEC";
        if (project is not null)
            foreach (var r in Validation.BuiltInRules.All)
            {
                var cfg = project.ValidationRules.GetValueOrDefault(r.Id);
                Rules.Add(new RuleSettingRow(r.Id, r.Title, r.DefaultSeverity.ToString(), cfg?.Enabled ?? true, cfg?.SeverityOverride ?? "Default"));
            }
    }

    public System.Collections.ObjectModel.ObservableCollection<RuleSettingRow> Rules { get; } = new();

    public override string Title => "Settings";
    public bool HasProject => _project is not null;
    public IReadOnlyList<string> Themes { get; } = new[] { "Light", "Dark" };
    public IReadOnlyList<string> Languages { get; } = new[] { "en", "fr" };
    public IReadOnlyList<string> Profiles { get; } = Calculations.StandardProfile.All.Select(p => p.Id).ToList();
    [ObservableProperty] private string _theme;
    [ObservableProperty] private string _accent;
    [ObservableProperty] private string _language;
    [ObservableProperty] private bool _advanced;
    [ObservableProperty] private int _autosave;
    [ObservableProperty] private int _backups;
    [ObservableProperty] private double _grid;
    [ObservableProperty] private string _userName;
    [ObservableProperty] private string _wireFormat;
    [ObservableProperty] private string _tagFormat;
    [ObservableProperty] private bool _perPotential;
    [ObservableProperty] private double _voltage;
    [ObservableProperty] private string _profile;

    protected override string? Validate()
    {
        if (Autosave is < 1 or > 120) return "Autosave interval must be between 1 and 120 minutes.";
        if (Grid is <= 0 or > 50) return "Grid spacing must be between 0 and 50 mm.";
        if (HasProject && !TagFormat.Contains("{N}")) return "The device tag format must contain {N}.";
        if (HasProject && !(WireFormat.Contains("{GSEQ") || WireFormat.Contains("{SEQ"))) return "The wire number format must contain {GSEQ} or {SEQ}.";
        return null;
    }

    public void Apply()
    {
        _settings.Theme = Theme; _settings.AccentColor = Accent; _settings.Language = Language; _settings.AdvancedMode = Advanced;
        _settings.AutosaveMinutes = Autosave; _settings.BackupsToKeep = Backups; _settings.GridSpacing = Grid; _settings.UserName = UserName;
        if (_project is null) return;
        _project.WireNumberFormat = WireFormat; _project.DeviceTagFormat = TagFormat;
        _project.WireNumberingMode = PerPotential ? WireNumberingMode.PerPotential : WireNumberingMode.PerWire;
        _project.NominalVoltage = Voltage; _project.StandardProfileId = Profile;
        _project.ValidationRules.Clear();
        foreach (var r in Rules.Where(r => !r.Enabled || r.Severity != "Default"))
            _project.ValidationRules[r.Id] = new ValidationRuleSetting(r.Enabled, r.Severity == "Default" ? null : r.Severity);
    }
}

public sealed partial class ReportChoice : ObservableObject
{
    public ReportChoice(string id, string title) { Id = id; Title = title; }
    public string Id { get; }
    public string Title { get; }
    [ObservableProperty] private bool _selected = true;
}

public sealed partial class ReportExportViewModel : DialogViewModel
{
    public ReportExportViewModel(string language)
    {
        _language = language;
        foreach (var (id, title) in ProjectReports.Catalogue(language)) Reports.Add(new ReportChoice(id, title));
    }

    public override string Title => "Generate reports";
    public ObservableCollection<ReportChoice> Reports { get; } = new();
    public IReadOnlyList<string> Formats { get; } = new[] { "pdf", "xlsx", "csv" };
    public IReadOnlyList<string> Languages { get; } = new[] { "en", "fr" };
    [ObservableProperty] private string _format = "pdf";
    [ObservableProperty] private string _language;
    [ObservableProperty] private bool _includeCover = true;
    [ObservableProperty] private string _footerText = string.Empty;

    public IReadOnlyList<string> SelectedReportIds => Reports.Where(r => r.Selected).Select(r => r.Id).ToList();

    [RelayCommand] private void SelectAll(bool? value) { foreach (var r in Reports) r.Selected = value ?? true; }

    protected override string? Validate() => SelectedReportIds.Count == 0 ? "Select at least one report." :
        Format == "csv" && SelectedReportIds.Count > 1 ? "CSV export writes one list: select exactly one report." : null;
}

public sealed class AboutViewModel : DialogViewModel
{
    public override string Title => "About";
    public string Product => ProductInfo.ProductName;
    public string Version => ProductInfo.VersionString;
    public string Author => ProductInfo.Author;
    public string Developer => ProductInfo.Developer;
    public string Copyright => ProductInfo.Copyright;
    public string PoweredBy => ProductInfo.PoweredBy;
    public string Tagline => ProductInfo.Tagline;
    public string Runtime => $".NET {Environment.Version} · {System.Runtime.InteropServices.RuntimeInformation.OSDescription}";
    public string Notice => "Calculation results are preliminary engineering aids and do not certify compliance with any standard. Demo projects contain fictional data.";
}

public sealed class TextViewerViewModel(string title, string text) : ObservableObject
{
    public string Title { get; } = title;
    public string Text { get; } = text;
}
