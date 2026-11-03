using Boors.Desktop.Controls;
using Boors.Desktop.Infrastructure;
using Boors.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Boors.Desktop.Views;

/// <summary>
/// The Simulations page: every simulation with its latest replay, headline tiles and a comparison of their equity
/// curves. A row opens the simulation; the tracker replays everything when new market data arrives.
/// </summary>
public sealed partial class SimulationsPage : Page, IReloadable
{
    private readonly Navigator navigator = App.Get<Navigator>();

    public SimulationsPage()
    {
        ViewModel = App.Get<SimulationsViewModel>();
        InitializeComponent();
        var lite = App.Get<SettingsService>().Current.Density == "Lite";
        Table.RowHeight = lite ? 38 : 30;
        Table.CellFontSize = lite ? 14 : 13;
        Table.EmptyText = Loc.T("No simulations yet.");
        Table.Columns =
        [
            Col.Of<SavedSimulation>(Loc.T("Simulation"), 200, s => s.Spec.Name, sort: s => s.Spec.Name, export: s => s.Spec.Name, exportHeader: "name"),
            Col.Of<SavedSimulation>(Loc.T("Status"), 190, _ => string.Empty, sort: SimulationsViewModel.Status, tag: SimulationsViewModel.StatusTag, export: SimulationsViewModel.Status, exportHeader: "status"),
            Col.Of<SavedSimulation>(Loc.T("Mode"), 90, s => SimulationText.Mode(s.Spec.Mode), sort: s => s.Spec.Mode, export: s => s.Spec.Mode, exportHeader: "mode"),
            Col.Of<SavedSimulation>(Loc.T("Strategy"), 190, s => s.Spec.Mode == SimulationText.Manual ? Fmt.Dash : SimulationText.Preset(s.Spec.Preset), sort: s => s.Spec.Preset, export: s => s.Spec.Preset, exportHeader: "preset", fill: true),
            Col.Of<SavedSimulation>(Loc.T("Started"), 100, s => Fmt.Date(s.Spec.Start), sort: s => s.Spec.Start, export: s => Col.Inv(s.Spec.Start), exportHeader: "start"),
            Col.Of<SavedSimulation>(Loc.T("Value (million toman)"), 140, s => s.Summary is { } m ? Fmt.MillionToman(m.Equity) : Fmt.Dash, sort: s => s.Summary?.Equity, numeric: true, export: s => Col.Inv(s.Summary?.Equity), exportHeader: "equity_rial"),
            Fraction(Loc.T("Return"), s => s.Summary?.TotalReturn, "total_return"),
            Fraction(Loc.T("Last session"), s => s.Summary?.DayReturn, "day_return"),
            Fraction(Loc.T("Against TEDPIX"), s => s.Summary?.ExcessReturn, "excess_return", 120),
            Fraction(Loc.T("In dollars"), s => s.Summary?.UsdReturn, "usd_return"),
            Fraction(Loc.T("Per year"), s => s.Summary?.AnnualizedReturn, "annualized_return"),
            Fraction(Loc.T("Deepest fall"), s => s.Summary?.MaxDrawdown, "max_drawdown"),
            Col.Of<SavedSimulation>(Loc.T("Sharpe"), 70, s => s.Summary?.Sharpe is { } v ? Fmt.Num(v, "N2") : Fmt.Dash, sort: s => s.Summary?.Sharpe, numeric: true, export: s => Col.Inv(s.Summary?.Sharpe), exportHeader: "sharpe"),
            Col.Of<SavedSimulation>(Loc.T("Positions"), 80, s => s.Summary is { } m ? Fmt.Number(m.Holdings) : Fmt.Dash, sort: s => s.Summary?.Holdings, numeric: true, export: s => Col.Inv(s.Summary?.Holdings), exportHeader: "positions"),
            Col.Of<SavedSimulation>(Loc.T("Trades"), 100, s => s.Summary is { } m ? s.NewTrades > 0 ? $"{Fmt.Number(m.Trades)} (+{Fmt.Number(s.NewTrades)})" : Fmt.Number(m.Trades) : Fmt.Dash,
                sort: s => s.Summary?.Trades, numeric: true, tone: s => s.NewTrades > 0 ? Tone.Accent : Tone.Neutral, export: s => Col.Inv(s.Summary?.Trades), exportHeader: "trades"),
            Col.Of<SavedSimulation>(Loc.T("Data up to"), 100, s => Fmt.Date(s.Summary?.AsOf), sort: s => s.Summary?.AsOf, export: s => Col.Inv(s.Summary?.AsOf), exportHeader: "as_of"),
        ];
        Table.RowInvoked += (_, row) => navigator.Navigate(Navigator.Simulation, ((SavedSimulation)row).Spec.Id);

        ViewModel.ChartChanged += OnChartChanged;
        ViewModel.Tracker.Changed += OnTrackerChanged;
        Unloaded += (_, _) =>
        {
            ViewModel.ChartChanged -= OnChartChanged;
            ViewModel.Tracker.Changed -= OnTrackerChanged;
        };
        OnChartChanged(null, EventArgs.Empty);
    }

    public SimulationsViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    public void Reload() => _ = ViewModel.LoadAsync(force: true);

    private static TableColumn Fraction(string header, Func<SavedSimulation, double?> value, string exportHeader, double width = 110) =>
        Col.Of<SavedSimulation>(header, width, s => Fmt.FractionChange(value(s)), sort: s => value(s), tone: s => Fmt.ToneOf(value(s) * 100),
            numeric: true, export: s => Col.Inv(value(s)), exportHeader: exportHeader);

    private void OnTrackerChanged(object? sender, EventArgs e) => ViewModel.Build();

    private void OnChartChanged(object? sender, EventArgs e) =>
        CompareChart.Show(ViewModel.ChartDates, ViewModel.ChartSeries, v => Fmt.Tick(v, "N0"), baselineValue: 100);

    private void OnCompareClick(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: CompareItem item } box)
            return;
        if (!ViewModel.Toggle(item, box.IsChecked == true))
        {
            box.IsChecked = false;
            ToolTipService.SetToolTip(box, Loc.T("At most six simulations at a time: clear one first."));
        }
    }

    private void OnNew(object sender, RoutedEventArgs e) => navigator.Navigate(Navigator.SimulationEditor);

    private void OnReload(object sender, RoutedEventArgs e) => Reload();

    // Quick starts: a ready definition, opened at once.
    private async void OnQuickAdaptive(object sender, RoutedEventArgs e) => await QuickStartAsync("adaptive", manual: false);

    private async void OnQuickManual(object sender, RoutedEventArgs e) => await QuickStartAsync("balanced", manual: true);

    private async Task QuickStartAsync(string preset, bool manual)
    {
        var editor = App.Get<SimulationEditorViewModel>();
        await editor.PrepareNewAsync(preset);
        if (manual)
        {
            editor.Mode = SimulationText.Manual;
            editor.Name = Loc.T("My portfolio");
        }
        else
        {
            editor.Name = SimulationText.Preset(preset);
            editor.StartDate = editor.StartDate?.AddYears(-1);
        }

        if (editor.Save() is { } id)
            navigator.Navigate(Navigator.Simulation, id);
    }
}
