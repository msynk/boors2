using Boors.Market.Rules;
using Boors.Market.Simulation;
using Boors.Market.Storage;

namespace Boors.Market.Tests;

/// <summary>The simulation engine over hand-built sessions whose fills, costs and decisions are known.</summary>
[TestClass]
public sealed class SimulationTests
{
    // Per side, from rules.yaml: exchange 0.155% + CSD 0.1% + technology 0.065% + list brokerage 0.144%; sells add 0.5% tax.
    private const decimal BuyRate = 0.00464m;
    private const decimal SellRate = 0.00964m;

    private static readonly SimulationSettings Plain = new()
    {
        SlippageBps = 0,
        ParticipationCap = 0,
        CashYield = 0,
        CashReserve = 0,
        RegimeFilter = false,
        MinHistorySessions = 5,
        MinValue = 0,
        StopLoss = 0,
        TrailingStop = 0,
        MinHoldSessions = 0,
        Weights = new Dictionary<FactorKind, double> { [FactorKind.Breakout] = 1 },
        Fill = FillPrice.NextClose,
        InstantSaleProceeds = false,
    };

    private static readonly SimulationSettings AtOnce = Plain with { Fill = FillPrice.Immediate, InstantSaleProceeds = true };

    private MarketStore store = null!;
    private MarketSimulator simulator = null!;
    private List<DateOnly> sessions = null!;

    [TestInitialize]
    public void Initialize()
    {
        store = MarketStore.OpenInMemory();
        simulator = new MarketSimulator(store, MarketRules.LoadDefault());
        sessions = Weekdays(new DateOnly(2025, 1, 6), 80);
    }

    [TestCleanup]
    public void Cleanup() => store.Dispose();

    [TestMethod]
    public void A_manual_order_fills_on_the_next_session_with_costs_and_sale_proceeds_settle_two_sessions_later()
    {
        AddEquities(1);
        AddPath(1, _ => 0m);
        var result = Run(Manual(
            Order("buy", 1, OrderSide.Buy, sessions[5], value: 50_000_000),
            Order("sell", 1, OrderSide.Sell, sessions[10])), end: sessions[14]);

        var buy = result.Trades[0];
        Assert.AreEqual(sessions[6], buy.Date);
        Assert.AreEqual(5_000m, buy.Shares);
        Assert.AreEqual(10_000m, buy.Price);
        Assert.AreEqual(50_000_000m * BuyRate, buy.Costs);

        var sell = result.Trades[1];
        Assert.AreEqual(sessions[11], sell.Date);
        Assert.AreEqual(50_000_000m * SellRate, sell.Costs);
        Assert.AreEqual(50_000_000m * (1 - SellRate) - 50_000_000m * (1 + BuyRate), sell.RealizedPnl);

        // Proceeds count in equity at once but are cash to spend only from T+2.
        var after = result.Equity.Single(p => p.Date == sessions[11]);
        Assert.AreEqual(100_000_000m - 50_000_000m * (BuyRate + SellRate), after.Equity);
        Assert.AreEqual(0m, after.Invested);
        Assert.IsEmpty(result.Holdings);
        Assert.AreSequenceEqual(new[] { OrderStates.Filled, OrderStates.Filled }, result.Orders.Select(o => o.State).ToArray());
    }

    [TestMethod]
    public void A_buy_waits_out_a_buy_queue_and_spends_sale_proceeds_only_once_settled()
    {
        AddEquities(1);
        AddPath(1, _ => 0m, statusOn: i => i == 6 ? DayStatus.QueueLockedUp : DayStatus.Traded);
        var result = Run(Manual(
            Order("first", 1, OrderSide.Buy, sessions[5], shares: 9_900),
            Order("out", 1, OrderSide.Sell, sessions[10]),
            Order("again", 1, OrderSide.Buy, sessions[11], shares: 9_000)), end: sessions[16]);

        Assert.AreEqual(sessions[7], result.Trades[0].Date, "no buy fills on a day locked in a buy queue");
        Assert.Contains(j => j.Kind == SimulationEvents.BlockedBuyQueue && j.Date == sessions[6], result.Journal);
        Assert.AreEqual(sessions[11], result.Trades[1].Date);
        Assert.AreEqual(sessions[13], result.Trades[2].Date, "the sale settles on T+2");
        Assert.AreEqual(9_000m, result.Trades[2].Shares);

        var instant = Run(Manual(
            Order("first", 1, OrderSide.Buy, sessions[5], shares: 9_900),
            Order("out", 1, OrderSide.Sell, sessions[10]),
            Order("again", 1, OrderSide.Buy, sessions[11], shares: 9_000)) with { Revisions = [new(sessions[0], Plain with { InstantSaleProceeds = true })] }, end: sessions[16]);
        Assert.AreEqual(sessions[12], instant.Trades[2].Date, "broker buying power spends the proceeds at once");
    }

    [TestMethod]
    public void A_capital_increase_adds_shares_and_keeps_the_value()
    {
        AddEquities(1);
        AddPath(1, _ => 0m, yesterdayOn: (i, previous) => i == 8 ? previous / 2 : previous);
        var result = Run(Manual(Order("buy", 1, OrderSide.Buy, sessions[5], shares: 1_000)), end: sessions[10]);

        var holding = result.Holdings.Single();
        Assert.AreEqual(2_000m, holding.Shares);
        Assert.AreEqual(5_000m, holding.LastPrice);
        Assert.AreEqual(10_000_000m, holding.Value);
        Assert.Contains(j => j.Kind == SimulationEvents.CorporateAction && j.Date == sessions[8], result.Journal);
    }

    [TestMethod]
    public void The_strategy_buys_the_best_ranked_share_at_the_next_session_and_plans_orders_for_the_session_after_the_latest()
    {
        AddEquities(1, 2);
        AddPath(1, _ => 0.01m);
        AddPath(2, _ => -0.01m);

        // Decided at the close of session 60 (data up to it), filled at the close of session 61 — never at 60's close.
        var result = Run(Automatic(sessions[60], Plain with { Positions = 1, RebalanceSessions = 100 }));
        var buy = result.Trades.First();
        Assert.AreEqual(1L, buy.InsCode);
        Assert.AreEqual(sessions[61], buy.Date);
        Assert.AreEqual(Close(1, 61), buy.Price);
        Assert.AreEqual(SimulationEvents.Entry, buy.Reason);
        Assert.AreEqual(1L, result.Holdings.Single().InsCode);

        // Started on the latest session: nothing has traded yet, the buy waits for the next session.
        var latest = Run(Automatic(sessions[^1], Plain with { Positions = 1 }));
        Assert.IsEmpty(latest.Trades);
        Assert.AreEqual(1L, latest.Planned.Single().InsCode);
        Assert.AreEqual(OrderSide.Buy, latest.Planned.Single().Side);
    }

    [TestMethod]
    public void A_stop_loss_sells_on_the_next_session_and_the_share_is_not_bought_back_during_the_cooldown()
    {
        AddEquities(1);
        AddPath(1, i => i <= 62 ? 0.002m : -0.03m);
        var result = Run(Automatic(sessions[60], Plain with { Positions = 1, RebalanceSessions = 2, StopLoss = 0.10, CooldownSessions = 100 }), end: sessions[75]);

        var sell = result.Trades.Single(t => t.Side == OrderSide.Sell);
        Assert.AreEqual(SimulationEvents.StopLoss, sell.Reason);
        var entry = result.Trades.First().Price;
        var trigger = Enumerable.Range(61, 20).First(i => Close(1, i) <= entry * 0.9m);
        Assert.AreEqual(sessions[trigger + 1], sell.Date);
        Assert.ContainsSingle(t => t.Side == OrderSide.Buy && t.Reason == SimulationEvents.Entry, result.Trades);
    }

    [TestMethod]
    public void A_new_set_of_parameters_applies_from_its_date_without_rewriting_the_past()
    {
        AddEquities(1, 2, 3);
        AddPath(1, _ => 0.012m);
        AddPath(2, _ => 0.008m);
        AddPath(3, _ => -0.005m);
        var spec = Automatic(sessions[60], Plain with { Positions = 1, RebalanceSessions = 5 }) with
        {
            Revisions = [new(sessions[60], Plain with { Positions = 1, RebalanceSessions = 5 }), new(sessions[70], Plain with { Positions = 2, RebalanceSessions = 5 })],
        };
        var result = Run(spec);
        var before = Run(Automatic(sessions[60], Plain with { Positions = 1, RebalanceSessions = 5 }));

        Assert.HasCount(2, result.Holdings);
        Assert.Contains(j => j.Kind == SimulationEvents.SettingsChanged && j.Date == sessions[70], result.Journal);
        Assert.IsTrue(result.Trades.Where(t => t.InsCode == 2).All(t => t.Date > sessions[70]));
        // The revision decides from the close of session 70 on; that session itself still filled under the old parameters.
        var past = result.Equity.TakeWhile(p => p.Date <= sessions[70]).ToList();
        Assert.AreSequenceEqual(before.Equity.Take(past.Count).ToList(), past, "sessions up to the revision are unchanged");
    }

    [TestMethod]
    public void A_factor_without_data_gives_its_weight_to_the_others_and_replays_are_deterministic()
    {
        AddEquities(1, 2);
        AddPath(1, _ => 0.01m);
        AddPath(2, _ => -0.01m);
        var spec = Automatic(sessions[60], Plain with { Positions = 1, Weights = new Dictionary<FactorKind, double> { [FactorKind.EarningsYield] = 3, [FactorKind.Breakout] = 1 } });
        var result = Run(spec);

        var eps = result.Factors.Single(f => f.Factor == FactorKind.EarningsYield);
        Assert.AreEqual(0.75, eps.Chosen, 1e-12);
        Assert.AreEqual(0, eps.Used);
        Assert.AreEqual(0, eps.Coverage);
        Assert.AreEqual(1, result.Factors.Single(f => f.Factor == FactorKind.Breakout).Used, 1e-12);
        Assert.AreEqual(1L, result.Holdings.Single().InsCode);
        Assert.AreSequenceEqual(result.Equity.ToList(), Run(spec).Equity.ToList());

        // An excluded share is never bought; the next best one is.
        var excluded = Run(spec with { Revisions = [new(sessions[60], spec.Revisions[0].Settings with { Excluded = new HashSet<long> { 1 } })] });
        Assert.AreEqual(2L, excluded.Holdings.Single().InsCode);
        Assert.DoesNotContain(t => t.InsCode == 1, excluded.Trades);
    }

    [TestMethod]
    public void An_order_cancelled_before_its_session_never_fills_and_an_expired_one_says_so()
    {
        // Share 2 keeps trading, so the days share 1 is halted are still market sessions.
        AddEquities(1, 2);
        AddPath(1, _ => 0m, statusOn: i => i is >= 6 and <= 20 ? DayStatus.SymbolHalted : DayStatus.Traded);
        AddPath(2, _ => 0m);
        var result = Run(Manual(
            Order("cancelled", 1, OrderSide.Buy, sessions[2], value: 1_000_000) with { CancelledOn = sessions[2] },
            Order("expired", 1, OrderSide.Buy, sessions[5], value: 1_000_000) with { GoodForSessions = 3 },
            Order("waiting", 1, OrderSide.Buy, sessions[^1], value: 1_000_000)));

        var states = result.Orders.ToDictionary(o => o.OrderId, o => o.State);
        Assert.AreEqual(OrderStates.Cancelled, states["cancelled"]);
        Assert.AreEqual(OrderStates.Expired, states["expired"]);
        Assert.AreEqual(OrderStates.Pending, states["waiting"]);
        Assert.IsEmpty(result.Trades);
        Assert.AreEqual("waiting", result.Planned.Single().OrderId);
    }

    [TestMethod]
    public void Tuning_ranks_parameter_sets_in_sample_and_measures_them_out_of_sample()
    {
        sessions = Weekdays(new DateOnly(2024, 1, 1), 560);
        AddEquities(1, 2, 3, 4);
        AddPath(1, i => i % 7 == 0 ? -0.02m : 0.006m);
        AddPath(2, i => i % 5 == 0 ? 0.02m : -0.003m);
        AddPath(3, _ => 0.001m);
        AddPath(4, i => i % 2 == 0 ? 0.01m : -0.009m);

        var spec = Automatic(sessions[^1], Plain);
        var tuning = simulator.Tune(spec, years: 1, TuningObjective.Return);

        Assert.HasCount(54, tuning.Rows);
        Assert.IsTrue(tuning.From < tuning.Split && tuning.Split < tuning.To);
        Assert.AreSequenceEqual(tuning.Rows.OrderByDescending(r => r.Objective).ToList(), tuning.Rows.ToList());
    }

    [TestMethod]
    public void A_stop_takes_away_the_strategy_s_waiting_buy_of_the_share()
    {
        // A thin share: the entry fills over sessions at half of each session's value, and the price drops 10% meanwhile.
        AddEquities(1);
        AddPath(1, i => i == 63 ? -0.10m : 0.001m, value: 20_000_000);
        var result = Run(Automatic(sessions[60], Plain with { Positions = 1, MaxWeight = 1, RebalanceSessions = 100, ParticipationCap = 0.5, StopLoss = 0.05, CooldownSessions = 100 }), end: sessions[75]);

        var stop = result.Trades.First(t => t.Reason == SimulationEvents.StopLoss);
        Assert.AreEqual(sessions[64], stop.Date);
        Assert.DoesNotContain(t => t.Side == OrderSide.Buy && t.Date >= stop.Date, result.Trades);
        Assert.IsEmpty(result.Holdings);
    }

    [TestMethod]
    public void Results_do_not_depend_on_which_panel_the_simulator_has_cached()
    {
        // 32 shares with deterministic noise, so rank correlations exist (the adaptive weights need 30 shares a formation).
        sessions = Weekdays(new DateOnly(2023, 1, 2), 760);
        var codes = Enumerable.Range(1, 32).Select(i => (long)i).ToArray();
        AddEquities(codes);
        foreach (var ins in codes)
            AddPath(ins, i => ((ins * 7919 + i * 104729 + i * i * ins) % 41 - 20) / 1000m);

        var settings = Plain with
        {
            Adaptive = true,
            AdaptiveLookbackSessions = 240,
            AdaptiveShrink = 0,
            Weights = new Dictionary<FactorKind, double> { [FactorKind.Breakout] = 1, [FactorKind.ShortReversal] = 1, [FactorKind.LowVolatility] = 1 },
            MinHistorySessions = 650,
            Positions = 3,
        };
        var spec = Automatic(sessions[700], settings);
        var first = Run(spec);

        // A start three sessions earlier makes the simulator reload its panel from earlier; the first spec then replays on it.
        Run(Automatic(sessions[697], settings));
        var again = Run(spec);

        Assert.IsNotEmpty(first.Trades, "history counts from each listing, not from the panel's first session (580 sessions before)");
        Assert.Contains(f => f.Months > 0, first.Factors);
        Assert.AreSequenceEqual(first.Equity.ToList(), again.Equity.ToList());
        Assert.AreSequenceEqual(first.Factors.ToList(), again.Factors.ToList());
    }

    [TestMethod]
    public void A_fill_stays_inside_the_session_s_range()
    {
        // Session 6 traded only at 10,300 while its official close says 10,000 (a base-volume close before 2025-12-22).
        AddEquities(1);
        AddPath(1, _ => 0m, shape: (i, bar) => i == 6 ? bar with { Open = 10_300, High = 10_300, Low = 10_300 } : bar);
        var result = Run(Manual(Order("buy", 1, OrderSide.Buy, sessions[5], shares: 100)), end: sessions[8]);
        Assert.AreEqual(10_300m, result.Trades.Single().Price);
    }

    [TestMethod]
    public void The_strategy_buys_from_the_top_of_the_ranking_but_never_below_the_hold_limit()
    {
        // Shares 1–5 in one sector, 6–10 in another, ranked 1 to 10; one share per sector, three positions, hold limit rank 3.
        AddEquities(ins => ins <= 5 ? "27" : "44", 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        foreach (var ins in Enumerable.Range(1, 10))
            AddPath(ins, _ => (11 - ins) / 1000m);
        var result = Run(Automatic(sessions[60], Plain with { Positions = 3, PerSector = 1, HoldPercentile = 0.3 }));

        // Rank 1 fills its sector; ranks 2 and 3 share it, and rank 6 (the other sector) lies below the hold limit.
        Assert.AreEqual(1L, result.Holdings.Single().InsCode);
    }

    [TestMethod]
    public void A_manual_sell_placed_with_its_buy_waits_for_the_buy_and_a_fund_order_is_refused()
    {
        AddEquities(1);
        AddPath(1, _ => 0m);
        var result = Run(Manual(
            Order("buy", 1, OrderSide.Buy, sessions[5], shares: 100),
            Order("sell", 1, OrderSide.Sell, sessions[5]),
            Order("fund", 999, OrderSide.Buy, sessions[5], value: 1_000_000)), end: sessions[10]);

        Assert.AreSequenceEqual(new[] { (OrderSide.Buy, sessions[6]), (OrderSide.Sell, sessions[7]) }, result.Trades.Select(t => (t.Side, t.Date)).ToArray());
        var states = result.Orders.ToDictionary(o => o.OrderId);
        Assert.AreEqual(OrderStates.Filled, states["sell"].State);
        Assert.AreEqual(OrderStates.Rejected, states["fund"].State);
        Assert.AreEqual(SimulationEvents.NotTradable, states["fund"].Reason);
    }

    [TestMethod]
    public void A_waiting_order_follows_a_capital_increase()
    {
        // 1,000 shares held; a sell of 1,000 waits through a 100% capital increase and sells the 2,000 they became.
        AddEquities(1);
        AddPath(1, _ => 0m, yesterdayOn: (i, previous) => i == 8 ? previous / 2 : previous, statusOn: i => i == 7 ? DayStatus.SymbolHalted : DayStatus.Traded);
        var result = Run(Manual(
            Order("buy", 1, OrderSide.Buy, sessions[4], shares: 1_000),
            Order("sell", 1, OrderSide.Sell, sessions[6], shares: 1_000)), end: sessions[10]);

        Assert.AreEqual(2_000m, result.Trades.Single(t => t.Side == OrderSide.Sell).Shares);
        Assert.IsEmpty(result.Holdings);
    }

    [TestMethod]
    public void Done_at_once_a_manual_order_fills_at_the_close_it_was_placed_on_and_the_value_carries_its_costs()
    {
        AddEquities(1);
        AddPath(1, _ => 0m, shape: (_, bar) => bar with { Open = 9_900, High = 10_100, Low = 9_900 });
        var result = Run(ManualAtOnce(
            Order("buy", 1, OrderSide.Buy, sessions[5], value: 50_000_000),
            Order("sell", 1, OrderSide.Sell, sessions[10])), end: sessions[14]);

        var buy = result.Trades[0];
        Assert.AreEqual(sessions[5], buy.Date);
        Assert.AreEqual(10_000m, buy.Price);
        Assert.AreEqual(sessions[10], result.Trades[1].Date);

        // One point a session, each after the fills of its own close.
        Assert.AreSequenceEqual(sessions.Take(15).ToArray(), result.Equity.Select(p => p.Date).ToArray());
        Assert.AreEqual(100_000_000m - 50_000_000m * BuyRate, result.Equity[5].Equity);
        Assert.AreEqual(50_000_000m, result.Equity[5].Invested);
        Assert.AreEqual(100_000_000m - 50_000_000m * (BuyRate + SellRate), result.Equity[10].Equity);
        Assert.AreEqual(0m, result.Equity[10].Invested);
        Assert.AreSequenceEqual(new[] { OrderStates.Filled, OrderStates.Filled }, result.Orders.Select(o => o.State).ToArray());
    }

    [TestMethod]
    public void A_strategy_started_on_the_latest_session_holds_its_shares_at_once_with_nothing_waiting()
    {
        AddEquities(1, 2);
        AddPath(1, _ => 0.01m);
        AddPath(2, _ => -0.01m);
        var result = Run(Automatic(sessions[^1], AtOnce with { Positions = 1 }));

        var buy = result.Trades.Single();
        Assert.AreEqual(1L, buy.InsCode);
        Assert.AreEqual(sessions[^1], buy.Date);
        Assert.AreEqual(Close(1, sessions.Count - 1), buy.Price);
        Assert.AreEqual(1L, result.Holdings.Single().InsCode);
        Assert.IsEmpty(result.Planned);
    }

    [TestMethod]
    public void A_buy_at_a_close_that_ended_in_a_queue_waits_through_a_locked_day_and_fills_when_it_can()
    {
        // Session 5 trades freely but ends at the ceiling, session 6 is locked there all day, session 7 is free again.
        AddEquities(1);
        AddPath(1, i => i is 5 or 6 ? 0.03m : 0m,
            statusOn: i => i == 6 ? DayStatus.QueueLockedUp : DayStatus.Traded,
            shape: (i, bar) => i == 5 ? bar with { Low = bar.Yesterday } : bar);
        var result = Run(ManualAtOnce(Order("buy", 1, OrderSide.Buy, sessions[5], shares: 1_000)), end: sessions[10]);

        Assert.Contains(j => j.Kind == SimulationEvents.BlockedBuyQueue && j.Date == sessions[5], result.Journal);
        Assert.AreEqual(sessions[7], result.Trades.Single().Date);
        Assert.AreEqual(OrderStates.Filled, result.Orders.Single().State);

        // Stopped at that close, it is simply waiting, with the reason.
        var waiting = Run(ManualAtOnce(Order("buy", 1, OrderSide.Buy, sessions[5], shares: 1_000)), end: sessions[5]);
        Assert.IsEmpty(waiting.Trades);
        Assert.AreEqual(SimulationEvents.BlockedBuyQueue, waiting.Orders.Single().Reason);
    }

    [TestMethod]
    public void A_waiting_order_fills_on_a_day_that_traded_freely_even_if_that_day_ended_in_a_queue()
    {
        // The order met the whole of session 5, not only how it ended; the one placed at 5's close met only its end.
        AddEquities(1);
        AddPath(1, i => i is 4 or 5 ? 0.03m : 0m, shape: (i, bar) => i is 4 or 5 ? bar with { Low = bar.Yesterday } : bar);
        var result = Run(ManualAtOnce(
            Order("rests", 1, OrderSide.Buy, sessions[4], shares: 1_000),
            Order("late", 1, OrderSide.Buy, sessions[5], shares: 1_000)), end: sessions[7]);

        var fills = result.Trades.Select(t => (t.OrderId, t.Date)).ToArray();
        Assert.AreSequenceEqual(new (string?, DateOnly)[] { ("rests", sessions[5]), ("late", sessions[6]) }, fills);
    }

    [TestMethod]
    public void A_sell_at_a_close_that_ended_in_a_sell_queue_waits()
    {
        AddEquities(1);
        AddPath(1, i => i == 5 ? -0.03m : 0m, shape: (i, bar) => i == 5 ? bar with { High = bar.Yesterday } : bar);
        var result = Run(ManualAtOnce(
            Order("buy", 1, OrderSide.Buy, sessions[3], shares: 1_000),
            Order("sell", 1, OrderSide.Sell, sessions[5])), end: sessions[8]);

        Assert.Contains(j => j.Kind == SimulationEvents.BlockedSellQueue && j.Date == sessions[5], result.Journal);
        Assert.AreEqual(sessions[6], result.Trades.Single(t => t.Side == OrderSide.Sell).Date);
    }

    [TestMethod]
    public void A_stop_hit_in_a_sell_queue_sells_when_the_queue_lets_it()
    {
        AddEquities(1);
        AddPath(1, i => i is 63 or 64 ? -0.03m : i < 60 ? 0.001m : 0m, shape: (i, bar) => i is 63 or 64 ? bar with { High = bar.Yesterday } : bar);
        var result = Run(Automatic(sessions[60], AtOnce with { Positions = 1, MaxWeight = 1, RebalanceSessions = 100, StopLoss = 0.05, CooldownSessions = 100 }), end: sessions[70]);

        Assert.AreEqual(sessions[60], result.Trades[0].Date);
        var stop = result.Trades.Single(t => t.Reason == SimulationEvents.StopLoss);
        Assert.AreEqual(sessions[65], stop.Date, "the stop was hit at 64's close, which ended in a sell queue");
        Assert.IsEmpty(result.Holdings);
    }

    [TestMethod]
    public void The_strategy_passes_over_a_share_that_ended_in_a_buy_queue()
    {
        AddEquities(1, 2);
        AddPath(1, i => i == sessions.Count - 1 ? 0.03m : 0.01m, shape: (i, bar) => i == sessions.Count - 1 ? bar with { Low = bar.Yesterday } : bar);
        AddPath(2, _ => 0.005m);
        var settings = AtOnce with { Positions = 1, HoldPercentile = 1 };
        var result = Run(Automatic(sessions[^1], settings));

        Assert.AreEqual(2L, result.Holdings.Single().InsCode);
        Assert.IsEmpty(result.Planned);

        // Filled on the next session, the queue at this close does not matter.
        var later = Run(Automatic(sessions[^1], settings with { Fill = FillPrice.NextClose, InstantSaleProceeds = false }));
        Assert.AreEqual(1L, later.Planned.Single().InsCode);
    }

    [TestMethod]
    public void At_the_close_a_limit_meets_the_closing_price_and_not_a_better_price_earlier_that_day()
    {
        // 9,500 traded during session 5, which closed at 10,000; 9,400 trades during session 7.
        AddEquities(1);
        AddPath(1, _ => 0m, shape: (i, bar) => i switch { 5 => bar with { Low = 9_500 }, 7 => bar with { Low = 9_400 }, _ => bar });
        var result = Run(ManualAtOnce(
            Order("low", 1, OrderSide.Buy, sessions[5], shares: 1_000) with { LimitPrice = 9_500 },
            Order("exact", 1, OrderSide.Buy, sessions[5], shares: 1_000) with { LimitPrice = 10_000 }) with
        {
            Revisions = [new(sessions[0], AtOnce with { SlippageBps = 10 })],
        }, end: sessions[9]);

        var fills = result.Trades.ToDictionary(t => t.OrderId!);
        Assert.AreEqual(sessions[5], fills["exact"].Date);
        Assert.AreEqual(10_000m, fills["exact"].Price);
        Assert.AreEqual(sessions[7], fills["low"].Date);
        Assert.AreEqual(9_500m, fills["low"].Price);
    }

    [TestMethod]
    public void Sale_proceeds_pay_for_a_buy_at_the_same_close_unless_the_simulation_waits_for_settlement()
    {
        AddEquities(1, 2);
        AddPath(1, _ => 0m);
        AddPath(2, _ => 0m);
        var orders = new[]
        {
            Order("first", 1, OrderSide.Buy, sessions[5], shares: 9_900),
            Order("out", 1, OrderSide.Sell, sessions[10]),
            Order("swap", 2, OrderSide.Buy, sessions[10], shares: 9_000),
        };

        var instant = Run(ManualAtOnce(orders), end: sessions[14]);
        Assert.AreEqual(sessions[10], instant.Trades.Single(t => t.OrderId == "swap").Date);

        var settling = Run(ManualAtOnce(orders) with { Revisions = [new(sessions[0], AtOnce with { InstantSaleProceeds = false })] }, end: sessions[14]);
        Assert.AreEqual(sessions[12], settling.Trades.Single(t => t.OrderId == "swap").Date, "T+2");
    }

    [TestMethod]
    public void The_end_of_day_book_decides_a_queue_when_it_was_collected()
    {
        // By its last trade session 5 ended in a queue, but offers were left at the ceiling. Session 6 looks free by its
        // last trade, but a bid sat at the ceiling with no offer against it.
        AddEquities(1);
        AddPath(1, i => i == 5 ? 0.03m : 0m, shape: (i, bar) => i == 5 ? bar with { Low = bar.Yesterday } : bar);
        var atFive = ManualAtOnce(Order("five", 1, OrderSide.Buy, sessions[5], shares: 1_000));
        var atSix = ManualAtOnce(Order("six", 1, OrderSide.Buy, sessions[6], shares: 1_000));
        Assert.AreEqual(sessions[6], Run(atFive, end: sessions[8]).Trades.Single().Date, "the last trade alone says queue");
        Assert.AreEqual(sessions[6], Run(atSix, end: sessions[8]).Trades.Single().Date);

        store.UpdateSessionSnapshots(
        [
            new SessionSnapshot(1, sessions[5], null, null, 9_700, 10_300, new BestLimitLevel { Level = 1, BidPrice = 10_300, BidVolume = 5_000, AskPrice = 10_300, AskVolume = 1_000 }),
            new SessionSnapshot(1, sessions[6], null, null, 9_990, 10_610, new BestLimitLevel { Level = 1, BidPrice = 10_610, BidVolume = 5_000, AskPrice = 10_620, AskVolume = 1_000 }),
        ]);

        Assert.AreEqual(sessions[5], Run(atFive, end: sessions[8]).Trades.Single().Date, "offers at the ceiling are no queue");
        Assert.AreEqual(sessions[7], Run(atSix, end: sessions[8]).Trades.Single().Date, "an offer above the ceiling cannot trade");
    }

    [TestMethod]
    public void A_revision_to_fills_at_once_changes_the_session_it_is_dated_and_not_the_ones_before()
    {
        AddEquities(1);
        AddPath(1, _ => 0m);
        var spec = Manual(
            Order("before", 1, OrderSide.Buy, sessions[5], shares: 1_000),
            Order("after", 1, OrderSide.Buy, sessions[10], shares: 1_000));
        var result = Run(spec with { Revisions = [new(sessions[0], Plain), new(sessions[8], AtOnce)] }, end: sessions[12]);

        Assert.AreSequenceEqual(new (string?, DateOnly)[] { ("before", sessions[6]), ("after", sessions[10]) }, result.Trades.Select(t => (t.OrderId, t.Date)).ToArray());

        // And back: from the revision on, orders wait for the next session again.
        var back = Run(spec with { Revisions = [new(sessions[0], AtOnce), new(sessions[8], Plain)] }, end: sessions[12]);
        Assert.AreSequenceEqual(new (string?, DateOnly)[] { ("before", sessions[5]), ("after", sessions[11]) }, back.Trades.Select(t => (t.OrderId, t.Date)).ToArray());
    }

    [TestMethod]
    public void Comparisons_stay_the_same_whichever_way_orders_fill()
    {
        // The average share compounds once a session, though a session done at once is valued twice.
        var codes = Enumerable.Range(1, 25).Select(i => (long)i).ToArray();
        AddEquities(codes);
        foreach (var ins in codes)
            AddPath(ins, i => (ins + i) % 2 == 0 ? 0.01m : -0.005m);

        var next = Run(Automatic(sessions[30], Plain with { Positions = 3 }));
        var once = Run(Automatic(sessions[30], AtOnce with { Positions = 3 }));
        Assert.AreSequenceEqual(next.Equity.Select(p => (p.Date, p.EqualWeight)).ToArray(), once.Equity.Select(p => (p.Date, p.EqualWeight)).ToArray());
    }

    // ---------------------------------------------------------------- helpers

    private SimulationResult Run(SimulationSpec spec, DateOnly? end = null) => simulator.Run(end is null ? spec : spec with { End = end });

    private SimulationSpec Manual(params ManualOrder[] orders) => new()
    {
        Id = "manual",
        Mode = SimulationMode.Manual,
        Capital = 100_000_000,
        Start = sessions[0],
        Revisions = [new(sessions[0], Plain)],
        Orders = orders,
    };

    private SimulationSpec ManualAtOnce(params ManualOrder[] orders) => Manual(orders) with { Revisions = [new(sessions[0], AtOnce)] };

    private static SimulationSpec Automatic(DateOnly start, SimulationSettings settings) => new()
    {
        Id = "auto",
        Capital = 100_000_000,
        Start = start,
        Revisions = [new(start, settings)],
    };

    private static ManualOrder Order(string id, long ins, OrderSide side, DateOnly placedOn, decimal? value = null, decimal? shares = null) =>
        new() { Id = id, InsCode = ins, Side = side, PlacedOn = placedOn, Value = value, Shares = shares, GoodForSessions = 10 };

    private decimal Close(long ins, int session) => store.GetDailyBars(ins).Single(b => b.Bar.Date == sessions[session]).Bar.Close;

    private static List<DateOnly> Weekdays(DateOnly from, int count)
    {
        var result = new List<DateOnly>();
        for (var d = from; result.Count < count; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                result.Add(d);
        }

        return result;
    }

    private void AddEquities(params long[] codes) => AddEquities(ins => $"{20 + ins}", codes);

    private void AddEquities(Func<long, string> sector, params long[] codes) =>
        store.UpsertInstruments(
            [.. codes.Select(ins => new Instrument
            {
                InsCode = ins,
                Symbol = $"S{ins}",
                Name = $"Company {ins}",
                Isin = $"IRO1S{ins:000}0001",
                Class = InstrumentClass.TseStock,
                SectorCode = sector(ins),
                TotalShares = 1_000_000,
            })], sessions[^1], "yval");

    /// <summary>
    /// One bar per session from 10,000 rials: close = previous close × (1 + step(i)); yesterday is the previous close
    /// unless <paramref name="yesterdayOn"/> moves it (a corporate action, the close then follows the new base); every
    /// session trades <paramref name="value"/> rials, and <paramref name="shape"/> can change a bar.
    /// </summary>
    private void AddPath(long ins, Func<int, decimal> step, Func<int, DayStatus>? statusOn = null, Func<int, decimal, decimal>? yesterdayOn = null,
        decimal value = 1_000_000_000_000, Func<int, DailyBar, DailyBar>? shape = null)
    {
        var bars = new List<ClassifiedBar>();
        var previous = 10_000m;
        for (var i = 0; i < sessions.Count; i++)
        {
            var yesterday = yesterdayOn?.Invoke(i, previous) ?? previous;
            var close = i == 0 ? previous : Math.Round(yesterday * (1 + step(i)), 4);
            var status = statusOn?.Invoke(i) ?? DayStatus.Traded;
            var traded = status is not (DayStatus.SymbolHalted or DayStatus.NoTrade);
            var bar = new DailyBar
            {
                InsCode = ins, Date = sessions[i], Open = close, High = close, Low = close, Close = traded ? close : yesterday, Last = close, Yesterday = yesterday,
                TradeCount = traded ? 10 : 0, Volume = traded ? 1_000_000 : 0, Value = traded ? value : 0,
            };
            bars.Add(new(shape?.Invoke(i, bar) ?? bar, status));
            previous = traded ? close : yesterday;
        }

        store.UpsertDailyBars(bars, "history");
    }
}
