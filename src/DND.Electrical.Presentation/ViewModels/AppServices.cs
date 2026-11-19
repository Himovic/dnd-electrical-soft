using DND.Electrical.Application.Abstractions;
using DND.Electrical.Application.Demo;
using DND.Electrical.Application.Exports;
using DND.Electrical.Application.Projects;
using DND.Electrical.Application.Services;
using DND.Electrical.Documentation;
using DND.Electrical.Plugins;
using DND.Electrical.Presentation.Services;
using DND.Electrical.Schematic.Services;
using DND.Electrical.Symbols;

namespace DND.Electrical.Presentation.ViewModels;

/// <summary>Services shared by the view models (composed by the host's dependency injection container).</summary>
public sealed record AppServices(
    ProjectService Projects,
    IProjectStore Store,
    SchematicService Schematic,
    SymbolLibraryService Symbols,
    DemoProjectFactory Demos,
    DocumentationService Documentation,
    IUserSettingsStore SettingsStore,
    UserSettings Settings,
    HelpLibrary Help,
    TutorialEngine Tutorials,
    PluginManager Plugins,
    AppPaths Paths,
    PermissionService Permissions,
    IAssistantProvider Assistant,
    AppEventBus Events,
    IDialogService Dialogs);
