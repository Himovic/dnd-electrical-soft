using System.Windows;
using System.Windows.Threading;
using DND.Electrical.App.Services;
using DND.Electrical.App.Views;
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
using DND.Electrical.Shared;
using DND.Electrical.Symbols;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DND.Electrical.App;

/// <summary>Application entry: generic host, dependency injection, logging, theming, splash and crash handling.</summary>
public partial class App : System.Windows.Application
{
    private IHost? _host;
    private DispatcherTimer? _autosave;

    public static IServiceProvider Services => ((App)Current)._host!.Services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = new AppPaths();
        paths.EnsureCreated();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(Path.Combine(paths.Logs, "dnd-electrical-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateLogger();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Fatal(args.ExceptionObject as Exception, "Unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, args) => { Log.Error(args.Exception, "Unobserved task exception"); args.SetObserved(); };

        var splash = new SplashWindow();
        splash.Show();
        try
        {
            _host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices(services => Configure(services, paths))
                .Build();
            await _host.StartAsync();
            var settings = Services.GetRequiredService<UserSettings>();
            ApplyTheme(settings.Theme == "Dark", settings.AccentColor);

            var plugins = Services.GetRequiredService<PluginManager>();
            plugins.TrustedHashes.UnionWith(settings.TrustedPluginHashes);
            foreach (var p in plugins.DiscoverAndLoad()) Log.Information("Plugin {Folder}: {State} {Message}", p.Folder, p.State, p.Message);

            var main = Services.GetRequiredService<MainWindow>();
            MainWindow = main;
            main.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            var vm = Services.GetRequiredService<MainViewModel>();
            _autosave = new DispatcherTimer { Interval = TimeSpan.FromMinutes(Math.Clamp(settings.AutosaveMinutes, 1, 120)) };
            _autosave.Tick += async (_, _) => await vm.AutosaveAsync();
            _autosave.Start();
            Log.Information("{Product} started", ProductInfo.FullTitle);
            vm.CheckLicenseOnStartup();
            if (e.Args.Length > 0 && File.Exists(e.Args[0])) await vm.OpenPathAsync(e.Args[0]);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Startup failed");
            MessageBox.Show($"The application could not start:\n\n{ex.Message}\n\nSee the log in {paths.Logs}.", ProductInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
        finally
        {
            splash.Close();
        }
    }

    private static void Configure(IServiceCollection s, AppPaths paths)
    {
        s.AddSingleton(paths);
        s.AddSingleton<IUserSettingsStore, JsonUserSettingsStore>();
        s.AddSingleton(sp => sp.GetRequiredService<IUserSettingsStore>().Load());
        s.AddSingleton(sp => new SymbolLibraryService(sp.GetRequiredService<UserSettings>().SymbolLibraryFolder ?? paths.Symbols));
        s.AddSingleton<SymbolResolver>();
        s.AddSingleton<SchematicBinder>();
        s.AddSingleton<SchematicService>();
        s.AddSingleton<SqliteProjectStore>();
        s.AddSingleton<IProjectStore>(sp => sp.GetRequiredService<SqliteProjectStore>());
        s.AddSingleton<IProjectLockService, FileProjectLockService>();
        s.AddSingleton<IBackupService>(sp => new FileBackupService(sp.GetRequiredService<UserSettings>().BackupsToKeep));
        s.AddSingleton<IRecoveryStore, FileRecoveryStore>();
        s.AddSingleton<ISecretStore, DpapiSecretStore>();
        s.AddSingleton<ProjectService>();
        s.AddSingleton<DemoProjectFactory>();
        s.AddSingleton<DocumentationService>();
        s.AddSingleton(_ => new HelpLibrary(Path.Combine(AppContext.BaseDirectory, "Documentation"), paths.Documentation));
        s.AddSingleton<TutorialEngine>();
        s.AddSingleton(sp => new PluginManager(paths.Plugins, () => sp.GetRequiredService<ProjectService>().Current?.Project, m => Log.Information(m)));
        s.AddSingleton(sp => new PermissionService(Enum.TryParse<UserRole>(sp.GetRequiredService<UserSettings>().Role, out var r) ? r : UserRole.Engineer));
        s.AddSingleton<IAssistantProvider>(sp => new LocalAssistantProvider(sp.GetRequiredService<SymbolLibraryService>(), sp.GetRequiredService<HelpLibrary>(),
            p => sp.GetRequiredService<SchematicBinder>().BindWithConnectivity(p)));
        s.AddSingleton(_ => new DND.Electrical.Licensing.LicenseManager(new DND.Electrical.Licensing.LicenseStore(paths.Licensing), DND.Electrical.Licensing.TrustedKeys.Product()));
        s.AddSingleton<DND.Electrical.Licensing.IEntitlements>(sp => sp.GetRequiredService<DND.Electrical.Licensing.LicenseManager>());
        s.AddSingleton<AppEventBus>();
        s.AddSingleton<IDialogService, WpfDialogService>();
        s.AddSingleton<AppServices>();
        s.AddSingleton<MainViewModel>();
        s.AddSingleton<MainWindow>();
    }

    public static void ApplyTheme(bool dark, string? accent = null)
    {
        var dicts = Current.Resources.MergedDictionaries;
        var theme = new ResourceDictionary { Source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative) };
        dicts[0] = theme;
        if (accent is not null && System.Windows.Media.ColorConverter.ConvertFromString(accent) is System.Windows.Media.Color c)
            Current.Resources["Brush.Accent"] = new System.Windows.Media.SolidColorBrush(c);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception");
        MessageBox.Show($"An unexpected error occurred:\n\n{e.Exception.Message}\n\nYour project is protected by autosave. Details were written to the log.",
            ProductInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _autosave?.Stop();
        try
        {
            if (_host is not null)
            {
                Services.GetRequiredService<PluginManager>().UnloadAll();
                Services.GetRequiredService<ProjectService>().Close(discardRecovery: false);
                await _host.StopAsync(TimeSpan.FromSeconds(3));
                _host.Dispose();
            }
        }
        finally
        {
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
