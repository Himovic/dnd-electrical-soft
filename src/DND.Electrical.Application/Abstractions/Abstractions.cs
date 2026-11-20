using DND.Electrical.Domain;

namespace DND.Electrical.Application.Abstractions;

/// <summary>Persists complete projects. The local implementation stores one SQLite database per project file.</summary>
public interface IProjectStore
{
    Task SaveAsync(ElectricalProject project, string path, CancellationToken ct = default);
    Task<ElectricalProject> LoadAsync(string path, CancellationToken ct = default);

    /// <summary>Reads only the metadata (for recent-project lists and pickers).</summary>
    Task<ProjectInfo?> ReadInfoAsync(string path, CancellationToken ct = default);
}

public sealed record LockInfo(string User, string Machine, int ProcessId, DateTimeOffset Since);

/// <summary>Prevents two users/processes from editing the same project file simultaneously.</summary>
public interface IProjectLockService
{
    /// <summary>Acquires the lock or returns the current holder when the project is locked by someone else.</summary>
    (bool acquired, LockInfo? holder) TryAcquire(string projectPath, bool force = false);
    void Release(string projectPath);
}

public sealed record BackupEntry(string Path, DateTimeOffset Created, long Size);

/// <summary>Keeps rolling backups of project files (version history).</summary>
public interface IBackupService
{
    BackupEntry? CreateBackup(string projectPath);
    IReadOnlyList<BackupEntry> List(string projectPath);
    void Restore(BackupEntry backup, string projectPath);
}

public sealed record RecoveryEntry(Guid ProjectId, string OriginalPath, string RecoveryPath, DateTimeOffset SavedAt, string ProjectName);

/// <summary>Crash recovery copies written by autosave (never overwrites the user's file).</summary>
public interface IRecoveryStore
{
    Task WriteAsync(ElectricalProject project, string? originalPath, CancellationToken ct = default);
    IReadOnlyList<RecoveryEntry> FindRecoverable();
    void Discard(Guid projectId);
}

/// <summary>Secure storage for secrets (API keys). Implementations must encrypt at rest.</summary>
public interface ISecretStore
{
    bool IsSupported { get; }
    void Set(string name, string? value);
    string? Get(string name);
}

public sealed class UserSettings
{
    public string Theme { get; set; } = "Light";
    public string AccentColor { get; set; } = "#2563EB";
    public string Language { get; set; } = "en";
    public bool AdvancedMode { get; set; }
    public int AutosaveMinutes { get; set; } = 5;
    public int BackupsToKeep { get; set; } = 10;
    public List<string> RecentProjects { get; set; } = new();
    public HashSet<string> CompletedTutorials { get; set; } = new();
    public string? SymbolLibraryFolder { get; set; }
    public bool ShowGrid { get; set; } = true;
    public bool OrthoMode { get; set; } = true;
    public double GridSpacing { get; set; } = 5;
    public string UserName { get; set; } = Environment.UserName;
    public string Role { get; set; } = "Administrator";
    public string AssistantProvider { get; set; } = "local";
    /// <summary>Claude model used when <see cref="AssistantProvider"/> is "claude".</summary>
    public string AssistantModel { get; set; } = "claude-opus-5-5";
    /// <summary>Allow sending a project summary (tags, counts, validation findings) to the external AI provider. Off by default.</summary>
    public bool AssistantShareProjectData { get; set; }
    public string LicenseServerUrl { get; set; } = "https://licensing.dnd-engineering.example/";
    public bool FirstRun { get; set; } = true;

    /// <summary>SHA-256 hashes of plugin assemblies trusted by an administrator.</summary>
    public HashSet<string> TrustedPluginHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void AddRecent(string path, int max = 12)
    {
        RecentProjects.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentProjects.Insert(0, path);
        if (RecentProjects.Count > max) RecentProjects.RemoveRange(max, RecentProjects.Count - max);
    }
}

public interface IUserSettingsStore
{
    UserSettings Load();
    void Save(UserSettings settings);
}

/// <summary>Well-known application folders (overridable for tests and portable installs).</summary>
public sealed class AppPaths
{
    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DND", "ElectricalSuite");
    }

    public string Root { get; }
    public string Settings => Path.Combine(Root, "settings.json");
    public string Recovery => Path.Combine(Root, "Recovery");
    public string Logs => Path.Combine(Root, "Logs");
    public string Symbols => Path.Combine(Root, "Symbols");
    public string Plugins => Path.Combine(Root, "Plugins");
    public string Documentation => Path.Combine(Root, "Documentation");
    public string DemoProjects => Path.Combine(Root, "DemoProjects");
    public string Secrets => Path.Combine(Root, "secrets.bin");
    public string Licensing => Path.Combine(Root, "Licensing");

    public void EnsureCreated()
    {
        foreach (var d in new[] { Root, Recovery, Logs, Symbols, Plugins, Documentation, DemoProjects }) Directory.CreateDirectory(d);
    }
}
