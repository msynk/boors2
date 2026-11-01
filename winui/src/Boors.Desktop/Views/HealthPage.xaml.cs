using Boors.Desktop.Controls;
using Boors.Desktop.Infrastructure;
using Boors.Desktop.ViewModels;
using Boors.Market.Services.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;

namespace Boors.Desktop.Views;

public sealed partial class HealthPage : Page, IReloadable
{
    private readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromSeconds(3) };

    public HealthPage()
    {
        ViewModel = App.Get<HealthViewModel>();
        InitializeComponent();
        if (Loc.IsPersian)
            SincePicker.CalendarIdentifier = Windows.Globalization.CalendarIdentifiers.Persian;

        DatasetsTable.Columns =
        [
            Col.Of<DatasetHealthDto>(Loc.T("Dataset"), 170, d => d.Dataset, sort: d => d.Dataset, fill: true),
            Col.Of<DatasetHealthDto>(Loc.T("Rows"), 110, d => Fmt.Number(d.Rows), sort: d => d.Rows, numeric: true),
            Col.Of<DatasetHealthDto>(Loc.T("Latest"), 110, d => Fmt.Date(d.LatestDate), sort: d => d.LatestDate),
            Col.Of<DatasetHealthDto>(Loc.T("Expected"), 110, d => Fmt.Date(d.ExpectedDate), sort: d => d.ExpectedDate),
            Col.Of<DatasetHealthDto>(Loc.T("Status"), 120, _ => string.Empty, sort: d => d.Status, tag: d => Status(d.Status)),
        ];
        RunsTable.Columns =
        [
            Col.Of<CollectionRunDto>(Loc.T("Job"), 150, r => HealthViewModel.JobName(r.Job), sort: r => r.Job),
            Col.Of<CollectionRunDto>(Loc.T("Started"), 140, r => Fmt.DateTime(r.StartedAt), sort: r => r.StartedAt),
            Col.Of<CollectionRunDto>(Loc.T("Duration"), 90, r => r.FinishedAt is { } f ? Fmt.Duration(f - r.StartedAt) : Loc.T("running"), numeric: true),
            Col.Of<CollectionRunDto>(Loc.T("Status"), 120, _ => string.Empty, sort: r => r.Status, tag: r => Status(r.Status)),
            Col.Of<CollectionRunDto>(Loc.T("Items"), 90, r => Fmt.Number(r.Items), sort: r => r.Items, numeric: true),
            Col.Of<CollectionRunDto>(Loc.T("Failures"), 72, r => Fmt.Number(r.Failures), sort: r => r.Failures, numeric: true),
            Col.Of<CollectionRunDto>(Loc.T("Message"), 460, r => r.Message ?? string.Empty, fill: true),
        ];
        SourcesTable.Columns =
        [
            Col.Of<SourceReachabilityDto>(Loc.T("Source"), 360, s => s.Name, sort: s => s.Name),
            Col.Of<SourceReachabilityDto>(Loc.T("Verdict"), 130, _ => string.Empty, sort: s => s.Verdict, tag: s => Status(s.Verdict)),
            Col.Of<SourceReachabilityDto>("HTTP", 70, s => s.Status is { } code ? Fmt.Number(code) : Fmt.Dash, sort: s => s.Status, numeric: true),
            Col.Of<SourceReachabilityDto>(Loc.T("Error"), 420, s => s.Error ?? string.Empty, fill: true),
        ];
        SourcesTable.EmptyText = Loc.T("Not probed yet (run boors probe on this node).");

        ViewModel.PropertyChanged += OnViewModelChanged;
        poll.Tick += async (_, _) => await ViewModel.RefreshJobsAsync();
        Unloaded += (_, _) =>
        {
            ViewModel.PropertyChanged -= OnViewModelChanged;
            poll.Stop();
        };
        Show();
    }

    public HealthViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (!ViewModel.IsLocal)
            poll.Start(); // remote: the node's queue is polled while this page is open
        await ViewModel.LoadAsync();
        App.Get<StartupOptions>().ApplyScroll(Scroller);
    }

    public void Reload() => _ = ViewModel.LoadAsync();

    private static (string Text, Tone Tone)? Status(string? status)
    {
        if (status is null)
            return null;
        var kind = HealthViewModel.StatusOf(status);
        var icon = kind switch { StatusKind.Good => "✓", StatusKind.Warning => "⚠", StatusKind.Critical => "✕", _ => "ℹ" };
        var tone = kind switch { StatusKind.Good => Tone.Good, StatusKind.Warning => Tone.Warning, StatusKind.Critical => Tone.Critical, _ => Tone.Info };
        return ($"{icon} {HealthViewModel.StateName(status)}", tone);
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Show();

    private void Show()
    {
        var health = ViewModel.Health;
        DatasetsTable.Items = health?.Datasets;
        RunsTable.Items = health?.Runs;
        SourcesTable.Items = health?.Sources;
        ProbeText.Text = health?.LastProbeAt is { } probe ? $"{Loc.T("Last probe")}: {Fmt.DateTime(probe)}" : Loc.T("Not probed yet");

        if (health is not null)
        {
            var tone = health.Healthy ? Tone.Good : Tone.Warning;
            var c = Palette.ColorOf(tone);
            OverallBadge.Background = new SolidColorBrush(Color.FromArgb(0x30, c.R, c.G, c.B));
            OverallText.Foreground = Palette.Brush(tone);
            OverallText.Text = health.Healthy ? $"✓ {Loc.T("Healthy")}" : $"⚠ {Loc.T("Needs attention")}";
        }
    }

    private void OnOpenJobs(object sender, RoutedEventArgs e) => App.Get<Navigator>().Navigate(Navigator.Jobs);

    private async void OnPull(object sender, RoutedEventArgs e)
    {
        ViewModel.NodeUrl = NodeUrlBox.Text;
        ViewModel.SyncToken = TokenBox.Password;
        ViewModel.SyncSince = SincePicker.Date;
        await ViewModel.PullAsync();
    }

    private void OnReload(object sender, RoutedEventArgs e) => Reload();
}
