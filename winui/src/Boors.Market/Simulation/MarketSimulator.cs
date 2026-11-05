using Boors.Market.Rules;
using Boors.Market.Storage;

namespace Boors.Market.Simulation;

/// <summary>
/// Paper-trading simulations over the market store. A simulation is replayed session by session from its start to the
/// latest session, so it follows the market every day as new sessions arrive, and its results are a pure function of
/// its definition (parameters, the user's orders) and the data. The replay respects how Tehran trades:
/// <list type="bullet">
/// <item>Decisions use data up to a session's close only. There is no exchange to wait for, so by default an order is
/// done at once, at the close it was decided on; a simulation can choose to fill on the next session instead.</item>
/// <item>Only what the market would have taken fills: nothing at a close the share ended queued against the order, no
/// buy on a later day locked in a buy queue, no sell on one locked in a sell queue, nothing while a share is halted or
/// not traded. Such orders wait for a session that allows them.</item>
/// <item>Costs per side from the dated rules (sale tax on sells), slippage, at most a share of the day's value traded,
/// whole shares, sale proceeds usable at once unless a simulation chooses to wait for settlement (T+2).</item>
/// <item>Capital increases and dividends are reinvested in the position (the reports' total-return convention).</item>
/// </list>
/// The strategy ranks the buyable universe by a weighted composite of percentile ranks of <see cref="FactorKind"/>
/// factors (optionally learning the weights walk-forward), keeps a buffer around its holdings to save costs, sizes
/// positions by risk, scales exposure with a regime filter and a volatility target, and exits on stops.
/// </summary>
public sealed class MarketSimulator(MarketStore store, MarketRules rules)
{
    /// <summary>Sessions of history a decision looks back at (52 weeks, 200-session averages, MACD), with a margin.</summary>
    private const int Lookback = 300;

    private readonly Lock gate = new();
    private SimulationPanel? cached;
    private DateOnly cachedFrom;

    public SimulationResult Run(SimulationSpec spec, bool detail = true, CancellationToken cancellationToken = default)
    {
        if (spec.Revisions.Count == 0)
            throw new ArgumentException("A simulation needs at least one set of parameters.", nameof(spec));

        var panel = Panel(PanelStart(spec.Start, spec.Revisions.Select(r => r.Settings)), cancellationToken);
        return panel is null ? new SimulationResult { Id = spec.Id } : new Replay(panel, spec, rules, detail, cancellationToken).Run();
    }

    /// <summary>
    /// Runs the strategy with a grid of parameter sets (positions, ranking interval, trailing stop, regime filter; the
    /// rest as in the simulation's current parameters) over the last <paramref name="years"/> years, and measures each
    /// on the first two thirds (in sample, what the rows are ranked by) and the last third (out of sample, never seen by
    /// the ranking). A set that only shines in sample was most likely fitted to noise.
    /// </summary>
    public SimulationTuning Tune(SimulationSpec spec, int years, TuningObjective objective, CancellationToken cancellationToken = default)
    {
        years = Math.Clamp(years, 1, 10);
        var current = spec.Revisions.Count > 0 ? spec.Revisions[^1].Settings : new SimulationSettings();
        var latest = store.Query("SELECT max(d) FROM daily_bar WHERE trade_count > 0", [], r => r.IsDBNull(0) ? (DateOnly?)null : r.GetFieldValue<DateOnly>(0)).Single();
        if (latest is not { } to)
            return SimulationTuning.Empty;

        var start = to.AddYears(-years);
        var grid = (
            from positions in new[] { 5, 8, 12 }
            from rebalance in new[] { 5, 10, 20 }
            from trailing in new[] { 0, 0.10, 0.20 }
            from regime in new[] { true, false }
            select current with { Positions = positions, RebalanceSessions = rebalance, TrailingStop = trailing, RegimeFilter = regime }).ToList();
        var panel = Panel(PanelStart(start, grid), cancellationToken);
        if (panel is null)
            return SimulationTuning.Empty;

        var from = panel.IndexOnOrAfter(start);
        var end = panel.Sessions.Length - 1;
        var split = from + (end - from) * 2 / 3;
        if (split - from < 40 || end - split < 20)
            return SimulationTuning.Empty;

        var rows = new TuningRow?[grid.Count];
        Parallel.For(0, grid.Count, new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, k =>
        {
            var variant = new SimulationSpec
            {
                Id = $"tune-{k}",
                Capital = spec.Capital,
                Start = panel.Sessions[from],
                Revisions = [new SettingsRevision(panel.Sessions[from], grid[k])],
            };
            var result = new Replay(panel, variant, rules, detail: false, cancellationToken).Run();
            var curve = result.Equity;
            var cut = curve.Count - (end - split) - 1;
            if (cut < 2)
                return;
            var inSample = SimulationStatistics.Segment(curve.Take(cut + 1).ToList(), grid[k].CashYield);
            var outSample = SimulationStatistics.Segment(curve.Skip(cut).ToList(), grid[k].CashYield);
            rows[k] = new TuningRow
            {
                Settings = grid[k],
                InReturn = inSample.Return,
                InAnnualized = inSample.Annualized,
                InSharpe = inSample.Sharpe,
                InMaxDrawdown = inSample.MaxDrawdown,
                OutReturn = outSample.Return,
                OutAnnualized = outSample.Annualized,
                OutSharpe = outSample.Sharpe,
                OutMaxDrawdown = outSample.MaxDrawdown,
                Trades = result.Stats.Trades,
                Turnover = result.Stats.Turnover,
                Objective = objective switch
                {
                    TuningObjective.Sharpe => inSample.Sharpe ?? double.MinValue,
                    TuningObjective.Calmar => inSample.Annualized is { } a && inSample.MaxDrawdown < -0.001 ? a / -inSample.MaxDrawdown : double.MinValue,
                    _ => inSample.Annualized ?? inSample.Return,
                },
            };
        });

        double? Change(int a, int b) => double.IsFinite(panel.Tedpix[a]) && double.IsFinite(panel.Tedpix[b]) && panel.Tedpix[a] > 0 ? panel.Tedpix[b] / panel.Tedpix[a] - 1 : null;
        return new SimulationTuning
        {
            From = panel.Sessions[from],
            Split = panel.Sessions[split],
            To = panel.Sessions[end],
            Objective = objective,
            TedpixIn = Change(from, split),
            TedpixOut = Change(split, end),
            Rows = [.. rows.OfType<TuningRow>().OrderByDescending(r => r.Objective)],
        };
    }

    /// <summary>
    /// Enough sessions before the simulation for its look-backs. Results do not depend on where the panel starts (every
    /// statistic is a difference within a window, history counts from each listing, formations align to the simulation's
    /// own start), so a cached panel that reaches further back gives the same replay.
    /// </summary>
    private DateOnly PanelStart(DateOnly start, IEnumerable<SimulationSettings> settings)
    {
        var sessions = Lookback + settings.Max(s => Math.Max(s.RegimeSessions, s.Adaptive ? s.AdaptiveLookbackSessions + 2 * SimulationPanel.Month : 0));
        return SimulationPanel.SessionsBefore(store, start, sessions);
    }

    /// <summary>The cached panel when it still matches the data and reaches back far enough; otherwise a new one.</summary>
    private SimulationPanel? Panel(DateOnly from, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var stamp = SimulationPanel.DataStamp(store);
            if (cached is not null && cached.Stamp == stamp && cachedFrom <= from)
                return cached;

            var earliest = cached is not null && cached.Stamp == stamp && cachedFrom < from ? cachedFrom : from;
            cached = null;
            cached = SimulationPanel.Load(store, rules, earliest, stamp, cancellationToken);
            cachedFrom = earliest;
            return cached;
        }
    }

    // ---------------------------------------------------------------- replay

    private sealed class Position
    {
        public required int Stock { get; init; }
        public decimal Shares { get; set; }

        /// <summary>What the shares held cost, buy costs included.</summary>
        public decimal Cost { get; set; }

        /// <summary>Average fill price (before costs), adjusted for corporate actions like the shares.</summary>
        public decimal Entry { get; set; }

        public decimal Peak { get; set; }
        public int OpenedAt { get; set; }
    }

    private sealed class Pending
    {
        public required int Stock { get; init; }
        public required OrderSide Side { get; init; }
        public decimal? Value { get; set; }
        public decimal? Shares { get; set; }
        public bool All { get; init; }
        public decimal? Limit { get; set; }
        public required int First { get; init; }
        public required int Last { get; init; }
        public required string Reason { get; init; }
        public string? OrderId { get; init; }

        /// <summary>A risk exit: it survives rankings and waits until it fills.</summary>
        public bool Risk { get; init; }

        public double? Score { get; init; }
        public int? Rank { get; init; }
        public bool Noted { get; set; }
    }

    private sealed record Ranking(Dictionary<int, (double Score, int Rank)> Ranks, List<int> Ordered, HashSet<int> Buyable, int Universe, IReadOnlyList<FactorWeight> Weights);

    private sealed class Replay(SimulationPanel panel, SimulationSpec spec, MarketRules rules, bool detail, CancellationToken cancellationToken)
    {
        private const int JournalLimit = 3000;
        private const int IcHorizon = SimulationPanel.Month;

        private readonly Dictionary<int, Position> positions = [];
        private readonly List<Pending> pending = [];
        private readonly List<(int Due, decimal Amount)> receivables = [];
        private readonly Dictionary<int, int> cooldown = [];
        private readonly List<EquityPoint> equity = [];
        private readonly List<SimulationTrade> trades = [];
        private readonly List<JournalEntry> journal = [];
        private readonly List<double> dailyReturns = [];
        private readonly Dictionary<string, OrderTrack> tracks = [];
        private readonly Dictionary<(int K, int Factor, decimal MinValue, int MinHistory), double> ics = [];
        private decimal cash = spec.Capital;
        private int s0;
        private bool riskOn = true;
        private double exposure = 1;
        private double equalLevel = 1;
        private Ranking? lastRanking;
        private SimulationSettings? lastSettings;

        public SimulationResult Run()
        {
            s0 = panel.IndexOnOrAfter(spec.Start);
            var latest = panel.Sessions.Length - 1;
            var end = spec.End is { } stop ? Math.Min(latest, panel.IndexOnOrBefore(stop)) : latest;
            var manual = spec.Mode == SimulationMode.Manual;
            foreach (var order in spec.Orders)
                tracks[order.Id] = new OrderTrack();
            if (s0 > latest || end < s0)
                return new SimulationResult { Id = spec.Id, Latest = panel.Sessions[latest], FundamentalsSince = panel.FundamentalsSince, Orders = Statuses() };

            // A manual order joins the book after the close it was decided on (placed before the start: at the start).
            var placed = manual
                ? spec.Orders.ToLookup(o => Math.Max(s0, panel.IndexOnOrBefore(o.PlacedOn)))
                : Enumerable.Empty<ManualOrder>().ToLookup(o => 0);

            Note(s0, SimulationEvents.Start, value: (double)spec.Capital);
            for (var t = s0; t <= end; t++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var settings = SettingsAt(t);
                if (lastSettings is not null && !ReferenceEquals(settings, lastSettings))
                    Note(t, SimulationEvents.SettingsChanged);
                lastSettings = settings;
                var immediate = settings.Fill == FillPrice.Immediate;

                // The session fills what was decided at the previous close, under the parameters in force then:
                // a revision dated t changes the decisions (and, done at once, the fills) from t's close on, never
                // what had already happened.
                if (t > s0)
                {
                    var previous = SettingsAt(t - 1);
                    CorporateActions(t);
                    Settle(t);
                    Accrue(t, previous);
                    if (!immediate)
                        Execute(t, previous);
                }

                Mark(t);
                foreach (var order in placed[t])
                    Book(order, t);
                if (!manual)
                    Decide(t, settings, commit: true, force: false);

                // Done at once: what the close allows fills at it, and the session's value already carries the costs.
                if (immediate)
                {
                    Execute(t, settings);
                    Mark(t, again: true);
                }
            }

            if (spec.End is not null && end < latest)
                Note(end, SimulationEvents.End);

            // A manual order whose last session was the last simulated one can no longer fill.
            foreach (var order in pending.Where(o => o.OrderId is not null && o.Last <= end))
                tracks[order.OrderId!].Expire(tracks[order.OrderId!].CancelledAt is { } c && c <= end);

            var tracking = end == latest;
            var final = SettingsAt(end);
            var suggestions = manual && tracking ? Decide(end, final, commit: false, force: true) : [];
            var holdings = Holdings(end);
            var stats = SimulationStatistics.Compute(equity, trades, spec.Capital, final.CashYield) with { UnrealizedPnl = holdings.Sum(h => h.UnrealizedPnl) };
            return new SimulationResult
            {
                Id = spec.Id,
                Start = panel.Sessions[s0],
                AsOf = panel.Sessions[end],
                Latest = panel.Sessions[latest],
                Stats = stats,
                Equity = equity,
                Holdings = holdings,
                Trades = detail ? trades : [],
                Journal = detail ? journal.TakeLast(JournalLimit).ToList() : [],
                Planned = tracking ? [.. pending.Where(o => o.Last > end).Select(o => Plan(o, end))] : [],
                Suggestions = [.. suggestions.Select(o => Plan(o, end))],
                Orders = Statuses(),
                Factors = lastRanking?.Weights ?? [],
                RiskOn = riskOn,
                TargetExposure = exposure,
                NextRebalanceIn = manual || !tracking ? null : final.RebalanceSessions - (end - s0) % Math.Max(1, final.RebalanceSessions),
                Universe = lastRanking?.Universe ?? 0,
                FundamentalsSince = panel.FundamentalsSince,
            };
        }

        private SimulationSettings SettingsAt(int t)
        {
            var date = panel.Sessions[t];
            var chosen = spec.Revisions[0].Settings;
            foreach (var revision in spec.Revisions.Skip(1).OrderBy(r => r.From))
            {
                if (revision.From <= date)
                    chosen = revision.Settings;
            }

            return chosen;
        }

        // ------------------------------------------------------------ the session's events

        /// <summary>A capital increase or dividend: shares (held and in waiting orders) × 1/f, prices and limits × f.</summary>
        private void CorporateActions(int t)
        {
            foreach (var stock in positions.Keys.Concat(pending.Select(o => o.Stock)).Distinct().ToList())
            {
                var s = panel.Stocks[stock];
                var f = s.Has(t) ? s.Adjust[s.I(t)] : 1f;
                if (f == 1f || f <= 0)
                    continue;
                var factor = (decimal)f;
                if (positions.TryGetValue(stock, out var p))
                {
                    p.Shares /= factor;
                    p.Entry *= factor;
                    p.Peak *= factor;
                    Note(t, SimulationEvents.CorporateAction, stock, f);
                }

                foreach (var order in pending.Where(o => o.Stock == stock))
                {
                    if (order.Shares is { } shares)
                        order.Shares = shares / factor;
                    if (order.Limit is { } limit)
                        order.Limit = limit * factor;
                }
            }
        }

        private void Settle(int t)
        {
            for (var k = receivables.Count - 1; k >= 0; k--)
            {
                if (receivables[k].Due <= t)
                {
                    cash += receivables[k].Amount;
                    receivables.RemoveAt(k);
                }
            }
        }

        private void Accrue(int t, SimulationSettings settings)
        {
            if (settings.CashYield <= 0 || cash <= 0)
                return;
            var days = panel.Sessions[t].DayNumber - panel.Sessions[t - 1].DayNumber;
            cash *= (decimal)Math.Pow(1 + settings.CashYield, days / 365.0);
        }

        /// <summary>The session's value; <paramref name="again"/> replaces it after the fills of its own close.</summary>
        private void Mark(int t, bool again = false)
        {
            if (again)
            {
                equity.RemoveAt(equity.Count - 1);
                if (equity.Count > 0 && equity[^1].Equity > 0)
                    dailyReturns.RemoveAt(dailyReturns.Count - 1);
            }

            decimal invested = 0;
            foreach (var p in positions.Values)
            {
                var s = panel.Stocks[p.Stock];
                if (!s.Has(t))
                    continue;
                var i = s.I(t);
                var close = (decimal)s.Close[i];
                if (PanelStock.IsPriced(s.Status[i]))
                    p.Peak = Math.Max(p.Peak, close);
                invested += p.Shares * close;
            }

            var held = cash + receivables.Sum(r => r.Amount);
            var total = held + invested;
            if (equity.Count > 0 && equity[^1].Equity > 0)
                dailyReturns.Add((double)(total / equity[^1].Equity) - 1);
            // The average share compounds from the simulation's start (1 there), whatever the panel's first session.
            if (t > s0 && !again)
                equalLevel *= 1 + panel.EqualReturn[t];
            equity.Add(new EquityPoint(panel.Sessions[t], total, held, invested, Finite(panel.Tedpix[t]), equalLevel, Finite(panel.Usd[t])));
        }

        private decimal EquityAt(int t) => cash + receivables.Sum(r => r.Amount) + positions.Values.Sum(p => p.Shares * Close(p.Stock, t));

        private decimal Close(int stock, int t)
        {
            var s = panel.Stocks[stock];
            return s.Has(t) ? (decimal)s.Close[s.I(t)] : 0;
        }

        // ------------------------------------------------------------ execution

        private void Book(ManualOrder order, int t)
        {
            // Only primary listings of shares are simulated (no funds, bonds, rights or TAL siblings).
            if (!panel.ByInsCode.TryGetValue(order.InsCode, out var stock))
            {
                tracks[order.Id].Reject(SimulationEvents.NotTradable);
                return;
            }

            // A cancel shortens the order's life only if it comes before its natural end (else the order expired).
            var last = t + Math.Max(1, order.GoodForSessions);
            if (order.CancelledOn is { } cancelled && panel.IndexOnOrBefore(cancelled) is var at && at < last)
            {
                last = at;
                tracks[order.Id].CancelledAt = at;
            }

            pending.Add(new Pending
            {
                Stock = stock,
                Side = order.Side,
                Value = order.Side == OrderSide.Buy ? order.Value : null,
                Shares = order.Shares,
                All = order.Side == OrderSide.Sell && order.Shares is null,
                Limit = order.LimitPrice,
                First = FirstFill(t, SettingsAt(t)),
                Last = last,
                Reason = SimulationEvents.Manual,
                OrderId = order.Id,
            });
        }

        /// <summary>The first session an order decided at the close of t can fill on.</summary>
        private static int FirstFill(int t, SimulationSettings settings) => settings.Fill == FillPrice.Immediate ? t : t + 1;

        private void Execute(int t, SimulationSettings settings)
        {
            // Rankings replace the strategy's own orders, so only manual orders run out (and a risk exit never does).
            foreach (var expired in pending.Where(o => o.Last < t).ToList())
            {
                pending.Remove(expired);
                if (expired.OrderId is not { } id)
                    continue;
                var cancelled = tracks[id].CancelledAt is { } c && c < t;
                tracks[id].Expire(cancelled);
                if (!cancelled)
                    Note(t, SimulationEvents.Expired, expired.Stock);
            }

            var costs = rules.GetCosts(panel.Sessions[t]).Value;
            var brokerage = settings.Brokerage ?? costs.BrokerageMax;
            var buyRate = costs.BuyCost(brokerage);
            var sellRate = costs.SellCost(brokerage);

            // Sells first (risk exits before the rest) so their proceeds can fund the day's buys under instant proceeds.
            var due = pending.Where(o => o.First <= t)
                .OrderBy(o => o.Side == OrderSide.Sell ? 0 : 1)
                .ThenBy(o => o.Risk ? 0 : 1)
                .ThenBy(o => o.Rank ?? int.MaxValue)
                .ToList();
            foreach (var order in due)
            {
                if (!Fill(order, t, settings, order.Side == OrderSide.Buy ? buyRate : sellRate))
                    continue;
                pending.Remove(order);
                if (order.OrderId is { } id)
                    tracks[id].Complete();
            }
        }

        /// <summary>Tries to fill an order on session t; true when nothing of it is left.</summary>
        private bool Fill(Pending order, int t, SimulationSettings settings, decimal rate)
        {
            var s = panel.Stocks[order.Stock];
            var cell = s.Has(t) ? s.Status[s.I(t)] : Cell.None;
            if (!PanelStock.IsPriced(cell))
                return Blocked(order, t, SimulationEvents.BlockedNoTrade);

            // An order decided at this very close meets only how the session ended; one that waited through the day met
            // all of it, so only a day locked from start to end kept it out.
            var atClose = settings.Fill == FillPrice.Immediate && t == order.First;
            if (order.Side == OrderSide.Buy && (cell == Cell.LockedUp || atClose && cell == Cell.ClosedUp))
                return Blocked(order, t, SimulationEvents.BlockedBuyQueue);
            if (order.Side == OrderSide.Sell && (cell == Cell.LockedDown || atClose && cell == Cell.ClosedDown))
                return Blocked(order, t, SimulationEvents.BlockedSellQueue);

            var i = s.I(t);
            var basis = settings.Fill == FillPrice.NextOpen && s.Open[i] > 0 ? s.Open[i] : s.Close[i];
            var slip = settings.SlippageBps / 10_000;
            var price = basis * (order.Side == OrderSide.Buy ? 1 + slip : 1 - slip);

            // Nothing fills outside the day's range: before 2025-12-22 the official close could lie outside it on a thin day.
            if (s.Low[i] > 0 && s.High[i] >= s.Low[i])
                price = Math.Clamp(price, s.Low[i], s.High[i]);
            if (order.Limit is { } limit)
            {
                var l = (double)limit;
                // Placed after the close, a limit meets the closing price: a better price earlier that day is hindsight.
                if (order.Side == OrderSide.Buy ? l < s.Low[i] || atClose && basis > l : s.High[i] > 0 && l > s.High[i] || atClose && basis < l)
                    return Blocked(order, t, SimulationEvents.BlockedLimit);
                price = order.Side == OrderSide.Buy ? Math.Min(price, l) : Math.Max(price, l);
            }

            var fill = Math.Round((decimal)price, 4);
            if (fill <= 0)
                return Blocked(order, t, SimulationEvents.BlockedNoTrade);
            var room = settings.ParticipationCap > 0 ? Math.Floor((decimal)(settings.ParticipationCap * s.Value[i]) / fill) : decimal.MaxValue;
            return order.Side == OrderSide.Sell ? Sell(order, t, fill, rate, room, settings) : Buy(order, t, fill, rate, room);
        }

        private bool Sell(Pending order, int t, decimal price, decimal rate, decimal room, SimulationSettings settings)
        {
            if (!positions.TryGetValue(order.Stock, out var p) || p.Shares <= 0)
            {
                // A manual sell placed with a manual buy of the same share waits for the buy to fill.
                if (order.OrderId is not null && pending.Any(o => o.OrderId is not null && o.Stock == order.Stock && o.Side == OrderSide.Buy))
                    return Blocked(order, t, SimulationEvents.NotHeld);
                if (order.OrderId is { } id && !tracks[id].HasFills)
                    tracks[id].Reject(SimulationEvents.NotHeld);
                Note(t, SimulationEvents.NotHeld, order.Stock);
                return true;
            }

            var wanted = order.All ? p.Shares : Math.Min(order.Shares ?? p.Shares, p.Shares);
            var quantity = Math.Min(wanted, room);
            if (quantity <= 0)
                return Blocked(order, t, SimulationEvents.PartialFill);

            var gross = quantity * price;
            var fee = gross * rate;
            var net = gross - fee;
            var cost = p.Cost * (quantity / p.Shares);
            p.Shares -= quantity;
            p.Cost -= cost;
            if (p.Shares <= 0.0001m)
                positions.Remove(order.Stock);
            if (settings.InstantSaleProceeds)
                cash += net;
            else
                receivables.Add((t + rules.SettlementDays, net));

            Trade(order, t, OrderSide.Sell, quantity, price, gross, fee, net - cost, cost > 0 ? (double)((net - cost) / cost) : null, t - p.OpenedAt);
            var left = wanted - quantity;
            if (left <= 0.0001m || !positions.ContainsKey(order.Stock))
                return true;
            if (!order.All)
                order.Shares = left;
            return Partial(order, t);
        }

        private bool Buy(Pending order, int t, decimal price, decimal rate, decimal room)
        {
            var affordable = Math.Floor(Math.Max(0, cash) / (price * (1 + rate)));
            var wanted = order.Shares is { } shares ? Math.Floor(shares) : Math.Floor((order.Value ?? 0) / price);
            if (wanted <= 0)
            {
                // Less than one share's worth: nothing (more) to buy.
                if (order.OrderId is { } id && !tracks[id].HasFills)
                    tracks[id].Reject(SimulationEvents.TooSmall);
                return true;
            }
            var quantity = Math.Min(Math.Min(wanted, affordable), Math.Floor(room));

            // When cash is what limits the fill (sale proceeds still settling), a sliver waits for the cash instead of a
            // dust trade; a thin session's share of its value is taken whatever its size.
            var cashBound = affordable < Math.Min(wanted, Math.Floor(room));
            if (quantity <= 0 || cashBound && quantity < wanted / 10)
                return Blocked(order, t, cashBound ? SimulationEvents.NoCash : SimulationEvents.PartialFill);

            var gross = quantity * price;
            var fee = gross * rate;
            cash -= gross + fee;
            if (!positions.TryGetValue(order.Stock, out var p))
                positions[order.Stock] = p = new Position { Stock = order.Stock, OpenedAt = t };
            p.Entry = (p.Entry * p.Shares + price * quantity) / (p.Shares + quantity);
            p.Shares += quantity;
            p.Cost += gross + fee;
            p.Peak = Math.Max(p.Peak, price);
            Trade(order, t, OrderSide.Buy, quantity, price, gross, fee, null, null, null);

            if (order.Shares is not null)
                order.Shares -= quantity;
            else
                order.Value -= gross;
            var left = order.Shares ?? Math.Floor((order.Value ?? 0) / price);
            return left < 1 || Partial(order, t);
        }

        private void Trade(Pending order, int t, OrderSide side, decimal quantity, decimal price, decimal gross, decimal fee, decimal? pnl, double? ret, int? held)
        {
            var s = panel.Stocks[order.Stock];
            trades.Add(new SimulationTrade
            {
                Date = panel.Sessions[t],
                InsCode = s.InsCode,
                Symbol = s.Symbol,
                Side = side,
                Shares = quantity,
                Price = price,
                Value = gross,
                Costs = fee,
                RealizedPnl = pnl,
                RealizedReturn = ret,
                SessionsHeld = held,
                Reason = order.Reason,
                OrderId = order.OrderId,
            });
            if (order.OrderId is { } id)
                tracks[id].Fill(quantity, price, panel.Sessions[t]);
        }

        /// <summary>Not (fully) filled today: noted once per order; the order waits for its next session.</summary>
        private bool Blocked(Pending order, int t, string reason)
        {
            if (!order.Noted)
            {
                order.Noted = true;
                Note(t, reason, order.Stock);
            }

            if (order.OrderId is { } id)
                tracks[id].LastReason = reason;
            return false;
        }

        private bool Partial(Pending order, int t) => Blocked(order, t, SimulationEvents.PartialFill);

        // ------------------------------------------------------------ decisions

        /// <summary>
        /// The strategy at the close of t: risk exits every session, a new ranking every
        /// <see cref="SimulationSettings.RebalanceSessions"/> (or now, with <paramref name="force"/>). With
        /// <paramref name="commit"/> the orders join the book; without, they are only returned (manual suggestions).
        /// </summary>
        private List<Pending> Decide(int t, SimulationSettings settings, bool commit, bool force)
        {
            var orders = new List<Pending>();

            // Only risk exits (and the user's own sells) outlive a ranking; the ranking re-decides its own exits.
            var exiting = pending.Where(o => o.Side == OrderSide.Sell && o.All && (o.Risk || o.OrderId is not null)).Select(o => o.Stock).ToHashSet();
            foreach (var p in positions.Values.OrderBy(p => p.Stock))
            {
                if (exiting.Contains(p.Stock) || RiskExit(p, t, settings) is not { } reason)
                    continue;
                orders.Add(new Pending { Stock = p.Stock, Side = OrderSide.Sell, All = true, First = FirstFill(t, settings), Last = int.MaxValue, Reason = reason, Risk = true });
                exiting.Add(p.Stock);
                if (commit)
                {
                    // The strategy's own orders in the share give way to the exit: no buy after a stop, one sell.
                    pending.RemoveAll(o => o.Stock == p.Stock && o.OrderId is null);
                    cooldown[p.Stock] = t + settings.CooldownSessions;
                    Note(t, reason, p.Stock, p.Entry > 0 ? (double)(Close(p.Stock, t) / p.Entry) - 1 : null);
                }
            }

            var target = Exposure(t, settings, commit);
            var interval = Math.Max(1, settings.RebalanceSessions);
            if (force || (t - s0) % interval == 0)
            {
                var ranking = Rank(t, settings);
                lastRanking = ranking;
                if (commit)
                    pending.RemoveAll(o => o.OrderId is null && !o.Risk);
                orders.AddRange(Rebalance(t, settings, ranking, exiting, target, commit));
            }

            if (commit)
                pending.AddRange(orders);
            return orders;
        }

        private string? RiskExit(Position p, int t, SimulationSettings settings)
        {
            var s = panel.Stocks[p.Stock];
            if (!s.Has(t) || !PanelStock.IsPriced(s.Status[s.I(t)]) || p.Entry <= 0)
                return null;
            var close = (decimal)s.Close[s.I(t)];
            if (settings.StopLoss > 0 && close <= p.Entry * (1 - (decimal)settings.StopLoss))
                return SimulationEvents.StopLoss;
            if (settings.TrailingStop > 0 && close <= Math.Max(p.Peak, p.Entry) * (1 - (decimal)settings.TrailingStop))
                return SimulationEvents.TrailingStop;
            if (settings.TakeProfit > 0 && close >= p.Entry * (1 + (decimal)settings.TakeProfit))
                return SimulationEvents.TakeProfit;
            if (settings.MaxHoldSessions > 0 && t - p.OpenedAt >= settings.MaxHoldSessions)
                return SimulationEvents.TimeExit;
            return null;
        }

        /// <summary>The share of the portfolio to invest: cut by the regime filter and scaled to the volatility target.</summary>
        private double Exposure(int t, SimulationSettings settings, bool commit)
        {
            var on = true;
            double? gap = null;
            var window = settings.RegimeSessions;
            if (settings.RegimeFilter && window > 1 && t >= window && double.IsFinite(panel.Tedpix[t]))
            {
                double sum = 0;
                var n = 0;
                for (var k = t - window + 1; k <= t; k++)
                {
                    if (double.IsFinite(panel.Tedpix[k]))
                    {
                        sum += panel.Tedpix[k];
                        n++;
                    }
                }

                if (n >= window / 2)
                {
                    gap = panel.Tedpix[t] / (sum / n) - 1;
                    on = gap >= 0;
                }
            }

            if (commit && on != riskOn)
                Note(t, on ? SimulationEvents.RiskOn : SimulationEvents.RiskOff, value: gap);
            if (commit || spec.Mode == SimulationMode.Manual)
                riskOn = on;

            var level = on ? 1 : Math.Clamp(settings.DefensiveExposure, 0, 1);
            if (settings.TargetVolatility > 0 && dailyReturns.Count >= 20)
            {
                var recent = dailyReturns.TakeLast(60).ToList();
                var mean = recent.Average();
                var realized = Math.Sqrt(recent.Sum(r => (r - mean) * (r - mean)) / (recent.Count - 1)) * Math.Sqrt(SimulationPanel.Year);
                if (realized > 0)
                    level *= Math.Clamp(settings.TargetVolatility / realized, 0.2, 1);
            }

            exposure = level;
            return level;
        }

        private IEnumerable<Pending> Rebalance(int t, SimulationSettings settings, Ranking ranking, HashSet<int> exiting, double target, bool commit)
        {
            var orders = new List<Pending>();
            var first = FirstFill(t, settings);
            var count = Math.Max(1, settings.Positions);
            var holdRank = Math.Max(count, (int)Math.Ceiling(ranking.Ordered.Count * Math.Clamp(settings.HoldPercentile, 0, 1)));
            var keep = new List<int>();
            foreach (var p in positions.Values.Where(p => !exiting.Contains(p.Stock)).OrderBy(p => ranking.Ranks.TryGetValue(p.Stock, out var r) ? r.Rank : int.MaxValue))
            {
                var young = t - p.OpenedAt < settings.MinHoldSessions;
                var s = panel.Stocks[p.Stock];
                var frozen = !s.Has(t) || !PanelStock.IsPriced(s.Status[s.I(t)]);
                var ranked = ranking.Ranks.TryGetValue(p.Stock, out var rank);
                if (young || frozen || (ranked && rank.Rank <= holdRank && keep.Count < count))
                {
                    keep.Add(p.Stock);
                    continue;
                }

                orders.Add(new Pending { Stock = p.Stock, Side = OrderSide.Sell, All = true, First = first, Last = t + settings.RebalanceSessions, Reason = SimulationEvents.RankExit, Rank = ranked ? rank.Rank : null, Score = ranked ? rank.Score : null });
                if (commit)
                    Note(t, SimulationEvents.RankExit, p.Stock, ranked ? rank.Rank : null);
            }

            var sectors = keep.GroupBy(k => panel.Stocks[k].SectorCode ?? string.Empty).ToDictionary(g => g.Key, g => g.Count());
            var added = new List<int>();
            foreach (var candidate in ranking.Ordered)
            {
                // Never buy a share the next ranking would sell at once: below the hold limit the buffer is gone.
                if (keep.Count + added.Count >= count || ranking.Ranks[candidate].Rank > holdRank)
                    break;
                if (positions.ContainsKey(candidate) || !ranking.Buyable.Contains(candidate) || cooldown.TryGetValue(candidate, out var until) && until > t)
                    continue;
                var sector = panel.Stocks[candidate].SectorCode ?? string.Empty;
                if (settings.PerSector > 0 && sectors.GetValueOrDefault(sector) >= settings.PerSector)
                    continue;
                sectors[sector] = sectors.GetValueOrDefault(sector) + 1;
                added.Add(candidate);
            }

            var chosen = keep.Concat(added).ToList();
            var weights = Weights(chosen, t, settings, ranking);
            var investable = EquityAt(t) * (decimal)(target * (1 - Math.Clamp(settings.CashReserve, 0, 0.9)));
            var band = (decimal)Math.Max(0, settings.RebalanceBand);
            for (var k = 0; k < chosen.Count; k++)
            {
                var stock = chosen[k];
                var goal = investable * (decimal)weights[k];
                var close = Close(stock, t);
                // A goal of nothing (risk off at 0% exposure) still sells what is held: only a new position needs a goal.
                if (close <= 0 || goal <= 0 && !positions.ContainsKey(stock))
                    continue;
                ranking.Ranks.TryGetValue(stock, out var r);
                var rank = ranking.Ranks.ContainsKey(stock) ? r.Rank : (int?)null;
                var score = ranking.Ranks.ContainsKey(stock) ? r.Score : (double?)null;
                if (!positions.TryGetValue(stock, out var p))
                {
                    orders.Add(new Pending { Stock = stock, Side = OrderSide.Buy, Value = goal, First = first, Last = t + settings.RebalanceSessions, Reason = SimulationEvents.Entry, Rank = rank, Score = score });
                    continue;
                }

                var now = p.Shares * close;
                var gap = goal - now;
                if (Math.Abs(gap) <= band * goal || !ranking.Ranks.ContainsKey(stock) || gap > 0 && settings.Excluded.Contains(panel.Stocks[stock].InsCode))
                    continue;
                orders.Add(gap > 0
                    ? new Pending { Stock = stock, Side = OrderSide.Buy, Value = gap, First = first, Last = t + settings.RebalanceSessions, Reason = SimulationEvents.Resize, Rank = rank, Score = score }
                    : new Pending { Stock = stock, Side = OrderSide.Sell, Shares = Math.Floor(-gap / close), First = first, Last = t + settings.RebalanceSessions, Reason = SimulationEvents.Resize, Rank = rank, Score = score });
            }

            if (commit)
                Note(t, SimulationEvents.Rebalance, value: chosen.Count, value2: target);
            return orders.Where(o => o.Side == OrderSide.Buy || o.All || o.Shares > 0);
        }

        /// <summary>Position weights (sum ≤ 1) by the sizing method, each capped at <see cref="SimulationSettings.MaxWeight"/>.</summary>
        private double[] Weights(List<int> chosen, int t, SimulationSettings settings, Ranking ranking)
        {
            if (chosen.Count == 0)
                return [];
            var snapshot = panel.Snapshot(t);
            var volatility = chosen.Select(stock =>
            {
                var j = Array.BinarySearch(snapshot.Stocks, stock);
                return j >= 0 && double.IsFinite(snapshot.Volatility60[j]) && snapshot.Volatility60[j] > 0 ? snapshot.Volatility60[j] : double.NaN;
            }).ToArray();
            var typical = volatility.Where(double.IsFinite).DefaultIfEmpty(0.02).Average();
            var raw = chosen.Select((stock, k) =>
            {
                var vol = double.IsFinite(volatility[k]) ? volatility[k] : typical;
                var score = ranking.Ranks.TryGetValue(stock, out var r) ? r.Score / 100 : 0.5;
                return settings.Sizing switch
                {
                    SizingMethod.Equal => 1,
                    SizingMethod.Conviction => (0.25 + score * score) / vol,
                    _ => 1 / vol,
                };
            }).ToArray();

            var cap = Math.Clamp(settings.MaxWeight, 0.01, 1);
            var weights = new double[raw.Length];
            var free = Enumerable.Range(0, raw.Length).ToList();
            var budget = 1.0;
            while (free.Count > 0)
            {
                var sum = free.Sum(k => raw[k]);
                var over = free.Where(k => raw[k] / sum * budget > cap).ToList();
                if (over.Count == 0)
                {
                    foreach (var k in free)
                        weights[k] = raw[k] / sum * budget;
                    break;
                }

                foreach (var k in over)
                {
                    weights[k] = cap;
                    budget -= cap;
                    free.Remove(k);
                }

                if (budget <= 1e-9)
                    break;
            }

            return weights;
        }

        // ------------------------------------------------------------ ranking

        private Ranking Rank(int t, SimulationSettings settings)
        {
            var snapshot = panel.Snapshot(t);
            var eligible = Eligible(snapshot, settings, applyExclusions: true);

            // A factor too few shares have a value for (EPS before snapshots were collected) gives its weight to the others.
            var weights = FactorWeights(t, settings).Select(w =>
            {
                var values = snapshot.Values[(int)w.Factor];
                var coverage = eligible.Count == 0 ? 0 : eligible.Count(j => double.IsFinite(values[j])) / (double)eligible.Count;
                return w with { Used = coverage >= 0.3 ? w.Used : 0, Coverage = coverage };
            }).ToList();
            var available = weights.Sum(w => w.Used);
            if (available > 0)
                weights = [.. weights.Select(w => w with { Used = w.Used / available })];
            var composite = new double[eligible.Count];
            var present = new double[eligible.Count];
            foreach (var w in weights.Where(w => w.Used > 0))
            {
                var values = snapshot.Values[(int)w.Factor];
                var percentiles = Analytics.MarketReports.PercentileRanks([.. eligible.Select(j => double.IsFinite(values[j]) ? values[j] : (double?)null)]);
                for (var k = 0; k < eligible.Count; k++)
                {
                    if (percentiles[k] is { } p)
                    {
                        composite[k] += w.Used * p;
                        present[k] += w.Used;
                    }
                }
            }

            var total = weights.Sum(w => w.Used);
            var scored = Enumerable.Range(0, eligible.Count)
                .Where(k => total > 0 && present[k] >= total / 2)
                .Select(k => (Stock: snapshot.Stocks[eligible[k]], Score: composite[k] / present[k] * 100, Cell: snapshot.Status[eligible[k]]))
                .OrderByDescending(x => x.Score).ThenBy(x => panel.Stocks[x.Stock].InsCode)
                .ToList();

            // A buy that could not fill is not worth a free place: a locked day keeps it out, and so does a queue at the
            // close when the fill is at that close.
            var immediate = settings.Fill == FillPrice.Immediate;
            var ranks = new Dictionary<int, (double, int)>();
            for (var k = 0; k < scored.Count; k++)
                ranks[scored[k].Stock] = (scored[k].Score, k + 1);
            return new Ranking(
                ranks,
                [.. scored.Select(x => x.Stock)],
                [.. scored.Where(x => x.Cell != Cell.LockedUp && !(immediate && x.Cell == Cell.ClosedUp) && !settings.Excluded.Contains(panel.Stocks[x.Stock].InsCode)).Select(x => x.Stock)],
                eligible.Count,
                weights);
        }

        /// <summary>Positions in the snapshot of the shares that pass the universe filters on that close.</summary>
        private List<int> Eligible(FactorSnapshot snapshot, SimulationSettings settings, bool applyExclusions)
        {
            var result = new List<int>();
            var minValue = (double)settings.MinValue;
            for (var j = 0; j < snapshot.Stocks.Length; j++)
            {
                if (PanelStock.IsPriced(snapshot.Status[j]) && snapshot.Priced20[j] >= SimulationPanel.Month / 2 && snapshot.Value20[j] >= minValue
                    && snapshot.PricedTotal[j] >= settings.MinHistorySessions
                    && !(applyExclusions && settings.Excluded.Contains(panel.Stocks[snapshot.Stocks[j]].InsCode) && !positions.ContainsKey(snapshot.Stocks[j])))
                {
                    result.Add(j);
                }
            }

            return result;
        }

        /// <summary>
        /// The chosen weights, or in adaptive mode a blend of them with what the trailing rank correlations (factor
        /// value at a close vs the next 20 sessions' return) say: only formations whose 20 sessions had ended by t count,
        /// a factor needs six of them, and a factor whose correlation was negative gets no learned weight.
        /// </summary>
        private IReadOnlyList<FactorWeight> FactorWeights(int t, SimulationSettings settings)
        {
            var chosen = settings.Weights.Where(w => w.Value > 0).ToDictionary(w => w.Key, w => w.Value);
            var total = chosen.Values.Sum();
            if (total <= 0)
                return [];
            var prior = chosen.ToDictionary(w => w.Key, w => w.Value / total);
            if (!settings.Adaptive)
                return [.. prior.Select(w => new FactorWeight(w.Key, w.Value, w.Value, null, 0))];

            var learned = new Dictionary<FactorKind, (double Mean, int Months)>();
            foreach (var factor in prior.Keys)
            {
                var values = new List<double>();
                // Formations every 20 sessions counted from the simulation's start, so they do not move with the panel.
                var first = Math.Max(0, t - settings.AdaptiveLookbackSessions);
                var offset = ((first - s0) % IcHorizon + IcHorizon) % IcHorizon;
                for (var k = offset == 0 ? first : first + IcHorizon - offset; k + IcHorizon <= t; k += IcHorizon)
                {
                    var ic = InformationCoefficient(k, factor, settings);
                    if (double.IsFinite(ic))
                        values.Add(ic);
                }

                learned[factor] = (values.Count > 0 ? values.Average() : double.NaN, values.Count);
            }

            var known = learned.Where(l => l.Value.Months >= 6).ToList();
            var positive = known.Sum(l => Math.Max(0, l.Value.Mean));
            var knownPrior = known.Sum(l => prior[l.Key]);
            var shrink = Math.Clamp(settings.AdaptiveShrink, 0, 1);
            return [.. prior.Select(w =>
            {
                var (mean, months) = learned[w.Key];
                var used = w.Value;
                if (months >= 6 && positive > 0)
                    used = shrink * w.Value + (1 - shrink) * Math.Max(0, mean) / positive * knownPrior;
                return new FactorWeight(w.Key, w.Value, used, double.IsFinite(mean) ? mean : null, months);
            })];
        }

        private double InformationCoefficient(int k, FactorKind factor, SimulationSettings settings)
        {
            var key = (k, (int)factor, settings.MinValue, settings.MinHistorySessions);
            if (ics.TryGetValue(key, out var cached))
                return cached;

            var snapshot = panel.Snapshot(k);
            var values = snapshot.Values[(int)factor];
            var x = new List<double>();
            var y = new List<double>();
            foreach (var j in Eligible(snapshot, settings, applyExclusions: false))
            {
                var s = panel.Stocks[snapshot.Stocks[j]];
                if (!double.IsFinite(values[j]) || !s.Has(k + IcHorizon))
                    continue;
                x.Add(values[j]);
                y.Add(s.Level[s.I(k + IcHorizon)] - s.Level[s.I(k)]);
            }

            var ic = x.Count >= 30 ? Analytics.MarketReports.Spearman(x, y) : double.NaN;
            ics[key] = ic;
            return ic;
        }

        // ------------------------------------------------------------ results

        private List<SimulationHolding> Holdings(int t)
        {
            var total = EquityAt(t);
            var settings = SettingsAt(t);
            return [.. positions.Values.Select(p =>
            {
                var s = panel.Stocks[p.Stock];
                var i = s.Has(t) ? s.I(t) : -1;
                var close = i >= 0 ? (decimal)s.Close[i] : 0;
                var value = p.Shares * close;
                var priced = i > 0 && PanelStock.IsPriced(s.Status[i]);
                decimal? stop = null;
                if (settings.StopLoss > 0)
                    stop = p.Entry * (1 - (decimal)settings.StopLoss);
                if (settings.TrailingStop > 0)
                    stop = Math.Max(stop ?? 0, Math.Max(p.Peak, p.Entry) * (1 - (decimal)settings.TrailingStop));
                (double Score, int Rank) r = default;
                var ranked = lastRanking?.Ranks.TryGetValue(p.Stock, out r) == true;
                return new SimulationHolding
                {
                    InsCode = s.InsCode,
                    Symbol = s.Symbol,
                    Name = s.Name,
                    SectorName = s.SectorName,
                    Shares = p.Shares,
                    AverageCost = p.Shares > 0 ? p.Cost / p.Shares : 0,
                    LastPrice = close,
                    Value = value,
                    Weight = total > 0 ? (double)(value / total) : 0,
                    UnrealizedPnl = value - p.Cost,
                    UnrealizedReturn = p.Cost > 0 ? (double)(value / p.Cost) - 1 : 0,
                    DayChange = priced && s.Priced[i] > s.Priced[i - 1] ? Math.Exp(s.Level[i] - s.Level[i - 1]) - 1 : null,
                    OpenedOn = panel.Sessions[p.OpenedAt],
                    SessionsHeld = t - p.OpenedAt,
                    StopPrice = stop,
                    DayStatus = i >= 0 ? StatusName(s.Status[i]) : null,
                    Score = ranked ? r.Score : null,
                    Rank = ranked ? r.Rank : null,
                };
            }).OrderByDescending(h => h.Value)];
        }

        private PlannedOrder Plan(Pending order, int t)
        {
            var s = panel.Stocks[order.Stock];
            var close = Close(order.Stock, t);
            var held = positions.TryGetValue(order.Stock, out var p) ? p.Shares : 0;
            var shares = order.Side == OrderSide.Sell
                ? order.All ? held : order.Shares ?? 0
                : order.Shares ?? (close > 0 ? Math.Floor((order.Value ?? 0) / close) : 0);
            return new PlannedOrder
            {
                InsCode = s.InsCode,
                Symbol = s.Symbol,
                Name = s.Name,
                Side = order.Side,
                Shares = shares,
                Value = order.Side == OrderSide.Buy && order.Value is { } v ? v : shares * close,
                Price = close,
                Reason = order.Reason,
                Score = order.Score ?? (lastRanking?.Ranks.TryGetValue(order.Stock, out var r) == true ? r.Score : null),
                Rank = order.Rank ?? (lastRanking?.Ranks.TryGetValue(order.Stock, out var q) == true ? q.Rank : null),
                OrderId = order.OrderId,
            };
        }

        private List<OrderStatus> Statuses() =>
            [.. spec.Orders.Select(o => tracks.TryGetValue(o.Id, out var track) ? track.ToStatus(o.Id, o.CancelledOn is not null) : new OrderStatus(o.Id, OrderStates.Pending, 0, null, null, null))];

        private void Note(int t, string kind, int? stock = null, double? value = null, double? value2 = null)
        {
            if (!detail && kind is not (SimulationEvents.RiskOff or SimulationEvents.RiskOn))
                return;
            var s = stock is { } k ? panel.Stocks[k] : null;
            journal.Add(new JournalEntry(panel.Sessions[t], kind, s?.InsCode, s?.Symbol, value, value2));
        }

        private static double? Finite(double value) => double.IsFinite(value) ? value : null;

        private static string? StatusName(Cell cell) => cell switch
        {
            Cell.Traded or Cell.ClosedUp or Cell.ClosedDown => "Traded",
            Cell.NoTrade => "NoTrade",
            Cell.Halted => "SymbolHalted",
            Cell.Closed => "MarketClosed",
            Cell.Reopen => "ReopenAuction",
            Cell.LockedUp => "QueueLockedUp",
            Cell.LockedDown => "QueueLockedDown",
            _ => null,
        };
    }

    /// <summary>What happened to one manual order so far: its fills and, once it left the book, how it ended.</summary>
    private sealed class OrderTrack
    {
        private decimal value;

        public decimal Filled { get; private set; }
        public DateOnly? LastFill { get; private set; }

        /// <summary>filled, expired, cancelled or rejected; null while the order is in the book.</summary>
        public string? Final { get; private set; }

        public string? LastReason { get; set; }
        public int? CancelledAt { get; set; }

        public bool HasFills => Filled > 0;

        public void Fill(decimal quantity, decimal price, DateOnly date)
        {
            Filled += quantity;
            value += quantity * price;
            LastFill = date;
        }

        public void Complete() => Final ??= OrderStates.Filled;

        public void Reject(string reason)
        {
            Final ??= OrderStates.Rejected;
            LastReason = reason;
        }

        public void Expire(bool cancelled) => Final ??= cancelled ? OrderStates.Cancelled : OrderStates.Expired;

        /// <summary>A cancel request takes effect at once for an order still in the book (it no longer fills after that day).</summary>
        public OrderStatus ToStatus(string id, bool cancelRequested)
        {
            var state = Final switch
            {
                OrderStates.Expired or OrderStates.Cancelled or OrderStates.Rejected when Filled > 0 => OrderStates.Partial,
                { } final => final,
                _ when cancelRequested => Filled > 0 ? OrderStates.Partial : OrderStates.Cancelled,
                _ => Filled > 0 ? OrderStates.Partial : OrderStates.Pending,
            };
            return new OrderStatus(id, state, Filled, Filled > 0 ? value / Filled : null, LastFill, LastReason);
        }
    }
}
