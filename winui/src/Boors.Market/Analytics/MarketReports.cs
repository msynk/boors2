using Boors.Market.Domain;
using Boors.Market.Rules;
using Boors.Market.Sources.Macro;
using Boors.Market.Sources.Tsetmc;
using Boors.Market.Storage;

namespace Boors.Market.Analytics;

/// <summary>
/// Multi-period reports for choosing stocks: performance over a window with risk and consistency measures, sector
/// returns, the walk-forward track record of ranking rules, and calendar returns by Jalali month. The conventions are
/// those of <see cref="MarketAnalytics"/>: returns compound <c>Close / Yesterday − 1</c> over priced sessions (correct
/// across capital increases, dividends and halts, and point-in-time), and the universe is primary listings of shares.
/// </summary>
public sealed class MarketReports(MarketStore store, MarketRules rules)
{
    /// <summary>Sessions per year used to annualise daily statistics (Saturday–Wednesday trading, less holidays).</summary>
    public const double SessionsPerYear = 240;

    /// <summary>Formations with fewer eligible equities than this are skipped (a fifth would be too small to mean anything).</summary>
    public const int MinimumCandidates = 30;

    private const string Equity = MarketAnalytics.Equity;
    private const string Return = MarketAnalytics.Return;

    /// <summary>
    /// Days on which shares traded. Funds and bonds also trade on days the stock market is shut (45 such days in the
    /// March–May 2026 closure), so "any instrument traded" is not a session of the equity reports: it would lower every
    /// share's days-traded share, empty the liquidity window and move monthly test dates onto days with no share prices.
    /// </summary>
    private const string EquitySessions = $"SELECT DISTINCT b.d FROM daily_bar b JOIN instrument i USING (ins_code) WHERE {Equity} AND b.trade_count > 0";

    public DateOnly? LatestSession() =>
        store.Query($"SELECT max(d) FROM ({EquitySessions})", [], r => r.IsDBNull(0) ? (DateOnly?)null : r.GetFieldValue<DateOnly>(0)).Single();

    /// <summary>The last session on or before <paramref name="date"/> (a base date on a weekend or holiday falls back to it).</summary>
    public DateOnly? SessionOnOrBefore(DateOnly date) =>
        store.Query($"SELECT max(d) FROM ({EquitySessions}) WHERE d <= $d", [("d", date)], r => r.IsDBNull(0) ? (DateOnly?)null : r.GetFieldValue<DateOnly>(0)).Single();

    private DateOnly? End(DateOnly? asOf) => asOf is { } date ? SessionOnOrBefore(date) : LatestSession();

    // ---------------------------------------------------------------- performance

    /// <summary>
    /// Ranks the equities that were listed for the whole window and traded at least <paramref name="minValue"/> rials a
    /// session (average of the 20 sessions up to the window's end). See <see cref="PerformanceReportKind"/> for the
    /// orderings. The window ends on the last session on or before <paramref name="asOf"/> (default: the latest) and
    /// starts at <paramref name="from"/> when given (a custom window), otherwise <paramref name="period"/> before its end.
    /// Everything is measured with data up to the end only; <see cref="PerformanceRow.SinceThen"/> then shows what followed.
    /// </summary>
    public PerformanceReport GetPerformance(PerformanceReportKind kind, ReportPeriod period, decimal minValue = 0, int take = 50, DateOnly? asOf = null, DateOnly? from = null)
    {
        var universe = GetUniverse(period, minValue, asOf, from);
        IEnumerable<PerformanceRow> rows = kind switch
        {
            PerformanceReportKind.TopPerformers => universe.Rows.OrderByDescending(r => r.Return),
            PerformanceReportKind.SteadyCompounders => universe.Rows.Where(IsSteady).OrderByDescending(r => r.Score),
            PerformanceReportKind.QualityOnDip => universe.Rows
                .Where(r => IsSteady(r) && r.Score >= 60 && r.ExcessReturn > 0 && r.FromHigh52Week <= -0.15)
                .OrderByDescending(r => r.Score),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        return universe with { Rows = [.. rows.Take(take)] };
    }

    /// <summary>Equal-weighted sector view of the same universe as <see cref="GetPerformance"/>, best median first.</summary>
    public SectorReturnsReport GetSectorReturns(ReportPeriod period, decimal minValue = 0, DateOnly? asOf = null, DateOnly? from = null)
    {
        var universe = GetUniverse(period, minValue, asOf, from);
        var sectors = universe.Rows
            .Where(r => r.SectorCode is not null)
            .GroupBy(r => r.SectorCode!)
            .Select(g =>
            {
                var best = g.MaxBy(r => r.Return)!;
                return new SectorReturn
                {
                    SectorCode = g.Key,
                    SectorName = g.First().SectorName,
                    Members = g.Count(),
                    MedianReturn = Median(g.Select(r => r.Return)),
                    AverageReturn = g.Average(r => r.Return),
                    MedianUsdReturn = Median(g.Where(r => r.UsdReturn is not null).Select(r => r.UsdReturn!.Value)),
                    PositiveShare = g.Count(r => r.Return > 0) / (double)g.Count(),
                    BeatTedpixShare = universe.TedpixReturn is null ? null : g.Count(r => r.ExcessReturn > 0) / (double)g.Count(),
                    BestInsCode = best.InsCode,
                    BestSymbol = best.Symbol,
                    BestReturn = best.Return,
                    Value20 = g.Sum(r => r.AverageValue20),
                    MedianSinceThen = Median(g.Where(r => r.SinceThen is not null).Select(r => r.SinceThen!.Value)),
                };
            })
            .OrderByDescending(s => s.MedianReturn)
            .ToList();

        return new SectorReturnsReport(universe.AsOf, universe.Period, universe.From, universe.TedpixReturn, universe.UsdReturn, sectors, universe.Latest, universe.TedpixSinceThen);
    }

    private static bool IsSteady(PerformanceRow r) => r.Coverage >= 0.75 && r.PricedSessions >= 40;

    private PerformanceReport GetUniverse(ReportPeriod period, decimal minValue, DateOnly? asOf, DateOnly? customFrom)
    {
        if (customFrom is not null)
            period = ReportPeriod.Custom;
        if (LatestSession() is not { } latest || End(asOf) is not { } to)
            return PerformanceReport.Empty with { Period = period };

        var from = customFrom ?? period.Start(to);
        if (from >= to)
            return PerformanceReport.Empty with { Period = period, AsOf = to, From = from, Latest = latest };
        (string, object?)[] window = [("from", from), ("to", to)];
        var sessions = store.Query($"SELECT count(*) FROM ({EquitySessions}) WHERE d > $from AND d <= $to", window, r => (int)r.GetInt64(0)).Single();

        var stats = store.Query($"""
            WITH {WindowReturns(listedAtStart: true)},
            lv AS (SELECT ins_code, d, close, ret, day_status,
                          sum(ln(1 + coalesce(ret, 0))) OVER (PARTITION BY ins_code ORDER BY d ROWS UNBOUNDED PRECEDING) AS cum
                   FROM r),
            dd AS (SELECT *, cum - greatest(0, max(cum) OVER (PARTITION BY ins_code ORDER BY d ROWS UNBOUNDED PRECEDING)) AS draw FROM lv),
            mo AS (SELECT ins_code, count(*) FILTER (WHERE m > 0) AS up, count(*) AS n
                   FROM (SELECT ins_code, date_trunc('month', d) AS month, sum(ln(1 + ret)) AS m FROM r WHERE ret IS NOT NULL GROUP BY ALL)
                   GROUP BY ins_code)
            SELECT dd.ins_code, exp(arg_max(dd.cum, dd.d)) - 1, count(dd.ret), stddev_samp(dd.ret), avg(dd.ret), exp(min(dd.draw)) - 1,
                   count(*) FILTER (WHERE dd.day_status = 'QueueLockedUp'), any_value(mo.up), any_value(mo.n), arg_max(dd.close, dd.d)
            FROM dd LEFT JOIN mo USING (ins_code)
            GROUP BY dd.ins_code
            """, window, r => new
        {
            InsCode = r.GetInt64(0),
            Total = r.GetDouble(1),
            Priced = (int)r.GetInt64(2),
            Deviation = r.IsDBNull(3) ? (double?)null : r.GetDouble(3),
            Mean = r.IsDBNull(4) ? (double?)null : r.GetDouble(4),
            Drawdown = r.GetDouble(5),
            LockedUp = (int)r.GetInt64(6),
            UpMonths = r.IsDBNull(7) ? 0 : (int)r.GetInt64(7),
            Months = r.IsDBNull(8) ? 0 : (int)r.GetInt64(8),
            Close = r.IsDBNull(9) ? (decimal?)null : r.GetDecimal(9),
        });

        var traded = store.Query($"""
            SELECT count(DISTINCT b.ins_code) FROM daily_bar b JOIN instrument i USING (ins_code)
            WHERE {Equity} AND b.trade_count > 0 AND b.d > $from AND b.d <= $to
            """, window, r => (int)r.GetInt64(0)).Single();

        var liquidity = store.Query($"""
            WITH s AS (SELECT d FROM ({EquitySessions}) WHERE d <= $to ORDER BY d DESC LIMIT 20)
            SELECT ins_code, coalesce(sum(value) FILTER (WHERE trade_count > 0), 0) / (SELECT count(*) FROM s)
            FROM daily_bar WHERE d IN (SELECT d FROM s) GROUP BY ins_code
            """, [("to", to)], r => (InsCode: r.GetInt64(0), Value: Convert.ToDecimal(r.GetValue(1), CultureInfo.InvariantCulture)))
            .ToDictionary(x => x.InsCode, x => x.Value);

        var fromHigh = store.Query($"""
            WITH {WindowReturns(listedAtStart: false)},
            lv AS (SELECT ins_code, d, ret, sum(ln(1 + coalesce(ret, 0))) OVER (PARTITION BY ins_code ORDER BY d ROWS UNBOUNDED PRECEDING) AS cum FROM r)
            SELECT ins_code, exp(arg_max(cum, d) - max(cum) FILTER (WHERE ret IS NOT NULL)) - 1 FROM lv GROUP BY ins_code HAVING count(ret) >= 20
            """, [("from", to.AddYears(-1)), ("to", to)], r => (InsCode: r.GetInt64(0), Value: r.GetDouble(1)))
            .ToDictionary(x => x.InsCode, x => x.Value);

        var usdSeries = Series(store.GetMacroDaily(TgjuSeries.UsdFreeMarket, TgjuClient.Source).Select(b => (b.Date, (double)b.Close)));
        var coinSeries = Series(store.GetMacroDaily(TgjuSeries.EmamiCoin, TgjuClient.Source).Select(b => (b.Date, (double)b.Close)));
        var tedpixSeries = Series(store.GetIndexBars(TsetmcEndpoints.TedpixInsCode).Select(b => (b.Date, (double)b.Close)));
        var usdReturn = Change(usdSeries, from, to);
        var tedpixReturn = Change(tedpixSeries, from, to);
        var winRates = to.DayNumber - from.DayNumber >= 540 ? UsdWinRates(window, from, usdSeries) : [];

        // What followed the window (shown next to the ranking, never part of it).
        var past = to < latest;
        var sinceThen = past
            ? store.Query($"""
                WITH {WindowReturns(listedAtStart: false)}
                SELECT ins_code, exp(sum(ln(1 + ret))) - 1 FROM r WHERE ret IS NOT NULL GROUP BY ins_code
                """, [("from", to), ("to", latest)], r => (InsCode: r.GetInt64(0), Value: r.GetDouble(1)))
                .ToDictionary(x => x.InsCode, x => x.Value)
            : [];

        var instruments = store.GetInstruments().ToDictionary(i => i.InsCode);
        var sectorNames = store.Query("SELECT code, name FROM sector", [], r => (Code: r.GetString(0), Name: r.GetString(1))).ToDictionary(s => s.Code, s => s.Name);
        var days = to.DayNumber - from.DayNumber;
        var illiquid = 0;
        var rows = new List<PerformanceRow>();
        foreach (var s in stats)
        {
            if (!instruments.TryGetValue(s.InsCode, out var instrument))
                continue;
            var value20 = liquidity.GetValueOrDefault(s.InsCode);
            if (s.Priced == 0 || value20 < minValue)
            {
                illiquid++;
                continue;
            }

            var deviation = s.Priced >= 20 ? s.Deviation : null;
            rows.Add(new PerformanceRow
            {
                InsCode = s.InsCode,
                Symbol = instrument.Symbol,
                Name = instrument.Name,
                InstrumentClass = instrument.Class.ToRuleKey(),
                SectorCode = instrument.SectorCode,
                SectorName = instrument.SectorCode is { } code ? sectorNames.GetValueOrDefault(code) ?? instrument.SectorName : instrument.SectorName,
                Close = s.Close,
                Return = s.Total,
                AnnualizedReturn = days >= 365 ? Math.Pow(1 + s.Total, 365.25 / days) - 1 : null,
                UsdReturn = usdReturn is { } usd ? (1 + s.Total) / (1 + usd) - 1 : null,
                ExcessReturn = tedpixReturn is { } index ? (1 + s.Total) / (1 + index) - 1 : null,
                Volatility = deviation * Math.Sqrt(SessionsPerYear),
                RiskAdjusted = deviation is > 0 && s.Mean is { } mean ? mean / deviation.Value * Math.Sqrt(SessionsPerYear) : null,
                MaxDrawdown = Math.Min(0, s.Drawdown),
                PositiveMonths = days >= 182 && s.Months >= 6 ? s.UpMonths / (double)s.Months : null,
                UsdWinRate = winRates.TryGetValue(s.InsCode, out var rate) ? rate : null,
                FromHigh52Week = fromHigh.TryGetValue(s.InsCode, out var high) ? Math.Min(0, high) : null,
                AverageValue20 = value20,
                PricedSessions = s.Priced,
                Coverage = sessions > 0 ? Math.Min(1, s.Priced / (double)sessions) : 0,
                BuyQueueShare = s.LockedUp / (double)s.Priced,
                SinceThen = sinceThen.TryGetValue(s.InsCode, out var next) ? next : null,
            });
        }

        return new PerformanceReport
        {
            AsOf = to,
            Period = period,
            From = from,
            Sessions = sessions,
            TedpixReturn = tedpixReturn,
            MedianReturn = Median(rows.Select(r => r.Return)),
            UsdReturn = usdReturn,
            CoinReturn = Change(coinSeries, from, to),
            Universe = rows.Count,
            ShortHistory = Math.Max(0, traded - stats.Count(s => s.Priced > 0)),
            Illiquid = illiquid,
            Rows = Score(rows),
            Latest = latest,
            TedpixSinceThen = past ? Change(tedpixSeries, to, latest) : null,
            UsdSinceThen = past ? Change(usdSeries, to, latest) : null,
        };
    }

    /// <summary>
    /// Daily returns of equities in ($from, $to]; optionally only those that traded on or before $from (listed for the
    /// whole window). The first traded day of an instrument gets no return: before a listing TSETMC carries a
    /// placeholder bar at the nominal price (usually 1,000 rials), so the first trade would read as +3,000% or more
    /// (پتروشیمی اروند, 2025-12-22: 1,000 → 37,500).
    /// </summary>
    private static string WindowReturns(bool listedAtStart) => $"""
        listing AS (SELECT ins_code, min(d) AS listed FROM daily_bar WHERE trade_count > 0 GROUP BY ins_code),
        r AS (
            SELECT b.ins_code, b.d, b.close, b.value, b.trade_count, b.day_status, l.listed, CASE WHEN b.d > l.listed THEN {Return} END AS ret
            FROM daily_bar b JOIN instrument i USING (ins_code) JOIN listing l USING (ins_code)
            WHERE {Equity} AND b.d > $from AND b.d <= $to{(listedAtStart ? " AND l.listed <= $from" : string.Empty)})
        """;

    /// <summary>Per equity: the share of one-year holds started at the window's start or a month-end that beat the dollar.</summary>
    private Dictionary<long, double> UsdWinRates((string, object?)[] window, DateOnly from, IReadOnlyList<(DateOnly Date, double Value)> usd)
    {
        var levels = store.Query($"""
            WITH {WindowReturns(listedAtStart: true)},
            lv AS (SELECT ins_code, d, sum(ln(1 + coalesce(ret, 0))) OVER (PARTITION BY ins_code ORDER BY d ROWS UNBOUNDED PRECEDING) AS cum FROM r),
            me AS (SELECT max(d) AS t FROM ({EquitySessions}) WHERE d > $from AND d <= $to GROUP BY date_trunc('month', d)),
            s AS (SELECT DISTINCT ins_code FROM lv)
            SELECT s.ins_code, me.t, lv.cum FROM s CROSS JOIN me ASOF JOIN lv ON lv.ins_code = s.ins_code AND me.t >= lv.d
            """, window, r => (InsCode: r.GetInt64(0), Date: r.GetFieldValue<DateOnly>(1), Level: r.GetDouble(2)));

        var monthEnds = levels.Select(l => l.Date).Distinct().Order().Prepend(from).ToList();
        var dollar = monthEnds.Select(d => ValueOn(usd, d)).ToList();
        var result = new Dictionary<long, double>();
        foreach (var stock in levels.GroupBy(l => l.InsCode))
        {
            // The window starts at level 0; a month-end without a bar yet keeps the previous level.
            var byDate = stock.ToDictionary(l => l.Date, l => l.Level);
            var path = new double[monthEnds.Count];
            for (var i = 1; i < path.Length; i++)
                path[i] = byDate.TryGetValue(monthEnds[i], out var level) ? level : path[i - 1];

            int holds = 0, wins = 0;
            for (var i = 0; i + 12 < path.Length; i++)
            {
                if (dollar[i] is not { } begin || begin <= 0 || dollar[i + 12] is not { } end)
                    continue;
                holds++;
                if (Math.Exp(path[i + 12] - path[i]) > end / begin)
                    wins++;
            }

            if (holds >= 6)
                result[stock.Key] = wins / (double)holds;
        }

        return result;
    }

    /// <summary>Adds the reliability score: the mean percentile of the available components (0–100).</summary>
    private static List<PerformanceRow> Score(List<PerformanceRow> rows)
    {
        Func<PerformanceRow, double?>[] components =
        [
            r => r.AnnualizedReturn ?? r.Return,
            r => r.RiskAdjusted,
            r => r.PositiveMonths,
            r => r.MaxDrawdown,
            r => r.UsdWinRate,
        ];
        var percentiles = components.Select(c => PercentileRanks(rows.Select(c).ToList())).ToList();
        return [.. rows.Select((row, i) =>
        {
            var available = percentiles.Select(p => p[i]).Where(p => p is not null).Select(p => p!.Value).ToList();
            return row with { Score = available.Count == 0 ? null : available.Average() * 100 };
        })];
    }

    // ---------------------------------------------------------------- shortlist

    /// <summary>Minimum average value traded per session for the short and mid lists (10 billion toman).</summary>
    public const decimal ShortlistTradingValue = 100_000_000_000m;

    /// <summary>Minimum average value traded per session for the long list (5 billion toman).</summary>
    public const decimal ShortlistHoldingValue = 50_000_000_000m;

    /// <summary>
    /// Three ranked lists of shares, one per horizon, from the performance reports on one base date (default: the
    /// latest session). They are screens built on what the reports and the walk-forward tests showed, not forecasts:
    /// <list type="bullet">
    /// <item><b>Short (1–3 months):</b> strong 12-month trend without the latest month, return per unit of risk, low
    /// volatility and closeness to the 52-week high (the rules that came closest to working); last month's hottest
    /// tenth (they tended to give back) and shares locked in buy queues on over 30% of the last three months' sessions
    /// are left out.</item>
    /// <item><b>Mid (6–12 months):</b> 1-year reliability score, 12-month trend, 1-year return in dollars, 3-year
    /// yearly return and a shallow deepest fall; only shares that beat the dollar over the year and rose in at least
    /// half of the months.</item>
    /// <item><b>Long (3–5 years):</b> mean of the 5- and 3-year reliability scores (3-year only, less 5 points, for a
    /// shorter history); only shares that beat TEDPIX, won at least half of their one-year holds against the dollar,
    /// never fell more than 50% and traded on at least 85% of the sessions.</item>
    /// </list>
    /// The short and mid lists need <see cref="ShortlistTradingValue"/>, the long list <see cref="ShortlistHoldingValue"/>
    /// of value traded per session, and every list keeps at most <paramref name="perSector"/> shares of one sector.
    /// </summary>
    public Shortlist GetShortlist(DateOnly? asOf = null, int take = 10, int perSector = 2)
    {
        var month = GetUniverse(ReportPeriod.OneMonth, ShortlistTradingValue, asOf, null);
        if (month.AsOf is null)
            return Shortlist.Empty;

        var quarter = GetUniverse(ReportPeriod.ThreeMonths, ShortlistTradingValue, asOf, null);
        var year = GetUniverse(ReportPeriod.OneYear, ShortlistTradingValue, asOf, null);
        var three = GetUniverse(ReportPeriod.ThreeYears, ShortlistHoldingValue, asOf, null);
        var five = GetUniverse(ReportPeriod.FiveYears, ShortlistHoldingValue, asOf, null);
        var m1 = month.Rows.ToDictionary(r => r.InsCode);
        var m3 = quarter.Rows.ToDictionary(r => r.InsCode);
        var y3 = three.Rows.ToDictionary(r => r.InsCode);
        var y5 = five.Rows.ToDictionary(r => r.InsCode);

        double? Momentum(PerformanceRow r) => m1.TryGetValue(r.InsCode, out var last) ? (1 + r.Return) / (1 + last.Return) - 1 : null;
        ShortlistPick Pick(PerformanceRow r) => new()
        {
            InsCode = r.InsCode,
            Symbol = r.Symbol,
            Name = r.Name,
            SectorCode = r.SectorCode,
            SectorName = r.SectorName,
            AverageValue20 = r.AverageValue20,
            SinceThen = r.SinceThen,
        };

        // Short: last month's hottest tenth tends to give back, and a share mostly in buy queues could not be bought.
        var hot = Quantile([.. month.Rows.Select(r => r.Return)], 0.9);
        var shortCandidates = year.Rows
            .Where(r => r.Coverage >= 0.8 && m1.TryGetValue(r.InsCode, out var last) && last.Return < hot
                && m3.TryGetValue(r.InsCode, out var q) && (q.BuyQueueShare ?? 0) <= 0.3)
            .ToList();
        var shortTerm = Rank(shortCandidates,
            [Momentum, r => r.RiskAdjusted, r => -r.Volatility, r => r.FromHigh52Week],
            (r, score) => Pick(r) with
            {
                Score = score,
                Return1M = m1[r.InsCode].Return,
                Return3M = m3[r.InsCode].Return,
                Return1Y = r.Return,
                Momentum12To1 = Momentum(r),
                RiskAdjusted = r.RiskAdjusted,
                Volatility = r.Volatility,
                FromHigh52Week = r.FromHigh52Week,
                PositiveMonths = r.PositiveMonths,
                MaxDrawdown = r.MaxDrawdown,
            });

        var midCandidates = year.Rows.Where(r => r.Coverage >= 0.8 && r.PositiveMonths >= 0.5 && r.UsdReturn > 0).ToList();
        var midTerm = Rank(midCandidates,
            [r => r.Score, Momentum, r => r.UsdReturn, r => y3.TryGetValue(r.InsCode, out var t) ? t.AnnualizedReturn : null, r => r.MaxDrawdown],
            (r, score) => Pick(r) with
            {
                Score = score,
                Return1Y = r.Return,
                UsdReturn1Y = r.UsdReturn,
                Momentum12To1 = Momentum(r),
                AnnualizedReturn3Y = y3.TryGetValue(r.InsCode, out var t) ? t.AnnualizedReturn : null,
                FromHigh52Week = r.FromHigh52Week,
                PositiveMonths = r.PositiveMonths,
                MaxDrawdown = r.MaxDrawdown,
            });

        var longTerm = Diversify(three.Rows
            .Select(r => (Three: r, Record: y5.GetValueOrDefault(r.InsCode) ?? r))
            .Where(x => x.Record.Coverage >= 0.85 && x.Record.MaxDrawdown >= -0.5 && x.Record.ExcessReturn > 0 && x.Record.UsdWinRate >= 0.5)
            .Select(x =>
            {
                var hasFive = !ReferenceEquals(x.Record, x.Three);
                return Pick(x.Three) with
                {
                    Score = hasFive ? ((x.Three.Score ?? 0) + (x.Record.Score ?? 0)) / 2 : (x.Three.Score ?? 0) - 5,
                    RecordYears = hasFive ? 5 : 3,
                    AnnualizedReturn = x.Record.AnnualizedReturn,
                    UsdReturn = x.Record.UsdReturn,
                    UsdWinRate = x.Record.UsdWinRate,
                    PositiveMonths = x.Record.PositiveMonths,
                    MaxDrawdown = x.Record.MaxDrawdown,
                    FromHigh52Week = x.Three.FromHigh52Week,
                };
            })
            .OrderByDescending(p => p.Score), take, perSector);

        List<ShortlistPick> Rank(List<PerformanceRow> candidates, Func<PerformanceRow, double?>[] parts, Func<PerformanceRow, double, ShortlistPick> pick)
        {
            var percentiles = parts.Select(part => PercentileRanks([.. candidates.Select(part)])).ToList();
            var ranked = candidates
                .Select((row, i) => (Row: row, Present: percentiles.Select(p => p[i]).Where(p => p is not null).Select(p => p!.Value).ToList()))
                .Where(x => x.Present.Count >= parts.Length - 1)
                .Select(x => pick(x.Row, x.Present.Average() * 100))
                .OrderByDescending(p => p.Score).ThenBy(p => p.Symbol, StringComparer.Ordinal);
            return Diversify(ranked, take, perSector);
        }

        // Which other lists hold the same share: the sturdiest candidates show up on more than one horizon.
        var lists = new Dictionary<string, List<ShortlistPick>> { [ShortlistHorizons.Short] = shortTerm, [ShortlistHorizons.Mid] = midTerm, [ShortlistHorizons.Long] = longTerm };
        IReadOnlyList<ShortlistPick> WithOthers(string horizon) =>
            [.. lists[horizon].Select(p => p with { AlsoIn = [.. lists.Where(l => l.Key != horizon && l.Value.Any(o => o.InsCode == p.InsCode)).Select(l => l.Key)] })];

        return new Shortlist
        {
            AsOf = year.AsOf,
            Latest = year.Latest,
            ShortTerm = WithOthers(ShortlistHorizons.Short),
            MidTerm = WithOthers(ShortlistHorizons.Mid),
            LongTerm = WithOthers(ShortlistHorizons.Long),
            Tedpix3M = quarter.TedpixReturn,
            Usd3M = quarter.UsdReturn,
            Tedpix1Y = year.TedpixReturn,
            Usd1Y = year.UsdReturn,
            Tedpix5Y = five.TedpixReturn,
            Usd5Y = five.UsdReturn,
            TedpixSinceThen = year.TedpixSinceThen,
            UsdSinceThen = year.UsdSinceThen,
            MissingHistory = five.ShortHistory,
        };
    }

    /// <summary>The first <paramref name="take"/> picks in order, at most <paramref name="perSector"/> of any one sector.</summary>
    private static List<ShortlistPick> Diversify(IEnumerable<ShortlistPick> ranked, int take, int perSector)
    {
        var bySector = new Dictionary<string, int>();
        var result = new List<ShortlistPick>();
        foreach (var pick in ranked)
        {
            var sector = pick.SectorCode ?? string.Empty;
            if (bySector.GetValueOrDefault(sector) >= perSector)
                continue;
            bySector[sector] = bySector.GetValueOrDefault(sector) + 1;
            result.Add(pick);
            if (result.Count == take)
                break;
        }

        return result;
    }

    // ---------------------------------------------------------------- signal track record

    /// <summary>
    /// Tests every <see cref="SignalKind"/> month by month over the last <paramref name="years"/> years, as an investor
    /// would have run it: on the same day of each month, rank the equities that could be bought that day (priced on
    /// the day and not in an all-day buy queue, at least 10 priced sessions out of 20, and not in the least liquid 30%)
    /// using only data up to that day, hold the top fifth for a month, and compare it with the average eligible equity
    /// and with the bottom fifth. Costs are the list round trip of the trading rules. The universe is today's
    /// instruments (delisted companies are missing, which flatters every group alike). The test ends on the last
    /// session on or before <paramref name="asOf"/> (default: the latest), where the picks are taken, and covers the
    /// whole months back to <paramref name="from"/> when given (at most 15 years), otherwise <paramref name="years"/>.
    /// </summary>
    public SignalTrackRecord GetSignalTrackRecord(int years, int picks = 20, DateOnly? asOf = null, DateOnly? from = null)
    {
        if (End(asOf) is not { } to)
            return SignalTrackRecord.Empty;

        int formations;
        if (from is { } start)
        {
            // Whole calendar months; the last day of a month counts as a full month (30 Sep back to 31 Mar is 6).
            var months = (to.Year - start.Year) * 12 + to.Month - start.Month;
            if (to.Day < start.Day && to.Day < DateTime.DaysInMonth(to.Year, to.Month))
                months--;
            formations = Math.Clamp(months, 1, 180);
            years = 0;
        }
        else
        {
            years = Math.Clamp(years, 1, 15);
            formations = years * 12;
        }

        // grid[0] is today; grid[k] is k months back. Formation k holds until grid[k − 1]; signals look back to grid[k + 12].
        var grid = Enumerable.Range(0, formations + 13).Select(k => to.AddMonths(-k)).ToArray();
        var gridIndex = grid.Select((date, k) => (date, k)).ToDictionary(x => x.date, x => x.k);
        var dates = string.Join(", ", grid.Select(d => $"DATE '{d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}'"));
        var panel = store.Query($"""
            WITH grid AS (SELECT unnest([{dates}]) AS g),
            sess AS (SELECT d FROM ({EquitySessions}) WHERE d > $from AND d <= $to),
            pts AS (SELECT grid.g, sess.d AS s FROM grid ASOF JOIN sess ON grid.g >= sess.d),
            {WindowReturns(listedAtStart: false)},
            lv AS (
                SELECT r.ins_code, r.d, r.day_status, r.listed,
                    sum(ln(1 + coalesce(r.ret, 0))) OVER (PARTITION BY r.ins_code ORDER BY r.d ROWS UNBOUNDED PRECEDING) AS cum,
                    count(r.ret) OVER (PARTITION BY r.ins_code ORDER BY r.d ROWS BETWEEN 19 PRECEDING AND CURRENT ROW) AS priced20,
                    avg(CAST(r.value AS DOUBLE)) OVER (PARTITION BY r.ins_code ORDER BY r.d ROWS BETWEEN 19 PRECEDING AND CURRENT ROW) AS value20,
                    stddev_samp(r.ret) OVER (PARTITION BY r.ins_code ORDER BY r.d ROWS BETWEEN 249 PRECEDING AND CURRENT ROW) AS vol250,
                    count(r.ret) OVER (PARTITION BY r.ins_code ORDER BY r.d ROWS BETWEEN 249 PRECEDING AND CURRENT ROW) AS priced250,
                    sum(CAST(c.buy_i_value - c.sell_i_value AS DOUBLE)) OVER (PARTITION BY r.ins_code ORDER BY r.d ROWS BETWEEN 19 PRECEDING AND CURRENT ROW)
                        / nullif(sum(CAST(r.value AS DOUBLE)) OVER (PARTITION BY r.ins_code ORDER BY r.d ROWS BETWEEN 19 PRECEDING AND CURRENT ROW), 0) AS flow20
                FROM r LEFT JOIN client_type_daily c ON c.ins_code = r.ins_code AND c.d = r.d),
            hl AS (SELECT *, max(cum) OVER (PARTITION BY ins_code ORDER BY d ROWS BETWEEN 249 PRECEDING AND CURRENT ROW) AS hi250 FROM lv),
            s AS (SELECT DISTINCT ins_code FROM r)
            SELECT s.ins_code, pts.g, pts.s, hl.d, hl.cum, hl.day_status, hl.priced20, hl.value20, hl.vol250, hl.priced250, hl.hi250, hl.flow20, hl.listed
            FROM s CROSS JOIN pts ASOF JOIN hl ON hl.ins_code = s.ins_code AND pts.s >= hl.d
            """, [("from", grid[^1].AddMonths(-1)), ("to", to)], r => (InsCode: r.GetInt64(0), Grid: r.GetFieldValue<DateOnly>(1), Point: new PanelPoint(
                r.GetFieldValue<DateOnly>(2) == r.GetFieldValue<DateOnly>(3),
                r.GetDouble(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                (int)r.GetInt64(6),
                r.IsDBNull(7) ? 0 : r.GetDouble(7),
                r.IsDBNull(8) ? null : r.GetDouble(8),
                (int)r.GetInt64(9),
                r.GetDouble(10),
                r.IsDBNull(11) ? null : r.GetDouble(11),
                r.GetFieldValue<DateOnly>(12))));

        var stocks = new Dictionary<long, PanelPoint?[]>();
        foreach (var (insCode, date, point) in panel)
        {
            if (!stocks.TryGetValue(insCode, out var points))
                stocks[insCode] = points = new PanelPoint?[grid.Length];
            points[gridIndex[date]] = point;
        }

        // Who could have been bought at each formation: priced that day (not halted, not queued to buy), active, liquid.
        var eligible = new List<(long InsCode, PanelPoint?[] Points)>[formations + 1];
        for (var k = 0; k <= formations; k++)
        {
            var buyable = stocks.Where(s => s.Value[k] is { } p && p.IsBuyable).Select(s => (s.Key, s.Value)).ToList();
            var cut = Quantile([.. buyable.Select(s => s.Value[k]!.Value.Value20)], 0.3);
            eligible[k] = [.. buyable.Where(s => s.Value[k]!.Value.Value20 >= cut)];
        }

        var instruments = store.GetInstruments().ToDictionary(i => i.InsCode);
        var cost = (double)rules.GetCosts(to).Value.ListRoundTrip;
        var verdicts = Enum.GetValues<SignalKind>().Select(kind => Evaluate(kind, eligible, grid, formations, cost) with
        {
            Picks = [.. eligible[0]
                .Select(s => (s.InsCode, Score: SignalValue(kind, s.Points, 0, grid)))
                .Where(x => x.Score is { } v && double.IsFinite(v) && instruments.ContainsKey(x.InsCode))
                .OrderByDescending(x => x.Score).ThenBy(x => x.InsCode)
                .Take(picks)
                .Select(x => instruments[x.InsCode] is var i ? new SignalPick(i.InsCode, i.Symbol, i.Name, i.SectorCode, Display(kind, x.Score!.Value)) : null!)],
        }).ToList();

        return new SignalTrackRecord(to, grid[formations], years, cost, verdicts);
    }

    private static SignalVerdict Evaluate(SignalKind kind, List<(long InsCode, PanelPoint?[] Points)>[] eligible, DateOnly[] grid, int formations, double cost)
    {
        var history = new List<SignalMonth>();
        HashSet<long>? previousTop = null;
        for (var k = formations; k >= 1; k--)
        {
            var candidates = eligible[k]
                .Select(s => (s.InsCode, Score: SignalValue(kind, s.Points, k, grid), Forward: Level(s.Points, k - 1) - Level(s.Points, k)))
                .Where(c => c.Score is { } v && double.IsFinite(v) && c.Forward is not null)
                .OrderByDescending(c => c.Score).ThenBy(c => c.InsCode)
                .ToList();
            if (candidates.Count < MinimumCandidates)
            {
                previousTop = null;
                continue;
            }

            var fifth = candidates.Count / 5;
            var top = candidates.Take(fifth).ToList();
            var topSet = top.Select(c => c.InsCode).ToHashSet();
            history.Add(new SignalMonth(
                grid[k],
                top.Average(c => Math.Exp(c.Forward!.Value) - 1),
                candidates.Average(c => Math.Exp(c.Forward!.Value) - 1),
                candidates.TakeLast(fifth).Average(c => Math.Exp(c.Forward!.Value) - 1),
                Spearman([.. candidates.Select(c => c.Score!.Value)], [.. candidates.Select(c => c.Forward!.Value)]),
                previousTop is null ? null : 1 - topSet.Count(previousTop.Contains) / (double)topSet.Count));
            previousTop = topSet;
        }

        if (history.Count == 0)
            return new SignalVerdict { Signal = kind, Verdict = SignalVerdicts.Insufficient };

        var excess = history.Select(m => m.Top - m.Universe).ToList();
        var mean = excess.Average();
        var deviation = excess.Count > 1 ? Math.Sqrt(excess.Sum(e => (e - mean) * (e - mean)) / (excess.Count - 1)) : 0;
        double? t = deviation > 1e-12 ? mean / (deviation / Math.Sqrt(excess.Count)) : null;
        var turnovers = history.Where(m => m.Turnover is not null).Select(m => m.Turnover!.Value).ToList();
        double? turnover = turnovers.Count > 0 ? turnovers.Average() : null;
        var net = mean - (turnover ?? 1) * cost;
        var verdict = history.Count < 12 || t is null ? SignalVerdicts.Insufficient
            : t >= 2 ? net > 0 ? SignalVerdicts.Works : SignalVerdicts.EatenByCosts
            : t <= -2 ? SignalVerdicts.Reverses
            : SignalVerdicts.NoEdge;

        return new SignalVerdict
        {
            Signal = kind,
            Months = history.Count,
            TopReturn = history.Average(m => m.Top),
            UniverseReturn = history.Average(m => m.Universe),
            BottomReturn = history.Average(m => m.Bottom),
            Excess = mean,
            HitRate = excess.Count(e => e > 0) / (double)excess.Count,
            TStat = t,
            InformationCoefficient = history.Average(m => m.InformationCoefficient),
            Turnover = turnover,
            NetExcess = net,
            AnnualNetExcess = Math.Pow(1 + net, 12) - 1,
            Verdict = verdict,
            History = history,
        };
    }

    /// <summary>The value of a signal at formation <paramref name="k"/> (log returns for the return-based signals).</summary>
    private static double? SignalValue(SignalKind kind, PanelPoint?[] points, int k, DateOnly[] grid)
    {
        var now = points[k]!.Value;
        var longHistory = now.Priced250 >= 120;
        double? Move(int recent, int earlier) => earlier < grid.Length && now.Listed <= grid[earlier] ? Level(points, recent) - Level(points, earlier) : null;
        return kind switch
        {
            SignalKind.Return1M => Move(k, k + 1),
            SignalKind.Return3M => Move(k, k + 3),
            SignalKind.Return6M => Move(k, k + 6),
            SignalKind.Return12M => Move(k, k + 12),
            SignalKind.Momentum12To1 => Move(k + 1, k + 12),
            SignalKind.Momentum6To1 => Move(k + 1, k + 6),
            SignalKind.High52WeekProximity => longHistory ? now.Level - now.High : null,
            SignalKind.LowVolatility => longHistory ? -now.Volatility : null,
            SignalKind.RiskAdjusted12M => longHistory && now.Volatility is > 0 ? Move(k, k + 12) / (now.Volatility * Math.Sqrt(SessionsPerYear)) : null,
            SignalKind.Consistency12M => now.Listed <= grid[Math.Min(k + 12, grid.Length - 1)] ? Consistency(points, k) : null,
            SignalKind.IndividualFlow20 => now.Flow,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    /// <summary>Up months out of the last 12, with the 12-month return as a tie-break far below one month's weight.</summary>
    private static double? Consistency(PanelPoint?[] points, int k)
    {
        if (Level(points, k + 12) is not { } start || Level(points, k) is not { } end)
            return null;
        var up = 0;
        for (var m = 0; m < 12; m++)
        {
            if (Level(points, k + m) - Level(points, k + m + 1) is > 0)
                up++;
        }

        return up / 12.0 + Math.Tanh(end - start) * 1e-4;
    }

    private static double Display(SignalKind kind, double value) => kind switch
    {
        SignalKind.LowVolatility => -value * Math.Sqrt(SessionsPerYear),
        SignalKind.RiskAdjusted12M or SignalKind.IndividualFlow20 => value,
        SignalKind.Consistency12M => Math.Round(value * 12) / 12,
        _ => Math.Exp(value) - 1,
    };

    private static double? Level(PanelPoint?[] points, int k) => k < points.Length ? points[k]?.Level : null;

    /// <summary>
    /// An equity on a grid date: whether it had a bar on the session the date falls on (<see cref="OnSession"/>), its
    /// cumulative log level and its trailing statistics as of its latest bar on or before that session, and its first
    /// traded day (bars before it are listing placeholders, so look-backs must not reach before it).
    /// </summary>
    private readonly record struct PanelPoint(bool OnSession, double Level, string? Status, int Priced20, double Value20, double? Volatility, int Priced250, double High, double? Flow, DateOnly Listed)
    {
        public bool IsBuyable => OnSession && Status is "Traded" or "ReopenAuction" or "QueueLockedDown" && Priced20 >= 10 && Value20 > 0;
    }

    // ---------------------------------------------------------------- seasonality

    /// <summary>
    /// Monthly returns by Jalali month: TEDPIX (month-end closes), TEDPIX in free-market dollars, the equal-weighted
    /// average equity (daily average of all priced equities, compounded) and the dollar itself. Data after
    /// <paramref name="asOf"/> is ignored (its month is then month-to-date), and months before the Jalali month of
    /// <paramref name="from"/> are left out.
    /// </summary>
    public SeasonalityReport GetSeasonality(DateOnly? asOf = null, DateOnly? from = null)
    {
        if ((asOf ?? LatestSession()) is not { } to)
            return SeasonalityReport.Empty;

        var (firstYear, firstMonth) = from is { } start ? ReportPeriods.JalaliMonth(start) : (int.MinValue, 1);

        var tedpix = MonthlyChanges(store.GetIndexBars(TsetmcEndpoints.TedpixInsCode).Where(b => b.Date <= to).Select(b => (b.Date, (double)b.Close)));
        var usd = MonthlyChanges(store.GetMacroDaily(TgjuSeries.UsdFreeMarket, TgjuClient.Source).Where(b => b.Date <= to).Select(b => (b.Date, (double)b.Close)));
        var equal = store.Query($"""
            WITH {WindowReturns(listedAtStart: false)}
            SELECT d, avg(ret) FROM r WHERE ret IS NOT NULL GROUP BY d HAVING count(*) >= 20 ORDER BY d
            """, [("from", DateOnly.MinValue), ("to", to)], r => (Date: r.GetFieldValue<DateOnly>(0), Return: r.GetDouble(1)))
            .GroupBy(x => ReportPeriods.JalaliMonth(x.Date))
            .ToDictionary(g => g.Key, g => g.Aggregate(1.0, (level, x) => level * (1 + x.Return)) - 1);

        var years = tedpix.Keys.Concat(equal.Keys).Select(k => k.Year).Where(y => y >= firstYear).Distinct().OrderDescending();
        var current = ReportPeriods.JalaliMonth(to);
        return new SeasonalityReport(to, current.Year, current.Month, [.. years.Select(y =>
        {
            double? Of(Dictionary<(int, int), double> months, int m) =>
                (y > firstYear || m >= firstMonth) && months.TryGetValue((y, m), out var v) ? v : null;
            var months = Enumerable.Range(1, 12).ToList();
            return new SeasonalityYear(
                y,
                [.. months.Select(m => Of(tedpix, m))],
                [.. months.Select(m => Of(tedpix, m) is { } t && Of(usd, m) is { } u ? (1 + t) / (1 + u) - 1 : (double?)null)],
                [.. months.Select(m => Of(equal, m))],
                [.. months.Select(m => Of(usd, m))]);
        })]);
    }

    /// <summary>Month-end to month-end changes of a daily series, keyed by Jalali (year, month).</summary>
    private static Dictionary<(int Year, int Month), double> MonthlyChanges(IEnumerable<(DateOnly Date, double Value)> daily)
    {
        var ends = daily.OrderBy(x => x.Date).GroupBy(x => ReportPeriods.JalaliMonth(x.Date)).ToDictionary(g => g.Key, g => g.Last().Value);
        var result = new Dictionary<(int, int), double>();
        foreach (var ((year, month), value) in ends)
        {
            var previous = month == 1 ? (year - 1, 12) : (year, month - 1);
            if (ends.TryGetValue(previous, out var start) && start > 0)
                result[(year, month)] = value / start - 1;
        }

        return result;
    }

    // ---------------------------------------------------------------- statistics

    private static List<(DateOnly Date, double Value)> Series(IEnumerable<(DateOnly Date, double Value)> points) => [.. points.OrderBy(p => p.Date)];

    /// <summary>The value on or before <paramref name="date"/> (binary search over a date-ordered series).</summary>
    private static double? ValueOn(IReadOnlyList<(DateOnly Date, double Value)> series, DateOnly date)
    {
        int lo = 0, hi = series.Count - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (series[mid].Date <= date)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return found < 0 ? null : series[found].Value;
    }

    private static double? Change(IReadOnlyList<(DateOnly Date, double Value)> series, DateOnly from, DateOnly to) =>
        ValueOn(series, from) is { } start && start > 0 && ValueOn(series, to) is { } end ? end / start - 1 : null;

    private static double? Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? null
            : sorted.Count % 2 == 1 ? sorted[sorted.Count / 2]
            : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    }

    private static double Quantile(List<double> values, double q)
    {
        if (values.Count == 0)
            return 0;
        values.Sort();
        var position = q * (values.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(lower + 1, values.Count - 1);
        return values[lower] + (values[upper] - values[lower]) * (position - lower);
    }

    /// <summary>Percentile (0–1) of each present value among the present values; ties share their average rank.</summary>
    internal static double?[] PercentileRanks(IReadOnlyList<double?> values)
    {
        var result = new double?[values.Count];
        var present = values.Select((v, i) => (Value: v, Index: i)).Where(x => x.Value is { } v && double.IsFinite(v)).OrderBy(x => x.Value).ToList();
        if (present.Count == 1)
            result[present[0].Index] = 0.5;
        if (present.Count < 2)
            return result;

        var ranks = Ranks([.. present.Select(p => p.Value!.Value)]);
        for (var i = 0; i < present.Count; i++)
            result[present[i].Index] = ranks[i] / (present.Count - 1);
        return result;
    }

    /// <summary>0-based ranks in ascending order; ties share their average rank.</summary>
    private static double[] Ranks(IReadOnlyList<double> values)
    {
        var order = Enumerable.Range(0, values.Count).OrderBy(i => values[i]).ToArray();
        var ranks = new double[values.Count];
        for (var start = 0; start < order.Length;)
        {
            var end = start;
            while (end + 1 < order.Length && values[order[end + 1]] == values[order[start]])
                end++;
            for (var j = start; j <= end; j++)
                ranks[order[j]] = (start + end) / 2.0;
            start = end + 1;
        }

        return ranks;
    }

    internal static double Spearman(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        var rx = Ranks(x);
        var ry = Ranks(y);
        var mx = rx.Average();
        var my = ry.Average();
        double sxy = 0, sxx = 0, syy = 0;
        for (var i = 0; i < rx.Length; i++)
        {
            sxy += (rx[i] - mx) * (ry[i] - my);
            sxx += (rx[i] - mx) * (rx[i] - mx);
            syy += (ry[i] - my) * (ry[i] - my);
        }

        return sxx > 0 && syy > 0 ? sxy / Math.Sqrt(sxx * syy) : 0;
    }
}
