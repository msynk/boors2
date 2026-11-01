using Boors.Desktop.Infrastructure;
using Boors.Desktop.ViewModels;
using Boors.Desktop.Views;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Boors.Desktop;

/// <summary>
/// The window: content extends into the title bar (the shell draws its own, with the symbol search), Mica where the
/// OS supports it (Windows 11) and a solid theme background otherwise (Windows 10), saved size and placement.
/// Appearance changes rebuild the shell in place so every label, direction and colour is evaluated again.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly SettingsService settings;
    private readonly StartupOptions startup;
    private ShellPage? shell;

    public MainWindow(SettingsService settings, StartupOptions startup)
    {
        this.settings = settings;
        this.startup = startup;
        Title = "Boors";
        ExtendsContentIntoTitleBar = true;
        if (MicaController.IsSupported())
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };

        RestorePlacement();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
    }

    /// <summary>Attaches the shell once the host is built (the constructor runs while it is still being built).</summary>
    public void Initialize()
    {
        App.Get<UiDispatcher>().Queue = DispatcherQueue;
        BuildShell(startup.StartPage);
        settings.Changed += OnSettingsChanged;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnFirstFrame;
        Closed += OnClosed;
    }

    public bool UsesMica => SystemBackdrop is not null;

    private static void OnFirstFrame(object? sender, object e)
    {
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnFirstFrame;
        StartupClock.Mark("first-frame");
    }

    private void BuildShell(string? startPage)
    {
        var current = settings.Current;
        App.ApplyCulture(current);
        var theme = current.Theme switch { "Light" => ElementTheme.Light, "System" => ElementTheme.Default, _ => ElementTheme.Dark };

        shell = new ShellPage(this) { RequestedTheme = theme, FlowDirection = Loc.Flow };
        ConfigurePalette(shell);
        // "Use system setting": a Windows theme switch rebuilds the shell so code-built brushes follow it.
        shell.ActualThemeChanged += (s, _) =>
        {
            if (settings.Current.Theme == "System")
                DispatcherQueue.TryEnqueue(() => BuildShell(null));
        };
        if (!UsesMica)
            shell.Background = Palette.WindowBackground;

        Content = shell;
        ApplyTitleBarTheme(theme == ElementTheme.Default ? shell.ActualTheme : theme);
        shell.Start(startPage);
    }

    private void ConfigurePalette(FrameworkElement root)
    {
        var dark = root.RequestedTheme switch
        {
            ElementTheme.Dark => true,
            ElementTheme.Light => false,
            _ => new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Background).R < 128,
        };
        Palette.Configure(dark, settings.Current.ColorPreset == "ColorBlind");
    }

    private void ApplyTitleBarTheme(ElementTheme theme)
    {
        AppWindow.TitleBar.PreferredTheme = theme switch
        {
            ElementTheme.Light => TitleBarTheme.Light,
            ElementTheme.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
    }

    private void OnSettingsChanged(object? sender, SettingsChange change)
    {
        if (change.HasFlag(SettingsChange.Language) || change.HasFlag(SettingsChange.Appearance) || change.HasFlag(SettingsChange.Density))
        {
            // Rebuild the shell and show the same page: labels, direction, digits, colours and row heights all follow.
            foreach (var vm in new PageViewModel[] { App.Get<OverviewViewModel>(), App.Get<MarketWatchViewModel>(), App.Get<SymbolViewModel>(), App.Get<ScreensViewModel>(), App.Get<ReportsViewModel>(), App.Get<HealthViewModel>() })
            {
                App.ApplyCulture(settings.Current);
                vm.Refresh();
            }

            BuildShell(null);
        }
        else if (change.HasFlag(SettingsChange.DataSource))
        {
            shell?.ReloadCurrent();
        }
    }

    private void RestorePlacement()
    {
        var s = settings.Current;
        var scale = 1.0;
        try
        {
            var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var work = display.WorkArea;
            var width = (int)Math.Min(Math.Max(startup.Size?.Width ?? s.WindowWidth, 900), work.Width);
            var height = (int)Math.Min(Math.Max(startup.Size?.Height ?? s.WindowHeight, 600), work.Height);
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));
            if (s.WindowMaximized && startup.Size is null && AppWindow.Presenter is OverlappedPresenter presenter)
                presenter.Maximize();
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Window placement not restored ({scale}): {e.Message}");
        }
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        var maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        if (!maximized)
            settings.SaveWindow(AppWindow.Size.Width, AppWindow.Size.Height, false);
        else
            settings.SaveWindow(settings.Current.WindowWidth, settings.Current.WindowHeight, true);
        await App.ShutdownAsync();
    }
}
