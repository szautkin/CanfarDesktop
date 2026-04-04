using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using CanfarDesktop.Helpers;
using CanfarDesktop.Services;
using CanfarDesktop.Services.HttpClients;
using CanfarDesktop.Services.Notebook;
using CanfarDesktop.ViewModels;
using CanfarDesktop.ViewModels.Notebook;
using CanfarDesktop.Views;
using CanfarDesktop.Views.Notebook;

namespace CanfarDesktop;

public partial class App : Application
{
    private Window? _window;

    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        Services = ConfigureServices();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        System.Diagnostics.Debug.WriteLine($"Unhandled exception: {e.Exception}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        NotificationService.Initialize();
        _window = new MainWindow();
        _window.Activate();

        // Handle file activation (double-click on .ipynb in File Explorer)
        var activationArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
        if (activationArgs?.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.File
            && activationArgs.Data is Windows.ApplicationModel.Activation.IFileActivatedEventArgs fileArgs
            && fileArgs.Files.Count > 0)
        {
            var filePath = fileArgs.Files[0].Path;
            if (filePath.EndsWith(".ipynb", StringComparison.OrdinalIgnoreCase) && _window is MainWindow mw)
                mw.OpenNotebook(filePath);
        }
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Helpers
        services.AddSingleton<ApiEndpoints>();
        services.AddSingleton<TokenStorage>();

        // Shared auth token (singleton — all HttpClients read from this)
        services.AddSingleton<AuthTokenProvider>();
        services.AddTransient<AuthTokenHandler>();

        // HttpClients — all use AuthTokenHandler to inject Bearer token
        services.AddHttpClient<IAuthService, AuthService>()
            .AddHttpMessageHandler<AuthTokenHandler>();
        services.AddHttpClient<ISessionService, SessionService>()
            .AddHttpMessageHandler<AuthTokenHandler>();
        services.AddHttpClient<IImageService, ImageService>()
            .AddHttpMessageHandler<AuthTokenHandler>();
        services.AddHttpClient<IPlatformService, PlatformService>()
            .AddHttpMessageHandler<AuthTokenHandler>();
        services.AddHttpClient<IStorageService, StorageService>()
            .AddHttpMessageHandler<AuthTokenHandler>();

        // Settings
        services.AddSingleton<ISettingsService, SettingsService>();

        // Recent launches
        services.AddSingleton<IRecentLaunchService, RecentLaunchService>();

        // Research
        services.AddSingleton<ObservationStore>();

        // Search (TAP is public, no auth needed)
        services.AddHttpClient<ITAPService, TAPService>(client =>
            client.Timeout = TimeSpan.FromMinutes(5));
        services.AddSingleton<ISearchStoreService, SearchStoreService>();
        services.AddHttpClient<DataLinkService>();

        // Notebook services
        services.AddTransient<IDirtyTracker, DirtyTracker>();
        services.AddTransient<IAutoSaveService, AutoSaveService>();
        services.AddSingleton<IRecoveryService, RecoveryService>();
        services.AddSingleton<IPythonDiscoveryService, PythonDiscoveryService>();
        services.AddSingleton<RecentNotebooksService>();
        services.AddSingleton<INotebookTabFactory, NotebookTabFactory>();
        services.AddTransient<IKernelService, LocalKernelService>();

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<SessionListViewModel>();
        services.AddTransient<SessionLaunchViewModel>();
        services.AddTransient<PlatformLoadViewModel>();
        services.AddTransient<StorageViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<ResearchViewModel>();
        services.AddTransient<StorageBrowserViewModel>();
        services.AddTransient<NotebookViewModel>();
        services.AddTransient<NotebookTabHostViewModel>();

        // Pages
        services.AddTransient<DashboardPage>();
        services.AddTransient<SearchPage>();
        services.AddTransient<ResearchPage>();
        services.AddTransient<StorageBrowserPage>();
        // NotebookPage is created manually by NotebookTabHost (not DI-resolved)

        return services.BuildServiceProvider();
    }
}
