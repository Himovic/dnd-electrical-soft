using DND.Electrical.Application.Abstractions;
using DND.Electrical.Application.Demo;
using DND.Electrical.Application.Exports;
using DND.Electrical.Application.Projects;
using DND.Electrical.Application.Services;
using DND.Electrical.Documentation;
using DND.Electrical.Infrastructure.Persistence;
using DND.Electrical.Infrastructure.Storage;
using DND.Electrical.Plugins;
using DND.Electrical.Presentation.Services;
using DND.Electrical.Presentation.ViewModels;
using DND.Electrical.Schematic.Services;
using DND.Electrical.Symbols;

namespace DND.Electrical.UIAutomationTests;

/// <summary>Scripted dialog service: answers are queued by the test, every interaction is recorded.</summary>
public sealed class FakeDialogs : IDialogService
{
    public Queue<string?> Prompts { get; } = new();
    public Queue<string?> SavePaths { get; } = new();
    public Queue<string?> OpenPaths { get; } = new();
    public bool ConfirmAnswer { get; set; } = true;
    public SaveChangesAnswer SaveChanges { get; set; } = SaveChangesAnswer.Discard;
    public Func<object, bool?> OnDialog { get; set; } = _ => true;
    public List<string> Log { get; } = new();
    public List<object> Shown { get; } = new();

    public void ShowInfo(string title, string message) => Log.Add($"info:{title}:{message}");
    public void ShowError(string title, string message) => Log.Add($"error:{title}:{message}");
    public bool Confirm(string title, string message) { Log.Add($"confirm:{title}"); return ConfirmAnswer; }
    public SaveChangesAnswer AskSaveChanges(string projectName) { Log.Add("ask-save"); return SaveChanges; }
    public string? OpenFile(string title, string filter) => OpenPaths.Count > 0 ? OpenPaths.Dequeue() : null;
    public string? SaveFile(string title, string filter, string defaultName) => SavePaths.Count > 0 ? SavePaths.Dequeue() : null;
    public string? PromptText(string title, string label, string defaultValue = "") => Prompts.Count > 0 ? Prompts.Dequeue() : defaultValue;
    public bool? ShowDialog(object viewModel) { Shown.Add(viewModel); return OnDialog(viewModel); }
    public void Show(object viewModel) => Shown.Add(viewModel);
}

/// <summary>Composes the real application services exactly like the desktop host (without WPF).</summary>
public sealed class UiHarness : IDisposable
{
    public UiHarness()
    {
        Folder = Directory.CreateTempSubdirectory("dnd-ui-").FullName;
        var paths = new AppPaths(Path.Combine(Folder, "appdata"));
        paths.EnsureCreated();
        var library = new SymbolLibraryService(paths.Symbols);
        var resolver = new SymbolResolver(library);
        var schematic = new SchematicService(resolver, new SchematicBinder(resolver));
        var store = new SqliteProjectStore();
        var projects = new ProjectService(store, new FileProjectLockService(), new FileBackupService(), new FileRecoveryStore(paths, store), schematic);
        var settingsStore = new JsonUserSettingsStore(paths);
        var settings = settingsStore.Load();
        var help = new HelpLibrary(Path.Combine(RepoRoot(), "documentation", "UserManual"), paths.Documentation);
        Dialogs = new FakeDialogs();
        Services = new AppServices(projects, store, schematic, library, new DemoProjectFactory(schematic), new DocumentationService(schematic),
            settingsStore, settings, help, new TutorialEngine(), new PluginManager(paths.Plugins, () => projects.Current?.Project),
            paths, new PermissionService(UserRole.Administrator), new LocalAssistantProvider(library, help, p => schematic.Binder.BindWithConnectivity(p)),
            new AppEventBus(), Dialogs);
        Main = new MainViewModel(Services);
    }

    public string Folder { get; }
    public FakeDialogs Dialogs { get; }
    public AppServices Services { get; }
    public MainViewModel Main { get; }
    public string PathFor(string name) => Path.Combine(Folder, name);

    public static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DND-Electrical-Engineering-Suite.sln"))) d = d.Parent;
        return d!.FullName;
    }

    public void Dispose()
    {
        Services.Projects.Close();
        try { Directory.Delete(Folder, true); } catch (IOException) { }
    }
}
