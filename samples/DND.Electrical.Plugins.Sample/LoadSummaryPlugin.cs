using DND.Electrical.Plugins;

namespace DND.Electrical.Plugins.Sample;

/// <summary>Example plugin: adds a command that summarises installed motor power (read-only project access).</summary>
public sealed class LoadSummaryPlugin : IPlugin
{
    private IPluginHost? _host;

    public string? LastResult { get; private set; }

    public void Initialize(IPluginHost host)
    {
        _host = host;
        host.RegisterCommand(new PluginCommand("motor-summary", "Motor power summary", "Plugins", Run));
        host.Log("Load summary plugin initialised.");
    }

    private void Run()
    {
        var p = _host?.CurrentProject;
        LastResult = p is null ? "No project open." : $"{p.Motors.Count} motors, {p.Motors.Sum(m => m.PowerKw):0.##} kW installed.";
        _host?.Log(LastResult);
    }

    public void Shutdown() => _host = null;
}
