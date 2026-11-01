using Boors.Desktop.Infrastructure;
using Boors.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Boors.Desktop.Views;

/// <summary>Title bar (search, palette, language and theme), navigation menu, the page frame and the command palette.</summary>
public sealed partial class ShellPage : Page
{
    private readonly MainWindow window;
    private readonly Navigator navigator;
    private readonly SettingsService settings;

    public ShellPage(MainWindow window)
    {
        this.window = window;
        navigator = App.Get<Navigator>();
        settings = App.Get<SettingsService>();
        ViewModel = App.Get<ShellViewModel>();
        InitializeComponent();
        window.SetTitleBar(AppTitleBar);
        LanguageButtonText.Text = settings.Current.IsPersian ? "EN" : "فا";
        navigator.Navigated += OnNavigated;
        ViewModel.SettingsRequested += OnSettingsRequested;
        ViewModel.DataChanged += OnDataChanged;
        Unloaded += (_, _) =>
        {
            navigator.Navigated -= OnNavigated;
            ViewModel.SettingsRequested -= OnSettingsRequested;
            ViewModel.DataChanged -= OnDataChanged;
        };
        ViewModel.UpdateJobIndicator();
    }

    public ShellViewModel ViewModel { get; }

    /// <summary>Shows the start page (from --page) or, after a rebuild, the page that was open.</summary>
    public void Start(string? startPage)
    {
        if (startPage is not null)
        {
            navigator.Frame = ContentFrame;
            if (startPage.StartsWith("symbol:", StringComparison.OrdinalIgnoreCase) && long.TryParse(startPage[7..], CultureInfo.InvariantCulture, out var insCode))
            {
                navigator.OpenSymbol(insCode);
            }
            else if (startPage.StartsWith("reports:", StringComparison.OrdinalIgnoreCase))
            {
                App.Get<ReportsViewModel>().Preselect(startPage[8..]);
                navigator.Navigate(Navigator.Reports);
            }
            else
            {
                navigator.Navigate(startPage);
            }
        }
        else
        {
            navigator.Restore(ContentFrame);
        }

        SyncSelection(navigator.CurrentKey);
    }

    public void ReloadCurrent()
    {
        if (ContentFrame.Content is IReloadable page)
            page.Reload();
    }

    // Data health reloads itself when a job finishes; every other page shows the new data now.
    private void OnDataChanged(object? sender, EventArgs e)
    {
        if (navigator.CurrentKey != Navigator.Health)
            ReloadCurrent();
    }

    private void OnNavigated(object? sender, string key)
    {
        SyncSelection(key);
        AppTitleBar.IsBackButtonEnabled = navigator.CanGoBack;
    }

    private void SyncSelection(string key)
    {
        var item = Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == key);
        Nav.SelectedItem = item; // null on the symbol workspace (not a menu page)
    }

    private void OnNavItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is string key)
            navigator.Navigate(key);
    }

    private void OnBackRequested(TitleBar sender, object args) => navigator.GoBack();

    private void OnPaneToggleRequested(TitleBar sender, object args) => Nav.IsPaneOpen = !Nav.IsPaneOpen;

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            _ = ViewModel.SuggestAsync(sender.Text);
    }

    private async void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        await ViewModel.OpenBestAsync(args.QueryText, args.ChosenSuggestion as PaletteEntry);
        sender.Text = string.Empty;
    }

    private void OnSearchAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private void OnReloadAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ReloadCurrent();
        args.Handled = true;
    }

    private void OnLanguageButton(object sender, RoutedEventArgs e) => settings.Update(s => s.Language = s.Language == "fa" ? "en" : "fa");

    private void OnThemeButton(object sender, RoutedEventArgs e) =>
        settings.Update(s => s.Theme = ActualTheme == ElementTheme.Light ? "Dark" : "Light");

    private void OnSettingsRequested(object? sender, Action<AppSettings> change) => DispatcherQueue.TryEnqueue(() => settings.Update(change));

    // ---------------------------------------------------------------- command palette

    private void OnPaletteAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (PaletteOverlay.Visibility == Visibility.Visible)
            ClosePalette();
        else
            OpenPalette();
        args.Handled = true;
    }

    private void OnPaletteButton(object sender, RoutedEventArgs e) => OpenPalette();

    public void OpenPalette()
    {
        PaletteOverlay.Visibility = Visibility.Visible;
        PaletteBox.Text = string.Empty;
        _ = ViewModel.UpdatePaletteAsync(string.Empty);
        PaletteBox.Focus(FocusState.Programmatic);
    }

    private void ClosePalette()
    {
        PaletteOverlay.Visibility = Visibility.Collapsed;
        ContentFrame.Focus(FocusState.Programmatic);
    }

    private void OnPaletteTextChanged(object sender, TextChangedEventArgs e) => _ = ViewModel.UpdatePaletteAsync(PaletteBox.Text);

    private async void OnPaletteKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var count = PaletteList.Items.Count;
        switch (e.Key)
        {
            case VirtualKey.Escape:
                ClosePalette();
                e.Handled = true;
                break;
            case VirtualKey.Down when count > 0:
                PaletteList.SelectedIndex = (PaletteList.SelectedIndex + 1) % count;
                PaletteList.ScrollIntoView(PaletteList.SelectedItem);
                e.Handled = true;
                break;
            case VirtualKey.Up when count > 0:
                PaletteList.SelectedIndex = PaletteList.SelectedIndex <= 0 ? count - 1 : PaletteList.SelectedIndex - 1;
                PaletteList.ScrollIntoView(PaletteList.SelectedItem);
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                e.Handled = true;
                var entry = PaletteList.SelectedItem as PaletteEntry ?? (count > 0 ? PaletteList.Items[0] as PaletteEntry : null);
                if (entry is not null)
                    await RunEntryAsync(entry);
                break;
        }
    }

    private async void OnPaletteItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PaletteEntry entry)
            await RunEntryAsync(entry);
    }

    private async Task RunEntryAsync(PaletteEntry entry)
    {
        ClosePalette();
        await entry.Run();
    }

    private void OnOverlayTapped(object sender, TappedRoutedEventArgs e) => ClosePalette();

    private void OnPaletteTapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;
}

/// <summary>A page that can reload its data (F5, data-source change).</summary>
public interface IReloadable
{
    void Reload();
}
