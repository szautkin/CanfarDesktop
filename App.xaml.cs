using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using CanfarDesktop.Helpers;
using CanfarDesktop.Services;
using CanfarDesktop.Services.HttpClients;
using CanfarDesktop.ViewModels;
using CanfarDesktop.Views;

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
        services.AddHttpClient<ITAPService, TAPService>();
        services.AddSingleton<ISearchStoreService, SearchStoreService>();
        services.AddHttpClient<DataLinkService>();

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<SessionListViewModel>();
        services.AddTransient<SessionLaunchViewModel>();
        services.AddTransient<PlatformLoadViewModel>();
        services.AddTransient<StorageViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<ResearchViewModel>();

        // Pages
        services.AddTransient<DashboardPage>();
        services.AddTransient<SearchPage>();
        services.AddTransient<ResearchPage>();

        return services.BuildServiceProvider();
    }
}
