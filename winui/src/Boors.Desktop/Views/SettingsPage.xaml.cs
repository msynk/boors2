using Boors.Desktop.Infrastructure;
using Boors.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace Boors.Desktop.Views;

public sealed partial class SettingsPage : Page
{
    private bool loading = true;

    public SettingsPage()
    {
        ViewModel = App.Get<SettingsViewModel>();
        InitializeComponent();
        var s = ViewModel.Current;
        Fill(LanguageBox, SettingsViewModel.Languages, s.Language);
        Fill(ThemeBox, SettingsViewModel.Themes, s.Theme);
        Fill(DigitsBox, SettingsViewModel.DigitChoices, s.Digits);
        Fill(PresetBox, SettingsViewModel.Presets, s.ColorPreset);
        Fill(DensityBox, SettingsViewModel.Densities, s.Density);
        Fill(ModeBox, SettingsViewModel.Modes, s.Mode);
        DigitsBox.IsEnabled = s.IsPersian;
        NodeUrlBox.Text = s.NodeUrl;
        TokenBox.Password = s.OperatorToken ?? string.Empty;
        OfflineSwitch.IsOn = s.Offline;
        AutoDownloadSwitch.IsOn = s.AutoDownload;
        NodeNameBox.Text = s.NodeName ?? string.Empty;
        NodeNameBox.PlaceholderText = ViewModel.EffectiveNode;
        DataRootBox.Text = s.DataRoot ?? string.Empty;
        DataRootBox.PlaceholderText = SettingsService.DefaultDataRoot;
        RemotePanel.Visibility = Ui.Show(s.Mode == "Remote");
        UpSample.Text = $"{Fmt.Change(2.5)}";
        UpSample.Foreground = Palette.UpBrush;
        DownSample.Text = $"{Fmt.Change(-2.5)}";
        DownSample.Foreground = Palette.DownBrush;
        DataRootNote.Text = ViewModel.DataRootOverridden
            ? Loc.F("In use: {0} (set by BOORS_DATA or --data for this session)", ViewModel.EffectiveDataRoot)
            : Loc.F("In use: {0}", ViewModel.EffectiveDataRoot);
        loading = false;
        Loaded += (_, _) => ShowAbout();
        Loaded += (_, _) => App.Get<StartupOptions>().ApplyScroll(Scroller);
    }

    public SettingsViewModel ViewModel { get; }

    private void ShowAbout()
    {
        static string Mark(string name) => StartupClock.Get(name) is { } ms ? Fmt.Num(ms, "N0") : Fmt.Dash;
        AboutText.Text = string.Join(Environment.NewLine,
            $"Boors Desktop {typeof(App).Assembly.GetName().Version} · WinUI 3 · Windows App SDK 2.5 · .NET {Environment.Version}",
            Loc.F("Settings: {0}", SettingsService.SettingsPath),
            Loc.F("Logs: {0}", Path.Combine(SettingsService.AppFolder, "logs")),
            Loc.F("Startup: first frame {0} ms, first page with data {1} ms", Mark("first-frame"), Mark("overview-frame")),
            Loc.T("Font: Vazirmatn (SIL Open Font License 1.1), bundled."));
    }

    private static void Fill(ComboBox box, IReadOnlyList<Choice> choices, string selected)
    {
        box.ItemsSource = choices;
        box.SelectedItem = choices.FirstOrDefault(c => c.Key == selected);
    }

    private static string? Key(object sender) => (sender as ComboBox)?.SelectedItem is Choice c ? c.Key : null;

    private void Set(Action<AppSettings> change)
    {
        if (!loading)
            ViewModel.Set(change);
    }

    private void OnLanguage(object sender, SelectionChangedEventArgs e)
    {
        if (Key(sender) is { } k)
            Set(s => s.Language = k);
    }

    private void OnTheme(object sender, SelectionChangedEventArgs e)
    {
        if (Key(sender) is { } k)
            Set(s => s.Theme = k);
    }

    private void OnDigits(object sender, SelectionChangedEventArgs e)
    {
        if (Key(sender) is { } k)
            Set(s => s.Digits = k);
    }

    private void OnPreset(object sender, SelectionChangedEventArgs e)
    {
        if (Key(sender) is { } k)
            Set(s => s.ColorPreset = k);
    }

    private void OnDensity(object sender, SelectionChangedEventArgs e)
    {
        if (Key(sender) is { } k)
            Set(s => s.Density = k);
    }

    private void OnMode(object sender, SelectionChangedEventArgs e)
    {
        if (Key(sender) is not { } k)
            return;
        RemotePanel.Visibility = Ui.Show(k == "Remote");
        Set(s => s.Mode = k);
    }

    private void OnNodeUrl(object sender, RoutedEventArgs e) => Set(s => s.NodeUrl = NodeUrlBox.Text.Trim());

    private void OnToken(object sender, RoutedEventArgs e) => Set(s => s.OperatorToken = string.IsNullOrWhiteSpace(TokenBox.Password) ? null : TokenBox.Password);

    private void OnOffline(object sender, RoutedEventArgs e) => Set(s => s.Offline = OfflineSwitch.IsOn);

    private void OnAutoDownload(object sender, RoutedEventArgs e)
    {
        if (!loading)
            ViewModel.SetAutoDownload(AutoDownloadSwitch.IsOn);
    }

    private void OnNodeName(object sender, RoutedEventArgs e)
    {
        if (!loading && (NodeNameBox.Text.Trim() != (ViewModel.Current.NodeName ?? string.Empty)))
            ViewModel.SetNodeName(NodeNameBox.Text);
    }

    private async void OnBrowse(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker(XamlRoot.ContentIslandEnvironment.AppWindowId) { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        if (await picker.PickSingleFolderAsync() is { } folder)
            DataRootBox.Text = folder.Path;
    }

    private void OnApplyDataRoot(object sender, RoutedEventArgs e) => ViewModel.SetDataRoot(DataRootBox.Text);

    private void OnRestart(object sender, RoutedEventArgs e)
    {
        // Start a fresh process with the same command line, then close this one (the store is released on close).
        var exe = Environment.ProcessPath;
        if (exe is not null)
        {
            var args = Environment.GetCommandLineArgs().Skip(1).Select(a => a.Contains(' ') ? $"\"{a}\"" : a).Append($"--wait-pid {Environment.ProcessId}");
            Process.Start(new ProcessStartInfo(exe, string.Join(' ', args)) { UseShellExecute = false });
        }

        App.MainWindow?.Close();
    }
}
