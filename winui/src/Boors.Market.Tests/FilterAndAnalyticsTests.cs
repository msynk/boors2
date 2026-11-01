using Boors.Market.Analytics;
using Boors.Market.Filters;
using Boors.Market.Sources.Macro;
using Boors.Market.Sources.Tsetmc;
using Boors.Market.Storage;

namespace Boors.Market.Tests;

[TestClass]
public sealed class TsetmcFilterTests
{
    private static readonly DateOnly Day = new(2026, 10, 17);

    private static FilterRow Row(long insCode, decimal close, decimal yesterday, long buyI = 100, long sellI = 50, decimal value = 1_000, params decimal[] previousCloses) => new()
    {
        InsCode = insCode,
        Symbol = $"S{insCode}",
        Close = close,
        Last = close,
        Yesterday = yesterday,
        Value = value,
        TradeCount = 10,
        ClientType = new ClientTypeDay { InsCode = insCode, Date = Day, BuyIndividualVolume = buyI, SellIndividualVolume = sellI },
        History = previousCloses.Select((c, i) => new DailyBar { InsCode = insCode, Date = Day.AddDays(-i - 1), Close = c, Yesterday = c - 1 }).ToList(),
        Statistics = new Dictionary<int, double> { [5] = insCode * 10 },
    };

    [TestMethod]
    public void Tsetmc_expressions_with_parenthesized_variables_and_client_types_work()
    {
        var filter = TsetmcFilter.Compile("(pc)>(py) && (ct).Buy_I_Volume>(ct).Sell_I_Volume*1.5");

        var result = filter.Apply([Row(1, 110, 100), Row(2, 90, 100), Row(3, 110, 100, buyI: 10)]);

        Assert.AreSequenceEqual(new[] { 1L }, result.Matches);
        Assert.IsFalse(filter.UsesHistory);
    }

    [TestMethod]
    public void History_and_statistics_tokens_are_rewritten_and_loaded_only_when_used()
    {
        var filter = TsetmcFilter.Compile("[ih][0].PClosing<[ih][1].PClosing && [is5]>15");

        var result = filter.Apply([Row(1, 10, 9, previousCloses: [9, 10]), Row(2, 10, 9, previousCloses: [9, 10]), Row(3, 10, 9, previousCloses: [11, 10])]);

        Assert.IsTrue(filter.UsesHistory);
        Assert.AreSequenceEqual(new[] { 5 }, filter.StatisticTypes);
        Assert.AreSequenceEqual(new[] { 2L }, result.Matches); // is5 = 10 for row 1, 20 for row 2; row 3 rose
    }

    [TestMethod]
    public void Code_mode_runs_and_a_row_error_skips_only_that_row()
    {
        var filter = TsetmcFilter.Compile("true==function(){ var up=0; for(var i=0;i<2;i++){ if([ih][i].PClosing>[ih][i].PriceYesterday) up++; } return up==2; }()");

        var result = filter.Apply([Row(1, 10, 9, previousCloses: [9, 8]), Row(2, 10, 9)]);

        Assert.AreSequenceEqual(new[] { 1L }, result.Matches);
        Assert.HasCount(1, result.Errors);
        Assert.AreEqual(2, result.Evaluated);
    }

    [TestMethod]
    public void Syntax_errors_are_reported_at_compile_time() =>
        Assert.ThrowsExactly<FilterSyntaxException>(() => TsetmcFilter.Compile("(pc)>(py) &&"));

    [TestMethod]
    public void The_sandbox_has_no_dotnet_access_and_stops_runaway_loops()
    {
        var probe = TsetmcFilter.Compile("typeof System === 'undefined' && typeof importNamespace === 'undefined'");
        var runaway = TsetmcFilter.Compile("true==function(){ while(true){} }()");

        Assert.HasCount(1, probe.Apply([Row(1, 1, 1)]).Matches);
        var result = runaway.Apply([Row(1, 1, 1)]);
        Assert.IsEmpty(result.Matches);
        Assert.HasCount(1, result.Errors);
    }
}

/// <summary>Analytics over a small hand-built store: 2 sessions, 3 equities in 2 sectors, 1 ETF and 1 secondary listing.</summary>
[TestClass]
public sealed class MarketAnalyticsTests
{
    private static readonly DateOnly D1 = new(2026, 10, 14);
    private static readonly DateOnly D2 = new(2026, 10, 17);
    private MarketStore store = null!;
    private MarketAnalytics analytics = null!;

    [TestInitialize]
    public void Initialize()
    {
        store = MarketStore.OpenInMemory();
        analytics = new MarketAnalytics(store);

        Instrument Make(long ins, string symbol, string isin, InstrumentClass c, string sector, decimal shares) =>
            new() { InsCode = ins, Symbol = symbol, Isin = isin, Class = c, SectorCode = sector, TotalShares = shares, Name = symbol + " Co" };
        store.UpsertInstruments(
            [
                Make(1, "الف", "IRO1AAAA0001", InstrumentClass.TseStock, "27", 1000),
                Make(2, "ب", "IRO1BBBB0001", InstrumentClass.TseStock, "27", 3000),
                Make(3, "پ", "IRO3CCCC0001", InstrumentClass.IfbStock, "44", 1000),
                Make(4, "صندوق", "IRT1DDDD0001", InstrumentClass.EtfFixedIncome, "68", 1000),
                Make(5, "الف3", "IRO1AAAA0003", InstrumentClass.TseStock, "27", 1000),
            ], D2, "yval");

        ClassifiedBar Bar(long ins, DateOnly d, decimal close, decimal yesterday, decimal value, DayStatus status = DayStatus.Traded) =>
            new(new DailyBar { InsCode = ins, Date = d, Close = close, Last = close, Yesterday = yesterday, Open = yesterday, High = Math.Max(close, yesterday), Low = Math.Min(close, yesterday), TradeCount = 10, Volume = 100, Value = value }, status);
        store.UpsertDailyBars(
            [
                Bar(1, D1, 100, 100, 1_000), Bar(2, D1, 50, 50, 2_000), Bar(3, D1, 20, 20, 500), Bar(4, D1, 10, 10, 9_000), Bar(5, D1, 100, 100, 0),
                Bar(1, D2, 103, 100, 3_000, DayStatus.QueueLockedUp), Bar(2, D2, 49, 50, 2_000), Bar(3, D2, 19, 20, 1_000), Bar(4, D2, 10, 10, 8_000), Bar(5, D2, 103, 103, 0),
            ], "history");
        store.UpdateSessionSnapshots([new SessionSnapshot(1, D2, 5, 20, 97, 103, new BestLimitLevel { Level = 1, BidPrice = 103, BidVolume = 1_000, AskPrice = 0, AskVolume = 0 })]);
        store.UpsertClientTypes(
            [
                new ClientTypeDay { InsCode = 1, Date = D2, BuyIndividualValue = 3_000, BuyIndividualCount = 3, SellIndividualValue = 1_000, SellIndividualCount = 10 },
                new ClientTypeDay { InsCode = 2, Date = D2, BuyIndividualValue = 500, BuyIndividualCount = 5, SellIndividualValue = 2_000, SellIndividualCount = 4 },
            ]);
        store.UpsertIndexBars(
            [
                new IndexBar { InsCode = TsetmcEndpoints.TedpixInsCode, Date = D1, Close = 1_000, Low = 1_000, High = 1_000 },
                new IndexBar { InsCode = TsetmcEndpoints.TedpixInsCode, Date = D2, Close = 1_020, Low = 1_000, High = 1_020 },
            ], "history");
        store.UpsertMacroDaily([new MacroBar(TgjuSeries.UsdFreeMarket, D1, 100, 100, 100, 100, "tgju"), new MacroBar(TgjuSeries.UsdFreeMarket, D2, 102, 102, 102, 102, "tgju")]);
    }

    [TestCleanup]
    public void Cleanup() => store.Dispose();

    [TestMethod]
    public void Overview_counts_primary_equities_queues_flows_and_usd_terms()
    {
        var o = analytics.GetOverview()!;

        Assert.AreEqual(D2, o.Session);
        Assert.AreEqual(6_000m, o.EquityValue); // 3,000 + 2,000 + 1,000; the ETF and the secondary listing are excluded
        Assert.AreEqual(8_000m, o.FundValue);
        Assert.AreEqual(1, o.Advancers);
        Assert.AreEqual(2, o.Decliners);
        Assert.AreEqual(1, o.BuyQueues);
        Assert.AreEqual(103_000m, o.BuyQueueValue);
        Assert.AreEqual(500m, o.IndividualNetFlow); // (3,000 − 1,000) + (500 − 2,000)
        Assert.AreEqual(2.0, o.Indices.Single().ChangePercent!.Value, 1e-9);
        Assert.AreEqual((1.02 / 1.02 - 1) * 100, o.TedpixUsdChangePercent!.Value, 1e-9);
    }

    [TestMethod]
    public void Sectors_weight_returns_by_market_cap()
    {
        var metals = analytics.GetSectors().Single(s => s.SectorCode == "27");

        // Caps: 100 × 1,000 = 100,000 at +3%; 50 × 3,000 = 150,000 at −2% → (3,000 − 3,000) / 250,000 = 0.
        Assert.AreEqual(2, metals.Instruments);
        Assert.AreEqual(0.0, metals.WeightedReturn!.Value, 1e-9);
        Assert.AreEqual(500m, metals.IndividualNetFlow);
    }

    [TestMethod]
    public void Movers_rank_by_the_requested_measure()
    {
        Assert.AreEqual("الف", analytics.GetMovers(MoverKind.Gainers, 1).Single().Symbol);
        Assert.AreEqual("پ", analytics.GetMovers(MoverKind.Losers, 1).Single().Symbol);
        Assert.AreEqual("ب", analytics.GetMovers(MoverKind.Outflow, 1).Single().Symbol);
    }

    [TestMethod]
    public void Instrument_metrics_report_returns_flows_and_buyer_power()
    {
        var m = analytics.GetInstrumentMetrics(1)!;

        Assert.AreEqual(D2, m.AsOf);
        Assert.AreEqual(3.0, m.ChangePercent!.Value, 1e-9);
        Assert.AreEqual(103m * 1000, m.MarketCap);
        Assert.IsNull(m.Returns["1w"]); // the window starts before the first stored bar: no partial-period return
        Assert.AreEqual(2_000m, m.IndividualNetFlow20);
        Assert.AreEqual((3_000m / 3) / (1_000m / 10), m.BuyerPower);
    }

    [TestMethod]
    public void Search_puts_exact_symbol_first_and_primary_before_secondary()
    {
        var results = analytics.Search("الف");

        Assert.AreEqual(1L, results[0].InsCode);
        Assert.AreEqual(5L, results[1].InsCode);
    }

    [TestMethod]
    public void Market_watch_applies_filters_and_reports_queues()
    {
        var service = new MarketWatchService(store, analytics);

        var all = service.Query(new MarketWatchRequest());
        var filtered = service.Query(new MarketWatchRequest { Filter = "(pc)>(py)" });

        Assert.HasCount(4, all.Entries); // secondary listing excluded
        Assert.AreEqual("buy", all.Entries.Single(e => e.InsCode == 1).Queue);
        Assert.AreSequenceEqual(new[] { 1L }, filtered.Entries.Select(e => e.InsCode));
    }
}
