using DND.Electrical.Application.Abstractions;
using DND.Electrical.Domain;
using DND.Electrical.Schematic.Connectivity;
using DND.Electrical.Wiring;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DND.Electrical.Infrastructure.Persistence;

/// <summary>
/// Stores each project in its own SQLite database file. Saving is atomic: the project is written to a
/// temporary database which then replaces the original file, so an interrupted save never corrupts it.
/// Opening never modifies the user's file: older formats are migrated on a temporary copy.
/// </summary>
public sealed class SqliteProjectStore(ILogger<SqliteProjectStore>? logger = null) : IProjectStore
{
    private readonly ILogger _log = logger ?? NullLogger<SqliteProjectStore>.Instance;

    public static DbContextOptions<ProjectDbContext> Options(string path, bool readOnly = false) =>
        new DbContextOptionsBuilder<ProjectDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString())
            .Options;

    public async Task SaveAsync(ElectricalProject project, string path, CancellationToken ct = default)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + $".saving-{Guid.NewGuid():N}";
        try
        {
            project.Touch();
            await using (var ctx = new ProjectDbContext(Options(temp)))
            {
                await ctx.Database.MigrateAsync(ct);
                ProjectGraphWriter.Add(ctx, project);
                await ctx.SaveChangesAsync(ct);
            }
            File.Move(temp, full, overwrite: true);
            _log.LogInformation("Project {Number} saved to {Path}", project.Info.Number, full);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    public async Task<ElectricalProject> LoadAsync(string path, CancellationToken ct = default)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new FileNotFoundException("Project file not found.", full);
        ProjectFileGuard.EnsureSqlite(full);

        string source = full;
        string? temp = null;
        try
        {
            await using (var probe = new ProjectDbContext(Options(full, readOnly: true)))
            {
                var pending = (await probe.Database.GetPendingMigrationsAsync(ct)).ToList();
                if (pending.Count > 0)
                {
                    _log.LogInformation("Project {Path} uses an older format; migrating a temporary copy ({Count} migrations).", full, pending.Count);
                    temp = Path.Combine(Path.GetTempPath(), $"dnd-migrate-{Guid.NewGuid():N}.dndx");
                    File.Copy(full, temp);
                    await using var m = new ProjectDbContext(Options(temp));
                    await m.Database.MigrateAsync(ct);
                    source = temp;
                }
            }
            await using var ctx = new ProjectDbContext(Options(source, readOnly: temp is null));
            return await ProjectGraphWriter.LoadAsync(ctx, ct);
        }
        finally
        {
            if (temp is not null) TryDelete(temp);
        }
    }

    public async Task<ProjectInfo?> ReadInfoAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path)) return null;
        try
        {
            await using var ctx = new ProjectDbContext(Options(path, readOnly: true));
            return await ctx.Projects.AsNoTracking().Select(p => p.Info).FirstOrDefaultAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not read project info from {Path}", path);
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }
}

/// <summary>Shared logic to write and read the full project graph with any provider.</summary>
public static class ProjectGraphWriter
{
    public static void Add(ProjectDbContext ctx, ElectricalProject project)
    {
        ctx.Add(project);
        Order(ctx, project.Revisions); Order(ctx, project.Sheets); Order(ctx, project.Devices); Order(ctx, project.Cables);
        foreach (var c in project.Cables) Order(ctx, c.Cores);
        Order(ctx, project.TerminalStrips);
        foreach (var t in project.TerminalStrips) Order(ctx, t.Terminals);
        Order(ctx, project.PlcStations);
        foreach (var s in project.PlcStations)
        {
            Order(ctx, s.Racks);
            foreach (var r in s.Racks)
            {
                Order(ctx, r.Modules);
                foreach (var m in r.Modules) Order(ctx, m.Channels);
            }
        }
        Order(ctx, project.Motors); Order(ctx, project.Cabinets);
        foreach (var c in project.Cabinets) Order(ctx, c.Items);
        Order(ctx, project.Catalog); Order(ctx, project.Procurement); Order(ctx, project.Loads); Order(ctx, project.Calculations);
        Order(ctx, project.Attachments); Order(ctx, project.AuditEvents); Order(ctx, project.ValidationRuns); Order(ctx, project.Symbols);
        Order(ctx, project.Members); Order(ctx, project.Tasks); Order(ctx, project.Milestones); Order(ctx, project.Issues); Order(ctx, project.Approvals);
        Order(ctx, project.Comments); Order(ctx, project.DocumentRegister); Order(ctx, project.Maintenance); Order(ctx, project.Commissioning); Order(ctx, project.Layouts3D); Order(ctx, project.PowerNetworks); Order(ctx, project.Instruments); Order(ctx, project.Loops); Order(ctx, project.Alarms); Order(ctx, project.CauseEffects); Order(ctx, project.ScadaNodes); Order(ctx, project.TwinPoints); Order(ctx, project.AiInteractions);

        // Derived, queryable connection table.
        var graph = ConnectivityGraph.Build(project);
        foreach (var r in WiringReports.ConnectionList(project, graph))
            ctx.Connections.Add(new ConnectionRecord
            {
                ProjectId = project.Id, WireId = r.WireId, SheetId = r.SheetId, WireNumber = r.WireNumber,
                FromDesignation = r.From, ToDesignation = r.To, Potential = r.Potential, Cable = r.Cable,
            });
    }

    private static void Order<T>(ProjectDbContext ctx, List<T> list) where T : class
    {
        for (int i = 0; i < list.Count; i++) ctx.Entry(list[i]).Property(ProjectDbContext.SortOrder).CurrentValue = i;
    }

    public static async Task<ElectricalProject> LoadAsync(ProjectDbContext ctx, CancellationToken ct, Guid? projectId = null)
    {
        var query = ctx.Projects.AsSplitQuery()
            .Include(x => x.Revisions).Include(x => x.Sheets).Include(x => x.Devices)
            .Include(x => x.Cables).ThenInclude(c => c.Cores)
            .Include(x => x.TerminalStrips).ThenInclude(t => t.Terminals)
            .Include(x => x.PlcStations).ThenInclude(s => s.Racks).ThenInclude(r => r.Modules).ThenInclude(m => m.Channels)
            .Include(x => x.Motors).Include(x => x.Cabinets).ThenInclude(c => c.Items)
            .Include(x => x.Catalog).Include(x => x.Procurement).Include(x => x.Loads).Include(x => x.Calculations)
            .Include(x => x.Attachments).Include(x => x.AuditEvents).Include(x => x.ValidationRuns)
            .Include(x => x.Members).Include(x => x.Tasks).Include(x => x.Milestones).Include(x => x.Issues).Include(x => x.Approvals)
            .Include(x => x.Comments).Include(x => x.DocumentRegister).Include(x => x.Maintenance).Include(x => x.Commissioning).Include(x => x.Layouts3D).Include(x => x.PowerNetworks).Include(x => x.Instruments).Include(x => x.Loops).Include(x => x.Alarms).Include(x => x.CauseEffects).Include(x => x.ScadaNodes).Include(x => x.TwinPoints).Include(x => x.AiInteractions);
        var project = projectId is { } id
            ? await query.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw new InvalidDataException($"Project {id} not found.")
            : await query.SingleOrDefaultAsync(ct) ?? throw new InvalidDataException("The file does not contain a project.");

        Sort(ctx, project.Revisions); Sort(ctx, project.Sheets); Sort(ctx, project.Devices); Sort(ctx, project.Cables);
        foreach (var c in project.Cables) Sort(ctx, c.Cores);
        Sort(ctx, project.TerminalStrips);
        foreach (var t in project.TerminalStrips) Sort(ctx, t.Terminals);
        Sort(ctx, project.PlcStations);
        foreach (var s in project.PlcStations)
        {
            Sort(ctx, s.Racks);
            foreach (var r in s.Racks)
            {
                Sort(ctx, r.Modules);
                foreach (var m in r.Modules) Sort(ctx, m.Channels);
            }
        }
        Sort(ctx, project.Motors); Sort(ctx, project.Cabinets);
        foreach (var c in project.Cabinets) Sort(ctx, c.Items);
        Sort(ctx, project.Catalog); Sort(ctx, project.Procurement); Sort(ctx, project.Loads); Sort(ctx, project.Calculations);
        Sort(ctx, project.Attachments); Sort(ctx, project.AuditEvents); Sort(ctx, project.ValidationRuns); Sort(ctx, project.Symbols);
        Sort(ctx, project.Members); Sort(ctx, project.Tasks); Sort(ctx, project.Milestones); Sort(ctx, project.Issues); Sort(ctx, project.Approvals);
        Sort(ctx, project.Comments); Sort(ctx, project.DocumentRegister); Sort(ctx, project.Maintenance); Sort(ctx, project.Commissioning); Sort(ctx, project.Layouts3D); Sort(ctx, project.PowerNetworks); Sort(ctx, project.Instruments); Sort(ctx, project.Loops); Sort(ctx, project.Alarms); Sort(ctx, project.CauseEffects); Sort(ctx, project.ScadaNodes); Sort(ctx, project.TwinPoints); Sort(ctx, project.AiInteractions);
        ctx.ChangeTracker.Clear();
        return project;
    }

    private static void Sort<T>(ProjectDbContext ctx, List<T> list) where T : class
    {
        var keyed = list.Select(x => (x, k: (int)(ctx.Entry(x).Property(ProjectDbContext.SortOrder).CurrentValue ?? 0))).OrderBy(t => t.k).Select(t => t.x).ToList();
        list.Clear();
        list.AddRange(keyed);
    }
}

public static class ProjectFileGuard
{
    private static readonly byte[] SqliteHeader = "SQLite format 3\0"u8.ToArray();

    /// <summary>Rejects files that are not SQLite databases before handing them to the database engine.</summary>
    public static void EnsureSqlite(string path)
    {
        using var fs = File.OpenRead(path);
        var buf = new byte[16];
        if (fs.Read(buf, 0, 16) != 16 || !buf.SequenceEqual(SqliteHeader))
            throw new InvalidDataException($"'{Path.GetFileName(path)}' is not a D&D project file.");
    }
}
