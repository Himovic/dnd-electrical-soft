using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DND.Electrical.Application.Services;
using DND.Electrical.Domain;
using DND.Electrical.Presentation.Services;

namespace DND.Electrical.Presentation.ViewModels;

public sealed record ConflictRow(EngineeringConflict Conflict)
{
    public string Object => Conflict.ObjectTag;
    public string Field => Conflict.Field;
    public string Values => string.Join("  /  ", Conflict.Values.Select(v => $"{v.Source}: {v.Value}"));
}

public sealed record RepresentationRow(string Kind, string Description, string Location);

/// <summary>
/// D&amp;D PROJECT module: lifecycle phase, members and roles, tasks, milestones, issue register, approvals,
/// document register, maintenance and commissioning records, and the unified engineering-object view with
/// conflict detection. Lists are edited in place (DataGrid) and every change marks the project dirty.
/// </summary>
public sealed partial class ProjectManagementViewModel : ObservableObject
{
    private readonly ElectricalProject _p;
    private readonly Action _changed;
    private readonly IDialogService _dialogs;
    private readonly string _user;

    public ProjectManagementViewModel(ElectricalProject project, Action changed, IDialogService dialogs, string user)
    {
        _p = project; _changed = changed; _dialogs = dialogs; _user = user;
        _phase = project.Info.Phase;
        Members = new(project.Members); Tasks = new(project.Tasks); Milestones = new(project.Milestones); Issues = new(project.Issues);
        Approvals = new(project.Approvals); Documents = new(project.DocumentRegister); Maintenance = new(project.Maintenance); Commissioning = new(project.Commissioning);
        Devices = project.Devices.OrderBy(d => d.Tag, StringComparer.OrdinalIgnoreCase).ToList();
        DetectConflicts();
    }

    public string Title => $"Project management – {_p.Info.Number}";
    public IReadOnlyList<LifecyclePhase> Phases { get; } = Enum.GetValues<LifecyclePhase>();
    public IReadOnlyList<Device> Devices { get; }
    public ObservableCollection<ProjectMember> Members { get; }
    public ObservableCollection<ProjectTask> Tasks { get; }
    public ObservableCollection<Milestone> Milestones { get; }
    public ObservableCollection<EngineeringIssue> Issues { get; }
    public ObservableCollection<ApprovalRecord> Approvals { get; }
    public ObservableCollection<DocumentRegisterEntry> Documents { get; }
    public ObservableCollection<MaintenanceRecord> Maintenance { get; }
    public ObservableCollection<CommissioningRecord> Commissioning { get; }
    public ObservableCollection<ConflictRow> Conflicts { get; } = new();
    public ObservableCollection<RepresentationRow> Representations { get; } = new();

    [ObservableProperty] private LifecyclePhase _phase;
    [ObservableProperty] private Device? _selectedDevice;
    [ObservableProperty] private ApprovalRecord? _selectedApproval;
    [ObservableProperty] private ConflictRow? _selectedConflict;

    public string Summary =>
        $"Phase: {_p.Info.Phase} · {Members.Count} members · {Tasks.Count(t => t.Status is not (WorkStatus.Done or WorkStatus.Cancelled))} open tasks · " +
        $"{Issues.Count(i => i.Status is not (WorkStatus.Done or WorkStatus.Cancelled))} open issues · {Approvals.Count(a => a.Status == ApprovalStatus.Pending)} pending approvals · {Conflicts.Count} representation conflicts";

    partial void OnPhaseChanged(LifecyclePhase value)
    {
        if (_p.Info.Phase == value) return;
        _p.Audit("Lifecycle phase", $"{_p.Info.Phase} → {value}", _user);
        _p.Info.Phase = value;
        Changed();
    }

    partial void OnSelectedDeviceChanged(Device? value)
    {
        Representations.Clear();
        if (value is null) return;
        foreach (var r in EngineeringObjectService.Describe(_p, value).Representations) Representations.Add(new(r.Kind.ToString(), r.Description, r.Location));
    }

    /// <summary>Called by the view after a cell edit so the project is marked modified.</summary>
    public void Changed() { _changed(); OnPropertyChanged(nameof(Summary)); }

    private void Add<T>(List<T> model, ObservableCollection<T> view, T item) { model.Add(item); view.Add(item); Changed(); }

    [RelayCommand]
    private void AddMember()
    {
        var name = _dialogs.PromptText("Add member", "Name");
        if (string.IsNullOrWhiteSpace(name)) return;
        Add(_p.Members, Members, new ProjectMember { Name = name.Trim() });
        _p.Audit("Member added", name.Trim(), _user);
    }

    [RelayCommand]
    private void AddTask()
    {
        var title = _dialogs.PromptText("Add task", "Task");
        if (string.IsNullOrWhiteSpace(title)) return;
        Add(_p.Tasks, Tasks, new ProjectTask { Title = title.Trim(), Phase = _p.Info.Phase, ObjectId = SelectedDevice?.Id });
    }

    [RelayCommand]
    private void AddMilestone()
    {
        var name = _dialogs.PromptText("Add milestone", "Milestone");
        if (string.IsNullOrWhiteSpace(name)) return;
        Add(_p.Milestones, Milestones, new Milestone { Name = name.Trim(), Phase = _p.Info.Phase, Due = DateTimeOffset.Now.Date.AddDays(14) });
    }

    [RelayCommand]
    private void RaiseIssue()
    {
        var title = _dialogs.PromptText("Raise issue", "Issue title");
        if (string.IsNullOrWhiteSpace(title)) return;
        var issue = ApprovalService.RaiseIssue(_p, title.Trim(), string.Empty, IssueSeverity.Medium, _user, SelectedDevice?.Id);
        Issues.Add(issue);
        Changed();
    }

    [RelayCommand]
    private void RequestApproval()
    {
        var approver = _dialogs.PromptText("Request approval", "Approver (project member name)");
        if (string.IsNullOrWhiteSpace(approver)) return;
        try
        {
            var a = ApprovalService.Request(_p, "Project revision", _p.Id, $"{_p.Info.Number} rev {_p.Info.CurrentRevision}", _user, approver.Trim());
            Approvals.Add(a);
            Changed();
        }
        catch (InvalidOperationException ex) { _dialogs.ShowError(Title, ex.Message); }
    }

    [RelayCommand] private void Approve() => Decide(true);
    [RelayCommand] private void Reject() => Decide(false);

    private void Decide(bool approve)
    {
        if (SelectedApproval is not { } a) { _dialogs.ShowError(Title, "Select an approval first."); return; }
        var comment = _dialogs.PromptText(approve ? "Approve" : "Reject", "Comment") ?? string.Empty;
        try
        {
            ApprovalService.Decide(_p, a, _user, approve, comment);
            var i = Approvals.IndexOf(a);
            Approvals[i] = a; // refresh row
            Changed();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException) { _dialogs.ShowError(Title, ex.Message); }
    }

    [RelayCommand]
    private void AddDocument()
    {
        var number = _dialogs.PromptText("Document register", "Document number", $"{_p.Info.Number}-DOC-{Documents.Count + 1:000}");
        if (string.IsNullOrWhiteSpace(number)) return;
        Add(_p.DocumentRegister, Documents, new DocumentRegisterEntry { Number = number.Trim(), Revision = _p.Info.CurrentRevision });
    }

    [RelayCommand]
    private void AddMaintenance()
    {
        if (SelectedDevice is not { } d) { _dialogs.ShowError(Title, "Select the equipment (engineering object) first."); return; }
        var text = _dialogs.PromptText("Maintenance record", $"Work performed on {d.Tag}");
        if (string.IsNullOrWhiteSpace(text)) return;
        Add(_p.Maintenance, Maintenance, new MaintenanceRecord { ObjectId = d.Id, Description = text.Trim(), Technician = _user });
        OnSelectedDeviceChanged(d);
    }

    [RelayCommand]
    private void AddCommissioning()
    {
        if (SelectedDevice is not { } d) { _dialogs.ShowError(Title, "Select the equipment (engineering object) first."); return; }
        var test = _dialogs.PromptText("Commissioning test", $"Test on {d.Tag}", "Insulation resistance");
        if (string.IsNullOrWhiteSpace(test)) return;
        Add(_p.Commissioning, Commissioning, new CommissioningRecord { ObjectId = d.Id, Test = test.Trim(), By = _user });
        OnSelectedDeviceChanged(d);
    }

    [RelayCommand]
    private void DetectConflicts()
    {
        Conflicts.Clear();
        foreach (var c in EngineeringObjectService.DetectAll(_p)) Conflicts.Add(new ConflictRow(c));
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>Synchronises the selected conflict after the user explicitly chose the authoritative representation.</summary>
    [RelayCommand]
    private void Synchronize()
    {
        if (SelectedConflict is not { } row) { _dialogs.ShowError(Title, "Select a conflict first."); return; }
        var options = row.Conflict.Values.Select(v => v.Source.ToString()).ToList();
        var choice = _dialogs.PromptText("Synchronise representations", $"{row.Conflict.Description}\n\nWhich representation is authoritative? ({string.Join(" / ", options)})", options[0]);
        if (choice is null) return;
        if (!Enum.TryParse<RepresentationKind>(choice.Trim(), true, out var source) || !options.Contains(source.ToString()))
        { _dialogs.ShowError(Title, $"'{choice}' is not one of: {string.Join(", ", options)}."); return; }
        if (!_dialogs.Confirm("Synchronise representations", $"Apply the {source} value to every other representation of {row.Object}?")) return;
        EngineeringObjectService.Synchronize(_p, row.Conflict, source, _user);
        Changed();
        DetectConflicts();
    }
}
