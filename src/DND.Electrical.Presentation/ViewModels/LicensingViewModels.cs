using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DND.Electrical.Application.Abstractions;
using DND.Electrical.Licensing;
using DND.Electrical.Presentation.Services;
using DND.Electrical.Shared;

namespace DND.Electrical.Presentation.ViewModels;

public sealed record ModuleRow(string Code, string Name, string Purpose, bool Licensed, string Maturity);

/// <summary>Licensing and subscription section: status, modules, activation (online/offline), refresh, deactivation.</summary>
public sealed partial class LicensingViewModel : ObservableObject
{
    private readonly LicenseManager _mgr;
    private readonly UserSettings _settings;
    private readonly IDialogService _dialogs;
    private readonly Action _changed;
    private readonly AppPaths _paths;

    public LicensingViewModel(LicenseManager manager, UserSettings settings, IDialogService dialogs, Action changed, AppPaths paths)
    {
        _mgr = manager; _settings = settings; _dialogs = dialogs; _changed = changed; _paths = paths;
        _serverUrl = settings.LicenseServerUrl;
        Load();
    }

    public string Title => "Licensing and subscription";
    public string Product => ProductInfo.ProductName;
    public string DeviceId => _mgr.DeviceId;
    public ObservableCollection<ModuleRow> Modules { get; } = new();
    public ObservableCollection<(string Label, string Value)> Details { get; } = new();
    [ObservableProperty] private string _state = string.Empty;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private string _licenseKey = string.Empty;
    [ObservableProperty] private string _serverUrl;
    [ObservableProperty] private bool _busy;

    private void Load()
    {
        var e = _mgr.Current;
        State = e.State.ToString();
        Message = e.Message;
        Details.Clear();
        var c = e.Claims;
        void D(string l, string? v) => Details.Add((l, string.IsNullOrEmpty(v) ? "—" : v));
        D("Licence status", e.State.ToString());
        D("Licence ID", c?.LicenseId);
        D("Licence owner", c?.Owner);
        D("Organization", c?.OrganizationName);
        D("Subscription type", c?.Subscription.ToString());
        D("Seat model", c is null ? null : $"{c.Seats} · users {c.MaxUsers} · devices {c.MaxDevices}" + (c.Seats == SeatModel.Floating ? $" · concurrent {c.MaxConcurrent}" : ""));
        D("Activation / validity start", c?.ValidFrom.ToString("d"));
        D("Expiration", c is null ? null : c.ValidUntil?.ToString("d") ?? "Perpetual");
        D("Maintenance until", c?.MaintenanceUntil?.ToString("d"));
        D("Offline use", c is null ? null : c.OfflineAllowed ? "Allowed" : $"Online revalidation every {c.RevalidationDays} days");
        D("Grace period", c is null ? null : $"{c.GracePeriodDays} days" + (e.GraceEndsAt is { } g ? $" (ends {g:d})" : ""));
        D("This device", _mgr.DeviceId);
        D("Bound device", c?.DeviceId ?? (c is null ? null : "Not device-bound"));
        D("Renewal", e.State is LicenseState.GracePeriod or LicenseState.Expired ? "Renewal required – contact D&D | Data&Development" : c?.ValidUntil is { } u ? $"Renew before {u:d}" : "—");
        Modules.Clear();
        foreach (var m in ModuleCatalog.All) Modules.Add(new ModuleRow(m.Code, m.Name, m.Purpose, _mgr.IsEnabled(m.Module), m.Maturity));
    }

    private void Done(string info)
    {
        _settings.LicenseServerUrl = ServerUrl;
        _mgr.Refresh();
        Load();
        _changed();
        _dialogs.ShowInfo(Title, info + "\n\n" + _mgr.Current.Message);
    }

    private LicenseServerClient Client() => new(new HttpClient { BaseAddress = new Uri(ServerUrl.EndsWith('/') ? ServerUrl : ServerUrl + "/"), Timeout = TimeSpan.FromSeconds(20) });

    [RelayCommand]
    private async Task ActivateAsync()
    {
        if (string.IsNullOrWhiteSpace(LicenseKey)) { _dialogs.ShowError(Title, "Enter the licence key provided by D&D | Data&Development."); return; }
        await Run(async () =>
        {
            var r = await Client().ActivateAsync(LicenseKey.Trim(), _mgr.DeviceId, DeviceIdentity.DeviceName, _settings.UserName);
            _mgr.Install(r.License, onlineValidated: true);
            Done(r.Message);
        });
    }

    [RelayCommand]
    private async Task RefreshOnlineAsync()
    {
        if (_mgr.Current.Claims is not { } c) { _dialogs.ShowError(Title, "No licence to refresh."); return; }
        await Run(async () =>
        {
            var client = Client();
            var r = await client.RefreshAsync(c.LicenseId, _mgr.DeviceId);
            _mgr.Install(r.License, onlineValidated: true);
            _mgr.InstallRevocationList(await client.RevocationsAsync());
            Done(r.Message);
        });
    }

    [RelayCommand]
    private async Task DeactivateAsync()
    {
        if (_mgr.Current.Claims is not { } c) return;
        if (!_dialogs.Confirm(Title, "Deactivate this device and free its seat? Projects stay available read-only.")) return;
        await Run(async () =>
        {
            var r = await Client().DeactivateAsync(c.LicenseId, _mgr.DeviceId);
            _mgr.Uninstall();
            Done(r.Message);
        });
    }

    [RelayCommand]
    private void ImportLicenseFile()
    {
        var file = _dialogs.OpenFile("Import licence file", "D&D licence (*.dndlic)|*.dndlic");
        if (file is null) return;
        try
        {
            _mgr.Install(SignedLicense.FromJson(File.ReadAllText(file)));
            Done("Licence imported.");
        }
        catch (Exception ex) when (ex is LicenseException or IOException or System.Text.Json.JsonException or InvalidDataException) { _dialogs.ShowError(Title, ex.Message); }
    }

    /// <summary>Offline activation: writes a request file to send to D&amp;D (or to the licence administrator).</summary>
    [RelayCommand]
    private void ExportActivationRequest()
    {
        var id = _dialogs.PromptText(Title, "Licence ID (from your licence certificate)", _mgr.Current.Claims?.LicenseId ?? "LIC-");
        if (string.IsNullOrWhiteSpace(id)) return;
        var path = _dialogs.SaveFile("Save activation request", "Activation request (*.json)|*.json", $"activation-request-{DeviceIdentity.DeviceName}.json");
        if (path is null) return;
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(_mgr.CreateActivationRequest(id.Trim(), _settings.UserName), LicenseJson.Indented));
        _dialogs.ShowInfo(Title, "Send this file to your licence administrator. Import the returned .dndlic file with \"Import licence file\".");
    }

    /// <summary>Installs the evaluation licence shipped with the product (signed like every licence).</summary>
    [RelayCommand]
    private void InstallEvaluation()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "Licensing", "evaluation.dndlic");
        if (!File.Exists(file)) { _dialogs.ShowError(Title, "No evaluation licence is bundled with this installation."); return; }
        try
        {
            _mgr.Install(SignedLicense.FromJson(File.ReadAllText(file)));
            Done("Evaluation licence installed. It is for evaluation only, not for commercial use.");
        }
        catch (LicenseException ex) { _dialogs.ShowError(Title, ex.Message); }
    }

    private async Task Run(Func<Task> action)
    {
        Busy = true;
        try { await action(); }
        catch (Exception ex) when (ex is LicenseException or HttpRequestException or TaskCanceledException or UriFormatException)
        {
            _dialogs.ShowError(Title, ex is HttpRequestException or TaskCanceledException
                ? $"The licensing server could not be reached ({ex.Message}). Your current licence keeps working offline within its terms; use offline activation if this workstation has no internet access."
                : ex.Message);
        }
        finally { Busy = false; }
    }
}

/// <summary>Upgrade / activation page shown when an unlicensed module is opened.</summary>
public sealed partial class ModuleLockedViewModel(EngineeringModule module, LicenseEvaluation evaluation, Action openLicensing) : DialogViewModel
{
    public override string Title => $"{ModuleCatalog.Get(module).Name} is not licensed";
    public string Code => ModuleCatalog.Code(module);
    public string Name => ModuleCatalog.Get(module).Name;
    public string Purpose => ModuleCatalog.Get(module).Purpose;
    public string Status => evaluation.Message;
    public string Advice => evaluation.AllowsFeatures
        ? $"Your licence does not include {Code}. Contact D&D | Data&Development to add the module to your subscription; after the upgrade, refresh the licence."
        : "Activate a licence (online key, offline licence file or the bundled evaluation licence). Your projects remain accessible read-only.";

    [RelayCommand]
    private void ManageLicence()
    {
        DialogResult = false;
        openLicensing();
    }
}
