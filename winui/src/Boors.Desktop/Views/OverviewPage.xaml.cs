using Boors.Desktop.Charts;
using Boors.Desktop.Controls;
using Boors.Desktop.Infrastructure;
using Boors.Desktop.ViewModels;
using Boors.Market.Services.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Boors.Desktop.Views;

public sealed partial class OverviewPage : Page, IReloadable
{
    private readonly Navigator navigator = App.Get<Navigator>();
    private readonly bool lite = App.Get<SettingsService>().Current.Density == "Lite";

    public OverviewPage()
    {
        ViewModel = App.Get<OverviewViewModel>();
        InitializeComponent();

        DenominationSwitch.Items = [new("rial", Loc.T("In rial")), new("usd", Loc.T("In free-market dollars"))];
        DenominationSwitch.SelectedKey = ViewModel.Denomination;
        DenominationSwitch.SelectionChanged += (_, key) => ViewModel.SetDenomination(key);
        foreach (var kind in OverviewViewModel.MoverKinds)
            MoverTabs.Items.Add(new SelectorBarItem { Text = kind.Text, Tag = kind.Key, IsSelected = kind.Key == ViewModel.MoverKind });

        MoversTable.RowHeight = lite ? 38 : 30;
        MoversTable.CellFontSize = lite ? 14 : 13;
        MoversTable.EmptyText = Loc.T("No movers for this session.");
        MoversTable.Columns =
        [
            Col.Of<MoverDto>(Loc.T("Symbol"), 84, m => m.Symbol, sort: m => m.Symbol),
            Col.Of<MoverDto>(Loc.T("Name"), 150, m => m.Name ?? string.Empty, sort: m => m.Name, fill: true),
            Col.Of<MoverDto>(Loc.T("Closing price"), 84, m => Fmt.Price(m.Close), sort: m => m.Close, numeric: true),
            Col.Of<MoverDto>(Loc.T("Change"), 92, m => Fmt.Change(m.ChangePercent), sort: m => m.ChangePercent, tone: m => Fmt.ToneOf(m.ChangePercent), numeric: true),
            Col.Of<MoverDto>(Loc.T("Value (billion toman)"), 92, m => Fmt.BillionToman(m.Value), sort: m => m.Value, numeric: true),
            Col.Of<MoverDto>(Loc.T("Individual net (billion toman)"), 100, m => Fmt.SignedBillionToman(m.IndividualNetFlow), sort: m => m.IndividualNetFlow, tone: m => Fmt.ToneOf(m.IndividualNetFlow), numeric: true),
            Col.Of<MoverDto>(Loc.T("Status"), 112, m => Fmt.DayStatus(m.DayStatus), sort: m => m.DayStatus),
        ];
        MoversTable.RowInvoked += (_, row) => navigator.OpenSymbol(((MoverDto)row).InsCode);
        SectorBars.LabelWidth = 170;
        SectorBars.BarInvoked += (_, bar) =>
        {
            // A sector opens the market watch filtered on it with the TSETMC sector variable (cs).
            if (!int.TryParse(bar.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sector))
                return;
            var watch = App.Get<MarketWatchViewModel>();
            watch.Filter = $"(cs)=={sector}";
            watch.Group = "shares";
            _ = watch.LoadAsync();
            navigator.Navigate(Navigator.Watch);
        };

        // Lite hides the dense panels (breadth, sectors) for quick decisions.
        BreadthCard.Visibility = SectorsCard.Visibility = lite ? Visibility.Collapsed : Visibility.Visible;
        SizeChanged += (_, e) => Arrange(e.NewSize.Width);
        ViewModel.IndexChanged += OnIndexChanged;
        Unloaded += (_, _) => ViewModel.IndexChanged -= OnIndexChanged;
    }

    public OverviewViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (ViewModel.HasData)
            ShowIndex();
        else
            await ViewModel.LoadAsync();
        StartupClock.Mark("overview-data");
        App.Get<StartupOptions>().ApplyScroll(Scroller);
        App.MainWindow?.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ReportStartup);
    }

    public void Reload() => _ = ViewModel.LoadAsync();

    private void OnIndexChanged(object? sender, EventArgs e) => ShowIndex();

    private TimeSeriesChart? indexChart;

    private void ShowIndex()
    {
        var chart = indexChart ??= new TimeSeriesChart();
        if (IndexChartHost.Child is null)
            IndexChartHost.Child = chart;
        IndexTitle.Text = ViewModel.Denomination == "usd" ? Loc.T("TEDPIX in free-market dollars, 1 year (start = 100)") : Loc.T("TEDPIX, 1 year (rial)");
        chart.Show(ViewModel.IndexDates, ViewModel.IndexValues,
            ViewModel.Denomination == "usd" ? v => Fmt.Tick(v, "N1") : v => Fmt.Tick(v),
            ViewModel.Denomination == "usd" ? 100 : null);
    }

    /// <summary>Two columns on wide windows (index + movers | breadth + sectors), one column otherwise.</summary>
    private void Arrange(double width)
    {
        var wide = width >= 1180 && !lite;
        ChartsGrid.ColumnDefinitions[1].Width = wide ? new GridLength(2, GridUnitType.Star) : new GridLength(0);
        ChartsGrid.ColumnSpacing = wide ? 16 : 0;
        Place(IndexCard, 0, 0);
        Place(BreadthCard, wide ? 0 : 1, wide ? 1 : 0);
        Place(MoversCard, wide ? 1 : 2, 0);
        Place(SectorsCard, wide ? 1 : 3, wide ? 1 : 0);
    }

    private static void Place(FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
    }

    private void OnMoverTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is string kind && kind != ViewModel.MoverKind)
            _ = ViewModel.LoadMoversAsync(kind);
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Reload();

    private void OnOpenHealth(object sender, RoutedEventArgs e) => navigator.Navigate(Navigator.Health);

    private void OnOpenSettings(object sender, RoutedEventArgs e) => navigator.Navigate(Navigator.Settings);

    private void ReportStartup()
    {
        // The frame after the data is laid out: the first page is usable.
        void OnRendering(object? s, object e)
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
            StartupClock.Mark("overview-frame");
            StartupClock.Report(App.Get<Microsoft.Extensions.Logging.ILogger<OverviewPage>>());
            if (App.Get<StartupOptions>().ExitAfterMs is { } ms)
                _ = CloseAfter(ms);
        }

        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnRendering;
    }

    private static async Task CloseAfter(int ms)
    {
        await Task.Delay(ms);
        App.MainWindow?.DispatcherQueue.TryEnqueue(() => App.MainWindow?.Close());
    }
}
