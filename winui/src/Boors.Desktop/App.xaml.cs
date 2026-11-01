using Boors.Desktop.Infrastructure;
using Boors.Desktop.ViewModels;
using Boors.Market.Services;
using Boors.Market.Services.Jobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace Boors.Desktop;

/// <summary>
/// Composition root. Startup overlaps its three slow parts: the UI thread creates the window while a pool thread
/// builds the host (DI, logging) and immediately starts the first page's queries (which opens the DuckDB store); the
/// shell is attached when both are ready, and the job worker and scheduler start in the background afterwards. The
/// scheduler downloads market data in the background (Settings → background download, Local mode) and follows the
/// setting without a restart.
/// </summary>
public partial class App : Application
{
    private MainWindow? window;

    public App()
    {
        StartupClock.Mark("app");
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    public static IHost Host { get; private set; } = default!;

    public static T Get<T>() where T : notnull => Host.Services.GetRequiredService<T>();

    public static MainWindow? MainWindow => (Current as App)?.window;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var startup = new StartupOptions([.. Environment.GetCommandLineArgs().Skip(1)]);
        WaitForPreviousInstance(startup);
        var settings = new SettingsService(startup);
        ApplyCulture(settings.Current);

        // DI and the overview's store reads run on a pool thread while this thread creates the window.
        var hostTask = Task.Run(() =>
        {
            var host = BuildHost(startup, settings);
            StartupClock.Mark("host");
            if (startup.StartPage is null or Navigator.Overview)
                host.Services.GetRequiredService<OverviewViewModel>().Prefetch();
            return host;
        });

        window = new MainWindow(settings, startup);
        StartupClock.Mark("window-created");
        Host = hostTask.GetAwaiter().GetResult();
        settings.LogDeferred(Get<ILogger<SettingsService>>());
        settings.Changed += (_, change) =>
        {
            if (change.HasFlag(SettingsChange.Collection))
                Get<MarketJobScheduler>().Enabled = settings.Current.DownloadsInBackground;
        };
        window.Initialize();
        window.Activate();
        StartupClock.Mark("window");

        // Background services (job worker, scheduler) start off the UI thread; the scheduler then catches up on the
        // jobs missed while the app was closed.
        _ = Task.Run(() => Host.StartAsync());
    }

    /// <summary>After "Restart now": wait (briefly) for the old process to release the store file.</summary>
    private static void WaitForPreviousInstance(StartupOptions startup)
    {
        if (!startup.Overrides.TryGetValue("wait-pid", out var pid) || !int.TryParse(pid, CultureInfo.InvariantCulture, out var id))
            return;
        try
        {
            using var previous = Process.GetProcessById(id);
            previous.WaitForExit(TimeSpan.FromSeconds(15));
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }

    public static void ApplyCulture(AppSettings settings)
    {
        Loc.SetLanguage(settings.Language);
        Fmt.Configure(settings.IsPersian, settings.Digits == "Persian");
    }

    private static IHost BuildHost(StartupOptions startup, SettingsService settings)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            ContentRootPath = AppContext.BaseDirectory,
            ApplicationName = "Boors.Desktop",
        });

        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        builder.Logging.AddFilter("Polly", LogLevel.Warning);
        builder.Logging.AddDebug();
        builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(SettingsService.AppFolder, "logs")));

        var services = builder.Services;
        services.AddSingleton(startup);
        services.AddSingleton(settings);
        services.AddSingleton<UiDispatcher>();
        services.AddSingleton<Navigator>();
        services.AddHttpClient("remote-node");

        // The router must be registered before AddBoorsLocalApi (which only TryAdds IMarketApi).
        services.AddSingleton<MarketApiRouter>();
        services.AddSingleton<IMarketApi>(sp => sp.GetRequiredService<MarketApiRouter>());
        services.AddBoorsLocalApi();
        services.AddOptions<Boors.Market.MarketDataOptions>().Configure<SettingsService>((options, settings) =>
        {
            options.DataRoot = settings.EffectiveDataRoot;
            options.Node = settings.EffectiveNode;
            options.Collect = settings.Current.DownloadsInBackground;
        });
        services.AddOptions<MarketApiOptions>().Configure<SettingsService>((options, settings) => options.Offline = settings.Current.Offline);

        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<OverviewViewModel>();
        services.AddSingleton<MarketWatchViewModel>();
        services.AddSingleton<SymbolViewModel>();
        services.AddSingleton<ScreensViewModel>();
        services.AddSingleton<ReportsViewModel>();
        services.AddSingleton<HealthViewModel>();
        services.AddSingleton<JobsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        return builder.Build();
    }

    /// <summary>Stops the background services and closes the store (DuckDB checkpoints on dispose).</summary>
    public static async Task ShutdownAsync()
    {
        var logger = Get<ILogger<App>>();
        if (Loc.Missing.Count > 0)
            logger.LogInformation("Untranslated strings: {Missing}", string.Join(" | ", Loc.Missing));
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Host.StopAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
        }

        Host.Dispose();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Keep the workstation alive on an unexpected UI error; it is logged with its stack.
        Get<ILogger<App>>().LogError(e.Exception, "Unhandled UI exception: {Message}", e.Message);
        e.Handled = true;
    }
}
