using System.Collections.Concurrent;
using Boors.Market.Analytics;
using Boors.Market.Domain;
using Boors.Market.Rules;
using Boors.Market.Sources.Macro;
using Boors.Market.Sources.Tsetmc;
using Boors.Market.Storage;

namespace Boors.Market.Simulation;

/// <summary>
/// Day status of a panel cell. <see cref="Traded"/>, <see cref="Reopen"/>, <see cref="LockedUp"/> and
/// <see cref="LockedDown"/> have trades and a real price; the others do not.
/// </summary>
internal enum Cell : byte
{
    None,
    Traded,
    NoTrade,
    Halted,
    Closed,
    Reopen,
    LockedUp,
    LockedDown,

    /// <summary>Traded freely during the day but ended it in a buy queue: nobody could buy at that close.</summary>
    ClosedUp,

    /// <summary>Traded freely during the day but ended it in a sell queue: nobody could sell at that close.</summary>
    ClosedDown,
}

/// <summary>
/// One equity over the panel's sessions, from its first row (<see cref="Offset"/>) to the panel's end. Prices are the
/// raw TSETMC prices (what an order fills at); <see cref="Adjust"/> carries the corporate actions (the base price of a
/// session ÷ the previous close), and the prefix sums run over log returns of priced sessions — the reports' return
/// convention — so every trailing statistic is a difference of two entries.
/// </summary>
internal sealed class PanelStock(int offset, int length)
{
    public required long InsCode { get; init; }
    public required string Symbol { get; init; }
    public string? Name { get; init; }
    public string? SectorCode { get; init; }
    public string? SectorName { get; init; }

    public int Offset { get; } = offset;

    public Cell[] Status { get; } = new Cell[length];
    public double[] Open { get; } = new double[length];
    public double[] High { get; } = new double[length];
    public double[] Low { get; } = new double[length];

    /// <summary>The last close, carried over sessions without a row.</summary>
    public double[] Close { get; } = new double[length];

    public double[] Value { get; } = new double[length];
    public float[] Adjust { get; } = new float[length];
    public float[] Eps { get; } = new float[length];
    public float[] LogBuyerPower { get; } = new float[length];

    /// <summary>Log level of the adjusted price: the running sum of log returns.</summary>
    public double[] Level { get; } = new double[length];

    public double[] SumSquares { get; } = new double[length];
    public double[] SumGains { get; } = new double[length];
    public double[] SumLevel { get; } = new double[length];
    public int[] Priced { get; } = new int[length];
    public double[] SumValue { get; } = new double[length];
    public double[] SumFlow { get; } = new double[length];
    public double[] SumFlowValue { get; } = new double[length];
    public int[] FlowDays { get; } = new int[length];

    public bool Has(int t) => t >= Offset && t - Offset < Status.Length;

    public int I(int t) => t - Offset;

    public static bool IsPriced(Cell cell) => cell is Cell.Traded or Cell.Reopen or Cell.LockedUp or Cell.LockedDown or Cell.ClosedUp or Cell.ClosedDown;
}

/// <summary>Raw factor values and trading facts of every equity at one session's close (shared by all simulations).</summary>
internal sealed class FactorSnapshot
{
    public required int T { get; init; }

    /// <summary>Indices into <see cref="SimulationPanel.Stocks"/> of the equities with a row on or before T.</summary>
    public required int[] Stocks { get; init; }

    /// <summary>[factor][j] for stock <see cref="Stocks"/>[j]; NaN where the factor cannot be computed.</summary>
    public required double[][] Values { get; init; }

    public required double[] Value20 { get; init; }
    public required int[] Priced20 { get; init; }
    public required int[] PricedTotal { get; init; }

    /// <summary>Daily volatility (log returns) of the last 60 sessions; NaN with fewer than 30 priced sessions.</summary>
    public required double[] Volatility60 { get; init; }

    public required Cell[] Status { get; init; }
}

/// <summary>
/// The market data a simulation reads, aligned to equity sessions (days on which shares traded): every primary listing
/// of a share, TEDPIX, the free-market dollar and an equal-weighted average share. Built once per data version and
/// shared; factor snapshots are computed on demand and cached, so parallel tuning runs reuse them.
/// </summary>
internal sealed class SimulationPanel
{
    public const int Month = 20;
    public const int Year = 240;
    private const int SnapshotLimit = 600;

    /// <summary>TSETMC rounds the band to the tick, so "at the band" allows this much (as <see cref="DayStatusClassifier"/> does).</summary>
    private const double QueueTolerance = 0.005;

    private static readonly FactorKind[] Factors = Enum.GetValues<FactorKind>();

    // Lazy: parallel tuning runs that ask for the same close build it once. The queue drops the oldest first.
    private readonly ConcurrentDictionary<int, Lazy<FactorSnapshot>> snapshots = new();
    private readonly ConcurrentQueue<int> built = new();

    private SimulationPanel(DateOnly[] sessions, PanelStock[] stocks, double[] tedpix, double[] usd, double[] equalReturn, DateOnly? fundamentalsSince, string stamp)
    {
        Sessions = sessions;
        Stocks = stocks;
        Tedpix = tedpix;
        Usd = usd;
        EqualReturn = equalReturn;
        FundamentalsSince = fundamentalsSince;
        Stamp = stamp;
        ByInsCode = stocks.Select((s, i) => (s.InsCode, i)).ToDictionary(x => x.InsCode, x => x.i);
    }

    public DateOnly[] Sessions { get; }
    public PanelStock[] Stocks { get; }
    public Dictionary<long, int> ByInsCode { get; }

    /// <summary>TEDPIX close per session (carried forward), NaN before its first value.</summary>
    public double[] Tedpix { get; }

    public double[] Usd { get; }

    /// <summary>The average share's return of each session (the mean over the priced equities; 0 with fewer than 20).</summary>
    public double[] EqualReturn { get; }

    public DateOnly? FundamentalsSince { get; }

    /// <summary>The data version the panel was built from (see <see cref="DataStamp"/>).</summary>
    public string Stamp { get; }

    public DateOnly From => Sessions[0];

    /// <summary>Index of the first session on or after <paramref name="date"/> (Sessions.Length when there is none).</summary>
    public int IndexOnOrAfter(DateOnly date)
    {
        var i = Array.BinarySearch(Sessions, date);
        return i >= 0 ? i : ~i;
    }

    /// <summary>Index of the last session on or before <paramref name="date"/> (−1 when there is none).</summary>
    public int IndexOnOrBefore(DateOnly date)
    {
        var i = Array.BinarySearch(Sessions, date);
        return i >= 0 ? i : ~i - 1;
    }

    /// <summary>
    /// Changes whenever a job adds or rewrites what a replay reads — bars (prices, statuses, EPS), flows, TEDPIX, the
    /// dollar, instruments — so a cached panel is never stale. A checksum, not counts: a history row replacing a
    /// market-watch row, or a re-classified day, keeps every count the same. It scans the tables (a few tens of ms).
    /// </summary>
    public static string DataStamp(MarketStore store) =>
        store.Query("""
            SELECT (SELECT count(*) || ':' || coalesce(sum(hash(ins_code, d, close, last, yesterday, trade_count, value, day_status, eps, allowed_low, allowed_high, bid1_price, bid1_volume, ask1_price, ask1_volume)), 0) FROM daily_bar),
                   (SELECT count(*) || ':' || coalesce(sum(hash(ins_code, d, buy_i_value, sell_i_value, buy_i_count, sell_i_count)), 0) FROM client_type_daily),
                   (SELECT count(*) || ':' || coalesce(sum(hash(ins_code, d, close)), 0) FROM index_daily),
                   (SELECT count(*) || ':' || coalesce(sum(hash(series, d, close)), 0) FROM macro_daily),
                   (SELECT count(*) || ':' || coalesce(sum(hash(ins_code, symbol, sector_code, instrument_class, parent_ins_code)), 0) FROM instrument)
            """, [], r => string.Join('|', Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "-" : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture)))).Single();

    /// <summary>
    /// The equity session <paramref name="count"/> sessions before <paramref name="date"/> (the first session when there
    /// are fewer): look-backs count sessions, and closures (45 days in 2026) make calendar estimates fall short.
    /// </summary>
    public static DateOnly SessionsBefore(MarketStore store, DateOnly date, int count) =>
        store.Query($"""
            WITH s AS (SELECT DISTINCT b.d FROM daily_bar b JOIN instrument i USING (ins_code) WHERE {MarketAnalytics.Equity} AND b.trade_count > 0 AND b.d < $date)
            SELECT coalesce((SELECT d FROM s ORDER BY d DESC LIMIT 1 OFFSET $skip), (SELECT min(d) FROM s), $date)
            """, [("date", date), ("skip", Math.Max(0, count - 1))], r => r.GetFieldValue<DateOnly>(0)).Single();

    /// <summary>The equity sessions from <paramref name="from"/> to the latest one; null when the store has none.</summary>
    public static SimulationPanel? Load(MarketStore store, MarketRules rules, DateOnly from, string stamp, CancellationToken cancellationToken)
    {
        var sessions = store.Query($"""
            SELECT DISTINCT b.d FROM daily_bar b JOIN instrument i USING (ins_code)
            WHERE {MarketAnalytics.Equity} AND b.trade_count > 0 AND b.d >= $from ORDER BY b.d
            """, [("from", from)], r => r.GetFieldValue<DateOnly>(0)).ToArray();
        if (sessions.Length == 0)
            return null;

        var index = sessions.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);
        var listed = store.Query("SELECT ins_code, min(d) FROM daily_bar WHERE trade_count > 0 GROUP BY ins_code", [],
            r => (InsCode: r.GetInt64(0), Date: r.GetFieldValue<DateOnly>(1))).ToDictionary(x => x.InsCode, x => x.Date);

        // Priced sessions before the panel starts, so a share's history (the minimum-history filter) counts from its
        // listing whatever the panel's first session is.
        var pricedBefore = store.Query($"""
            WITH listing AS (SELECT ins_code, min(d) AS listed FROM daily_bar WHERE trade_count > 0 GROUP BY ins_code)
            SELECT b.ins_code, count(*) FROM daily_bar b JOIN instrument i USING (ins_code) JOIN listing l USING (ins_code)
            WHERE {MarketAnalytics.Equity} AND b.d < $from AND b.d > l.listed AND b.trade_count > 0 AND b.yesterday > 0 AND b.close > 0
              AND (b.day_status IS NULL OR b.day_status IN ('Traded', 'ReopenAuction', 'QueueLockedUp', 'QueueLockedDown'))
            GROUP BY b.ins_code
            """, [("from", from)], r => (InsCode: r.GetInt64(0), Count: (int)r.GetInt64(1))).ToDictionary(x => x.InsCode, x => x.Count);
        var instruments = store.GetInstruments().ToDictionary(i => i.InsCode);
        var sectorNames = store.Query("SELECT code, name FROM sector", [], r => (Code: r.GetString(0), Name: r.GetString(1))).ToDictionary(s => s.Code, s => s.Name);
        cancellationToken.ThrowIfCancellationRequested();

        // Rows arrive ordered by instrument, so each stock is built from one reusable buffer.
        var stocks = new List<PanelStock>();
        DateOnly? fundamentalsSince = null;
        var buffer = new List<Row>();
        store.Stream($"""
            SELECT b.ins_code, b.d, CAST(b.open AS DOUBLE), CAST(b.high AS DOUBLE), CAST(b.low AS DOUBLE), CAST(b.close AS DOUBLE),
                   CAST(b.yesterday AS DOUBLE), b.trade_count, CAST(b.value AS DOUBLE), b.day_status, CAST(b.eps AS DOUBLE),
                   CAST(c.buy_i_value - c.sell_i_value AS DOUBLE), CAST(c.buy_i_value AS DOUBLE), CAST(c.sell_i_value AS DOUBLE), c.buy_i_count, c.sell_i_count,
                   CAST(b.last AS DOUBLE), CAST(b.allowed_low AS DOUBLE), CAST(b.allowed_high AS DOUBLE),
                   CAST(b.bid1_price AS DOUBLE), b.bid1_volume, CAST(b.ask1_price AS DOUBLE), b.ask1_volume
            FROM daily_bar b JOIN instrument i USING (ins_code)
            LEFT JOIN client_type_daily c ON c.ins_code = b.ins_code AND c.d = b.d
            WHERE {MarketAnalytics.Equity} AND b.d >= $from
            ORDER BY b.ins_code, b.d
            """, [("from", from)], r =>
        {
            var row = new Row(
                r.GetInt64(0), r.GetFieldValue<DateOnly>(1), D(r, 2), D(r, 3), D(r, 4), D(r, 5), D(r, 6), r.GetInt64(7), D(r, 8),
                r.IsDBNull(9) ? null : r.GetString(9), r.IsDBNull(10) ? double.NaN : r.GetDouble(10),
                r.IsDBNull(11) ? double.NaN : r.GetDouble(11), D(r, 12), D(r, 13), r.IsDBNull(14) ? 0 : r.GetInt32(14), r.IsDBNull(15) ? 0 : r.GetInt32(15),
                D(r, 16), D(r, 17), D(r, 18), r.IsDBNull(19) ? null : r.GetDouble(19), r.IsDBNull(20) ? 0 : r.GetInt64(20), r.IsDBNull(21) ? null : r.GetDouble(21), r.IsDBNull(22) ? 0 : r.GetInt64(22));
            if (buffer.Count > 0 && buffer[0].InsCode != row.InsCode)
            {
                Add(buffer);
                buffer.Clear();
            }

            buffer.Add(row);
        });
        Add(buffer);
        cancellationToken.ThrowIfCancellationRequested();

        void Add(List<Row> bars)
        {
            if (bars.Count == 0 || !instruments.TryGetValue(bars[0].InsCode, out var instrument) || !listed.TryGetValue(bars[0].InsCode, out var listing))
                return;
            var firstIndex = bars.FindIndex(b => index.ContainsKey(b.Date));
            if (firstIndex < 0)
                return;

            var offset = index[bars[firstIndex].Date];
            var stock = new PanelStock(offset, sessions.Length - offset)
            {
                InsCode = instrument.InsCode,
                Symbol = instrument.Symbol,
                Name = instrument.Name,
                SectorCode = instrument.SectorCode,
                SectorName = instrument.SectorCode is { } code ? sectorNames.GetValueOrDefault(code) ?? instrument.SectorName : instrument.SectorName,
            };
            Array.Fill(stock.Adjust, 1f);
            Array.Fill(stock.Eps, float.NaN);
            Array.Fill(stock.LogBuyerPower, float.NaN);

            double previousClose = 0, pendingFactor = 1;
            foreach (var bar in bars)
            {
                // A corporate action changes the base price (yesterday) against the previous row's close.
                if (previousClose > 0 && bar.Yesterday > 0 && Math.Abs(bar.Yesterday / previousClose - 1) > 0.0005)
                    pendingFactor *= bar.Yesterday / previousClose;
                if (bar.Close > 0)
                    previousClose = bar.Close;
                if (!index.TryGetValue(bar.Date, out var t))
                    continue;

                var i = t - offset;
                var cell = ToCell(bar.Status, bar.TradeCount);
                if (cell == Cell.Traded)
                    cell = AtClose(bar, instrument.Class, rules);
                stock.Status[i] = cell;
                stock.Open[i] = bar.Open;
                stock.High[i] = bar.High;
                stock.Low[i] = bar.Low;
                stock.Close[i] = bar.Close;
                stock.Value[i] = bar.TradeCount > 0 ? bar.Value : 0;
                stock.Adjust[i] = (float)pendingFactor;
                pendingFactor = 1;
                if (double.IsFinite(bar.Eps) && bar.Eps != 0)
                {
                    stock.Eps[i] = (float)bar.Eps;
                    if (fundamentalsSince is null || bar.Date < fundamentalsSince)
                        fundamentalsSince = bar.Date;
                }

                if (double.IsFinite(bar.Flow))
                {
                    stock.SumFlow[i] = bar.Flow;
                    stock.SumFlowValue[i] = bar.TradeCount > 0 ? bar.Value : 0;
                    stock.FlowDays[i] = 1;
                    if (bar.BuyCount > 0 && bar.SellCount > 0 && bar.BuyValue > 0 && bar.SellValue > 0)
                        stock.LogBuyerPower[i] = (float)Math.Log(bar.BuyValue / bar.BuyCount / (bar.SellValue / bar.SellCount));
                }

                // The first traded day of a listing is never a return (TSETMC's placeholder bar sits at the nominal price).
                if (PanelStock.IsPriced(cell) && bar.Yesterday > 0 && bar.Close > 0 && bar.Date > listing)
                {
                    var r = Math.Log(bar.Close / bar.Yesterday);
                    stock.Level[i] = r;
                    stock.SumSquares[i] = r * r;
                    stock.SumGains[i] = Math.Max(r, 0);
                    stock.Priced[i] = 1;
                }
            }

            // Carry prices over sessions without a row, then turn the per-session values into running sums.
            for (var i = 0; i < stock.Status.Length; i++)
            {
                if (i > 0)
                {
                    if (stock.Status[i] == Cell.None)
                        stock.Close[i] = stock.Close[i - 1];
                    stock.Level[i] += stock.Level[i - 1];
                    stock.SumSquares[i] += stock.SumSquares[i - 1];
                    stock.SumGains[i] += stock.SumGains[i - 1];
                    stock.Priced[i] += stock.Priced[i - 1];
                    stock.SumValue[i] = stock.SumValue[i - 1] + stock.Value[i];
                    stock.SumFlow[i] += stock.SumFlow[i - 1];
                    stock.SumFlowValue[i] += stock.SumFlowValue[i - 1];
                    stock.FlowDays[i] += stock.FlowDays[i - 1];
                }
                else
                {
                    stock.SumValue[i] = stock.Value[i];
                    stock.Priced[i] += pricedBefore.GetValueOrDefault(stock.InsCode);
                }

                stock.SumLevel[i] = stock.Level[i] + (i > 0 ? stock.SumLevel[i - 1] : 0);
            }

            stocks.Add(stock);
        }

        var tedpix = Align(sessions, store.GetIndexBars(TsetmcEndpoints.TedpixInsCode).Select(b => (b.Date, (double)b.Close)));
        var usd = Align(sessions, store.GetMacroDaily(TgjuSeries.UsdFreeMarket, TgjuClient.Source).Select(b => (b.Date, (double)b.Close)));

        // The average share: the mean simple return of the priced equities of each session.
        var equal = new double[sessions.Length];
        for (var t = 1; t < sessions.Length; t++)
        {
            double sum = 0;
            var n = 0;
            foreach (var s in stocks)
            {
                if (!s.Has(t) || !s.Has(t - 1))
                    continue;
                var i = s.I(t);
                if (s.Priced[i] == s.Priced[i - 1])
                    continue;
                sum += Math.Exp(s.Level[i] - s.Level[i - 1]) - 1;
                n++;
            }

            equal[t] = n >= 20 ? sum / n : 0;
        }

        return new SimulationPanel(sessions, [.. stocks], tedpix, usd, equal, fundamentalsSince, stamp);
    }

    private static double D(DuckDB.NET.Data.DuckDBDataReader r, int i) => r.IsDBNull(i) ? 0 : r.GetDouble(i);

    private static Cell ToCell(string? status, long trades) => status switch
    {
        "Traded" when trades > 0 => Cell.Traded,
        "ReopenAuction" when trades > 0 => Cell.Reopen,
        "QueueLockedUp" when trades > 0 => Cell.LockedUp,
        "QueueLockedDown" when trades > 0 => Cell.LockedDown,
        "SymbolHalted" => Cell.Halted,
        "MarketClosed" => Cell.Closed,
        null when trades > 0 => Cell.Traded,
        _ => Cell.NoTrade,
    };

    /// <summary>
    /// Whether a traded session ended in a queue. The end-of-day book says so when it was collected: a bid at the ceiling
    /// with no offer inside the band, or the mirror at the floor. History has only the last trade, so a last trade at the
    /// band and at the day's extreme counts: on the book of 2026-11-04 that caught all 242 real queues, and about 85% of
    /// what it caught was one. The rest closed at the band with offers left and wait a session they did not have to.
    /// </summary>
    private static Cell AtClose(in Row bar, InstrumentClass instrumentClass, MarketRules rules)
    {
        if (bar.AllowedHigh > 0 && bar.AllowedLow > 0 && (bar.Bid is not null || bar.Ask is not null))
        {
            if (bar.Bid >= bar.AllowedHigh && !(bar.AskVolume > 0 && bar.Ask <= bar.AllowedHigh))
                return Cell.ClosedUp;
            return bar.Ask > 0 && bar.Ask <= bar.AllowedLow && !(bar.BidVolume > 0 && bar.Bid >= bar.AllowedLow) ? Cell.ClosedDown : Cell.Traded;
        }

        var atHigh = bar.Last >= bar.High;
        var atLow = bar.Last <= bar.Low;
        if (bar.Last <= 0 || bar.Yesterday <= 0 || !atHigh && !atLow)
            return Cell.Traded;

        var limit = (double)rules.GetPriceLimit(instrumentClass, bar.Date).Value;
        if (atHigh && bar.Last >= bar.Yesterday * (1 + limit) * (1 - QueueTolerance))
            return Cell.ClosedUp;
        return atLow && bar.Last <= bar.Yesterday * (1 - limit) * (1 + QueueTolerance) ? Cell.ClosedDown : Cell.Traded;
    }

    private static double[] Align(DateOnly[] sessions, IEnumerable<(DateOnly Date, double Value)> series)
    {
        var points = series.Where(p => p.Value > 0).OrderBy(p => p.Date).ToList();
        var result = new double[sessions.Length];
        var j = -1;
        for (var t = 0; t < sessions.Length; t++)
        {
            while (j + 1 < points.Count && points[j + 1].Date <= sessions[t])
                j++;
            result[t] = j >= 0 ? points[j].Value : double.NaN;
        }

        return result;
    }

    // ---------------------------------------------------------------- factors

    public FactorSnapshot Snapshot(int t)
    {
        if (snapshots.TryGetValue(t, out var cached))
            return cached.Value;
        var lazy = snapshots.GetOrAdd(t, k => new Lazy<FactorSnapshot>(() => Build(k)));
        built.Enqueue(t);
        while (snapshots.Count > SnapshotLimit && built.TryDequeue(out var oldest))
            snapshots.TryRemove(oldest, out _);
        return lazy.Value;
    }

    private FactorSnapshot Build(int t)
    {
        var members = Enumerable.Range(0, Stocks.Length).Where(k => Stocks[k].Has(t) && Stocks[k].I(t) >= Month).ToArray();
        var values = Factors.Select(_ => new double[members.Length]).ToArray();
        var value20 = new double[members.Length];
        var priced20 = new int[members.Length];
        var pricedTotal = new int[members.Length];
        var volatility = new double[members.Length];
        var status = new Cell[members.Length];
        var yields = new double[members.Length];

        for (var j = 0; j < members.Length; j++)
        {
            var s = Stocks[members[j]];
            var i = s.I(t);
            status[j] = s.Status[i];
            value20[j] = (s.SumValue[i] - s.SumValue[i - Month]) / Month;
            priced20[j] = s.Priced[i] - s.Priced[i - Month];
            pricedTotal[j] = s.Priced[i];
            volatility[j] = Volatility(s, i, 60, 30);
            var eps = LatestEps(s, i, 120, out _);
            yields[j] = double.IsFinite(eps) && s.Close[i] > 0 ? eps / s.Close[i] : double.NaN;
            foreach (var factor in Factors)
                values[(int)factor][j] = Compute(factor, s, i, volatility[j], yields[j]);
        }

        // Sector value: earnings yield against the sector's median earnings yield at the same close.
        var sectorValue = values[(int)FactorKind.SectorValue];
        foreach (var sector in Enumerable.Range(0, members.Length).Where(j => double.IsFinite(yields[j])).GroupBy(j => Stocks[members[j]].SectorCode ?? string.Empty))
        {
            var list = sector.ToList();
            if (list.Count < 3)
                continue;
            var sorted = list.Select(j => yields[j]).Order().ToList();
            var median = sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
            foreach (var j in list)
                sectorValue[j] = yields[j] - median;
        }

        return new FactorSnapshot
        {
            T = t,
            Stocks = members,
            Values = values,
            Value20 = value20,
            Priced20 = priced20,
            PricedTotal = pricedTotal,
            Volatility60 = volatility,
            Status = status,
        };
    }

    private static double Compute(FactorKind factor, PanelStock s, int i, double volatility60, double earningsYield)
    {
        var level = s.Level;
        switch (factor)
        {
            case FactorKind.Momentum:
                return Enough(s, i, Year, Year / 2) ? level[i - Month] - level[i - Year] : double.NaN;
            case FactorKind.Trend:
                return Enough(s, i, 200, 100) ? level[i] - (s.SumLevel[i] - s.SumLevel[i - 200]) / 200 : double.NaN;
            case FactorKind.High52Week:
                return Enough(s, i, Year, Year / 2) ? level[i] - Max(level, i - Year + 1, i) : double.NaN;
            case FactorKind.Breakout:
                return Enough(s, i, 56, 28) ? level[i] - Max(level, i - 55, i - 1) : double.NaN;
            case FactorKind.ShortReversal:
                return Enough(s, i, Month, Month / 2) ? -(level[i] - level[i - Month]) : double.NaN;
            case FactorKind.RsiPullback:
            {
                if (!Enough(s, i, 200, 100))
                    return double.NaN;
                var gains = s.SumGains[i] - s.SumGains[i - 14];
                var losses = gains - (level[i] - level[i - 14]);
                var rsi = gains + losses > 0 ? 100 * gains / (gains + losses) : 50;
                var uptrend = level[i] > (s.SumLevel[i] - s.SumLevel[i - 200]) / 200;
                return (uptrend ? 100 : 0) + 100 - rsi;
            }

            case FactorKind.Macd:
                return Enough(s, i, 150, 75) ? MacdHistogram(level, i - 149, i) : double.NaN;
            case FactorKind.LowVolatility:
                return double.IsFinite(volatility60) ? -volatility60 : double.NaN;
            case FactorKind.EarningsYield:
                return earningsYield;
            case FactorKind.SectorValue:
                return double.NaN; // filled across the sector after every share's yield is known
            case FactorKind.EpsRevision:
            {
                var now = LatestEps(s, i, 20, out var at);
                if (!double.IsFinite(now) || at - 60 < 0)
                    return double.NaN;
                var before = LatestEps(s, at - 60, 60, out _);
                return double.IsFinite(before) && before > 0 ? now / before - 1 : double.NaN;
            }

            case FactorKind.IndividualFlow:
            {
                var value = s.SumFlowValue[i] - s.SumFlowValue[i - Month];
                return s.FlowDays[i] - s.FlowDays[i - Month] >= Month / 2 && value > 0 ? (s.SumFlow[i] - s.SumFlow[i - Month]) / value : double.NaN;
            }

            case FactorKind.BuyerPower:
            {
                double sum = 0;
                var n = 0;
                for (var k = i - 4; k <= i; k++)
                {
                    if (float.IsFinite(s.LogBuyerPower[k]))
                    {
                        sum += s.LogBuyerPower[k];
                        n++;
                    }
                }

                return n >= 3 ? sum / n : double.NaN;
            }

            case FactorKind.Consistency:
            {
                if (!Enough(s, i, Year, Year / 2))
                    return double.NaN;
                var up = 0;
                for (var m = 0; m < 12; m++)
                {
                    if (level[i - m * Month] - level[i - (m + 1) * Month] > 0)
                        up++;
                }

                return up / 12.0 + Math.Tanh(level[i] - level[i - Year]) * 1e-4;
            }

            case FactorKind.RiskAdjusted:
            {
                if (!Enough(s, i, Year, Year / 2))
                    return double.NaN;
                var volatility = Volatility(s, i, Year, Year / 2);
                return volatility > 0 ? (level[i] - level[i - Year]) / (volatility * Math.Sqrt(Year)) : double.NaN;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(factor));
        }
    }

    /// <summary>The stock has <paramref name="window"/> sessions of history before i and at least <paramref name="priced"/> priced ones among them.</summary>
    private static bool Enough(PanelStock s, int i, int window, int priced) => i >= window && s.Priced[i] - s.Priced[i - window] >= priced;

    /// <summary>Sample standard deviation of the log returns of the priced sessions in (i − window, i].</summary>
    internal static double Volatility(PanelStock s, int i, int window, int minimum)
    {
        if (i < window)
            return double.NaN;
        var n = s.Priced[i] - s.Priced[i - window];
        if (n < minimum || n < 2)
            return double.NaN;
        var sum = s.Level[i] - s.Level[i - window];
        var squares = s.SumSquares[i] - s.SumSquares[i - window];
        return Math.Sqrt(Math.Max(0, (squares - sum * sum / n) / (n - 1)));
    }

    /// <summary>The latest EPS published on or before i, at most <paramref name="age"/> sessions old.</summary>
    private static double LatestEps(PanelStock s, int i, int age, out int at)
    {
        for (var k = i; k >= Math.Max(0, i - age); k--)
        {
            if (float.IsFinite(s.Eps[k]))
            {
                at = k;
                return s.Eps[k];
            }
        }

        at = -1;
        return double.NaN;
    }

    private static double Max(double[] values, int from, int to)
    {
        var max = double.MinValue;
        for (var k = from; k <= to; k++)
            max = Math.Max(max, values[k]);
        return max;
    }

    private static double MacdHistogram(double[] level, int from, int to)
    {
        double fast = level[from], slow = level[from], signal = 0;
        const double a12 = 2.0 / 13, a26 = 2.0 / 27, a9 = 2.0 / 10;
        for (var k = from + 1; k <= to; k++)
        {
            fast += a12 * (level[k] - fast);
            slow += a26 * (level[k] - slow);
            signal += a9 * (fast - slow - signal);
        }

        return fast - slow - signal;
    }

    private readonly record struct Row(
        long InsCode, DateOnly Date, double Open, double High, double Low, double Close, double Yesterday, long TradeCount, double Value,
        string? Status, double Eps, double Flow, double BuyValue, double SellValue, int BuyCount, int SellCount,
        double Last, double AllowedLow, double AllowedHigh, double? Bid, long BidVolume, double? Ask, long AskVolume);
}
