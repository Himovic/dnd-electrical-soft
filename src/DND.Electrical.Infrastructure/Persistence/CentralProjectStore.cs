using DND.Electrical.Domain;
using Microsoft.EntityFrameworkCore;

namespace DND.Electrical.Infrastructure.Persistence;

public enum CentralProvider { Sqlite, PostgreSql }

/// <summary>
/// Foundation for centralised deployments: many projects in one database (PostgreSQL or shared SQLite).
/// Saving replaces a project's rows inside a transaction. Authentication/authorisation is enforced by the
/// application layer (see <see cref="Application.Services.PermissionService"/>).
/// </summary>
public sealed class CentralProjectStore(CentralProvider provider, string connectionString)
{
    public DbContextOptions<ProjectDbContext> Options => provider switch
    {
        CentralProvider.PostgreSql => new DbContextOptionsBuilder<ProjectDbContext>().UseNpgsql(connectionString).Options,
        _ => new DbContextOptionsBuilder<ProjectDbContext>().UseSqlite(connectionString).Options,
    };

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var ctx = new ProjectDbContext(Options);
        // Migrations in this repository target SQLite; PostgreSQL schemas are created from the model.
        if (provider == CentralProvider.Sqlite) await ctx.Database.MigrateAsync(ct);
        else await ctx.Database.EnsureCreatedAsync(ct);
    }

    public async Task SaveAsync(ElectricalProject project, CancellationToken ct = default)
    {
        await using var ctx = new ProjectDbContext(Options);
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);
        await ctx.Connections.Where(c => c.ProjectId == project.Id).ExecuteDeleteAsync(ct);
        await ctx.Projects.Where(p => p.Id == project.Id).ExecuteDeleteAsync(ct);
        project.Touch();
        ProjectGraphWriter.Add(ctx, project);
        await ctx.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<ElectricalProject> LoadAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var ctx = new ProjectDbContext(Options);
        return await ProjectGraphWriter.LoadAsync(ctx, ct, projectId);
    }

    public async Task<IReadOnlyList<(Guid id, ProjectInfo info)>> ListAsync(CancellationToken ct = default)
    {
        await using var ctx = new ProjectDbContext(Options);
        var rows = await ctx.Projects.AsNoTracking().Select(p => new { p.Id, p.Info }).ToListAsync(ct);
        return rows.Select(r => (r.Id, r.Info)).ToList();
    }

    public async Task DeleteAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var ctx = new ProjectDbContext(Options);
        await ctx.Connections.Where(c => c.ProjectId == projectId).ExecuteDeleteAsync(ct);
        await ctx.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync(ct);
    }
}
