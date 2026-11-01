using Boors.Market.Analytics;
using Boors.Market.Rules;
using Boors.Market.Sources.Macro;
using Boors.Market.Sources.Tsetmc;
using Boors.Market.Storage;

namespace Boors.Market.Tests;

/// <summary>The Reports page analytics over hand-built price paths whose answers are known.</summary>
[TestClass]
public sealed class MarketReportsTests
{
    private static readonly DateOnly AsOf = new(2025, 12, 31);
    private static readonly DateOnly YearAgo = new(2024, 12, 31);
    private MarketStore store = null!;
    private MarketReports reports = null!;

    [TestInitialize]
    public void Initialize()
    {
        store = MarketStore.OpenInMemory();
        reports = new MarketReports(store, MarketRules.LoadDefault());
    }

    [TestCleanup]
    public void Cleanup() => store.Dispose();

    [TestMethod]
    public void Performance_compounds_returns_measures_risk_and_excludes_new_listings_and_illiquid_shares()
    {
        var sessions = Weekdays(new DateOnly(2024, 6, 1), AsOf);
        var inWindow = sessions.Where(d => d > YearAgo).ToList();
        AddEquities(1, 2, 3, 4, 5);

        // 1: steady, +0.25% / +0.15% alternating. 2: volatile, +6% / −5.2%. 4: like 1 but barely traded.
        AddPath(1, sessions, i => i % 2 == 0 ? 0.0025m : 0.0015m);
        AddPath(2, sessions, i => i % 2 == 0 ? 0.06m : -0.052m);
        AddPath(4, sessions, i => 0.002m, value: 1_000);

        // 3: first traded inside the 1-year window, though TSETMC carried a placeholder bar at the nominal 1,000 rials long before.
        var listing = new DateOnly(2025, 3, 3);
        AddPath(3, sessions.Where(d => d >= listing).ToList(), i => 0.001m, firstClose: 30_000, placeholderOn: sessions[0]);

        // 5: +10%, then −20%, then flat: a 20% drawdown and a −12% return.
        AddPath(5, sessions, i => sessions[i] == inWindow[0] ? 0.10m : sessions[i] == inWindow[1] ? -0.20m : 0m);

        store.UpsertIndexBars([Index(YearAgo, 1_000), Index(AsOf, 1_500)], "history");
        store.UpsertMacroDaily([Usd(YearAgo, 100), Usd(AsOf, 150)]);

        // A fixed-income fund also trades on Saturdays, when no share does (as through the 2026 closure): not a session.
        store.UpsertInstruments([new Instrument { InsCode = 90, Symbol = "F90", Isin = "IRT1F0900001", Class = InstrumentClass.EtfFixedIncome }], AsOf, "yval");
        store.UpsertDailyBars(Enumerable.Range(0, 60).Select(w => new ClassifiedBar(new DailyBar
        {
            InsCode = 90, Date = new DateOnly(2025, 1, 4).AddDays(7 * w), Close = 10, Last = 10, Yesterday = 10, TradeCount = 5, Volume = 1, Value = 10,
        }, DayStatus.Traded)), "history");

        var report = reports.GetPerformance(PerformanceReportKind.TopPerformers, ReportPeriod.OneYear, minValue: 1_000_000, take: 10);

        Assert.AreEqual(AsOf, report.AsOf);
        Assert.AreEqual(YearAgo, report.From);
        Assert.AreEqual(inWindow.Count, report.Sessions);
        Assert.AreEqual(0.5, report.TedpixReturn!.Value, 1e-12);
        Assert.AreEqual(3, report.Universe);
        Assert.AreEqual(1, report.ShortHistory);  // 3
        Assert.AreEqual(1, report.Illiquid);      // 4
        Assert.AreSequenceEqual(new[] { 2L, 1L, 5L }, report.Rows.Select(r => r.InsCode).ToArray());

        var steady = report.Rows.Single(r => r.InsCode == 1);
        var expected = ExpectedReturn(1, sessions, YearAgo, AsOf);
        Assert.AreEqual(expected, steady.Return, 1e-9);
        Assert.AreEqual((1 + expected) / 1.5 - 1, steady.UsdReturn!.Value, 1e-9);
        Assert.AreEqual((1 + expected) / 1.5 - 1, steady.ExcessReturn!.Value, 1e-9);
        Assert.AreEqual(1.0, steady.PositiveMonths);
        Assert.AreEqual(0, steady.MaxDrawdown);
        Assert.AreEqual(1.0, steady.Coverage);

        var drop = report.Rows.Single(r => r.InsCode == 5);
        Assert.AreEqual(-0.12, drop.Return, 1e-9);
        Assert.AreEqual(-0.20, drop.MaxDrawdown, 1e-9);

        // The steady share beats the volatile one on everything but return, so it scores higher and leads the steady list.
        var volatile_ = report.Rows.Single(r => r.InsCode == 2);
        Assert.IsGreaterThan(volatile_.Score!.Value, steady.Score!.Value);
        Assert.IsLessThan(-0.05, volatile_.MaxDrawdown);
        var steadyList = reports.GetPerformance(PerformanceReportKind.SteadyCompounders, ReportPeriod.OneYear, minValue: 1_000_000);
        Assert.AreEqual(1L, steadyList.Rows[0].InsCode);
    }

    [TestMethod]
    public void A_listing_day_jump_is_never_a_return()
    {
        // A placeholder at 1,000 rials in January, the first trade at 30,000 in July, then +0.1% a session.
        var sessions = Weekdays(new DateOnly(2025, 1, 1), AsOf);
        AddEquities(3);
        var listing = new DateOnly(2025, 7, 1);
        AddPath(3, sessions.Where(d => d >= listing).ToList(), i => 0.001m, firstClose: 30_000, placeholderOn: sessions[0]);

        // One year: the placeholder predates the window but the first trade does not, so the share is left out.
        var year = reports.GetPerformance(PerformanceReportKind.TopPerformers, ReportPeriod.OneYear);
        Assert.IsEmpty(year.Rows);
        Assert.AreEqual(1, year.ShortHistory);

        // Three months: traded before the window, so it is ranked, with the compounded +0.1% sessions only.
        var quarter = reports.GetPerformance(PerformanceReportKind.TopPerformers, ReportPeriod.ThreeMonths);
        var row = quarter.Rows.Single();
        Assert.AreEqual(ExpectedReturn(3, sessions, ReportPeriod.ThreeMonths.Start(AsOf), AsOf), row.Return, 1e-9);
        Assert.IsLessThan(0.2, row.Return);
        Assert.AreEqual(0, row.FromHigh52Week);
    }

    [TestMethod]
    public void Sector_returns_summarise_the_same_universe()
    {
        var sessions = Weekdays(new DateOnly(2025, 6, 1), AsOf);
        AddEquities(1, 2, 3);
        AddPath(1, sessions, i => 0.002m);
        AddPath(2, sessions, i => -0.001m);
        AddPath(3, sessions, i => 0.001m);

        var sectors = reports.GetSectorReturns(ReportPeriod.ThreeMonths).Sectors;

        var a = sectors.Single(s => s.SectorCode == "27");  // 1 and 3
        Assert.AreEqual(2, a.Members);
        Assert.AreEqual(1.0, a.PositiveShare);
        Assert.AreEqual(1L, a.BestInsCode);
        var b = sectors.Single(s => s.SectorCode == "44");  // 2
        Assert.AreEqual(0.0, b.PositiveShare);
        Assert.AreEqual("27", sectors[0].SectorCode);
    }

    [TestMethod]
    public void A_past_base_date_uses_only_data_up_to_it_and_shows_what_followed()
    {
        // 1 leads until the end of June, then falls; 2 lags, then leads; 3 only became liquid after the base date.
        var sessions = Weekdays(new DateOnly(2025, 1, 1), AsOf);
        var turn = new DateOnly(2025, 6, 30);
        AddEquities(1, 2, 3);
        AddPath(1, sessions, i => sessions[i] <= turn ? 0.003m : -0.002m);
        AddPath(2, sessions, i => sessions[i] <= turn ? 0.001m : 0.005m);
        AddPath(3, sessions, i => 0.004m, valueOn: d => d <= turn.AddDays(30) ? 1_000 : 1_000_000_000);
        store.UpsertIndexBars([Index(new DateOnly(2025, 7, 4), 1_000), Index(AsOf, 1_250)], "history");

        // A Saturday base falls back to Friday's session.
        var past = reports.GetPerformance(PerformanceReportKind.TopPerformers, ReportPeriod.ThreeMonths, minValue: 1_000_000, asOf: new DateOnly(2025, 7, 5));

        var friday = new DateOnly(2025, 7, 4);
        Assert.AreEqual(friday, past.AsOf);
        Assert.AreEqual(friday.AddMonths(-3), past.From);
        Assert.AreEqual(AsOf, past.Latest);
        Assert.AreEqual(1, past.Illiquid);
        Assert.AreSequenceEqual(new[] { 1L, 2L }, past.Rows.Select(r => r.InsCode).ToArray());
        Assert.AreEqual(ExpectedReturn(1, sessions, friday.AddMonths(-3), friday), past.Rows[0].Return, 1e-9);
        Assert.AreEqual(ExpectedReturn(1, sessions, friday, AsOf), past.Rows[0].SinceThen!.Value, 1e-9);
        Assert.IsLessThan(0, past.Rows[0].SinceThen!.Value);
        Assert.IsGreaterThan(0, past.Rows[1].SinceThen!.Value);
        Assert.AreEqual(0.25, past.TedpixSinceThen!.Value, 1e-12);

        // At the latest session nothing has followed yet; a custom window measures between its own dates.
        Assert.IsNull(reports.GetPerformance(PerformanceReportKind.TopPerformers, ReportPeriod.ThreeMonths).Rows[0].SinceThen);
        var from = new DateOnly(2025, 3, 1);
        var custom = reports.GetPerformance(PerformanceReportKind.TopPerformers, ReportPeriod.OneYear, asOf: new DateOnly(2025, 9, 30), from: from);
        Assert.AreEqual(ReportPeriod.Custom, custom.Period);
        Assert.AreEqual(from, custom.From);
        Assert.AreEqual(new DateOnly(2025, 9, 30), custom.AsOf);
        var three = custom.Rows.Single(r => r.InsCode == 3);
        Assert.AreEqual(ExpectedReturn(3, sessions, from, new DateOnly(2025, 9, 30)), three.Return, 1e-9);
        Assert.IsNull(custom.Rows.Single(r => r.InsCode == 3).AnnualizedReturn); // under a year

        Assert.IsEmpty(reports.GetPerformance(PerformanceReportKind.TopPerformers, ReportPeriod.OneYear, asOf: from, from: from).Rows);
        Assert.IsNull(reports.GetPerformance(PerformanceReportKind.TopPerformers, ReportPeriod.OneYear, asOf: new DateOnly(2020, 1, 1)).AsOf);
    }

    [TestMethod]
    public void The_shortlist_ranks_each_horizon_by_its_own_rules_and_keeps_two_shares_per_sector()
    {
        // Six steady risers in one sector, a riser that crashed 60% in 2023, two wild swingers and three decliners.
        var sessions = Weekdays(new DateOnly(2020, 3, 1), AsOf);
        var crash = new DateOnly(2023, 6, 1);
        AddEquitiesIn(ins => ins <= 6 ? "A" : $"S{ins}", Enumerable.Range(1, 12).Select(i => (long)i));
        for (long ins = 1; ins <= 6; ins++)
        {
            var drift = 0.0020m + ins * 0.0002m;
            AddPath(ins, sessions, i => i % 2 == 0 ? drift + 0.001m : drift - 0.001m, value: 1_000_000_000_000);
        }

        AddPath(7, sessions, i => sessions[i] == crash ? -0.60m : 0.003m, value: 1_000_000_000_000);
        AddPath(8, sessions, i => i % 2 == 0 ? 0.06m : -0.052m, value: 1_000_000_000_000);
        AddPath(9, sessions, i => i % 2 == 0 ? 0.05m : -0.042m, value: 1_000_000_000_000);
        for (long ins = 10; ins <= 12; ins++)
            AddPath(ins, sessions, i => -0.001m, value: 1_000_000_000_000);

        // Monthly TEDPIX (+3%) and dollar (+2%): the steady risers (about +5% a month) beat both.
        var months = Enumerable.Range(0, 72).Select(m => new DateOnly(2020, 2, 1).AddMonths(m)).Where(d => d <= AsOf).ToList();
        store.UpsertIndexBars([.. months.Select((d, m) => Index(d, 1_000m * (decimal)Math.Pow(1.03, m)))], "history");
        store.UpsertMacroDaily([.. months.Select((d, m) => Usd(d, 100m * (decimal)Math.Pow(1.02, m)))]);

        var list = reports.GetShortlist();

        Assert.AreEqual(AsOf, list.AsOf);
        foreach (var horizon in new[] { list.ShortTerm, list.MidTerm, list.LongTerm })
        {
            Assert.IsLessThanOrEqualTo(2, horizon.Count(p => p.SectorCode == "A"));
            Assert.IsTrue(horizon.All(p => p.Score is >= 0 and <= 100));
            Assert.IsTrue(horizon.All(p => p.SinceThen is null)); // nothing follows the latest session
        }

        // Long: two steady risers (one sector) lead; the swingers never fell 50% and qualify behind them; the crash broke
        // the 50% limit and the decliners lost to TEDPIX.
        Assert.AreSequenceEqual(new[] { "A", "A" }, list.LongTerm.Take(2).Select(p => p.SectorCode).ToArray());
        Assert.AreSequenceEqual(new[] { 1L, 2L, 3L, 4L, 5L, 6L, 8L, 9L }.Intersect(list.LongTerm.Select(p => p.InsCode)).Order().ToArray(),
            list.LongTerm.Select(p => p.InsCode).Order().ToArray());
        Assert.IsTrue(list.LongTerm.All(p => p.RecordYears == 5 && p.UsdWinRate >= 0.5 && p.MaxDrawdown >= -0.5));
        Assert.DoesNotContain(7L, list.LongTerm.Select(p => p.InsCode));

        // Mid: the dollar beaters only (no decliner); a share on two lists says so.
        Assert.IsEmpty(list.MidTerm.Where(p => p.InsCode >= 10));
        Assert.Contains(7L, list.MidTerm.Select(p => p.InsCode)); // a year after the crash it is a riser again
        foreach (var pick in list.LongTerm.Where(p => list.MidTerm.Any(m => m.InsCode == p.InsCode)))
            Assert.Contains(ShortlistHorizons.Mid, pick.AlsoIn);

        // Short: last month's hottest share is left out.
        var hottest = reports.GetPerformance(PerformanceReportKind.TopPerformers, ReportPeriod.OneMonth, minValue: MarketReports.ShortlistTradingValue).Rows[0].InsCode;
        Assert.DoesNotContain(hottest, list.ShortTerm.Select(p => p.InsCode));
        Assert.IsTrue(list.ShortTerm.All(p => p.Momentum12To1 is not null && p.Return1M is not null));

        // On a past base date every pick shows what followed.
        var past = reports.GetShortlist(asOf: AsOf.AddMonths(-6));
        Assert.AreEqual(AsOf, past.Latest);
        Assert.IsTrue(past.LongTerm.Concat(past.MidTerm).All(p => p.SinceThen is not null));
        Assert.IsNotNull(past.TedpixSinceThen);
    }

    [TestMethod]
    public void A_persistent_trend_is_found_by_the_walk_forward_test_and_today_s_picks_follow_it()
    {
        // 50 shares with fixed drifts from −0.24% to +0.25% a session plus 1% noise: past winners keep winning.
        var sessions = Weekdays(new DateOnly(2023, 9, 1), AsOf);
        var random = new Random(7);
        var codes = Enumerable.Range(1, 50).Select(i => (long)i).ToArray();
        AddEquities(codes);
        foreach (var ins in codes)
        {
            var drift = (ins - 25) * 0.0001;
            var noise = sessions.Select(_ => (random.NextDouble() - 0.5) * 0.035).ToArray();
            AddPath(ins, sessions, i => (decimal)(drift + noise[i]), value: 1_000_000_000);
        }

        var record = reports.GetSignalTrackRecord(years: 1, picks: 5);

        Assert.AreEqual(AsOf, record.AsOf);
        Assert.AreEqual(AsOf.AddMonths(-12), record.From);
        Assert.AreEqual(0.01428, record.RoundTripCost, 1e-9);
        var trend = record.Signals.Single(s => s.Signal == SignalKind.Return12M);
        Assert.AreEqual(12, trend.Months);
        Assert.AreEqual(SignalVerdicts.Works, trend.Verdict);
        Assert.IsGreaterThan(trend.UniverseReturn!.Value, trend.TopReturn!.Value);
        Assert.IsGreaterThan(trend.BottomReturn!.Value, trend.UniverseReturn!.Value);
        Assert.IsGreaterThan(0.3, trend.InformationCoefficient!.Value);
        Assert.IsLessThan(0.5, trend.Turnover!.Value);
        Assert.HasCount(5, trend.Picks);
        Assert.IsTrue(trend.Picks.All(p => p.InsCode >= 35), string.Join(", ", trend.Picks.Select(p => p.InsCode)));
        Assert.IsGreaterThan(0, trend.Picks[0].Value); // shown as a simple return

        // A custom range ending three months ago: whole months back to the start, picks taken on the base date.
        var past = reports.GetSignalTrackRecord(years: 5, picks: 5, asOf: AsOf.AddMonths(-3), from: AsOf.AddMonths(-9));
        var end = new DateOnly(2025, 9, 30);
        Assert.AreEqual(end, past.AsOf);
        Assert.AreEqual(end.AddMonths(-6), past.From);
        Assert.AreEqual(0, past.Years);
        var pastTrend = past.Signals.Single(s => s.Signal == SignalKind.Return12M);
        Assert.AreEqual(6, pastTrend.Months);
        Assert.AreEqual(SignalVerdicts.Insufficient, pastTrend.Verdict);
        Assert.HasCount(5, pastTrend.Picks);
    }

    [TestMethod]
    public void Seasonality_files_month_end_changes_under_jalali_months()
    {
        // 1403/12/28, 1404/01/31 and 1404/02/31.
        DateOnly esfand = new(2025, 3, 18), farvardin = new(2025, 4, 20), ordibehesht = new(2025, 5, 21);
        store.UpsertIndexBars([Index(esfand, 1_000), Index(farvardin, 1_100), Index(ordibehesht, 990)], "history");
        store.UpsertMacroDaily([Usd(esfand, 100), Usd(farvardin, 110), Usd(ordibehesht, 121)]);

        var report = reports.GetSeasonality(ordibehesht);

        Assert.AreEqual(1404, report.CurrentYear);
        Assert.AreEqual(2, report.CurrentMonth);
        var year = report.Years.Single(y => y.Year == 1404);
        Assert.AreEqual(0.1, year.Tedpix[0]!.Value, 1e-12);
        Assert.AreEqual(-0.1, year.Tedpix[1]!.Value, 1e-12);
        Assert.IsNull(year.Tedpix[2]);
        Assert.AreEqual(0.0, year.TedpixUsd[0]!.Value, 1e-12);
        Assert.AreEqual(0.9 / 1.1 - 1, year.TedpixUsd[1]!.Value, 1e-12);
        Assert.DoesNotContain(1403, report.Years.Select(y => y.Year)); // no Bahman close to measure Esfand from

        // From a date in Ordibehesht: Farvardin is left out.
        var fromOrdibehesht = reports.GetSeasonality(ordibehesht, from: new DateOnly(2025, 4, 25)).Years.Single();
        Assert.IsNull(fromOrdibehesht.Tedpix[0]);
        Assert.AreEqual(-0.1, fromOrdibehesht.Tedpix[1]!.Value, 1e-12);
    }

    [TestMethod]
    public void Percentile_ranks_share_ties_and_skip_missing_values()
    {
        var ranks = MarketReports.PercentileRanks([3.0, null, 1.0, 3.0, 2.0]);

        Assert.AreEqual(5 / 6.0, ranks[0]!.Value, 1e-12);
        Assert.IsNull(ranks[1]);
        Assert.AreEqual(0.0, ranks[2]);
        Assert.AreEqual(5 / 6.0, ranks[3]!.Value, 1e-12);
        Assert.AreEqual(1 / 3.0, ranks[4]!.Value, 1e-12);
        Assert.AreEqual(-1.0, MarketReports.Spearman([1, 2, 3, 4], [8, 6, 4, 2]), 1e-12);
    }

    private static List<DateOnly> Weekdays(DateOnly from, DateOnly to)
    {
        var days = new List<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                days.Add(d);
        }

        return days;
    }

    private void AddEquities(params long[] codes) => AddEquitiesIn(ins => ins == 2 ? "44" : "27", codes);

    private void AddEquitiesIn(Func<long, string> sector, IEnumerable<long> codes) =>
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
            })], AsOf, "yval");

    /// <summary>
    /// One bar per session: close = previous close × (1 + step(i)), rounded to the store's 4 decimals; yesterday is the
    /// previous close. With <paramref name="placeholderOn"/>, a non-traded bar at 1,000 rials on that day precedes the first trade.
    /// </summary>
    private void AddPath(long ins, IReadOnlyList<DateOnly> sessions, Func<int, decimal> step, decimal value = 1_000_000_000, decimal firstClose = 10_000, DateOnly? placeholderOn = null, Func<DateOnly, decimal>? valueOn = null)
    {
        var bars = new List<ClassifiedBar>();
        var previous = firstClose;
        if (placeholderOn is { } placeholder)
        {
            bars.Add(new(new DailyBar { InsCode = ins, Date = placeholder, Close = 1_000, Last = 1_000, Yesterday = 1_000 }, DayStatus.NoTrade));
            previous = 1_000;
        }

        for (var i = 0; i < sessions.Count; i++)
        {
            var close = i == 0 ? firstClose : Math.Round(previous * (1 + step(i)), 4);
            bars.Add(new(new DailyBar { InsCode = ins, Date = sessions[i], Open = close, High = close, Low = close, Close = close, Last = close, Yesterday = previous, TradeCount = 10, Volume = 100, Value = valueOn?.Invoke(sessions[i]) ?? value }, DayStatus.Traded));
            previous = close;
        }

        store.UpsertDailyBars(bars, "history");
    }

    private double ExpectedReturn(long ins, IReadOnlyList<DateOnly> sessions, DateOnly from, DateOnly to)
    {
        var bars = store.GetDailyBars(ins);
        var start = bars.Last(b => b.Bar.Date <= from).Bar.Close;
        var end = bars.Last(b => b.Bar.Date <= to).Bar.Close;
        return (double)(end / start) - 1;
    }

    private static IndexBar Index(DateOnly d, decimal close) => new() { InsCode = TsetmcEndpoints.TedpixInsCode, Date = d, Close = close, Low = close, High = close };

    private static MacroBar Usd(DateOnly d, decimal close) => new(TgjuSeries.UsdFreeMarket, d, close, close, close, close, TgjuClient.Source);
}
