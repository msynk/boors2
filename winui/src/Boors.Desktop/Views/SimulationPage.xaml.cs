using System.ComponentModel;
using System.Text;
using Boors.Desktop.Controls;
using Boors.Desktop.Infrastructure;
using Boors.Desktop.ViewModels;
using Boors.Market.Services.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.Storage.Pickers;

namespace Boors.Desktop.Views;

/// <summary>One simulation: tiles, charts, holdings, orders (the ticket in manual mode), trades, journal, performance and strategy.</summary>
public sealed partial class SimulationPage : Page, IReloadable
{
    private readonly Navigator navigator = App.Get<Navigator>();
    private bool? columnsForManual;

    public SimulationPage()
    {
        ViewModel = App.Get<SimulationViewModel>();
        InitializeComponent();
        var lite = App.Get<SettingsService>().Current.Density == "Lite";
        foreach (var table in new[] { HoldingsTable, FirstOrdersTable, SecondOrdersTable, TradesTable, JournalTable, StatsTable, MonthsTable, FactorsTable, ParametersTable })
        {
            table.RowHeight = lite ? 38 : 30;
            table.CellFontSize = lite ? 14 : 13;
        }

        DenominationSwitch.Items = SimulationViewModel.Denominations;
        DenominationSwitch.SelectedKey = ViewModel.Denomination;
        DenominationSwitch.SelectionChanged += (_, key) => ViewModel.SetDenomination(key);
        SideSwitch.Items = SimulationViewModel.Sides;
        SideSwitch.SelectionChanged += (_, key) =>
        {
            ViewModel.TicketSide = key;
            ViewModel.TicketUnit = key == "Sell" ? "all" : "value";
        };
        UnitSwitch.SelectionChanged += (_, key) => ViewModel.TicketUnit = key;
        SyncTicket();

        HoldingsTable.EmptyText = Loc.T("No positions: the portfolio is in cash.");
        HoldingsTable.Columns =
        [
            Col.Of<SimulationHoldingDto>(Loc.T("Symbol"), 96, h => h.Symbol, sort: h => h.Symbol, export: h => h.Symbol, exportHeader: "symbol"),
            Col.Of<SimulationHoldingDto>(Loc.T("Name"), 190, h => h.Name ?? string.Empty, sort: h => h.Name, export: h => h.Name, exportHeader: "name", fill: true),
            Col.Of<SimulationHoldingDto>(Loc.T("Shares"), 110, h => Fmt.Decimal(h.Shares, 0), sort: h => h.Shares, numeric: true, export: h => Col.Inv(h.Shares), exportHeader: "shares"),
            Col.Of<SimulationHoldingDto>(Loc.T("Cost per share"), 110, h => Fmt.Price(h.AverageCost), sort: h => h.AverageCost, numeric: true, export: h => Col.Inv(h.AverageCost), exportHeader: "average_cost"),
            Col.Of<SimulationHoldingDto>(Loc.T("Last price"), 100, h => Fmt.Price(h.LastPrice), sort: h => h.LastPrice, numeric: true, export: h => Col.Inv(h.LastPrice), exportHeader: "last_price"),
            Col.Of<SimulationHoldingDto>(Loc.T("Value (million toman)"), 130, h => Fmt.MillionToman(h.Value), sort: h => h.Value, numeric: true, export: h => Col.Inv(h.Value), exportHeader: "value_rial"),
            Col.Of<SimulationHoldingDto>(Loc.T("Weight"), 80, h => Fmt.Percent(h.Weight, 1), sort: h => h.Weight, numeric: true, export: h => Col.Inv(h.Weight), exportHeader: "weight"),
            Col.Of<SimulationHoldingDto>(Loc.T("Profit (million toman)"), 130, h => Million(h.UnrealizedPnl), sort: h => h.UnrealizedPnl, tone: h => Fmt.ToneOf(h.UnrealizedPnl), numeric: true, export: h => Col.Inv(h.UnrealizedPnl), exportHeader: "unrealized_rial"),
            Col.Of<SimulationHoldingDto>(Loc.T("Return"), 100, h => Fmt.FractionChange(h.UnrealizedReturn), sort: h => h.UnrealizedReturn, tone: h => Fmt.ToneOf(h.UnrealizedReturn * 100), numeric: true, export: h => Col.Inv(h.UnrealizedReturn), exportHeader: "return"),
            Col.Of<SimulationHoldingDto>(Loc.T("Last session"), 100, h => Fmt.FractionChange(h.DayChange), sort: h => h.DayChange, tone: h => Fmt.ToneOf(h.DayChange * 100), numeric: true, export: h => Col.Inv(h.DayChange), exportHeader: "day_change"),
            Col.Of<SimulationHoldingDto>(Loc.T("Held (sessions)"), 100, h => Fmt.Number(h.SessionsHeld), sort: h => h.SessionsHeld, numeric: true, export: h => Col.Inv(h.SessionsHeld), exportHeader: "sessions_held"),
            Col.Of<SimulationHoldingDto>(Loc.T("Stop at"), 100, h => Fmt.Price(h.StopPrice), sort: h => h.StopPrice, numeric: true, export: h => Col.Inv(h.StopPrice), exportHeader: "stop_price"),
            Col.Of<SimulationHoldingDto>(Loc.T("Rank"), 70, h => h.Rank is { } r ? Fmt.Number(r) : Fmt.Dash, sort: h => h.Rank, numeric: true, export: h => Col.Inv(h.Rank), exportHeader: "rank"),
            Col.Of<SimulationHoldingDto>(Loc.T("Score"), 70, h => h.Score is { } s ? Fmt.Num(s, "N0") : Fmt.Dash, sort: h => h.Score, numeric: true, export: h => Col.Inv(h.Score), exportHeader: "score"),
            Col.Of<SimulationHoldingDto>(Loc.T("Day status"), 150, h => Fmt.DayStatus(h.DayStatus), sort: h => h.DayStatus, export: h => h.DayStatus, exportHeader: "day_status"),
            Col.Of<SimulationHoldingDto>(Loc.T("Since"), 100, h => Fmt.Date(h.OpenedOn), sort: h => h.OpenedOn, export: h => Col.Inv(h.OpenedOn), exportHeader: "opened_on"),
        ];
        HoldingsTable.RowInvoked += OnHoldingInvoked;

        TradesTable.EmptyText = Loc.T("No trades yet.");
        TradesTable.Columns =
        [
            Col.Of<SimulationTradeDto>(Loc.T("Date"), 100, t => Fmt.Date(t.Date), sort: t => t.Date, export: t => Col.Inv(t.Date), exportHeader: "date"),
            Col.Of<SimulationTradeDto>(Loc.T("Side"), 70, _ => string.Empty, sort: t => t.Side, tag: t => (SimulationText.Side(t.Side), SimulationText.SideTone(t.Side)), export: t => t.Side, exportHeader: "side"),
            Col.Of<SimulationTradeDto>(Loc.T("Symbol"), 96, t => t.Symbol, sort: t => t.Symbol, export: t => t.Symbol, exportHeader: "symbol"),
            Col.Of<SimulationTradeDto>(Loc.T("Shares"), 110, t => Fmt.Decimal(t.Shares, 0), sort: t => t.Shares, numeric: true, export: t => Col.Inv(t.Shares), exportHeader: "shares"),
            Col.Of<SimulationTradeDto>(Loc.T("Price"), 100, t => Fmt.Price(t.Price), sort: t => t.Price, numeric: true, export: t => Col.Inv(t.Price), exportHeader: "price"),
            Col.Of<SimulationTradeDto>(Loc.T("Value (million toman)"), 130, t => Fmt.MillionToman(t.Value), sort: t => t.Value, numeric: true, export: t => Col.Inv(t.Value), exportHeader: "value_rial"),
            Col.Of<SimulationTradeDto>(Loc.T("Costs (million toman)"), 120, t => Fmt.Decimal(t.Costs / Fmt.RialsPerMillionToman, 1), sort: t => t.Costs, numeric: true, export: t => Col.Inv(t.Costs), exportHeader: "costs_rial"),
            Col.Of<SimulationTradeDto>(Loc.T("Profit (million toman)"), 130, t => t.RealizedPnl is { } p ? Million(p) : Fmt.Dash, sort: t => t.RealizedPnl, tone: t => Fmt.ToneOf(t.RealizedPnl), numeric: true, export: t => Col.Inv(t.RealizedPnl), exportHeader: "realized_rial"),
            Col.Of<SimulationTradeDto>(Loc.T("Return"), 100, t => Fmt.FractionChange(t.RealizedReturn), sort: t => t.RealizedReturn, tone: t => Fmt.ToneOf(t.RealizedReturn * 100), numeric: true, export: t => Col.Inv(t.RealizedReturn), exportHeader: "return"),
            Col.Of<SimulationTradeDto>(Loc.T("Held (sessions)"), 100, t => t.SessionsHeld is { } h ? Fmt.Number(h) : Fmt.Dash, sort: t => t.SessionsHeld, numeric: true, export: t => Col.Inv(t.SessionsHeld), exportHeader: "sessions_held"),
            Col.Of<SimulationTradeDto>(Loc.T("Why"), 200, t => SimulationText.Reason(t.Reason), sort: t => t.Reason, export: t => t.Reason, exportHeader: "reason", fill: true),
        ];
        TradesTable.RowInvoked += (_, row) => navigator.OpenSymbol(((SimulationTradeDto)row).InsCode);

        JournalTable.EmptyText = Loc.T("Nothing happened yet.");
        JournalTable.Columns =
        [
            Col.Of<JournalLine>(Loc.T("Date"), 100, j => Fmt.Date(j.Date), sort: j => j.Date, export: j => Col.Inv(j.Date), exportHeader: "date"),
            Col.Of<JournalLine>(Loc.T("What happened"), 600, j => j.Text, sort: j => j.Kind, tone: j => j.Tone, export: j => j.Kind, exportHeader: "kind", fill: true),
            Col.Of<JournalLine>(Loc.T("Symbol"), 96, j => j.Symbol ?? string.Empty, sort: j => j.Symbol, export: j => j.Symbol, exportHeader: "symbol"),
        ];
        JournalTable.RowInvoked += (_, row) =>
        {
            if (row is JournalLine { InsCode: { } ins })
                navigator.OpenSymbol(ins);
        };

        StatsTable.Columns =
        [
            Col.Of<StatLine>(Loc.T("Group"), 90, s => s.Group, export: s => s.Group, exportHeader: "group"),
            Col.Of<StatLine>(Loc.T("Measure"), 290, s => s.Label, export: s => s.Key, exportHeader: "measure", fill: true),
            Col.Of<StatLine>(Loc.T("Value"), 150, s => s.Value, tone: s => s.Tone, numeric: true, export: s => s.Raw, exportHeader: "value"),
        ];
        MonthsTable.EmptyText = Loc.T("No complete month yet.");
        MonthsTable.Columns = MonthColumns();

        FactorsTable.EmptyText = Loc.T("Manual mode ranks only for its suggestions; they appear after the first replay.");
        FactorsTable.Columns =
        [
            Col.Of<SimulationFactorDto>(Loc.T("Factor"), 260, f => SimulationText.Factor(f.Factor), sort: f => SimulationText.Factor(f.Factor), export: f => f.Factor, exportHeader: "factor", fill: true),
            Col.Of<SimulationFactorDto>(Loc.T("Family"), 110, f => SimulationText.Family(f.Family), sort: f => f.Family, export: f => f.Family, exportHeader: "family"),
            Col.Of<SimulationFactorDto>(Loc.T("Chosen weight"), 110, f => Fmt.Percent(f.Chosen, 0), sort: f => f.Chosen, numeric: true, export: f => Col.Inv(f.Chosen), exportHeader: "chosen"),
            Col.Of<SimulationFactorDto>(Loc.T("Weight in use"), 110, f => Fmt.Percent(f.Used, 0), sort: f => f.Used, numeric: true, tone: f => f.Used > f.Chosen + 0.005 ? Tone.Up : f.Used < f.Chosen - 0.005 ? Tone.Down : Tone.Neutral, export: f => Col.Inv(f.Used), exportHeader: "used"),
            Col.Of<SimulationFactorDto>(Loc.T("Rank correlation"), 120, f => f.MeanIc is { } ic ? Fmt.Num(ic, "N3") : Fmt.Dash, sort: f => f.MeanIc, numeric: true, tone: f => Fmt.ToneOf(f.MeanIc * 100), export: f => Col.Inv(f.MeanIc), exportHeader: "mean_ic"),
            Col.Of<SimulationFactorDto>(Loc.T("Months learned"), 110, f => f.Months > 0 ? Fmt.Number(f.Months) : Fmt.Dash, sort: f => f.Months, numeric: true, export: f => Col.Inv(f.Months), exportHeader: "months"),
            Col.Of<SimulationFactorDto>(Loc.T("Shares with data"), 120, f => Fmt.Percent(f.Coverage, 0), sort: f => f.Coverage, numeric: true, export: f => Col.Inv(f.Coverage), exportHeader: "coverage"),
        ];
        ParametersTable.Columns =
        [
            Col.Of<StatLine>(Loc.T("Group"), 100, s => s.Group, export: s => s.Group, exportHeader: "group"),
            Col.Of<StatLine>(Loc.T("Parameter"), 270, s => s.Label, export: s => s.Label, exportHeader: "parameter"),
            Col.Of<StatLine>(Loc.T("Value"), 300, s => s.Value, export: s => s.Value, exportHeader: "value", fill: true),
        ];

        FirstOrdersTable.RowInvoked += OnOrderInvoked;
        SecondOrdersTable.RowInvoked += OnOrderInvoked;
        ViewModel.PropertyChanged += OnViewModelChanged;
        ViewModel.ChartChanged += OnChartChanged;
        Unloaded += (_, _) =>
        {
            ViewModel.PropertyChanged -= OnViewModelChanged;
            ViewModel.ChartChanged -= OnChartChanged;
        };
        ConfigureMode();
        UpdateHeaders();
        OnChartChanged(null, EventArgs.Empty);
    }

    public SimulationViewModel ViewModel { get; }

    public string? SimulationId => ViewModel.Id;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string id)
            await ViewModel.LoadAsync(id);
        else if (ViewModel.Id is { } current)
            await ViewModel.LoadAsync(current);
    }

    public void Reload()
    {
        if (ViewModel.Id is { } id)
            _ = ViewModel.LoadAsync(id);
    }

    private static string Million(decimal rials) => Fmt.Signed(rials, x => x / Fmt.RialsPerMillionToman, "N0");

    private static List<TableColumn> MonthColumns()
    {
        var names = ReportsViewModel.MonthNames;
        var columns = new List<TableColumn> { Col.Of<MonthRow>(Loc.T("Year"), 80, m => Fmt.Ltr(m.Year.ToString(CultureInfo.InvariantCulture)), export: m => Col.Inv(m.Year), exportHeader: "year") };
        for (var k = 0; k < 12; k++)
        {
            var month = k;
            columns.Add(Col.Of<MonthRow>(names[k], 84, m => Fmt.FractionChange(m.Months[month]), tone: m => Fmt.ToneOf(m.Months[month] * 100), numeric: true,
                export: m => Col.Inv(m.Months[month]), exportHeader: $"m{month + 1:00}"));
        }

        columns.Add(Col.Of<MonthRow>(Loc.T("Year total"), 100, m => Fmt.FractionChange(m.Total), tone: m => Fmt.ToneOf(m.Total * 100), numeric: true, export: m => Col.Inv(m.Total), exportHeader: "year_total"));
        return columns;
    }

    // ---------------------------------------------------------------- state

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SimulationViewModel.IsManual):
                ConfigureMode();
                break;
            case nameof(SimulationViewModel.IsTracking) or nameof(SimulationViewModel.Holdings) or nameof(SimulationViewModel.Trades) or nameof(SimulationViewModel.Orders):
                UpdateHeaders();
                ShowOrders();
                break;
            case nameof(SimulationViewModel.Planned) or nameof(SimulationViewModel.Suggestions):
                ShowOrders();
                break;
            case nameof(SimulationViewModel.TicketSide) or nameof(SimulationViewModel.TicketUnit) or nameof(SimulationViewModel.TicketInstrument):
                SyncTicket();
                break;
        }
    }

    /// <summary>Manual mode: the ticket, your orders and the strategy's suggestions. Automatic: the next session's orders.</summary>
    private void ConfigureMode()
    {
        var manual = ViewModel.IsManual;
        TicketPanel.Visibility = Ui.Show(manual);
        TicketColumn.Width = manual ? new GridLength(360) : new GridLength(0);
        SecondOrdersTitle.Visibility = SecondOrdersCard.Visibility = Ui.Show(manual);
        HoldingsHint.Text = manual ? Loc.T("Click a holding to sell it.") : Loc.T("Click a holding to open its symbol page.");
        TicketNote.Text = Loc.T("The order is decided with the data up to the latest session and fills at the next session's price (your parameters choose its open or its close), only if the share trades and is not locked in a queue against you, for at most a share of the session's value. Costs and the 0.5% sale tax are taken from the rules in force.");
        if (columnsForManual == manual)
            return;
        columnsForManual = manual;

        FirstOrdersTable.Columns = manual ? OrderColumns() : PlanColumns(withScore: true);
        FirstOrdersTable.EmptyText = manual ? Loc.T("You have not placed an order yet.") : Loc.T("Nothing to trade at the next session.");
        SecondOrdersTable.Columns = PlanColumns(withScore: true);
        SecondOrdersTable.EmptyText = Loc.T("The strategy would not change anything now.");
        ShowOrders();
    }

    private void ShowOrders()
    {
        if (ViewModel.IsManual)
        {
            FirstOrdersTitle.Text = Loc.T("Your orders (click a waiting one to cancel it)");
            FirstOrdersTable.Items = ViewModel.Orders;
            SecondOrdersTitle.Text = Loc.T("What the strategy would do now with your parameters (click one to fill the ticket)");
            SecondOrdersTable.Items = ViewModel.Suggestions;
        }
        else
        {
            FirstOrdersTitle.Text = Loc.T("Orders for the next session, decided at the latest close");
            FirstOrdersTable.Items = ViewModel.Planned;
        }
    }

    private static List<TableColumn> PlanColumns(bool withScore) =>
    [
        Col.Of<SimulationOrderPlanDto>(Loc.T("Side"), 70, _ => string.Empty, sort: p => p.Side, tag: p => (SimulationText.Side(p.Side), SimulationText.SideTone(p.Side)), export: p => p.Side, exportHeader: "side"),
        Col.Of<SimulationOrderPlanDto>(Loc.T("Symbol"), 96, p => p.Symbol, sort: p => p.Symbol, export: p => p.Symbol, exportHeader: "symbol"),
        Col.Of<SimulationOrderPlanDto>(Loc.T("Name"), 170, p => p.Name ?? string.Empty, sort: p => p.Name, export: p => p.Name, exportHeader: "name", fill: true),
        Col.Of<SimulationOrderPlanDto>(Loc.T("Shares"), 100, p => Fmt.Decimal(p.Shares, 0), sort: p => p.Shares, numeric: true, export: p => Col.Inv(p.Shares), exportHeader: "shares"),
        Col.Of<SimulationOrderPlanDto>(Loc.T("Last price"), 100, p => Fmt.Price(p.Price), sort: p => p.Price, numeric: true, export: p => Col.Inv(p.Price), exportHeader: "price"),
        Col.Of<SimulationOrderPlanDto>(Loc.T("Value (million toman)"), 130, p => Fmt.MillionToman(p.Value), sort: p => p.Value, numeric: true, export: p => Col.Inv(p.Value), exportHeader: "value_rial"),
        Col.Of<SimulationOrderPlanDto>(Loc.T("Why"), 170, p => SimulationText.Reason(p.Reason), sort: p => p.Reason, export: p => p.Reason, exportHeader: "reason"),
        .. withScore
            ? [
                Col.Of<SimulationOrderPlanDto>(Loc.T("Rank"), 70, p => p.Rank is { } r ? Fmt.Number(r) : Fmt.Dash, sort: p => p.Rank, numeric: true, export: p => Col.Inv(p.Rank), exportHeader: "rank"),
                Col.Of<SimulationOrderPlanDto>(Loc.T("Score"), 70, p => p.Score is { } s ? Fmt.Num(s, "N0") : Fmt.Dash, sort: p => p.Score, numeric: true, export: p => Col.Inv(p.Score), exportHeader: "score"),
            ]
            : Array.Empty<TableColumn>(),
    ];

    private static List<TableColumn> OrderColumns() =>
    [
        Col.Of<OrderLine>(Loc.T("Placed on"), 100, o => Fmt.Date(o.Order.PlacedOn), sort: o => o.Order.PlacedAt, export: o => Col.Inv(o.Order.PlacedOn), exportHeader: "placed_on"),
        Col.Of<OrderLine>(Loc.T("Side"), 70, _ => string.Empty, sort: o => o.Order.Side, tag: o => (SimulationText.Side(o.Order.Side), SimulationText.SideTone(o.Order.Side)), export: o => o.Order.Side, exportHeader: "side"),
        Col.Of<OrderLine>(Loc.T("Symbol"), 96, o => o.Order.Symbol, sort: o => o.Order.Symbol, export: o => o.Order.Symbol, exportHeader: "symbol"),
        Col.Of<OrderLine>(Loc.T("Amount"), 150, o => o.Order switch
        {
            { Value: { } v } => Loc.F("{0} million toman", Fmt.MillionToman(v)),
            { Shares: { } n } => Loc.F("{0} shares", Fmt.Decimal(n, 0)),
            _ => Loc.T("Everything"),
        }, export: o => Col.Inv(o.Order.Value ?? o.Order.Shares), exportHeader: "amount"),
        Col.Of<OrderLine>(Loc.T("Limit"), 90, o => o.Order.LimitPrice is { } l ? Fmt.Price(l) : Fmt.Dash, numeric: true, export: o => Col.Inv(o.Order.LimitPrice), exportHeader: "limit"),
        Col.Of<OrderLine>(Loc.T("State"), 130, _ => string.Empty, sort: o => o.State, tag: o => SimulationText.OrderStateTag(o.State), export: o => o.State, exportHeader: "state"),
        Col.Of<OrderLine>(Loc.T("Filled"), 100, o => o.Status is { FilledShares: > 0 } s ? Fmt.Decimal(s.FilledShares, 0) : Fmt.Dash, sort: o => o.Status?.FilledShares, numeric: true, export: o => Col.Inv(o.Status?.FilledShares), exportHeader: "filled_shares"),
        Col.Of<OrderLine>(Loc.T("Average price"), 100, o => Fmt.Price(o.Status?.AveragePrice), sort: o => o.Status?.AveragePrice, numeric: true, export: o => Col.Inv(o.Status?.AveragePrice), exportHeader: "average_price"),
        Col.Of<OrderLine>(Loc.T("Note"), 220, o => o.Status?.Reason is { } r && o.State is "pending" or "partial" or "rejected" or "expired"
            ? SimulationText.Event(new SimulationEventDto(default, r, null, o.Order.Symbol, null, null))
            : string.Empty, export: o => o.Status?.Reason, exportHeader: "note", fill: true),
    ];

    private void UpdateHeaders()
    {
        OverviewTab.Text = Loc.T("Overview");
        HoldingsTab.Text = $"{Loc.T("Holdings")} ({Fmt.Number(ViewModel.Holdings.Count)})";
        OrdersTab.Text = ViewModel.IsManual
            ? $"{Loc.T("Orders")} ({Fmt.Number(ViewModel.Orders.Count(o => o.State is "pending" or "partial"))})"
            : $"{Loc.T("Next session")} ({Fmt.Number(ViewModel.Planned.Count)})";
        TradesTab.Text = $"{Loc.T("Trades")} ({Fmt.Number(ViewModel.Trades.Count)})";
        JournalTab.Text = Loc.T("Journal");
        PerformanceTab.Text = Loc.T("Performance");
        StrategyTab.Text = Loc.T("Strategy");
        TrackingButton.Content = ViewModel.IsTracking ? Loc.T("Stop tracking") : Loc.T("Follow the market again");
        ToolTipService.SetToolTip(TrackingButton, ViewModel.IsTracking
            ? Loc.T("Freeze the simulation at the latest session: it no longer trades or follows the market.")
            : Loc.T("Follow the market again: the sessions since it stopped are replayed."));
    }

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is string key)
            ShowTab(key);
    }

    private void ShowTab(string key)
    {
        OverviewPanel.Visibility = Ui.Show(key == "overview");
        HoldingsPanel.Visibility = Ui.Show(key == "holdings");
        OrdersPanel.Visibility = Ui.Show(key == "orders");
        TradesPanel.Visibility = Ui.Show(key == "trades");
        JournalPanel.Visibility = Ui.Show(key == "journal");
        PerformancePanel.Visibility = Ui.Show(key == "performance");
        StrategyPanel.Visibility = Ui.Show(key == "strategy");
    }

    private void SelectTab(string key)
    {
        foreach (var item in Tabs.Items)
            item.IsSelected = (string)item.Tag == key;
        ShowTab(key);
    }

    private void OnChartChanged(object? sender, EventArgs e)
    {
        var dates = ViewModel.ChartDates;
        EquityChart.Summary = dates.Count < 2 ? null : Loc.F("{0} to {1}", Fmt.Date(dates[0]), Fmt.Date(dates[^1]));
        EquityChart.Show(dates, ViewModel.EquitySeries, v => Fmt.Tick(v, "N0"), baselineValue: 100);
        DrawdownChart.Show(dates, ViewModel.DrawdownSeries, v => Fmt.Tick(v, "N0") + "%", baselineValue: 0);
    }

    // ---------------------------------------------------------------- the ticket

    private void SyncTicket()
    {
        SideSwitch.SelectedKey = ViewModel.TicketSide;
        UnitSwitch.Items = SimulationViewModel.Units(ViewModel.TicketSide);
        UnitSwitch.SelectedKey = ViewModel.TicketUnit;
        AmountBox.Visibility = Ui.Show(ViewModel.TicketUnit != "all");
        AmountBox.Header = ViewModel.TicketUnit == "shares" ? Loc.T("Shares") : Loc.T("Amount (million toman)");
        TicketInstrumentText.Text = ViewModel.TicketInstrument is { } i ? $"{i.Symbol} · {i.Name}" : Loc.T("Search a symbol, or click a suggestion or a holding.");
        if (ViewModel.TicketInstrument is null)
            SymbolBox.Text = string.Empty;
    }

    private void OnSymbolTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            _ = ViewModel.SearchAsync(sender.Text);
    }

    private void OnSymbolChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is InstrumentSearchResultDto instrument)
        {
            ViewModel.TicketInstrument = instrument;
            sender.Text = instrument.Symbol;
        }
    }

    private void OnSymbolSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if ((args.ChosenSuggestion ?? ViewModel.TicketMatches.FirstOrDefault()) is InstrumentSearchResultDto instrument)
        {
            ViewModel.TicketInstrument = instrument;
            sender.Text = instrument.Symbol;
        }
    }

    private async void OnPlaceOrder(object sender, RoutedEventArgs e) => await ViewModel.PlaceOrderAsync();

    private void OnHoldingInvoked(object? sender, object row)
    {
        var holding = (SimulationHoldingDto)row;
        if (!ViewModel.IsManual)
        {
            navigator.OpenSymbol(holding.InsCode);
            return;
        }

        ViewModel.SellHolding(holding);
        SelectTab("orders");
    }

    private async void OnOrderInvoked(object? sender, object row)
    {
        switch (row)
        {
            case SimulationOrderPlanDto plan when ViewModel.IsManual:
                ViewModel.UseSuggestion(plan);
                break;
            case SimulationOrderPlanDto plan:
                navigator.OpenSymbol(plan.InsCode);
                break;
            case OrderLine { CanCancel: true } line:
                if (await ConfirmAsync(Loc.F("Cancel the order to {0} {1}?", SimulationText.Side(line.Order.Side), line.Order.Symbol),
                        Loc.T("It will not fill on any session after the latest one."), Loc.T("Cancel the order")))
                    await ViewModel.CancelOrderAsync(line);
                break;
        }
    }

    // ---------------------------------------------------------------- actions

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Id is { } id)
            navigator.Navigate(Navigator.SimulationEditor, id);
    }

    private async void OnTracking(object sender, RoutedEventArgs e) => await ViewModel.SetTrackingAsync(!ViewModel.IsTracking);

    private void OnReplay(object sender, RoutedEventArgs e) => Reload();

    private void OnDuplicate(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Saved is { } saved)
            navigator.Navigate(Navigator.Simulation, App.Get<SimulationsViewModel>().Duplicate(saved).Spec.Id);
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Saved is not { } saved)
            return;
        if (!await ConfirmAsync(Loc.F("Delete “{0}”?", saved.Spec.Name), Loc.T("Its definition and orders are removed from this computer. This cannot be undone."), Loc.T("Delete")))
            return;
        App.Get<SimulationsViewModel>().Delete(saved);
        navigator.Navigate(Navigator.Simulations);
    }

    private async Task<bool> ConfirmAsync(string title, string text, string action)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = text,
            PrimaryButtonText = action,
            CloseButtonText = Loc.T("Keep"),
            DefaultButton = ContentDialogButton.Close,
            FlowDirection = Loc.Flow,
            RequestedTheme = ActualTheme,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async void OnExportTrades(object sender, RoutedEventArgs e) => await ExportAsync("trades", TradesTable.ExportCsv);

    private async void OnExportHoldings(object sender, RoutedEventArgs e) => await ExportAsync("holdings", HoldingsTable.ExportCsv);

    private async void OnExportEquity(object sender, RoutedEventArgs e) => await ExportAsync("daily", writer =>
    {
        writer.WriteLine("date,equity_rial,cash_rial,invested_rial,tedpix,equal_weight,usd_rial");
        foreach (var p in ViewModel.Result?.Equity ?? [])
            writer.WriteLine(string.Join(',', Col.Inv(p.Date), Col.Inv(p.Equity), Col.Inv(p.Cash), Col.Inv(p.Invested), Col.Inv(p.Tedpix), Col.Inv(p.EqualWeight), Col.Inv(p.Usd)));
    });

    private async Task ExportAsync(string what, Action<TextWriter> write)
    {
        var name = string.Concat((ViewModel.Saved?.Spec.Name ?? "simulation").Select(c => char.IsLetterOrDigit(c) ? c : '-'));
        var picker = new FileSavePicker(XamlRoot.ContentIslandEnvironment.AppWindowId) { SuggestedFileName = $"simulation-{name}-{what}" };
        picker.FileTypeChoices.Add("CSV", [".csv"]);
        if (await picker.PickSaveFileAsync() is { } file)
        {
            await using var stream = File.Create(file.Path);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(true));
            write(writer);
        }
    }
}
