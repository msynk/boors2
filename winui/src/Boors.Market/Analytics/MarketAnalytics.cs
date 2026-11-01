using Boors.Market.Domain;
using Boors.Market.Sources.Macro;
using Boors.Market.Sources.Tsetmc;
using Boors.Market.Storage;
using Boors.Market.Text;
using DuckDB.NET.Data;

namespace Boors.Market.Analytics;

/// <summary>
/// Read-side analytics over the market store: the home screen's overview, breadth, sectors, movers, per-instrument
/// metrics and screens. Everything is computed from stored bars with the same conventions as the backtests:
/// returns are <c>Close / Yesterday − 1</c> on priced days only, and secondary listings are excluded.
/// </summary>
public sealed class MarketAnalytics(MarketStore store)
{
    /// <summary>Primary listings of shares (TSE, IFB, base market) — the universe for breadth, flows and screens.</summary>
    public const string EquityClasses = "('tse_stock', 'ifb_stock', 'ifb_base_yellow', 'ifb_base_orange', 'ifb_base_red')";

    public const string FundClasses = "('etf_equity', 'etf_fixed_income', 'etf_real_estate', 'etf_mixed', 'fund_leveraged', 'fund_gold_silver', 'fund_of_funds')";

    internal const string Equity = $"i.parent_ins_code IS NULL AND i.instrument_class IN {EquityClasses}";

    internal const string Return = """
        CASE WHEN b.trade_count > 0 AND b.yesterday > 0 AND b.day_status IN ('Traded', 'ReopenAuction', 'QueueLockedUp', 'QueueLockedDown')
             THEN CAST(b.close AS DOUBLE) / CAST(b.yesterday AS DOUBLE) - 1 END
        """;

    private static readonly long[] HeadlineIndices = [TsetmcEndpoints.TedpixInsCode, 67130298613737946];

    /// <summary>The most recent sessions (days on which anything traded), newest first.</summary>
    public IReadOnlyList<DateOnly> GetSessions(int count) =>
        store.Query("SELECT DISTINCT d FROM daily_bar WHERE trade_count > 0 ORDER BY d DESC LIMIT $n", [("n", count)], r => r.GetFieldValue<DateOnly>(0));

    public DateOnly? LatestSession() => GetSessions(1).Cast<DateOnly?>().FirstOrDefault();

    public MarketOverview? GetOverview(DateOnly? session = null)
    {
        if ((session ?? LatestSession()) is not { } d)
            return null;

        var equity = store.Query($"""
            SELECT
                coalesce(sum(b.value) FILTER (WHERE b.trade_count > 0), 0),
                CAST(coalesce(sum(b.trade_count), 0) AS BIGINT),
                count(*) FILTER (WHERE b.trade_count > 0 AND b.close > b.yesterday),
                count(*) FILTER (WHERE b.trade_count > 0 AND b.close < b.yesterday),
                count(*) FILTER (WHERE b.trade_count > 0 AND b.close = b.yesterday),
                count(*) FILTER (WHERE b.trade_count = 0),
                count(*) FILTER (WHERE b.day_status = 'SymbolHalted'),
                count(*) FILTER (WHERE b.bid1_price IS NOT NULL),
                count(*) FILTER (WHERE b.allowed_high > 0 AND b.bid1_price >= b.allowed_high AND coalesce(b.ask1_volume, 0) = 0),
                sum(CAST(b.bid1_volume AS DECIMAL(38,4)) * b.bid1_price) FILTER (WHERE b.allowed_high > 0 AND b.bid1_price >= b.allowed_high AND coalesce(b.ask1_volume, 0) = 0),
                count(*) FILTER (WHERE b.allowed_low > 0 AND b.ask1_price > 0 AND b.ask1_price <= b.allowed_low AND coalesce(b.bid1_volume, 0) = 0),
                sum(CAST(b.ask1_volume AS DECIMAL(38,4)) * b.ask1_price) FILTER (WHERE b.allowed_low > 0 AND b.ask1_price > 0 AND b.ask1_price <= b.allowed_low AND coalesce(b.bid1_volume, 0) = 0),
                count(*) FILTER (WHERE b.day_status = 'QueueLockedUp'),
                count(*) FILTER (WHERE b.day_status = 'QueueLockedDown')
            FROM daily_bar b JOIN instrument i USING (ins_code)
            WHERE b.d = $d AND {Equity}
            """, [("d", d)], r => new
        {
            Value = r.GetDecimal(0),
            Trades = r.GetInt64(1),
            Advancers = (int)r.GetInt64(2),
            Decliners = (int)r.GetInt64(3),
            Unchanged = (int)r.GetInt64(4),
            NotTraded = (int)r.GetInt64(5),
            Halted = (int)r.GetInt64(6),
            HasSnapshot = r.GetInt64(7) > 0,
            BuyQueues = (int)r.GetInt64(8),
            BuyQueueValue = r.IsDBNull(9) ? 0m : r.GetDecimal(9),
            SellQueues = (int)r.GetInt64(10),
            SellQueueValue = r.IsDBNull(11) ? 0m : r.GetDecimal(11),
            LockedUp = (int)r.GetInt64(12),
            LockedDown = (int)r.GetInt64(13),
        }).Single();

        var fundValue = store.Query($"""
            SELECT coalesce(sum(b.value), 0) FROM daily_bar b JOIN instrument i USING (ins_code)
            WHERE b.d = $d AND b.trade_count > 0 AND i.instrument_class IN {FundClasses}
            """, [("d", d)], r => r.GetDecimal(0)).Single();

        var flows = store.Query($"""
            SELECT coalesce(sum(c.buy_i_value - c.sell_i_value), 0), sum(c.buy_i_value), CAST(sum(c.buy_i_count) AS BIGINT), sum(c.sell_i_value), CAST(sum(c.sell_i_count) AS BIGINT), coalesce(bool_or(c.values_estimated), false)
            FROM client_type_daily c JOIN instrument i USING (ins_code)
            WHERE c.d = $d AND {Equity}
            """, [("d", d)], r => new
        {
            Net = r.GetDecimal(0),
            PerCapitaBuy = r.IsDBNull(1) || r.IsDBNull(2) || Convert.ToInt64(r.GetValue(2), CultureInfo.InvariantCulture) == 0 ? (decimal?)null : r.GetDecimal(1) / Convert.ToInt64(r.GetValue(2), CultureInfo.InvariantCulture),
            PerCapitaSell = r.IsDBNull(3) || r.IsDBNull(4) || Convert.ToInt64(r.GetValue(4), CultureInfo.InvariantCulture) == 0 ? (decimal?)null : r.GetDecimal(3) / Convert.ToInt64(r.GetValue(4), CultureInfo.InvariantCulture),
            Estimated = r.GetBoolean(5),
        }).Single();

        var indices = GetIndexMoves(d);
        var usd = GetMacroQuote(TgjuSeries.UsdFreeMarket, d);
        var tedpix = indices.FirstOrDefault(i => i.InsCode == TsetmcEndpoints.TedpixInsCode);

        return new MarketOverview
        {
            Session = d,
            Indices = indices,
            EquityValue = equity.Value,
            FundValue = fundValue,
            EquityTrades = equity.Trades,
            Advancers = equity.Advancers,
            Decliners = equity.Decliners,
            Unchanged = equity.Unchanged,
            NotTraded = equity.NotTraded,
            Halted = equity.Halted,
            BuyQueues = equity.HasSnapshot ? equity.BuyQueues : equity.LockedUp,
            BuyQueueValue = equity.HasSnapshot ? equity.BuyQueueValue : null,
            SellQueues = equity.HasSnapshot ? equity.SellQueues : equity.LockedDown,
            SellQueueValue = equity.HasSnapshot ? equity.SellQueueValue : null,
            IndividualNetFlow = flows.Net,
            IndividualPerCapitaBuy = flows.PerCapitaBuy,
            IndividualPerCapitaSell = flows.PerCapitaSell,
            FlowValuesEstimated = flows.Estimated,
            Usd = usd,
            Coin = GetMacroQuote(TgjuSeries.EmamiCoin, d),
            TedpixUsdChangePercent = tedpix?.ChangePercent is { } change && usd?.ChangePercent is { } usdChange ? ((1 + change / 100) / (1 + usdChange / 100) - 1) * 100 : null,
        };
    }

    public IReadOnlyList<BreadthDay> GetBreadth(int sessions)
    {
        var dates = GetSessions(sessions);
        if (dates.Count == 0)
            return [];

        return store.Query($"""
            SELECT b.d,
                count(*) FILTER (WHERE b.trade_count > 0 AND b.close > b.yesterday),
                count(*) FILTER (WHERE b.trade_count > 0 AND b.close < b.yesterday),
                count(*) FILTER (WHERE b.trade_count > 0 AND b.close = b.yesterday),
                count(*) FILTER (WHERE b.trade_count = 0),
                count(*) FILTER (WHERE b.day_status = 'SymbolHalted'),
                count(*) FILTER (WHERE b.day_status = 'QueueLockedUp'),
                count(*) FILTER (WHERE b.day_status = 'QueueLockedDown'),
                coalesce(sum(b.value) FILTER (WHERE b.trade_count > 0), 0)
            FROM daily_bar b JOIN instrument i USING (ins_code)
            WHERE {Equity} AND b.d >= $from
            GROUP BY b.d ORDER BY b.d
            """, [("from", dates[^1])], r => new BreadthDay(
                r.GetFieldValue<DateOnly>(0), (int)r.GetInt64(1), (int)r.GetInt64(2), (int)r.GetInt64(3), (int)r.GetInt64(4), (int)r.GetInt64(5),
                (int)r.GetInt64(6), (int)r.GetInt64(7), r.GetDecimal(8)))
            .Where(b => dates.Contains(b.Date))
            .ToList();
    }

    public IReadOnlyList<SectorPerformance> GetSectors(DateOnly? session = null)
    {
        if ((session ?? LatestSession()) is not { } d)
            return [];

        return store.Query($"""
            WITH x AS (
                SELECT i.sector_code, i.sector_name, b.ins_code, b.trade_count, b.value, b.close, b.yesterday,
                       {Return} AS ret,
                       CAST(b.yesterday AS DOUBLE) * CAST(i.total_shares AS DOUBLE) AS cap
                FROM daily_bar b JOIN instrument i USING (ins_code)
                WHERE b.d = $d AND {Equity} AND i.sector_code IS NOT NULL),
            f AS (SELECT ins_code, buy_i_value - sell_i_value AS net FROM client_type_daily WHERE d = $d)
            SELECT x.sector_code, coalesce(any_value(s.name), max(x.sector_name)),
                   count(*), count(*) FILTER (WHERE x.trade_count > 0),
                   coalesce(sum(x.value) FILTER (WHERE x.trade_count > 0), 0),
                   sum(x.ret * x.cap) / nullif(sum(x.cap) FILTER (WHERE x.ret IS NOT NULL), 0),
                   median(x.ret),
                   coalesce(sum(f.net), 0),
                   count(*) FILTER (WHERE x.trade_count > 0 AND x.close > x.yesterday),
                   count(*) FILTER (WHERE x.trade_count > 0 AND x.close < x.yesterday)
            FROM x LEFT JOIN f USING (ins_code) LEFT JOIN sector s ON s.code = x.sector_code
            GROUP BY x.sector_code
            ORDER BY 5 DESC
            """, [("d", d)], r => new SectorPerformance
        {
            SectorCode = r.GetString(0),
            SectorName = r.IsDBNull(1) ? null : r.GetString(1),
            Instruments = (int)r.GetInt64(2),
            Traded = (int)r.GetInt64(3),
            Value = r.GetDecimal(4),
            WeightedReturn = r.IsDBNull(5) ? null : r.GetDouble(5),
            MedianReturn = r.IsDBNull(6) ? null : r.GetDouble(6),
            IndividualNetFlow = r.GetDecimal(7),
            Advancers = (int)r.GetInt64(8),
            Decliners = (int)r.GetInt64(9),
        });
    }

    public IReadOnlyList<Mover> GetMovers(MoverKind kind, int take = 15, DateOnly? session = null)
    {
        if ((session ?? LatestSession()) is not { } d)
            return [];

        var order = kind switch
        {
            MoverKind.Gainers => "ret DESC NULLS LAST",
            MoverKind.Losers => "ret ASC NULLS LAST",
            MoverKind.Value => "b.value DESC",
            MoverKind.Inflow => "net DESC NULLS LAST",
            MoverKind.Outflow => "net ASC NULLS LAST",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        return store.Query($"""
            SELECT b.ins_code, i.symbol, i.name, i.instrument_class, b.day_status, b.close, {Return} AS ret, b.value, c.buy_i_value - c.sell_i_value AS net
            FROM daily_bar b JOIN instrument i USING (ins_code)
            LEFT JOIN client_type_daily c ON c.ins_code = b.ins_code AND c.d = b.d
            WHERE b.d = $d AND {Equity} AND b.trade_count > 0
            ORDER BY {order}
            LIMIT $take
            """, [("d", d), ("take", take)], r => new Mover(
                r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                r.GetDecimal(5), r.IsDBNull(6) ? null : r.GetDouble(6) * 100, r.GetDecimal(7), r.IsDBNull(8) ? null : r.GetDecimal(8)));
    }

    public InstrumentMetrics? GetInstrumentMetrics(long insCode)
    {
        var instrument = store.GetInstrument(insCode);
        if (instrument is null)
            return null;

        var sectorName = instrument.SectorName ?? (instrument.SectorCode is { } code
            ? store.Query("SELECT name FROM sector WHERE code = $code", [("code", code)], r => r.GetString(0)).FirstOrDefault()
            : null);
        var stored = store.GetDailyBars(insCode);
        var metrics = new InstrumentMetrics
        {
            InsCode = insCode,
            Symbol = instrument.Symbol,
            Name = instrument.Name,
            InstrumentClass = instrument.Class.ToRuleKey(),
            SectorCode = instrument.SectorCode,
            SectorName = sectorName,
        };
        if (stored.Count == 0)
            return metrics;

        var last = stored[^1];
        var asOf = last.Bar.Date;
        var adjusted = PointInTime.AdjustFromBars(stored.Select(s => (s.Bar, s.Status)).ToList(), asOf);
        var priced = adjusted.Where(a => a.Return is not null || a.Status is DayStatus.Traded).ToList();
        var lastClose = adjusted[^1].AdjustedClose;

        decimal? CloseOnOrBefore(DateOnly date) => adjusted.LastOrDefault(a => a.Date <= date)?.AdjustedClose;
        double? ReturnSince(DateOnly start) => CloseOnOrBefore(start) is { } c && c > 0 ? (double)(lastClose / c - 1) : null;

        var yearWindow = adjusted.Where(a => a.Date > asOf.AddDays(-365) && (a.Status is null or DayStatus.Traded or DayStatus.ReopenAuction or DayStatus.QueueLockedUp or DayStatus.QueueLockedDown)).ToList();
        var high = yearWindow.Count > 0 ? yearWindow.Max(a => a.AdjustedClose) : (decimal?)null;
        var low = yearWindow.Count > 0 ? yearWindow.Min(a => a.AdjustedClose) : (decimal?)null;

        var flows = store.GetClientTypes(insCode);
        var recentSessions = stored.Where(s => s.Bar.TradeCount > 0).TakeLast(20).ToList();
        var recentDates = recentSessions.Select(s => s.Bar.Date).ToHashSet();
        var lastFlow = flows.LastOrDefault(f => f.Date == asOf);
        var powerHistory = flows.TakeLast(250).Select(f => f.BuyerPower).Where(p => p is not null).Select(p => p!.Value).ToList();

        var usd = store.GetMacroDaily(TgjuSeries.UsdFreeMarket, TgjuClient.Source);
        decimal? UsdOn(DateOnly date) => usd.LastOrDefault(u => u.Date <= date)?.Close;
        var oneYear = ReturnSince(asOf.AddDays(-365));
        double? usdReturn = oneYear is { } r1 && UsdOn(asOf) is { } usdNow && UsdOn(asOf.AddDays(-365)) is { } usdThen && usdThen > 0
            ? (1 + r1) / (double)(usdNow / usdThen) - 1
            : null;

        return metrics with
        {
            AsOf = asOf,
            DayStatus = last.Status?.ToString(),
            Close = last.Bar.Close,
            ChangePercent = PointInTime.DailyReturn(last.Bar, last.Status) is { } change ? (double)change * 100 : null,
            MarketCap = instrument.TotalShares is { } shares ? last.Bar.Close * shares : null,
            High52Week = high,
            Low52Week = low,
            FromHigh52WeekPercent = high is > 0 ? (double)(lastClose / high.Value - 1) * 100 : null,
            Returns = new Dictionary<string, double?>
            {
                ["1w"] = ReturnSince(asOf.AddDays(-7)),
                ["1m"] = ReturnSince(asOf.AddDays(-30)),
                ["3m"] = ReturnSince(asOf.AddDays(-91)),
                ["6m"] = ReturnSince(asOf.AddDays(-182)),
                ["1y"] = oneYear,
            },
            UsdReturn1Year = usdReturn,
            AverageValue20 = recentSessions.Count > 0 ? recentSessions.Average(s => s.Bar.Value) : null,
            IndividualNetFlow20 = flows.Where(f => recentDates.Contains(f.Date)).Sum(f => f.IndividualNetValue),
            PerCapitaBuy = lastFlow?.PerCapitaBuy,
            PerCapitaSell = lastFlow?.PerCapitaSell,
            BuyerPower = lastFlow?.BuyerPower,
            BuyerPowerPercentile = lastFlow?.BuyerPower is { } power && powerHistory.Count >= 20
                ? powerHistory.Count(p => p <= power) / (double)powerHistory.Count
                : null,
        };
    }

    /// <summary>Ranks equities by one factor as of <paramref name="asOf"/> (default: latest session), best first.</summary>
    public IReadOnlyList<ScreenRow> Screen(ScreenKind kind, int take = 30, DateOnly? asOf = null)
    {
        if ((asOf ?? LatestSession()) is not { } d)
            return [];

        var sessions = GetSessions(400).Where(s => s <= d).ToList();
        (string Sql, (string, object?)[] Parameters) query = kind switch
        {
            ScreenKind.Momentum6To1 => ($"""
                WITH r AS (SELECT b.ins_code, {Return} AS ret FROM daily_bar b JOIN instrument i USING (ins_code)
                           WHERE {Equity} AND b.d > $from AND b.d <= $to)
                SELECT ins_code, exp(sum(ln(1 + ret))) - 1, count(ret) FROM r GROUP BY ins_code HAVING count(ret) >= 60
                """, [("from", d.AddDays(-182)), ("to", d.AddDays(-30))]),
            ScreenKind.High52WeekProximity => ($"""
                WITH r AS (SELECT b.ins_code, b.d, {Return} AS ret FROM daily_bar b JOIN instrument i USING (ins_code)
                           WHERE {Equity} AND b.d > $from AND b.d <= $to),
                     lvl AS (SELECT ins_code, d, ret, exp(sum(ln(1 + coalesce(ret, 0))) OVER (PARTITION BY ins_code ORDER BY d)) AS level FROM r)
                SELECT ins_code, last(level ORDER BY d) / max(level), count(ret) FROM lvl GROUP BY ins_code HAVING count(ret) >= 120
                """, [("from", d.AddDays(-365)), ("to", d)]),
            ScreenKind.IndividualFlowIntensity20 => ($"""
                SELECT b.ins_code, sum(c.buy_i_value - c.sell_i_value) / nullif(sum(b.value), 0), count(*)
                FROM daily_bar b JOIN instrument i USING (ins_code) JOIN client_type_daily c ON c.ins_code = b.ins_code AND c.d = b.d
                WHERE {Equity} AND b.trade_count > 0 AND b.d > $from AND b.d <= $to
                GROUP BY b.ins_code HAVING count(*) >= 15
                """, [("from", sessions.Count > 20 ? sessions[20] : DateOnly.MinValue), ("to", d)]),
            ScreenKind.BuyerPower5 => ($"""
                SELECT c.ins_code, avg((c.buy_i_value / c.buy_i_count) / (c.sell_i_value / c.sell_i_count)), count(*)
                FROM client_type_daily c JOIN instrument i USING (ins_code)
                WHERE {Equity} AND c.buy_i_count > 0 AND c.sell_i_count > 0 AND c.sell_i_value > 0 AND c.d > $from AND c.d <= $to
                GROUP BY c.ins_code HAVING count(*) >= 4
                """, [("from", sessions.Count > 5 ? sessions[5] : DateOnly.MinValue), ("to", d)]),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var scores = store.Query(query.Sql, query.Parameters, r => (InsCode: r.GetInt64(0), Score: r.IsDBNull(1) ? double.NaN : Convert.ToDouble(r.GetValue(1), CultureInfo.InvariantCulture), Count: (int)Convert.ToInt64(r.GetValue(2), CultureInfo.InvariantCulture)))
            .Where(s => !double.IsNaN(s.Score))
            .OrderByDescending(s => s.Score)
            .Take(take)
            .ToList();
        var instruments = store.GetInstruments().ToDictionary(i => i.InsCode);
        return scores
            .Where(s => instruments.ContainsKey(s.InsCode))
            .Select(s => instruments[s.InsCode] is var i ? new ScreenRow(s.InsCode, i.Symbol, i.Name, i.Class.ToRuleKey(), i.SectorCode, s.Score, s.Count) : null!)
            .ToList();
    }

    /// <summary>Symbol and name search for the command palette: exact symbol first, then prefix, then contains.</summary>
    public IReadOnlyList<Instrument> Search(string text, int take = 20)
    {
        var key = PersianText.MatchKey(text);
        if (key.Length == 0)
            return [];

        return store.GetInstruments()
            .Select(i => (Instrument: i, Symbol: PersianText.MatchKey(i.Symbol), Name: PersianText.MatchKey(i.Name ?? string.Empty)))
            .Select(x => (x.Instrument, Rank: x.Symbol == key ? 0 : x.Symbol.StartsWith(key, StringComparison.Ordinal) ? 1 : x.Name.Contains(key, StringComparison.Ordinal) || x.Symbol.Contains(key, StringComparison.Ordinal) ? 2 : 9))
            .Where(x => x.Rank < 9)
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Instrument.ParentInsCode is not null)
            .ThenBy(x => x.Instrument.Symbol.Length)
            .Take(take)
            .Select(x => x.Instrument)
            .ToList();
    }

    private IReadOnlyList<IndexMove> GetIndexMoves(DateOnly d)
    {
        var codes = HeadlineIndices.ToList();
        var ifb = store.GetIndexInfo().FirstOrDefault(i => i.Flow == 2 && PersianText.MatchKey(i.Name) == PersianText.MatchKey("شاخص کل فرابورس"));
        if (ifb.InsCode != 0)
            codes.Add(ifb.InsCode);

        var names = store.GetIndexInfo().ToDictionary(i => i.InsCode, i => i.Name);
        return store.Query($"""
            SELECT ins_code, close, lag(close) OVER (PARTITION BY ins_code ORDER BY d) AS prev
            FROM index_daily WHERE ins_code IN ({string.Join(',', codes)}) AND d <= $d
            QUALIFY row_number() OVER (PARTITION BY ins_code ORDER BY d DESC) = 1
            """, [("d", d)], r => new IndexMove(
                r.GetInt64(0),
                names.GetValueOrDefault(r.GetInt64(0)) ?? r.GetInt64(0).ToString(CultureInfo.InvariantCulture),
                r.GetDecimal(1),
                r.IsDBNull(2) || r.GetDecimal(2) == 0 ? null : (double)(r.GetDecimal(1) / r.GetDecimal(2) - 1) * 100))
            .OrderBy(m => codes.IndexOf(m.InsCode))
            .ToList();
    }

    private MacroQuote? GetMacroQuote(string series, DateOnly d) =>
        store.Query("""
            SELECT d, close, lag(close) OVER (ORDER BY d) FROM macro_daily WHERE series = $series AND d <= $d
            QUALIFY row_number() OVER (ORDER BY d DESC) = 1
            """, [("series", series), ("d", d)], r => new MacroQuote(
                series, r.GetFieldValue<DateOnly>(0), r.GetDecimal(1),
                r.IsDBNull(2) || r.GetDecimal(2) == 0 ? null : (double)(r.GetDecimal(1) / r.GetDecimal(2) - 1) * 100))
            .FirstOrDefault();
}
