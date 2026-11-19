using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using DND.Electrical.Domain;
using DND.Electrical.Shared;

namespace DND.Electrical.Plugins;

/// <summary>Capabilities a plugin may request. Only granted permissions are exposed through <see cref="IPluginHost"/>.</summary>
[Flags]
public enum PluginPermission { None = 0, ProjectRead = 1, ProjectWrite = 2, UiCommands = 4, FileExport = 8 }

public sealed record PluginManifest(
    string Id,
    string Name,
    string Version,
    string Description,
    string Author,
    string MinHostVersion,
    string? MaxHostVersion,
    string EntryAssembly,
    string EntryType,
    PluginPermission Permissions,
    string Sha256);

public sealed record PluginCommand(string Id, string Title, string Category, Action Execute);

/// <summary>Host services available to plugins (gated by permissions).</summary>
public interface IPluginHost
{
    Version HostVersion { get; }
    PluginPermission Granted { get; }

    /// <summary>Read-only access requires <see cref="PluginPermission.ProjectRead"/>; returns null otherwise or when no project is open.</summary>
    ElectricalProject? CurrentProject { get; }

    void RegisterCommand(PluginCommand command);
    void Log(string message);
}

/// <summary>Contract implemented by plugin entry types (public parameterless constructor).</summary>
public interface IPlugin
{
    void Initialize(IPluginHost host);
    void Shutdown();
}

public enum PluginState { Loaded, Untrusted, Incompatible, Invalid, Failed, Disabled }

public sealed record PluginInfo(PluginManifest? Manifest, string Folder, PluginState State, string Message, string? ActualSha256 = null);

/// <summary>
/// Discovers, verifies and loads plugins. A plugin is executed only when: its manifest is valid, the host
/// version is in range, the entry assembly hash matches the manifest, and that hash is in the administrator's
/// trust list. Each plugin is loaded in its own collectible <see cref="AssemblyLoadContext"/>.
/// </summary>
public sealed class PluginManager(string pluginFolder, Func<ElectricalProject?> currentProject, Action<string>? log = null)
{
    private readonly List<(PluginInfo info, IPlugin instance, AssemblyLoadContext alc)> _loaded = new();
    private readonly List<PluginCommand> _commands = new();

    public HashSet<string> TrustedHashes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<PluginCommand> Commands => _commands;
    public List<PluginInfo> Discovered { get; } = new();

    public static string ComputeSha256(string file)
    {
        using var s = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    public static (PluginManifest? manifest, string? error) ReadManifest(string folder)
    {
        var path = Path.Combine(folder, "plugin.json");
        if (!File.Exists(path)) return (null, "plugin.json not found.");
        try
        {
            var m = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
            if (m is null || string.IsNullOrWhiteSpace(m.Id) || string.IsNullOrWhiteSpace(m.EntryAssembly) || string.IsNullOrWhiteSpace(m.EntryType) || string.IsNullOrWhiteSpace(m.Sha256))
                return (null, "Manifest is missing required fields (id, entryAssembly, entryType, sha256).");
            if (m.EntryAssembly.Contains("..") || Path.IsPathRooted(m.EntryAssembly)) return (null, "Entry assembly must be inside the plugin folder.");
            return (m, null);
        }
        catch (JsonException ex) { return (null, "Invalid manifest: " + ex.Message); }
    }

    public static bool IsCompatible(PluginManifest m, Version host) =>
        Version.TryParse(m.MinHostVersion, out var min) && host >= min &&
        (m.MaxHostVersion is null || (Version.TryParse(m.MaxHostVersion, out var max) && host <= max));

    /// <summary>Verifies all plugins and loads the trusted, compatible ones.</summary>
    public IReadOnlyList<PluginInfo> DiscoverAndLoad(Version? hostVersion = null)
    {
        var host = hostVersion ?? ProductInfo.Version;
        Discovered.Clear();
        if (!Directory.Exists(pluginFolder)) return Discovered;
        foreach (var dir in Directory.EnumerateDirectories(pluginFolder))
        {
            var (m, error) = ReadManifest(dir);
            if (m is null) { Discovered.Add(new PluginInfo(null, dir, PluginState.Invalid, error!)); continue; }
            if (!IsCompatible(m, host)) { Discovered.Add(new PluginInfo(m, dir, PluginState.Incompatible, $"Requires host {m.MinHostVersion}–{m.MaxHostVersion ?? "any"}.")); continue; }
            var asm = Path.Combine(dir, m.EntryAssembly);
            if (!File.Exists(asm)) { Discovered.Add(new PluginInfo(m, dir, PluginState.Invalid, "Entry assembly not found.")); continue; }
            var hash = ComputeSha256(asm);
            if (!string.Equals(hash, m.Sha256, StringComparison.OrdinalIgnoreCase))
            { Discovered.Add(new PluginInfo(m, dir, PluginState.Invalid, "Assembly hash does not match the manifest (file modified).", hash)); continue; }
            if (!TrustedHashes.Contains(hash))
            { Discovered.Add(new PluginInfo(m, dir, PluginState.Untrusted, "Not trusted by an administrator.", hash)); continue; }
            Discovered.Add(Load(m, dir, asm, hash, host));
        }
        return Discovered;
    }

    private PluginInfo Load(PluginManifest m, string dir, string asmPath, string hash, Version host)
    {
        var alc = new AssemblyLoadContext($"plugin:{m.Id}", isCollectible: true);
        try
        {
            var asm = alc.LoadFromAssemblyPath(Path.GetFullPath(asmPath));
            var type = asm.GetType(m.EntryType, throwOnError: true)!;
            if (!typeof(IPlugin).IsAssignableFrom(type)) throw new InvalidOperationException($"{m.EntryType} does not implement IPlugin.");
            var instance = (IPlugin)Activator.CreateInstance(type)!;
            instance.Initialize(new Host(this, m, host));
            var info = new PluginInfo(m, dir, PluginState.Loaded, "Loaded.", hash);
            _loaded.Add((info, instance, alc));
            return info;
        }
        catch (Exception ex) when (ex is TargetInvocationException or InvalidOperationException or TypeLoadException or BadImageFormatException or FileLoadException or MissingMethodException)
        {
            alc.Unload();
            return new PluginInfo(m, dir, PluginState.Failed, ex.InnerException?.Message ?? ex.Message, hash);
        }
    }

    public void UnloadAll()
    {
        foreach (var (_, instance, alc) in _loaded)
        {
            try { instance.Shutdown(); } catch (Exception ex) when (ex is InvalidOperationException) { }
            alc.Unload();
        }
        _loaded.Clear();
        _commands.Clear();
    }

    private sealed class Host(PluginManager owner, PluginManifest manifest, Version host) : IPluginHost
    {
        public Version HostVersion => host;
        public PluginPermission Granted => manifest.Permissions;
        public ElectricalProject? CurrentProject => Granted.HasFlag(PluginPermission.ProjectRead) ? owner.currentProjectAccessor() : null;

        public void RegisterCommand(PluginCommand command)
        {
            if (!Granted.HasFlag(PluginPermission.UiCommands)) throw new UnauthorizedAccessException($"Plugin {manifest.Id} lacks the UiCommands permission.");
            owner._commands.Add(command with { Id = $"{manifest.Id}.{command.Id}" });
        }

        public void Log(string message) => owner.logAccessor?.Invoke($"[{manifest.Id}] {message}");
    }

    private Func<ElectricalProject?> currentProjectAccessor => currentProject;
    private Action<string>? logAccessor => log;
}
