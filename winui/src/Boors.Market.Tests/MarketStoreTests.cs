using Boors.Market.Sources.Funds;
using Boors.Market.Storage;

namespace Boors.Market.Tests;

[TestClass]
public sealed class MarketStoreTests
{
    private static readonly DateOnly Day = new(2026, 10, 17);
    private MarketStore store = null!;

    [TestInitialize]
    public void Initialize() => store = MarketStore.OpenInMemory();

    [TestCleanup]
    public void Cleanup() => store.Dispose();

    private static Instrument Stock(long insCode, string symbol, string isin, InstrumentClass instrumentClass = InstrumentClass.TseStock, string? name = null) =>
        new() { InsCode = insCode, Symbol = symbol, Isin = isin, Name = name, Class = instrumentClass };

    private static DailyBar Bar(long insCode, decimal close, long trades = 10) =>
        new() { InsCode = insCode, Date = Day, Close = close, Yesterday = close, Low = close, High = close, Open = close, Last = close, TradeCount = trades, Volume = trades * 100, Value = trades * 100 * close };

    [TestMethod]
    public void Instrument_merge_keeps_known_fields_and_respects_class_source_rank()
    {
        store.UpsertInstruments([Stock(1, "الف", "IRT1ALEF0001", InstrumentClass.EtfEquity, name: "صندوق الف")], Day, "isin");
        store.ApplyFundTypes([new FundRecord { RegNo = "1", InsCode = 1, Name = "صندوق الف", FundType = 4, TypeOfInvest = "Negotiable" }]);

        // A later market-watch row without a name and with a lower-ranked class source must not undo either.
        store.UpsertInstruments([Stock(1, "الف", "IRT1ALEF0001", InstrumentClass.EtfEquity)], Day.AddDays(1), "isin");
        var instrument = store.GetInstrument(1)!;

        Assert.AreEqual("صندوق الف", instrument.Name);
        Assert.AreEqual(InstrumentClass.EtfFixedIncome, instrument.Class);
    }

    [TestMethod]
    public void Secondary_listings_are_linked_to_the_primary_isin()
    {
        store.UpsertInstruments([Stock(10, "آباد", "IRO1ABAD0001"), Stock(11, "آباد3", "IRO1ABAD0003"), Stock(12, "تنها", "IRO1TANH0003")], Day, "isin");

        Assert.AreEqual(10L, store.GetInstrument(11)!.ParentInsCode);
        Assert.IsNull(store.GetInstrument(10)!.ParentInsCode);
        Assert.IsNull(store.GetInstrument(12)!.ParentInsCode); // no primary listing exists
    }

    [TestMethod]
    public void A_marketwatch_bar_never_replaces_a_history_bar_but_history_replaces_marketwatch()
    {
        store.UpsertDailyBars([new ClassifiedBar(Bar(1, 100), DayStatus.Traded)], "history");
        store.UpsertDailyBars([new ClassifiedBar(Bar(1, 999), DayStatus.Traded)], "marketwatch");
        Assert.AreEqual(100m, store.GetDailyBars(1).Single().Bar.Close);

        store.UpsertDailyBars([new ClassifiedBar(Bar(2, 50), DayStatus.Traded)], "marketwatch");
        store.UpsertDailyBars([new ClassifiedBar(Bar(2, 51), DayStatus.QueueLockedUp)], "history");
        var replaced = store.GetDailyBars(2).Single();
        Assert.AreEqual(51m, replaced.Bar.Close);
        Assert.AreEqual(DayStatus.QueueLockedUp, replaced.Status);
        Assert.AreEqual("history", replaced.Source);
    }

    [TestMethod]
    public void Estimated_flows_never_overwrite_official_flows()
    {
        var official = new ClientTypeDay { InsCode = 1, Date = Day, BuyIndividualValue = 1000, BuyIndividualCount = 5 };
        store.UpsertClientTypes([official]);
        store.UpsertClientTypes([official with { BuyIndividualValue = 7777, ValuesEstimated = true }]);

        var stored = store.GetClientTypes(1).Single();
        Assert.AreEqual(1000m, stored.BuyIndividualValue);
        Assert.IsFalse(stored.ValuesEstimated);
    }

    [TestMethod]
    public void Messages_link_numbered_sibling_symbols_to_the_parent()
    {
        var message = new SupervisorMessage { Id = 1, Date = Day, Title = "توقف", Kind = MessageKind.Halt, Reason = HaltReason.Disclosure, MentionedSymbols = ["فولاد1"] };

        store.UpsertMessages([message], new Dictionary<string, long> { ["فولاد"] = Golden.FooladInsCode });

        Assert.AreEqual(1L, store.GetMessages(Golden.FooladInsCode).Single().Id);
    }

    [TestMethod]
    public void Freshness_and_runs_report_latest_dates_and_statuses()
    {
        store.UpsertDailyBars([new ClassifiedBar(Bar(1, 100), DayStatus.Traded)], "history");
        store.RecordRun(new CollectionRun("r1", "eod", "n", DateTimeOffset.UnixEpoch, null, "running", 0, 0, null));
        store.RecordRun(new CollectionRun("r1", "eod", "n", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1), "ok", 5, 0, null));

        Assert.AreEqual(Day, store.GetFreshness().Single(f => f.Dataset == "daily_bar").LatestDate);
        Assert.AreEqual("ok", store.GetLatestRuns().Single().Status);
    }

    [TestMethod]
    public void A_store_with_another_schema_version_refuses_to_open()
    {
        var path = Path.Combine(Path.GetTempPath(), $"boors-store-{Guid.NewGuid():N}.duckdb");
        try
        {
            using (var created = MarketStore.Open(path))
                created.RunQuery("UPDATE schema_info SET version = 0");

            Assert.ThrowsExactly<InvalidOperationException>(() => MarketStore.Open(path));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".wal");
        }
    }
}
