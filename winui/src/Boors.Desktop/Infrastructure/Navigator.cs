using Boors.Desktop.Views;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Boors.Desktop.Infrastructure;

/// <summary>Page keys and navigation. The shell owns the frame; everything else navigates through this service.</summary>
public sealed class Navigator
{
    public const string Overview = "overview";
    public const string Watch = "watch";
    public const string Screens = "screens";
    public const string Reports = "reports";
    public const string Health = "health";
    public const string Jobs = "jobs";
    public const string Settings = "settings";
    public const string Symbol = "symbol";

    public Frame? Frame { get; set; }

    public string CurrentKey { get; private set; } = Overview;

    public object? CurrentParameter { get; private set; }

    /// <summary>Raised after a navigation; the shell syncs its menu selection.</summary>
    public event EventHandler<string>? Navigated;

    public bool CanGoBack => Frame?.CanGoBack ?? false;

    public void Navigate(string key, object? parameter = null)
    {
        if (Frame is null)
            return;
        if (key == CurrentKey && Equals(parameter, CurrentParameter) && Frame.Content is not null)
            return;

        CurrentKey = key;
        CurrentParameter = parameter;
        Frame.Navigate(PageType(key), parameter, new SuppressNavigationTransitionInfo());
        Navigated?.Invoke(this, key);
    }

    public void OpenSymbol(long insCode) => Navigate(Symbol, insCode);

    public void GoBack()
    {
        if (Frame is not { CanGoBack: true })
            return;
        Frame.GoBack(new SuppressNavigationTransitionInfo());
        CurrentKey = KeyOf(Frame.Content?.GetType());
        CurrentParameter = Frame.Content is SymbolPage symbol ? symbol.InsCode : null;
        Navigated?.Invoke(this, CurrentKey);
    }

    /// <summary>After the shell is rebuilt (language change): show the same page again, without history.</summary>
    public void Restore(Frame frame)
    {
        Frame = frame;
        var key = CurrentKey;
        var parameter = CurrentParameter;
        CurrentKey = string.Empty;
        Navigate(key, parameter);
    }

    public static Type PageType(string key) => key switch
    {
        Watch => typeof(MarketWatchPage),
        Screens => typeof(ScreensPage),
        Reports => typeof(ReportsPage),
        Health => typeof(HealthPage),
        Jobs => typeof(JobsPage),
        Settings => typeof(SettingsPage),
        Symbol => typeof(SymbolPage),
        _ => typeof(OverviewPage),
    };

    public static string KeyOf(Type? page) => page?.Name switch
    {
        nameof(MarketWatchPage) => Watch,
        nameof(ScreensPage) => Screens,
        nameof(ReportsPage) => Reports,
        nameof(HealthPage) => Health,
        nameof(JobsPage) => Jobs,
        nameof(SettingsPage) => Settings,
        nameof(SymbolPage) => Symbol,
        _ => Overview,
    };
}
