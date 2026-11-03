using Boors.Desktop.Charts;
using Boors.Desktop.Infrastructure;
using Boors.Market.Services;
using Boors.Market.Services.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Boors.Desktop.ViewModels;

/// <summary>A labelled figure of the performance and parameter tables; <paramref name="Key"/> and <paramref name="Raw"/> are its ASCII export.</summary>
public sealed record StatLine(string Group, string Label, string Value, Tone Tone = Tone.Neutral, string? Key = null, string? Raw = null);

/// <summary>A journal line in words.</summary>
public sealed record JournalLine(DateOnly Date, string Text, Tone Tone, string Kind, string? Symbol, long? InsCode);

/// <summary>Monthly returns of one Jalali year (index 0 = Farvardin), with the year's total.</summary>
public sealed record MonthRow(int Year, IReadOnlyList<double?> Months, double? Total);

/// <summary>A manual order with what happened to it.</summary>
public sealed record OrderLine(ManualOrderDto Order, SimulationOrderStatusDto? Status)
{
    public string State => Status?.State ?? "pending";

    public bool CanCancel => State is "pending" or "partial" && Order.CancelledOn is null;
}

/// <summary>
/// One simulation: its replay (headline tiles, the equity curve against TEDPIX and the average share, drawdown,
/// holdings, the next session's orders, trades, journal, performance and monthly returns, the strategy's factor
/// weights) and its actions — the order ticket in manual mode, accepting a suggestion, cancelling an order, stopping
/// or resuming the tracking. Every change saves the definition and replays it.
/// </summary>
public sealed partial class SimulationViewModel(IMarketApi api, SimulationTracker tracker, ILogger<SimulationViewModel> logger) : PageViewModel(api, logger)
{
    public const string Rial = "rial";
    public const string Dollar = "usd";

    private CancellationTokenSource? searching;

    public string? Id { get; private set; }

    public SavedSimulation? Saved { get; private set; }

    public SimulationResultDto? Result { get; private set; }

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Subtitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsManual { get; set; }

    [ObservableProperty]
    public partial bool IsTracking { get; set; } = true;

    [ObservableProperty]
    public partial List<KpiItem> Kpis { get; set; } = [];

    /// <summary>What the user should know about this replay (waiting for data, fundamentals not collected yet...).</summary>
    [ObservableProperty]
    public partial string? Notice { get; set; }

    [ObservableProperty]
    public partial string NextText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<SimulationHoldingDto> Holdings { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<SimulationTradeDto> Trades { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<JournalLine> Journal { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<JournalLine> Recent { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<SimulationOrderPlanDto> Planned { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<SimulationOrderPlanDto> Suggestions { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<OrderLine> Orders { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<SimulationFactorDto> Factors { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<StatLine> Stats { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<StatLine> Parameters { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<MonthRow> Months { get; set; } = [];

    [ObservableProperty]
    public partial string Denomination { get; set; } = Rial;

    public IReadOnlyList<DateOnly> ChartDates { get; private set; } = [];

    public IReadOnlyList<ChartSeries> EquitySeries { get; private set; } = [];

    public IReadOnlyList<ChartSeries> DrawdownSeries { get; private set; } = [];

    /// <summary>Raised when the charts have new data.</summary>
    public event EventHandler? ChartChanged;

    public static IReadOnlyList<Choice> Denominations =>
    [
        new(Rial, Loc.T("In rials")),
        new(Dollar, Loc.T("In dollars")),
    ];

    public async Task LoadAsync(string id)
    {
        if (id != Id)
        {
            Id = id;
            Result = null;
            HasData = false;
            ResetTicket();
        }

        Saved = tracker.Find(id);
        if (Saved is null)
        {
            Error = Loc.T("This simulation no longer exists.");
            return;
        }

        Build();
        await RunAsync(async ct =>
        {
            Result = await tracker.ReplayAsync(Saved, detail: true, ct);
            tracker.MarkSeen(Saved);
            HasData = true;
            Build();
        }, "Simulation");
    }

    public override void Refresh() => Build();

    public void SetDenomination(string key)
    {
        Denomination = key;
        BuildCharts();
    }

    private void Build()
    {
        if (Saved is not { } saved)
            return;
        var spec = saved.Spec;
        IsManual = spec.Mode == SimulationText.Manual;
        IsTracking = spec.End is null;
        Title = spec.Name;
        var parts = new List<string> { SimulationText.Mode(spec.Mode) };
        if (!IsManual)
            parts.Add(SimulationText.Preset(spec.Preset));
        parts.Add(Loc.F("from {0}", Fmt.Date(spec.Start)));
        parts.Add(Loc.F("capital {0} million toman", Fmt.MillionToman(spec.Capital)));
        parts.Add(SimulationsViewModel.Status(saved));
        Subtitle = string.Join(" · ", parts);
        BuildParameters(spec);
        if (Result is not { } r)
            return;

        BuildKpis(r, spec);
        BuildCharts();
        Holdings = r.Holdings;
        Trades = [.. r.Trades.AsEnumerable().Reverse()];
        Journal = [.. r.Journal.AsEnumerable().Reverse().Select(e => new JournalLine(e.Date, SimulationText.Event(e), SimulationText.EventTone(e.Kind), e.Kind, e.Symbol, e.InsCode))];
        Recent = [.. Journal.Where(j => j.Kind is not ("rebalance" or "partial-fill")).Take(8)];
        Planned = r.Planned;
        Suggestions = r.Suggestions;
        var statuses = r.Orders.ToDictionary(o => o.OrderId);
        Orders = [.. spec.Orders.OrderByDescending(o => o.PlacedAt ?? DateTimeOffset.MinValue).Select(o => new OrderLine(o, statuses.GetValueOrDefault(o.Id)))];
        Factors = [.. r.Factors.OrderByDescending(f => f.Used)];
        Stats = BuildStats(r, spec);
        Months = BuildMonths(r, spec.Capital);

        var buys = r.Planned.Count(p => p.Side == "Buy");
        var sells = r.Planned.Count - buys;
        NextText = !IsTracking ? Loc.F("Tracking stopped on {0}.", Fmt.Date(r.AsOf))
            : r.Planned.Count > 0 ? Loc.F("Next session: {0} buys and {1} sells are waiting to fill.", Fmt.Number(buys), Fmt.Number(sells))
            : IsManual ? Loc.T("No order is waiting. Place one below, or take a suggestion of the strategy.")
            : Loc.T("Nothing to trade at the next session.");
        if (!IsManual && IsTracking && r.NextRebalanceIn is { } next)
            NextText += " " + Loc.F("Next ranking in {0} sessions.", Fmt.Number(next));

        Notice = r.AsOf is null ? Loc.F("No session on or after {0} yet: the simulation starts with the first one.", Fmt.Date(spec.Start))
            : r.Factors.Any(f => f.Family == "fundamental" && f.Chosen > 0 && f.Used == 0)
                ? r.FundamentalsSince is { } since
                    ? Loc.F("Fundamental factors have too little EPS data yet (end-of-day snapshots since {0}); their weight goes to the other factors until there is enough.", Fmt.Date(since))
                    : Loc.T("Fundamental factors need the EPS of end-of-day market-watch snapshots, which this store does not have yet; their weight goes to the other factors.")
                : null;
    }

    private void BuildKpis(SimulationResultDto r, SimulationSpecDto spec)
    {
        var s = r.Stats;
        Kpis =
        [
            new(Loc.T("Value (million toman)"), Fmt.MillionToman(s.Equity), Change: Fmt.FractionChange(s.DayReturn), ChangeTone: Fmt.ToneOf(s.DayReturn * 100),
                Note: Loc.F("Last session · started with {0}", Fmt.MillionToman(spec.Capital)), Spark: [.. r.Equity.TakeLast(60).Select(p => (double)p.Equity)]),
            new(Loc.T("Return"), Fmt.FractionChange(s.TotalReturn), Fmt.ToneOf(s.TotalReturn * 100),
                Note: s.AnnualizedReturn is { } a ? Loc.F("{0} a year", Fmt.FractionChange(a)) : Loc.F("{0} sessions", Fmt.Number(s.Sessions))),
            new(Loc.T("Against TEDPIX"), Fmt.FractionChange(s.ExcessReturn), Fmt.ToneOf(s.ExcessReturn * 100), Note: Loc.F("TEDPIX {0}", Fmt.FractionChange(s.TedpixReturn))),
            new(Loc.T("In dollars"), Fmt.FractionChange(s.UsdReturn), Fmt.ToneOf(s.UsdReturn * 100), Note: Loc.F("Average share {0}", Fmt.FractionChange(s.EqualWeightReturn))),
            new(Loc.T("Deepest fall"), Fmt.FractionChange(s.MaxDrawdown), s.MaxDrawdown < -0.0001 ? Tone.Down : Tone.Neutral, Note: Loc.F("Now {0} from the peak", Fmt.FractionChange(s.CurrentDrawdown))),
            new(Loc.T("Return per risk (Sharpe)"), s.Sharpe is { } sharpe ? Fmt.Num(sharpe, "N2") : Fmt.Dash, Note: Loc.F("Volatility {0} a year", Fmt.Percent(s.Volatility, 0))),
            new(Loc.T("Winning trades"), Fmt.Percent(s.WinRate, 0), Note: Loc.F("{0} closed · profit factor {1}", Fmt.Number(s.ClosedTrades), s.ProfitFactor is { } pf ? Fmt.Num(pf, "N2") : Fmt.Dash)),
            new(Loc.T("Invested"), Fmt.Percent(s.Equity > 0 ? (double)(s.Invested / s.Equity) : null, 0),
                Note: Loc.F("{0} positions · cash {1} million toman", Fmt.Number(r.Holdings.Count), Fmt.MillionToman(s.Cash))),
        ];
    }

    /// <summary>The portfolio, TEDPIX and the average share indexed to 100 at the start (in rials or in dollars), and the drawdown.</summary>
    private void BuildCharts()
    {
        if (Result is not { Equity.Count: > 0 } r)
        {
            ChartDates = [];
            EquitySeries = DrawdownSeries = [];
            ChartChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var points = r.Equity;
        var dollars = Denomination == Dollar;
        double[] Indexed(Func<SimulationPointDto, double?> value)
        {
            double? Of(SimulationPointDto p) => value(p) is { } v && (!dollars || p.Usd is > 0) ? dollars ? v / p.Usd!.Value : v : null;
            var first = points.Select(Of).FirstOrDefault(v => v is > 0);
            return [.. points.Select(p => first is { } f && Of(p) is { } v ? v / f * 100 : double.NaN)];
        }

        ChartDates = [.. points.Select(p => p.Date)];
        EquitySeries =
        [
            new(Saved?.Spec.Name ?? Loc.T("Simulation"), Indexed(p => (double)p.Equity), Palette.Series(0), Emphasis: true),
            new(Loc.T("TEDPIX"), Indexed(p => p.Tedpix), Palette.Series(1)),
            new(Loc.T("Average share"), Indexed(p => p.EqualWeight), Palette.Series(2)),
        ];

        var peak = 0m;
        DrawdownSeries =
        [
            new(Loc.T("Fall from the peak"), [.. points.Select(p =>
            {
                peak = Math.Max(peak, p.Equity);
                return peak > 0 ? ((double)(p.Equity / peak) - 1) * 100 : 0;
            })], Palette.Down, Emphasis: true, Fill: true),
        ];
        ChartChanged?.Invoke(this, EventArgs.Empty);
    }

    private static List<StatLine> BuildStats(SimulationResultDto r, SimulationSpecDto spec)
    {
        var s = r.Stats;
        string returns = Loc.T("Returns"), risk = Loc.T("Risk"), trading = Loc.T("Trading");
        StatLine Change(string group, string label, double? value, string key) => new(group, label, Fmt.FractionChange(value), Fmt.ToneOf(value * 100), key, Controls.Col.Inv(value));
        StatLine Ratio(string group, string label, double? value, string key, string format = "N2") => new(group, label, value is { } v ? Fmt.Num(v, format) : Fmt.Dash, Key: key, Raw: Controls.Col.Inv(value));
        StatLine Share(string group, string label, double? value, string key) => new(group, label, Fmt.Percent(value, 1), Key: key, Raw: Controls.Col.Inv(value));
        StatLine Money(string group, string label, decimal value, string key, bool signed = false) =>
            new(group, label, signed ? Fmt.Signed(value, x => x / Fmt.RialsPerMillionToman, "N0") : Fmt.MillionToman(value), signed ? Fmt.ToneOf(value) : Tone.Neutral, key, Controls.Col.Inv(value));

        return
        [
            Change(returns, Loc.T("Total return"), s.TotalReturn, "total_return"),
            Change(returns, Loc.T("Per year"), s.AnnualizedReturn, "annualized_return"),
            Change(returns, Loc.T("Last session"), s.DayReturn, "day_return"),
            Change(returns, Loc.T("Last 5 sessions"), s.WeekReturn, "week_return"),
            Change(returns, Loc.T("Last 20 sessions"), s.MonthReturn, "month_return"),
            Change(returns, Loc.T("TEDPIX over the same sessions"), s.TedpixReturn, "tedpix_return"),
            Change(returns, Loc.T("Against TEDPIX"), s.ExcessReturn, "excess_return"),
            Change(returns, Loc.T("Average share over the same sessions"), s.EqualWeightReturn, "equal_weight_return"),
            Change(returns, Loc.T("In dollars"), s.UsdReturn, "usd_return"),
            Share(risk, Loc.T("Volatility, yearly"), s.Volatility, "volatility"),
            Ratio(risk, Loc.F("Sharpe (over {0} cash)", Fmt.Percent(spec.Revisions.LastOrDefault()?.Settings.CashYield, 0)), s.Sharpe, "sharpe"),
            Ratio(risk, Loc.T("Sortino"), s.Sortino, "sortino"),
            Change(risk, Loc.T("Deepest fall"), s.MaxDrawdown, "max_drawdown"),
            Change(risk, Loc.T("Fall from the peak now"), s.CurrentDrawdown, "current_drawdown"),
            Ratio(risk, Loc.T("Yearly return ÷ deepest fall (Calmar)"), s.Calmar, "calmar"),
            Ratio(risk, Loc.T("Beta to TEDPIX"), s.Beta, "beta"),
            Change(risk, Loc.T("Alpha against TEDPIX, yearly"), s.Alpha, "alpha"),
            Ratio(risk, Loc.T("Correlation with TEDPIX"), s.Correlation, "correlation"),
            Change(risk, Loc.T("Best session"), s.BestDay, "best_day"),
            Change(risk, Loc.T("Worst session"), s.WorstDay, "worst_day"),
            new(trading, Loc.T("Trades"), Fmt.Number(s.Trades), Key: "trades", Raw: Controls.Col.Inv(s.Trades)),
            new(trading, Loc.T("Closed trades"), Fmt.Number(s.ClosedTrades), Key: "closed_trades", Raw: Controls.Col.Inv(s.ClosedTrades)),
            Share(trading, Loc.T("Winning trades"), s.WinRate, "win_rate"),
            Ratio(trading, Loc.T("Profit factor (gains ÷ losses)"), s.ProfitFactor, "profit_factor"),
            Change(trading, Loc.T("Average winning trade"), s.AverageWin, "average_win"),
            Change(trading, Loc.T("Average losing trade"), s.AverageLoss, "average_loss"),
            Ratio(trading, Loc.T("Average holding (sessions)"), s.AverageHoldSessions, "average_hold_sessions", "N0"),
            Ratio(trading, Loc.T("Turnover a year (× the portfolio)"), s.Turnover, "turnover", "N1"),
            Money(trading, Loc.T("Costs paid (million toman)"), s.CostsPaid, "costs_rial"),
            Share(trading, Loc.T("Costs as a share of the capital"), spec.Capital > 0 ? (double)(s.CostsPaid / spec.Capital) : null, "cost_share"),
            Money(trading, Loc.T("Realized profit (million toman)"), s.RealizedPnl, "realized_rial", signed: true),
            Money(trading, Loc.T("Unrealized profit (million toman)"), s.UnrealizedPnl, "unrealized_rial", signed: true),
            Share(trading, Loc.T("Average share invested"), s.Exposure, "exposure"),
        ];
    }

    /// <summary>Month-end to month-end returns by Jalali month (the first month from the starting capital).</summary>
    private static List<MonthRow> BuildMonths(SimulationResultDto r, decimal capital)
    {
        var jalali = new PersianCalendar();
        var ends = r.Equity
            .GroupBy(p => (Year: jalali.GetYear(p.Date.ToDateTime(TimeOnly.MinValue)), Month: jalali.GetMonth(p.Date.ToDateTime(TimeOnly.MinValue))))
            .Select(g => (g.Key.Year, g.Key.Month, Equity: g.Last().Equity))
            .ToList();
        var returns = new Dictionary<(int, int), double>();
        var previous = capital;
        foreach (var (year, month, equity) in ends)
        {
            if (previous > 0)
                returns[(year, month)] = (double)(equity / previous) - 1;
            previous = equity;
        }

        return [.. ends.Select(e => e.Year).Distinct().OrderDescending().Select(year =>
        {
            var months = Enumerable.Range(1, 12).Select(m => returns.TryGetValue((year, m), out var v) ? v : (double?)null).ToList();
            var present = months.Where(m => m is not null).Select(m => m!.Value).ToList();
            return new MonthRow(year, months, present.Count > 0 ? present.Aggregate(1.0, (level, m) => level * (1 + m)) - 1 : null);
        })];
    }

    private void BuildParameters(SimulationSpecDto spec)
    {
        if (spec.Revisions.Count == 0)
        {
            Parameters = [];
            return;
        }

        var s = spec.Revisions[^1].Settings;
        string portfolio = Loc.T("Portfolio"), risk = Loc.T("Risk"), execution = Loc.T("Execution and costs"), universe = Loc.T("Universe");
        string Pct(double value) => value > 0 ? Fmt.Percent(value, 0) : Loc.T("Off");
        Parameters =
        [
            new(portfolio, Loc.T("Positions"), Fmt.Number(s.Positions)),
            new(portfolio, Loc.T("Ranking every"), Loc.F("{0} sessions", Fmt.Number(s.RebalanceSessions))),
            new(portfolio, Loc.T("Keep a holding while it stays in the top"), Fmt.Percent(s.HoldPercentile, 0)),
            new(portfolio, Loc.T("Sizing"), SimulationEditorViewModel.SizingName(s.Sizing)),
            new(portfolio, Loc.T("Largest position"), Fmt.Percent(s.MaxWeight, 0)),
            new(portfolio, Loc.T("Most positions per sector"), Fmt.Number(s.PerSector)),
            new(portfolio, Loc.T("Learns the factor weights"), s.Adaptive ? Loc.T("Yes") : Loc.T("No")),
            new(risk, Loc.T("Stop loss"), Pct(s.StopLoss)),
            new(risk, Loc.T("Trailing stop"), Pct(s.TrailingStop)),
            new(risk, Loc.T("Take profit"), Pct(s.TakeProfit)),
            new(risk, Loc.T("Longest holding"), s.MaxHoldSessions > 0 ? Loc.F("{0} sessions", Fmt.Number(s.MaxHoldSessions)) : Loc.T("Off")),
            new(risk, Loc.T("Regime filter"), s.RegimeFilter ? Loc.F("TEDPIX under its {0}-session average: invest {1}", Fmt.Number(s.RegimeSessions), Fmt.Percent(s.DefensiveExposure, 0)) : Loc.T("Off")),
            new(risk, Loc.T("Volatility target"), Pct(s.TargetVolatility)),
            new(universe, Loc.T("Least value traded per session"), Loc.F("{0} billion toman", Fmt.BillionToman(s.MinValue))),
            new(universe, Loc.T("Excluded shares"), Fmt.Number(s.Excluded.Count)),
            new(execution, Loc.T("Fills at"), s.Fill == "NextOpen" ? Loc.T("The next session's open") : Loc.T("The next session's close")),
            new(execution, Loc.T("Slippage"), Loc.F("{0} basis points", Fmt.Num(s.SlippageBps, "N0"))),
            new(execution, Loc.T("At most of a session's value"), Pct(s.ParticipationCap)),
            new(execution, Loc.T("Sale proceeds"), s.InstantSaleProceeds ? Loc.T("Usable at once (broker buying power)") : Loc.T("Usable after settlement (T+2)")),
            new(execution, Loc.T("Idle cash earns"), Loc.F("{0} a year", Fmt.Percent(s.CashYield, 0))),
            .. spec.Revisions.Count > 1
                ? spec.Revisions.Select((rev, i) => new StatLine(Loc.T("Parameter history"), Fmt.Date(rev.From), i == 0 ? Loc.T("At the start") : Loc.T("Changed")))
                : Array.Empty<StatLine>(),
        ];
    }

    // ---------------------------------------------------------------- order ticket (manual mode)

    [ObservableProperty]
    public partial IReadOnlyList<InstrumentSearchResultDto> TicketMatches { get; set; } = [];

    [ObservableProperty]
    public partial InstrumentSearchResultDto? TicketInstrument { get; set; }

    [ObservableProperty]
    public partial string TicketSide { get; set; } = "Buy";

    /// <summary>value (million toman), shares, or all (a sell of the whole position).</summary>
    [ObservableProperty]
    public partial string TicketUnit { get; set; } = "value";

    [ObservableProperty]
    public partial double TicketAmount { get; set; } = 100;

    [ObservableProperty]
    public partial double TicketLimit { get; set; } = double.NaN;

    [ObservableProperty]
    public partial double TicketGoodFor { get; set; } = 5;

    [ObservableProperty]
    public partial string? TicketMessage { get; set; }

    public static IReadOnlyList<Choice> Sides => [new("Buy", Loc.T("Buy")), new("Sell", Loc.T("Sell"))];

    public static IReadOnlyList<Choice> Units(string side) => side == "Sell"
        ? [new("all", Loc.T("Everything")), new("shares", Loc.T("Shares"))]
        : [new("value", Loc.T("Amount (million toman)")), new("shares", Loc.T("Shares"))];

    public async Task SearchAsync(string text)
    {
        searching?.Cancel();
        var cts = searching = new CancellationTokenSource();
        if (string.IsNullOrWhiteSpace(text))
        {
            TicketMatches = [];
            return;
        }

        try
        {
            await Task.Delay(150, cts.Token);
            var results = await Api.SearchAsync(text.Trim(), 10, cts.Token);
            TicketMatches = [.. results.Where(r => r.ParentInsCode is null && SimulationText.IsSimulated(r.InstrumentClass))];
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Logger.LogInformation("Symbol search failed: {Message}", e.Message);
        }
    }

    /// <summary>A suggestion or a holding fills the ticket; the user checks it and places it.</summary>
    public void UseSuggestion(SimulationOrderPlanDto plan)
    {
        TicketInstrument = new InstrumentSearchResultDto(plan.InsCode, plan.Symbol, plan.Name, string.Empty, null);
        TicketSide = plan.Side;
        if (plan.Side == "Sell")
        {
            var held = Holdings.FirstOrDefault(h => h.InsCode == plan.InsCode)?.Shares ?? 0;
            TicketUnit = plan.Shares >= held ? "all" : "shares";
            TicketAmount = (double)plan.Shares;
        }
        else
        {
            TicketUnit = "value";
            TicketAmount = Math.Round((double)(plan.Value / Fmt.RialsPerMillionToman));
        }

        TicketMessage = null;
    }

    public void SellHolding(SimulationHoldingDto holding)
    {
        TicketInstrument = new InstrumentSearchResultDto(holding.InsCode, holding.Symbol, holding.Name, string.Empty, null);
        TicketSide = "Sell";
        TicketUnit = "all";
        TicketAmount = (double)holding.Shares;
        TicketMessage = null;
    }

    public async Task<bool> PlaceOrderAsync()
    {
        if (Saved is not { } saved || TicketInstrument is not { } instrument)
        {
            TicketMessage = Loc.T("Choose a symbol first.");
            return false;
        }

        var amount = double.IsFinite(TicketAmount) ? (decimal)TicketAmount : 0;
        if (TicketUnit != "all" && amount <= 0)
        {
            TicketMessage = Loc.T("Enter an amount greater than zero.");
            return false;
        }

        if (TicketSide == "Sell" && Holdings.All(h => h.InsCode != instrument.InsCode) && saved.Spec.Orders.All(o => o.InsCode != instrument.InsCode || o.Side != "Buy"))
        {
            TicketMessage = Loc.F("The simulation does not hold {0}.", instrument.Symbol);
            return false;
        }

        // Decided with the data up to the latest session: it can fill from the next session on.
        var order = new ManualOrderDto(
            SimulationEditorViewModel.NewId(),
            Result?.Latest ?? DateOnly.FromDateTime(DateTime.Today),
            instrument.InsCode,
            instrument.Symbol,
            TicketSide,
            TicketSide == "Buy" && TicketUnit == "value" ? amount * Fmt.RialsPerMillionToman : null,
            TicketUnit == "shares" ? Math.Floor(amount) : null,
            double.IsFinite(TicketLimit) && TicketLimit > 0 ? (decimal)TicketLimit : null,
            Math.Clamp((int)(double.IsFinite(TicketGoodFor) ? TicketGoodFor : 5), 1, 60),
            null,
            DateTimeOffset.Now);
        tracker.Update(saved, saved.Spec with { Orders = [.. saved.Spec.Orders, order] });
        TicketMessage = Loc.F("{0} {1} placed: it fills at the next session if the share trades and is not queued against you.", SimulationText.Side(order.Side), order.Symbol);
        ResetTicket(keepMessage: true);
        await LoadAsync(saved.Spec.Id);
        return true;
    }

    /// <summary>Cancels an order from the latest session on: it no longer fills on the sessions after it.</summary>
    public async Task CancelOrderAsync(OrderLine line)
    {
        if (Saved is not { } saved || !line.CanCancel)
            return;
        var cancelled = line.Order with { CancelledOn = Result?.Latest ?? DateOnly.FromDateTime(DateTime.Today) };
        tracker.Update(saved, saved.Spec with { Orders = [.. saved.Spec.Orders.Select(o => o.Id == cancelled.Id ? cancelled : o)] });
        await LoadAsync(saved.Spec.Id);
    }

    /// <summary>Stops following the market after the latest session, or follows it again (the sessions since are then replayed).</summary>
    public async Task SetTrackingAsync(bool tracking)
    {
        if (Saved is not { } saved)
            return;
        tracker.Update(saved, saved.Spec with { End = tracking ? null : Result?.AsOf ?? Result?.Latest ?? DateOnly.FromDateTime(DateTime.Today) });
        await LoadAsync(saved.Spec.Id);
    }

    private void ResetTicket(bool keepMessage = false)
    {
        TicketInstrument = null;
        TicketMatches = [];
        TicketSide = "Buy";
        TicketUnit = "value";
        TicketAmount = 100;
        TicketLimit = double.NaN;
        TicketGoodFor = 5;
        if (!keepMessage)
            TicketMessage = null;
    }
}
