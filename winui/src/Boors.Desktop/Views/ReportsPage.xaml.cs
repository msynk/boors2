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

/// <summary>
/// The Reports page: the window (a period or a custom range ending on a base date, steppable back and forth), one
/// table whose columns follow the selected report, benchmark tiles above it and, for the signal track record, the
/// selected rule's record and its picks below it.
/// </summary>
public sealed partial class ReportsPage : Page, IReloadable
{
    private readonly Navigator navigator = App.Get<Navigator>();
    private string? columnsFor;
    private string? periodsFor;
    private bool syncingDates;

    public ReportsPage()
    {
        ViewModel = App.Get<ReportsViewModel>();
        InitializeComponent();
        var lite = App.Get<SettingsService>().Current.Density == "Lite";

        foreach (var report in ReportsViewModel.Reports)
            ReportBar.Items.Add(new SelectorBarItem { Text = report.Title, Tag = report.Key, IsSelected = report.Key == ViewModel.Report });

        PeriodSwitch.SelectionChanged += (_, key) => _ = ViewModel.SetPeriodAsync(key);
        foreach (var picker in new[] { FromPicker, EndPicker })
        {
            // Jalali calendar and Saturday-first weeks under Persian; no future dates (there is no data there).
            picker.CalendarIdentifier = Loc.IsPersian ? "PersianCalendar" : "GregorianCalendar";
            picker.Language = Loc.IsPersian ? "fa-IR" : "en-US";
            picker.FirstDayOfWeek = Loc.IsPersian ? Windows.Globalization.DayOfWeek.Saturday : Windows.Globalization.DayOfWeek.Monday;
            picker.DateFormat = "{year.full}/{month.integer(2)}/{day.integer(2)}";
            picker.MaxDate = DateTimeOffset.Now;
        }

        MeasureSwitch.Items = ReportsViewModel.MeasureChoices;
        MeasureSwitch.SelectedKey = ViewModel.Measure;
        MeasureSwitch.SelectionChanged += (_, key) => ViewModel.SetMeasure(key);
        LiquidityBox.ItemsSource = ReportsViewModel.LiquidityChoices;
        LiquidityBox.SelectedItem = ReportsViewModel.LiquidityChoices.FirstOrDefault(c => c.Key == ViewModel.Liquidity);

        foreach (var table in new[] { Table, PicksTable })
        {
            table.RowHeight = lite ? 38 : 30;
            table.CellFontSize = lite ? 14 : 13;
        }

        Table.RowInvoked += OnRowInvoked;
        PicksTable.EmptyText = Loc.T("No share qualifies today.");
        PicksTable.Columns =
        [
            Col.Of<RankedPick>("#", 44, r => Fmt.Number(r.Rank), sort: r => r.Rank, numeric: true, export: r => Col.Inv(r.Rank)),
            Col.Of<RankedPick>(Loc.T("Symbol"), 96, r => r.Pick.Symbol, sort: r => r.Pick.Symbol, export: r => r.Pick.Symbol),
            Col.Of<RankedPick>(Loc.T("Name"), 150, r => r.Pick.Name ?? string.Empty, sort: r => r.Pick.Name, export: r => r.Pick.Name, fill: true),
            Col.Of<RankedPick>(Loc.T("Value"), 110, r => ViewModel.FormatPick(r.Pick.Value), sort: r => r.Pick.Value, numeric: true, export: r => Col.Inv(r.Pick.Value)),
        ];
        PicksTable.RowInvoked += (_, row) => navigator.OpenSymbol(((RankedPick)row).Pick.InsCode);

        ViewModel.PropertyChanged += OnViewModelChanged;
        ViewModel.ChartChanged += OnChartChanged;
        Unloaded += (_, _) =>
        {
            ViewModel.PropertyChanged -= OnViewModelChanged;
            ViewModel.ChartChanged -= OnChartChanged;
        };
        UpdateOptions();
        SyncDates();
        ShowRows();
        OnChartChanged(null, EventArgs.Empty);
    }

    public ReportsViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Refresh();
        if (!ViewModel.HasData)
            await ViewModel.LoadAsync();
    }

    public void Reload() => _ = ViewModel.LoadAsync(force: true);

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReportsViewModel.Rows))
        {
            ShowRows();
        }
        else if (e.PropertyName is nameof(ReportsViewModel.Report) or nameof(ReportsViewModel.Period) or nameof(ReportsViewModel.SectorFilter)
            or nameof(ReportsViewModel.EndDate) or nameof(ReportsViewModel.StartDate))
        {
            UpdateOptions();
            SyncDates();
        }
    }

    /// <summary>Columns first (they depend on the report), then the rows, in the order the report ranked them.</summary>
    private void ShowRows()
    {
        var key = $"{ViewModel.ShownReport}|{ViewModel.ShownPast}|{ViewModel.ShownVariant}";
        if (key != columnsFor)
        {
            columnsFor = key;
            Table.SetSort(-1, false);
            Table.Columns = ColumnsFor(ViewModel.ShownReport);
            Table.EmptyText = ViewModel.ShownReport switch
            {
                ReportsViewModel.Dip => Loc.T("No steady compounder is on a dip right now."),
                ReportsViewModel.Shortlist => Loc.T("No share passes this horizon's rules on this date."),
                ReportsViewModel.Signals or ReportsViewModel.Seasonality => Loc.T("Not enough history for this report yet."),
                _ => Loc.T("No share has enough history and liquidity for this period yet."),
            };
        }

        Table.Items = ViewModel.Rows;
    }

    private void UpdateOptions()
    {
        var info = ViewModel.Info;
        var report = info.Key;
        PeriodSwitch.Visibility = Ui.Show(info.Periods.Count > 0);

        // Rebuild the buttons only for another report: this also runs from a period button's own toggle event.
        if (periodsFor != report)
        {
            periodsFor = report;
            PeriodSwitch.Items = ReportsViewModel.PeriodChoices(info.Periods);
        }

        PeriodSwitch.SelectedKey = ViewModel.Period;

        var custom = ViewModel.Period == ReportsViewModel.Custom;
        FromPicker.Visibility = Ui.Show(custom);
        RangeDash.Visibility = Ui.Show(custom);
        PreviousButton.Visibility = Ui.Show(info.CanStep);
        NextButton.Visibility = Ui.Show(info.CanStep);
        NextButton.IsEnabled = ViewModel.EndDate is not null;
        LatestButton.Visibility = Ui.Show(ViewModel.EndDate is not null);
        MeasureSwitch.Visibility = Ui.Show(report == ReportsViewModel.Seasonality);
        LiquidityPanel.Visibility = Ui.Show(info.UsesLiquidity);
        SectorChip.Visibility = Ui.Show(ViewModel.SectorFilter is not null && report is ReportsViewModel.Top or ReportsViewModel.Steady or ReportsViewModel.Dip);
    }

    /// <summary>Shows the view model's dates in the pickers without reacting to the change.</summary>
    private void SyncDates()
    {
        syncingDates = true;
        FromPicker.Date = ToOffset(ViewModel.StartDate);
        EndPicker.Date = ToOffset(ViewModel.EndDate);
        syncingDates = false;
    }

    private static DateTimeOffset? ToOffset(DateOnly? date) => date is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue)) : null;

    private static DateOnly? ToDate(DateTimeOffset? date) => date is { } d ? DateOnly.FromDateTime(d.Date) : null;

    private void OnFromChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (!syncingDates && ToDate(sender.Date) is { } start && start != ViewModel.StartDate)
            _ = ViewModel.SetDatesAsync(ViewModel.EndDate, start);
    }

    private void OnEndChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (!syncingDates && ToDate(sender.Date) is var end && end != ViewModel.EndDate)
            _ = ViewModel.SetDatesAsync(end, ViewModel.StartDate);
    }

    private void OnPrevious(object sender, RoutedEventArgs e) => _ = ViewModel.StepAsync(-1);

    private void OnNext(object sender, RoutedEventArgs e) => _ = ViewModel.StepAsync(+1);

    private void OnLatest(object sender, RoutedEventArgs e) => _ = ViewModel.ShowLatestAsync();

    private void OnReportChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is string report && report != ViewModel.Report)
            _ = ViewModel.ShowReportAsync(report);
    }

    private void OnLiquidityChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LiquidityBox.SelectedItem is Choice choice && choice.Key != ViewModel.Liquidity)
        {
            ViewModel.Liquidity = choice.Key;
            _ = ViewModel.LoadAsync();
        }
    }

    private void OnClearSector(object sender, RoutedEventArgs e) => ViewModel.ClearSector();

    private void OnRowInvoked(object? sender, object row)
    {
        switch (row)
        {
            case RankedPerformanceRow r:
                navigator.OpenSymbol(r.Row.InsCode);
                break;
            case RankedShortlistPick p:
                navigator.OpenSymbol(p.Pick.InsCode);
                break;
            case SectorReturnDto s:
                // Switch the report first, so the tab's own selection change finds it already shown.
                _ = ViewModel.ShowSectorAsync(s.SectorCode, s.SectorName);
                SelectReport(ReportsViewModel.Top);
                break;
            case SignalVerdictDto v:
                ViewModel.SelectSignal(v.Signal);
                break;
        }
    }

    private void SelectReport(string report)
    {
        foreach (var item in ReportBar.Items)
            item.IsSelected = (string)item.Tag == report;
    }

    private void OnChartChanged(object? sender, EventArgs e)
    {
        var values = ViewModel.ChartValues;
        SignalChart.Summary = values.Count < 2 ? null : Loc.F("{0} to {1}: {2}", Fmt.Date(ViewModel.ChartDates[0]), Fmt.Date(ViewModel.ChartDates[^1]), Fmt.Num(values[^1], "N1"));
        SignalChart.Show(ViewModel.ChartDates, values, v => Fmt.Tick(v, "N0"), baselineValue: 100);
    }

    private List<TableColumn> ColumnsFor(string report) => report switch
    {
        ReportsViewModel.Shortlist => ShortlistColumns(ViewModel.ShownVariant),
        ReportsViewModel.Steady =>
        [
            .. Lead(),
            Score(),
            .. SinceThen(),
            Annual(),
            Usd(),
            Share(Loc.T("Up months"), r => r.PositiveMonths, "up_months"),
            Share(Loc.T("1-year holds beating the dollar"), r => r.UsdWinRate, "usd_win_rate", 150),
            Drawdown(),
            Col.Of<RankedPerformanceRow>(Loc.T("Return / risk"), 96, r => r.Row.RiskAdjusted is { } v ? Fmt.Num(v, "N2") : Fmt.Dash, sort: r => r.Row.RiskAdjusted, numeric: true, export: r => Col.Inv(r.Row.RiskAdjusted), exportHeader: "risk_adjusted"),
            Share(Loc.T("Volatility"), r => r.Volatility, "volatility"),
            Return(),
            .. Trading(),
        ],
        ReportsViewModel.Dip =>
        [
            .. Lead(),
            Fraction(Loc.T("From 52-week high"), r => r.FromHigh52Week, "from_high_52w", 120),
            .. SinceThen(),
            Score(),
            Annual(),
            Fraction(Loc.T("Against TEDPIX"), r => r.ExcessReturn, "excess_return"),
            Share(Loc.T("Up months"), r => r.PositiveMonths, "up_months"),
            Drawdown(),
            Usd(),
            .. Trading(),
        ],
        ReportsViewModel.Sectors =>
        [
            Col.Of<SectorReturnDto>(Loc.T("Sector"), 240, s => s.SectorName ?? s.SectorCode, sort: s => s.SectorName, export: s => s.SectorName ?? s.SectorCode, exportHeader: "sector", fill: true),
            Col.Of<SectorReturnDto>(Loc.T("Shares"), 70, s => Fmt.Number(s.Members), sort: s => s.Members, numeric: true, export: s => Col.Inv(s.Members), exportHeader: "members"),
            SectorFraction(Loc.T("Median return"), s => s.MedianReturn, "median_return"),
            .. ViewModel.ShownPast ? [SectorFraction(Loc.T("Median since then"), s => s.MedianSinceThen, "median_since_then")] : Array.Empty<TableColumn>(),
            SectorFraction(Loc.T("Average return"), s => s.AverageReturn, "average_return"),
            SectorFraction(Loc.T("Median in dollars"), s => s.MedianUsdReturn, "median_usd_return"),
            Col.Of<SectorReturnDto>(Loc.T("Rose"), 80, s => Fmt.Percent(s.PositiveShare, 0), sort: s => s.PositiveShare, numeric: true, export: s => Col.Inv(s.PositiveShare), exportHeader: "positive_share"),
            Col.Of<SectorReturnDto>(Loc.T("Beat TEDPIX"), 96, s => Fmt.Percent(s.BeatTedpixShare, 0), sort: s => s.BeatTedpixShare, numeric: true, export: s => Col.Inv(s.BeatTedpixShare), exportHeader: "beat_tedpix_share"),
            Col.Of<SectorReturnDto>(Loc.T("Best share"), 100, s => s.BestSymbol ?? string.Empty, sort: s => s.BestSymbol, export: s => s.BestSymbol, exportHeader: "best_symbol"),
            SectorFraction(Loc.T("Its return"), s => s.BestReturn, "best_return"),
            Col.Of<SectorReturnDto>(Loc.T("Value per session (billion toman)"), 150, s => Fmt.BillionToman(s.Value20), sort: s => s.Value20, numeric: true, export: s => Col.Inv(s.Value20), exportHeader: "value20_rial"),
        ],
        ReportsViewModel.Signals =>
        [
            Col.Of<SignalVerdictDto>(Loc.T("Rule"), 240, v => ReportsViewModel.SignalName(v.Signal), sort: v => ReportsViewModel.SignalName(v.Signal), export: v => v.Signal, exportHeader: "signal", fill: true),
            Col.Of<SignalVerdictDto>(Loc.T("Verdict"), 170, v => string.Empty, sort: v => v.Verdict, tag: v => ReportsViewModel.VerdictTag(v.Verdict), export: v => v.Verdict, exportHeader: "verdict"),
            Col.Of<SignalVerdictDto>(Loc.T("Months"), 70, v => Fmt.Number(v.Months), sort: v => v.Months, numeric: true, export: v => Col.Inv(v.Months), exportHeader: "months"),
            SignalFraction(Loc.T("Top fifth / month"), v => v.TopReturn, "top_return"),
            SignalFraction(Loc.T("Average share / month"), v => v.UniverseReturn, "universe_return"),
            SignalFraction(Loc.T("Bottom fifth / month"), v => v.BottomReturn, "bottom_return"),
            SignalFraction(Loc.T("Edge / month"), v => v.Excess, "excess"),
            Col.Of<SignalVerdictDto>(Loc.T("Months won"), 90, v => Fmt.Percent(v.HitRate, 0), sort: v => v.HitRate, numeric: true, export: v => Col.Inv(v.HitRate), exportHeader: "hit_rate"),
            Col.Of<SignalVerdictDto>(Loc.T("t-stat"), 70, v => v.TStat is { } t ? Fmt.Num(t, "N1") : Fmt.Dash, sort: v => v.TStat, numeric: true, export: v => Col.Inv(v.TStat), exportHeader: "t_stat"),
            Col.Of<SignalVerdictDto>(Loc.T("Rank correlation"), 110, v => v.InformationCoefficient is { } ic ? Fmt.Num(ic, "N3") : Fmt.Dash, sort: v => v.InformationCoefficient, numeric: true, export: v => Col.Inv(v.InformationCoefficient), exportHeader: "ic"),
            Col.Of<SignalVerdictDto>(Loc.T("Turnover / month"), 110, v => Fmt.Percent(v.Turnover, 0), sort: v => v.Turnover, numeric: true, export: v => Col.Inv(v.Turnover), exportHeader: "turnover"),
            SignalFraction(Loc.T("Edge after costs / year"), v => v.AnnualNetExcess, "annual_net_excess", 140),
        ],
        ReportsViewModel.Seasonality => SeasonalityColumns(),
        _ =>
        [
            .. Lead(),
            Return(),
            .. SinceThen(),
            Annual(),
            Usd(),
            Fraction(Loc.T("Against TEDPIX"), r => r.ExcessReturn, "excess_return"),
            Drawdown(),
            Share(Loc.T("Volatility"), r => r.Volatility, "volatility"),
            Share(Loc.T("Up months"), r => r.PositiveMonths, "up_months"),
            Score(),
            Fraction(Loc.T("From 52-week high"), r => r.FromHigh52Week, "from_high_52w", 120),
            .. Trading(),
        ],
    };

    /// <summary>The symbol stays in view while scrolling sideways (the table freezes its first column), then the rank.</summary>
    /// <summary>The columns of one shortlist horizon: what that list was ranked on, then trading facts.</summary>
    private List<TableColumn> ShortlistColumns(string horizon)
    {
        List<TableColumn> columns =
        [
            Col.Of<RankedShortlistPick>(Loc.T("Symbol"), 96, r => r.Pick.Symbol, sort: r => r.Pick.Symbol, export: r => r.Pick.Symbol, exportHeader: "symbol"),
            Col.Of<RankedShortlistPick>("#", 48, r => Fmt.Number(r.Rank), sort: r => r.Rank, numeric: true, export: r => Col.Inv(r.Rank), exportHeader: "rank"),
            Col.Of<RankedShortlistPick>(Loc.T("Name"), 200, r => r.Pick.Name ?? string.Empty, sort: r => r.Pick.Name, export: r => r.Pick.Name, exportHeader: "name", fill: true),
            Col.Of<RankedShortlistPick>(Loc.T("Sector"), 170, r => r.Pick.SectorName ?? r.Pick.SectorCode ?? string.Empty, sort: r => r.Pick.SectorName, export: r => r.Pick.SectorName, exportHeader: "sector"),
            Col.Of<RankedShortlistPick>(Loc.T("Score"), 70, r => Fmt.Num(r.Pick.Score, "N0"), sort: r => r.Pick.Score, numeric: true, export: r => Col.Inv(r.Pick.Score), exportHeader: "score"),
            Col.Of<RankedShortlistPick>(Loc.T("Also on"), 110, r => string.Join(" · ", r.Pick.AlsoIn.Select(ReportsViewModel.HorizonName)), sort: r => r.Pick.AlsoIn.Count, export: r => string.Join(' ', r.Pick.AlsoIn), exportHeader: "also_in"),
        ];
        if (ViewModel.ShownPast)
            columns.Add(PickFraction(Loc.T("Since then"), p => p.SinceThen, "since_then"));

        columns.AddRange(horizon switch
        {
            "short" =>
            [
                PickFraction(Loc.T("3M"), p => p.Return3M, "return_3m"),
                PickFraction(Loc.T("1M"), p => p.Return1M, "return_1m"),
                PickFraction(Loc.T("12-month trend (skipping the last month)"), p => p.Momentum12To1, "momentum_12_1", 140),
                Col.Of<RankedShortlistPick>(Loc.T("Return / risk"), 96, r => r.Pick.RiskAdjusted is { } v ? Fmt.Num(v, "N2") : Fmt.Dash, sort: r => r.Pick.RiskAdjusted, numeric: true, export: r => Col.Inv(r.Pick.RiskAdjusted), exportHeader: "risk_adjusted"),
                PickShare(Loc.T("Volatility"), p => p.Volatility, "volatility"),
                PickFraction(Loc.T("From 52-week high"), p => p.FromHigh52Week, "from_high_52w", 120),
            ],
            "mid" =>
            [
                PickFraction(Loc.T("1Y"), p => p.Return1Y, "return_1y"),
                PickFraction(Loc.T("In dollars"), p => p.UsdReturn1Y, "usd_return_1y"),
                PickFraction(Loc.T("12-month trend (skipping the last month)"), p => p.Momentum12To1, "momentum_12_1", 140),
                PickFraction(Loc.T("3 years, per year"), p => p.AnnualizedReturn3Y, "annualized_return_3y", 120),
                PickShare(Loc.T("Up months"), p => p.PositiveMonths, "up_months"),
                PickFraction(Loc.T("Deepest fall"), p => p.MaxDrawdown, "max_drawdown"),
            ],
            _ =>
            [
                Col.Of<RankedShortlistPick>(Loc.T("Record"), 80, r => r.Pick.RecordYears is { } y ? Loc.F("{0} years", Fmt.Number(y)) : Fmt.Dash, sort: r => r.Pick.RecordYears, numeric: true, export: r => Col.Inv(r.Pick.RecordYears), exportHeader: "record_years"),
                PickFraction(Loc.T("Per year"), p => p.AnnualizedReturn, "annualized_return"),
                PickFraction(Loc.T("In dollars"), p => p.UsdReturn, "usd_return"),
                PickShare(Loc.T("1-year holds beating the dollar"), p => p.UsdWinRate, "usd_win_rate", 150),
                PickShare(Loc.T("Up months"), p => p.PositiveMonths, "up_months"),
                PickFraction(Loc.T("Deepest fall"), p => p.MaxDrawdown, "max_drawdown"),
                PickFraction(Loc.T("From 52-week high"), p => p.FromHigh52Week, "from_high_52w", 120),
            ],
        });
        columns.Add(Col.Of<RankedShortlistPick>(Loc.T("Value per session (billion toman)"), 150, r => Fmt.BillionToman(r.Pick.AverageValue20), sort: r => r.Pick.AverageValue20, numeric: true, export: r => Col.Inv(r.Pick.AverageValue20), exportHeader: "value20_rial"));
        return columns;
    }

    private static TableColumn PickFraction(string header, Func<ShortlistPickDto, double?> value, string exportHeader, double width = 110) =>
        Col.Of<RankedShortlistPick>(header, width, r => Fmt.FractionChange(value(r.Pick)), sort: r => value(r.Pick), tone: r => Fmt.ToneOf(value(r.Pick) * 100),
            numeric: true, export: r => Col.Inv(value(r.Pick)), exportHeader: exportHeader);

    private static TableColumn PickShare(string header, Func<ShortlistPickDto, double?> value, string exportHeader, double width = 96) =>
        Col.Of<RankedShortlistPick>(header, width, r => Fmt.Percent(value(r.Pick), 0), sort: r => value(r.Pick), numeric: true, export: r => Col.Inv(value(r.Pick)), exportHeader: exportHeader);

    private static IEnumerable<TableColumn> Lead() =>
    [
        Col.Of<RankedPerformanceRow>(Loc.T("Symbol"), 96, r => r.Row.Symbol, sort: r => r.Row.Symbol, export: r => r.Row.Symbol, exportHeader: "symbol"),
        Col.Of<RankedPerformanceRow>("#", 48, r => Fmt.Number(r.Rank), sort: r => r.Rank, numeric: true, export: r => Col.Inv(r.Rank), exportHeader: "rank"),
        Col.Of<RankedPerformanceRow>(Loc.T("Name"), 200, r => r.Row.Name ?? string.Empty, sort: r => r.Row.Name, export: r => r.Row.Name, exportHeader: "name", fill: true),
        Col.Of<RankedPerformanceRow>(Loc.T("Sector"), 170, r => r.Row.SectorName ?? r.Row.SectorCode ?? string.Empty, sort: r => r.Row.SectorName, export: r => r.Row.SectorName, exportHeader: "sector"),
    ];

    private static IEnumerable<TableColumn> Trading() =>
    [
        Col.Of<RankedPerformanceRow>(Loc.T("Value per session (billion toman)"), 150, r => Fmt.BillionToman(r.Row.AverageValue20), sort: r => r.Row.AverageValue20, numeric: true, export: r => Col.Inv(r.Row.AverageValue20), exportHeader: "value20_rial"),
        Col.Of<RankedPerformanceRow>(Loc.T("Days traded"), 96, r => Fmt.Percent(r.Row.Coverage, 0), sort: r => r.Row.Coverage, numeric: true, export: r => Col.Inv(r.Row.Coverage), exportHeader: "coverage"),
        Col.Of<RankedPerformanceRow>(Loc.T("Days in buy queue"), 120, r => Fmt.Percent(r.Row.BuyQueueShare, 0), sort: r => r.Row.BuyQueueShare, numeric: true, export: r => Col.Inv(r.Row.BuyQueueShare), exportHeader: "buy_queue_share",
            tone: r => r.Row.BuyQueueShare > 0.25 ? Tone.Accent : Tone.Neutral),
        Col.Of<RankedPerformanceRow>(Loc.T("Close"), 90, r => Fmt.Price(r.Row.Close), sort: r => r.Row.Close, numeric: true, export: r => Col.Inv(r.Row.Close), exportHeader: "close"),
    ];

    private static TableColumn Return() => Fraction(Loc.T("Return"), r => r.Return, "return", 120);

    /// <summary>What followed a window that ends in the past (absent at the latest session).</summary>
    private IEnumerable<TableColumn> SinceThen() =>
        ViewModel.ShownPast ? [Fraction(Loc.T("Since then"), r => r.SinceThen, "since_then", 110)] : [];

    private static TableColumn Annual() => Fraction(Loc.T("Per year"), r => r.AnnualizedReturn, "annualized_return");

    private static TableColumn Usd() => Fraction(Loc.T("In dollars"), r => r.UsdReturn, "usd_return");

    private static TableColumn Drawdown() => Fraction(Loc.T("Deepest fall"), r => r.MaxDrawdown, "max_drawdown");

    private static TableColumn Score() =>
        Col.Of<RankedPerformanceRow>(Loc.T("Score"), 70, r => r.Row.Score is { } s ? Fmt.Num(s, "N0") : Fmt.Dash, sort: r => r.Row.Score, numeric: true, export: r => Col.Inv(r.Row.Score), exportHeader: "score");

    private static TableColumn Fraction(string header, Func<PerformanceRowDto, double?> value, string exportHeader, double width = 110) =>
        Col.Of<RankedPerformanceRow>(header, width, r => Fmt.FractionChange(value(r.Row)), sort: r => value(r.Row), tone: r => Fmt.ToneOf(value(r.Row) * 100),
            numeric: true, export: r => Col.Inv(value(r.Row)), exportHeader: exportHeader);

    private static TableColumn Share(string header, Func<PerformanceRowDto, double?> value, string exportHeader, double width = 96) =>
        Col.Of<RankedPerformanceRow>(header, width, r => Fmt.Percent(value(r.Row), 0), sort: r => value(r.Row), numeric: true, export: r => Col.Inv(value(r.Row)), exportHeader: exportHeader);

    private static TableColumn SectorFraction(string header, Func<SectorReturnDto, double?> value, string exportHeader) =>
        Col.Of<SectorReturnDto>(header, 110, s => Fmt.FractionChange(value(s)), sort: s => value(s), tone: s => Fmt.ToneOf(value(s) * 100), numeric: true, export: s => Col.Inv(value(s)), exportHeader: exportHeader);

    private static TableColumn SignalFraction(string header, Func<SignalVerdictDto, double?> value, string exportHeader, double width = 110) =>
        Col.Of<SignalVerdictDto>(header, width, v => Fmt.FractionChange(value(v)), sort: v => value(v), tone: v => Fmt.ToneOf(value(v) * 100), numeric: true, export: v => Col.Inv(value(v)), exportHeader: exportHeader);

    /// <summary>Year (or summary) and the twelve Jalali months; not sortable, so the summary rows stay on top.</summary>
    private static List<TableColumn> SeasonalityColumns()
    {
        var names = ReportsViewModel.MonthNames;
        var columns = new List<TableColumn>
        {
            Col.Of<SeasonalityRow>(Loc.T("Year"), 96, r => r.Label, export: r => r.Label, exportHeader: "year"),
        };
        for (var m = 0; m < 12; m++)
        {
            var month = m;
            columns.Add(Col.Of<SeasonalityRow>(names[m], 92, r => Cell(r, r.Months[month]) + (r.PartialMonth == month + 1 ? " *" : string.Empty),
                tone: r => r.IsShare ? Tone.Neutral : Fmt.ToneOf(r.Months[month] * 100), numeric: true, export: r => Col.Inv(r.Months[month]),
                exportHeader: $"m{month + 1:00}"));
        }

        columns.Add(Col.Of<SeasonalityRow>(Loc.T("Year total"), 104, r => Cell(r, r.Total), tone: r => r.IsShare ? Tone.Neutral : Fmt.ToneOf(r.Total * 100),
            numeric: true, export: r => Col.Inv(r.Total), exportHeader: "year_total"));
        return columns;
    }

    private static string Cell(SeasonalityRow row, double? value) => row.IsShare ? Fmt.Percent(value, 0) : Fmt.FractionChange(value);

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        var window = ViewModel.Period == ReportsViewModel.Custom ? $"{ViewModel.StartDate:yyyy-MM-dd}" : ViewModel.Period;
        var end = ViewModel.EndDate is { } date ? $"-{date:yyyy-MM-dd}" : string.Empty;
        var picker = new FileSavePicker(XamlRoot.ContentIslandEnvironment.AppWindowId) { SuggestedFileName = $"report-{ViewModel.ShownReport}-{window}{end}" };
        picker.FileTypeChoices.Add("CSV", [".csv"]);
        if (await picker.PickSaveFileAsync() is { } file)
        {
            await using var stream = File.Create(file.Path);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(true));
            Table.ExportCsv(writer);
        }
    }
}
