using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DND.Electrical.Application.Services;
using DND.Electrical.Domain;
using DND.Electrical.Licensing;

namespace DND.Electrical.Presentation.ViewModels;

/// <summary>INTEGRATION module window: connector list (available and planned), target configuration and runs.</summary>
public sealed partial class IntegrationViewModel(ElectricalProject project, IEntitlements entitlements, Action changed) : ObservableObject
{
    private readonly IntegrationService _svc = new(entitlements);
    public string Title => $"Integrations – {project.Info.Number}";
    public IReadOnlyList<ConnectorInfo> Connectors => _svc.All.ToList();
    public ObservableCollection<string> Log { get; } = new();
    [ObservableProperty] private ConnectorInfo? _selected;
    [ObservableProperty] private string _target = string.Empty;
    [ObservableProperty] private string _token = string.Empty;
    [ObservableProperty] private string _signingSecret = string.Empty;
    public bool CanRun => Selected is { Status: ConnectorStatus.Available };

    partial void OnSelectedChanged(ConnectorInfo? value) => OnPropertyChanged(nameof(CanRun));

    [RelayCommand]
    private async Task RunAsync()
    {
        if (Selected is null) return;
        try
        {
            var r = await _svc.RunAsync(Selected.Id, project, new ConnectorSettings(Target.Trim(), Token.Length > 0 ? Token : null, SigningSecret.Length > 0 ? SigningSecret : null));
            Log.Insert(0, $"{DateTime.Now:HH:mm:ss} {Selected.Name}: {(r.Success ? "OK" : "FAILED")} – {r.Message}" + (r.Outputs.Count > 0 ? $" ({string.Join(", ", r.Outputs.Select(Path.GetFileName))})" : ""));
            if (r.Success) changed();
        }
        catch (LicenseException ex) { Log.Insert(0, ex.Message); }
    }
}
