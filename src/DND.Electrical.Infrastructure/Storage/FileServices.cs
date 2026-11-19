using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DND.Electrical.Application.Abstractions;
using DND.Electrical.Domain;
using DND.Electrical.Infrastructure.Persistence;

namespace DND.Electrical.Infrastructure.Storage;

/// <summary>Lock file next to the project ("project.dndx.lock") with holder information and stale-lock detection.</summary>
public sealed class FileProjectLockService : IProjectLockService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string LockPath(string projectPath) => Path.GetFullPath(projectPath) + ".lock";

    public (bool acquired, LockInfo? holder) TryAcquire(string projectPath, bool force = false)
    {
        var lockPath = LockPath(projectPath);
        var me = new LockInfo(Environment.UserName, Environment.MachineName, Environment.ProcessId, DateTimeOffset.Now);
        if (File.Exists(lockPath) && !force)
        {
            LockInfo? holder = null;
            try { holder = JsonSerializer.Deserialize<LockInfo>(File.ReadAllText(lockPath)); } catch (Exception) { }
            if (holder is not null && !IsStale(holder) && !(holder.Machine == me.Machine && holder.ProcessId == me.ProcessId))
                return (false, holder);
        }
        File.WriteAllText(lockPath, JsonSerializer.Serialize(me, Json));
        return (true, null);
    }

    public void Release(string projectPath)
    {
        var lockPath = LockPath(projectPath);
        try
        {
            if (!File.Exists(lockPath)) return;
            var holder = JsonSerializer.Deserialize<LockInfo>(File.ReadAllText(lockPath));
            if (holder is null || (holder.Machine == Environment.MachineName && holder.ProcessId == Environment.ProcessId)) File.Delete(lockPath);
        }
        catch (IOException) { }
        catch (JsonException) { }
    }

    /// <summary>A lock is stale when its process no longer runs on this machine (remote locks are trusted).</summary>
    private static bool IsStale(LockInfo l)
    {
        if (!string.Equals(l.Machine, Environment.MachineName, StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            using var p = Process.GetProcessById(l.ProcessId);
            return p.HasExited;
        }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
    }
}

/// <summary>Rolling timestamped backups in a "&lt;name&gt;.backups" folder next to the project.</summary>
public sealed class FileBackupService(int keep = 10) : IBackupService
{
    public static string Folder(string projectPath) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, Path.GetFileName(projectPath) + ".backups");

    public BackupEntry? CreateBackup(string projectPath)
    {
        if (!File.Exists(projectPath)) return null;
        var dir = Folder(projectPath);
        Directory.CreateDirectory(dir);
        var name = $"{Path.GetFileNameWithoutExtension(projectPath)}_{DateTime.Now:yyyyMMdd_HHmmss_fff}{Path.GetExtension(projectPath)}";
        var target = Path.Combine(dir, name);
        File.Copy(projectPath, target, overwrite: false);
        foreach (var old in List(projectPath).Skip(keep)) File.Delete(old.Path);
        var fi = new FileInfo(target);
        return new BackupEntry(target, fi.CreationTime, fi.Length);
    }

    public IReadOnlyList<BackupEntry> List(string projectPath)
    {
        var dir = Folder(projectPath);
        if (!Directory.Exists(dir)) return Array.Empty<BackupEntry>();
        return new DirectoryInfo(dir).GetFiles("*" + Path.GetExtension(projectPath))
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Select(f => new BackupEntry(f.FullName, f.LastWriteTime, f.Length)).ToList();
    }

    public void Restore(BackupEntry backup, string projectPath)
    {
        CreateBackup(projectPath);
        File.Copy(backup.Path, projectPath, overwrite: true);
    }
}

/// <summary>Autosave target: recovery copies in the application data folder with a small manifest.</summary>
public sealed class FileRecoveryStore(AppPaths paths, SqliteProjectStore store) : IRecoveryStore
{
    private sealed record Manifest(Guid ProjectId, string? OriginalPath, DateTimeOffset SavedAt, string ProjectName);

    public async Task WriteAsync(ElectricalProject project, string? originalPath, CancellationToken ct = default)
    {
        Directory.CreateDirectory(paths.Recovery);
        var file = Path.Combine(paths.Recovery, $"{project.Id:N}.dndx");
        await store.SaveAsync(project, file, ct);
        var manifest = new Manifest(project.Id, originalPath, DateTimeOffset.Now, $"{project.Info.Number} {project.Info.Name}");
        await File.WriteAllTextAsync(Path.Combine(paths.Recovery, $"{project.Id:N}.json"), JsonSerializer.Serialize(manifest), ct);
    }

    public IReadOnlyList<RecoveryEntry> FindRecoverable()
    {
        if (!Directory.Exists(paths.Recovery)) return Array.Empty<RecoveryEntry>();
        var result = new List<RecoveryEntry>();
        foreach (var json in Directory.EnumerateFiles(paths.Recovery, "*.json"))
        {
            try
            {
                var m = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(json));
                if (m is null) continue;
                var data = Path.ChangeExtension(json, ".dndx");
                if (!File.Exists(data)) continue;
                // Only offer recovery when the autosave is newer than the saved project file.
                if (m.OriginalPath is not null && File.Exists(m.OriginalPath) && File.GetLastWriteTime(m.OriginalPath) >= m.SavedAt.LocalDateTime) continue;
                result.Add(new RecoveryEntry(m.ProjectId, m.OriginalPath ?? string.Empty, data, m.SavedAt, m.ProjectName));
            }
            catch (JsonException) { }
        }
        return result.OrderByDescending(r => r.SavedAt).ToList();
    }

    public void Discard(Guid projectId)
    {
        foreach (var ext in new[] { ".dndx", ".json" })
        {
            var f = Path.Combine(paths.Recovery, $"{projectId:N}{ext}");
            try { if (File.Exists(f)) File.Delete(f); } catch (IOException) { }
        }
    }
}

public sealed class JsonUserSettingsStore(AppPaths paths) : IUserSettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public UserSettings Load()
    {
        try
        {
            if (File.Exists(paths.Settings))
                return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(paths.Settings), Json) ?? new UserSettings();
        }
        catch (JsonException) { /* corrupt settings: fall back to defaults, keep the file for diagnosis */ }
        return new UserSettings();
    }

    public void Save(UserSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Settings)!);
        var tmp = paths.Settings + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Json));
        File.Move(tmp, paths.Settings, overwrite: true);
    }
}

/// <summary>
/// Secret storage encrypted with Windows DPAPI (current user scope). On other platforms secrets are not
/// stored (IsSupported = false) rather than being written in clear text.
/// </summary>
public sealed class DpapiSecretStore(AppPaths paths) : ISecretStore
{
    public bool IsSupported => OperatingSystem.IsWindows();

    public void Set(string name, string? value)
    {
        if (!IsSupported) throw new PlatformNotSupportedException("Secure secret storage is only available on Windows.");
        var all = ReadAll();
        if (value is null) all.Remove(name); else all[name] = value;
        var plain = JsonSerializer.SerializeToUtf8Bytes(all);
        var protectedBytes = Protect(plain);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Secrets)!);
        File.WriteAllBytes(paths.Secrets, protectedBytes);
    }

    public string? Get(string name) => IsSupported ? ReadAll().GetValueOrDefault(name) : null;

    private Dictionary<string, string> ReadAll()
    {
        if (!File.Exists(paths.Secrets)) return new();
        try
        {
            var plain = Unprotect(File.ReadAllBytes(paths.Secrets));
            return JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? new();
        }
        catch (CryptographicException) { return new(); }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[] ProtectWin(byte[] data) => ProtectedData.Protect(data, Encoding.UTF8.GetBytes("DND.Electrical"), DataProtectionScope.CurrentUser);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[] UnprotectWin(byte[] data) => ProtectedData.Unprotect(data, Encoding.UTF8.GetBytes("DND.Electrical"), DataProtectionScope.CurrentUser);

    private static byte[] Protect(byte[] data) => OperatingSystem.IsWindows() ? ProtectWin(data) : throw new PlatformNotSupportedException();
    private static byte[] Unprotect(byte[] data) => OperatingSystem.IsWindows() ? UnprotectWin(data) : throw new PlatformNotSupportedException();
}
