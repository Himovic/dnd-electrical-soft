using DND.Electrical.Application.Abstractions;
using DND.Electrical.Application.Templates;
using DND.Electrical.Domain;
using DND.Electrical.Schematic.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DND.Electrical.Application.Projects;

/// <summary>An open project with its file location and dirty state.</summary>
public sealed class ProjectSession
{
    private int _changeCount;

    public ProjectSession(ElectricalProject project, string? filePath)
    {
        Project = project;
        FilePath = filePath;
    }

    public ElectricalProject Project { get; }
    public string? FilePath { get; internal set; }
    public bool IsDirty => _changeCount > 0;
    public bool IsReadOnly { get; internal set; }
    public DateTimeOffset? LastSaved { get; internal set; }
    public DateTimeOffset? LastAutosave { get; internal set; }

    public event EventHandler? Changed;

    public string DisplayName => $"{Project.Info.Number} – {Project.Info.Name}{(IsDirty ? " *" : string.Empty)}";

    /// <summary>Called by editors whenever the project data changed.</summary>
    public void MarkDirty()
    {
        _changeCount++;
        Project.Touch();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void MarkSaved()
    {
        _changeCount = 0;
        LastSaved = DateTimeOffset.Now;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class ProjectLockedException(LockInfo holder)
    : Exception($"The project is being edited by {holder.User} on {holder.Machine} since {holder.Since:g}.")
{
    public LockInfo Holder { get; } = holder;
}

/// <summary>Creates, opens, saves and closes projects, coordinating locking, backups, recovery and auditing.</summary>
public sealed class ProjectService(
    IProjectStore store,
    IProjectLockService locks,
    IBackupService backups,
    IRecoveryStore recovery,
    SchematicService schematic,
    ILogger<ProjectService>? logger = null)
{
    private readonly ILogger _log = logger ?? NullLogger<ProjectService>.Instance;

    public ProjectSession? Current { get; private set; }
    public SchematicService Schematic { get; } = schematic;
    public IBackupService Backups { get; } = backups;
    public IRecoveryStore Recovery { get; } = recovery;

    public event EventHandler? SessionChanged;

    public ProjectSession CreateNew(ProjectTemplate template, ProjectInfo info, string? user = null)
    {
        var project = template.Create(info, Schematic);
        project.Audit("Created", $"Project created from template '{template.Name}'.", user);
        Schematic.Binder.BindProject(project);
        return Activate(new ProjectSession(project, null));
    }

    /// <summary>Activates an in-memory project (e.g. a freshly generated demo) without a file.</summary>
    public ProjectSession OpenInMemory(ElectricalProject project, string? path = null)
    {
        Schematic.Binder.BindProject(project);
        return Activate(new ProjectSession(project, path));
    }

    public async Task<ProjectSession> OpenAsync(string path, bool forceLock = false, CancellationToken ct = default)
    {
        var full = Path.GetFullPath(path);
        var (acquired, holder) = locks.TryAcquire(full, forceLock);
        if (!acquired) throw new ProjectLockedException(holder!);
        try
        {
            var project = await store.LoadAsync(full, ct);
            Schematic.Binder.BindProject(project);
            Schematic.Resolver.UpdateCachedSymbols(project);
            project.Audit("Opened", full);
            _log.LogInformation("Opened project {Number} from {Path}", project.Info.Number, full);
            return Activate(new ProjectSession(project, full));
        }
        catch
        {
            locks.Release(full);
            throw;
        }
    }

    /// <summary>Opens a crash-recovery copy. The session keeps the original path so that Save writes back there.</summary>
    public async Task<ProjectSession> RecoverAsync(RecoveryEntry entry, CancellationToken ct = default)
    {
        var project = await store.LoadAsync(entry.RecoveryPath, ct);
        Schematic.Binder.BindProject(project);
        project.Audit("Recovered", $"Recovered from autosave of {entry.SavedAt:g}.");
        var session = Activate(new ProjectSession(project, string.IsNullOrEmpty(entry.OriginalPath) ? null : entry.OriginalPath));
        if (session.FilePath is not null) locks.TryAcquire(session.FilePath);
        session.MarkDirty();
        return session;
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        var s = Current ?? throw new InvalidOperationException("No project is open.");
        if (s.FilePath is null) throw new InvalidOperationException("The project has no file yet. Use Save As.");
        await SaveToAsync(s, s.FilePath, ct);
    }

    public async Task SaveAsAsync(string path, CancellationToken ct = default)
    {
        var s = Current ?? throw new InvalidOperationException("No project is open.");
        var full = Path.GetFullPath(path);
        if (!string.Equals(full, s.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            var (acquired, holder) = locks.TryAcquire(full);
            if (!acquired) throw new ProjectLockedException(holder!);
            if (s.FilePath is not null) locks.Release(s.FilePath);
        }
        await SaveToAsync(s, full, ct);
        s.FilePath = full;
    }

    private async Task SaveToAsync(ProjectSession s, string path, CancellationToken ct)
    {
        if (s.IsReadOnly) throw new InvalidOperationException("The project is open read-only.");
        if (File.Exists(path)) Backups.CreateBackup(path);
        s.Project.Audit("Saved", path);
        await store.SaveAsync(s.Project, path, ct);
        Recovery.Discard(s.Project.Id);
        s.MarkSaved();
    }

    public async Task AutosaveAsync(CancellationToken ct = default)
    {
        var s = Current;
        if (s is null || !s.IsDirty) return;
        await Recovery.WriteAsync(s.Project, s.FilePath, ct);
        s.LastAutosave = DateTimeOffset.Now;
    }

    /// <summary>Closes the current session. The caller must have asked the user about unsaved changes.</summary>
    public void Close(bool discardRecovery = true)
    {
        if (Current is null) return;
        if (Current.FilePath is not null) locks.Release(Current.FilePath);
        if (discardRecovery) Recovery.Discard(Current.Project.Id);
        Current = null;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Copies the project to a new file with a new identity and number.</summary>
    public async Task<string> DuplicateAsync(string sourcePath, string targetPath, string newNumber, string? newName = null, CancellationToken ct = default)
    {
        var project = await store.LoadAsync(sourcePath, ct);
        project.Id = Guid.NewGuid();
        project.Info.Number = newNumber;
        if (newName is not null) project.Info.Name = newName;
        project.Info.Created = DateTimeOffset.Now;
        project.Info.Status = ProjectStatus.Draft;
        project.Info.IsDemo = false;
        project.AuditEvents.Clear();
        project.Audit("Duplicated", $"Copied from {Path.GetFileName(sourcePath)}.");
        await store.SaveAsync(project, targetPath, ct);
        return targetPath;
    }

    public ProjectRevision CreateRevision(string code, string description, string author)
    {
        var p = Current?.Project ?? throw new InvalidOperationException("No project is open.");
        if (p.Revisions.Any(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Revision {code} already exists.");
        foreach (var r in p.Revisions.Where(r => r.Status == RevisionStatus.Issued)) r.Status = RevisionStatus.Superseded;
        var rev = new ProjectRevision { Code = code, Description = description, Author = author, Status = RevisionStatus.Issued };
        p.Revisions.Add(rev);
        p.Info.CurrentRevision = code;
        foreach (var sheet in p.Sheets) sheet.Revision = code;
        p.Audit("Revision", $"Revision {code}: {description}", author);
        Current!.MarkDirty();
        return rev;
    }

    public Attachment AddAttachment(string filePath, string description)
    {
        var p = Current?.Project ?? throw new InvalidOperationException("No project is open.");
        var fi = new FileInfo(filePath);
        if (fi.Length > 100 * 1024 * 1024) throw new InvalidOperationException("Attachments are limited to 100 MB.");
        var a = new Attachment
        {
            FileName = fi.Name, Description = description, Content = File.ReadAllBytes(filePath),
            ContentType = fi.Extension.ToLowerInvariant() switch { ".pdf" => "application/pdf", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".txt" => "text/plain", _ => "application/octet-stream" },
        };
        p.Attachments.Add(a);
        p.Audit("Attachment", $"Added {a.FileName}");
        Current!.MarkDirty();
        return a;
    }

    private ProjectSession Activate(ProjectSession session)
    {
        if (Current is not null && Current.FilePath is not null && Current.FilePath != session.FilePath) locks.Release(Current.FilePath);
        Current = session;
        SessionChanged?.Invoke(this, EventArgs.Empty);
        return session;
    }
}
