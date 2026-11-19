using DND.Electrical.Application.Abstractions;
using DND.Electrical.Application.Demo;
using DND.Electrical.Application.Exports;
using DND.Electrical.Application.Projects;
using DND.Electrical.Infrastructure.Persistence;
using DND.Electrical.Infrastructure.Storage;
using DND.Electrical.Schematic.Services;
using DND.Electrical.Symbols;

namespace DND.Electrical.IntegrationTests;

/// <summary>Wires the real services together on a temporary folder.</summary>
public sealed class TestEnvironment : IDisposable
{
    public static readonly SymbolLibraryService Library = new();

    public TestEnvironment()
    {
        Folder = Directory.CreateTempSubdirectory("dnd-it-").FullName;
        Paths = new AppPaths(Path.Combine(Folder, "appdata"));
        Paths.EnsureCreated();
        var resolver = new SymbolResolver(Library);
        Schematic = new SchematicService(resolver, new SchematicBinder(resolver));
        Store = new SqliteProjectStore();
        Locks = new FileProjectLockService();
        Backups = new FileBackupService(3);
        Recovery = new FileRecoveryStore(Paths, Store);
        Projects = new ProjectService(Store, Locks, Backups, Recovery, Schematic);
        Demos = new DemoProjectFactory(Schematic);
        Docs = new DocumentationService(Schematic);
    }

    public string Folder { get; }
    public AppPaths Paths { get; }
    public SchematicService Schematic { get; }
    public SqliteProjectStore Store { get; }
    public FileProjectLockService Locks { get; }
    public FileBackupService Backups { get; }
    public FileRecoveryStore Recovery { get; }
    public ProjectService Projects { get; }
    public DemoProjectFactory Demos { get; }
    public DocumentationService Docs { get; }

    public string PathFor(string name) => Path.Combine(Folder, name);

    public void Dispose()
    {
        try { Directory.Delete(Folder, recursive: true); } catch (IOException) { }
    }
}
