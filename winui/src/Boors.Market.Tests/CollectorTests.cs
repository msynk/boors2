using Boors.Market.Ingestion;
using Boors.Market.RawCache;
using Boors.Market.Sources;
using Boors.Market.Sources.Tsetmc;
using Boors.Market.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Boors.Market.Tests;

/// <summary>
/// Runs the real collector against a raw cache seeded with golden responses and <see cref="CachePolicy.CacheOnly"/>,
/// so the whole fetch → cache → parse → classify → store path is exercised without the network.
/// </summary>
[TestClass]
public sealed class CollectorTests
{
    private string root = null!;
    private ServiceProvider services = null!;

    // When the golden files were captured: Saturday 2026-10-17, 15:30 Iran time (after the close).
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero));

    [TestInitialize]
    public async Task Initialize()
    {
        root = Path.Combine(Path.GetTempPath(), "boors-collector-tests", Guid.NewGuid().ToString("N"));
        services = new ServiceCollection()
            .AddLogging(b => b.SetMinimumLevel(LogLevel.Warning))
            .AddSingleton<TimeProvider>(time)
            .AddBoorsMarketData(o =>
            {
                o.DataRoot = root;
                o.StorePath = ":memory:";
                o.Node = "test";
            })
            .BuildServiceProvider();

        var cache = services.GetRequiredService<IRawCache>();
        var endpoints = services.GetRequiredService<TsetmcClient>().Endpoints;
        var fetchedAt = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        async Task Seed(RawRequest request, string golden) => await cache.StoreAsync(request, Golden.Read(golden), 200, "application/json", fetchedAt);

        await Seed(endpoints.ClosingPriceDailyList(Golden.FooladInsCode), "tsetmc/closing-price-daily-folad.json");
        await Seed(endpoints.ClientTypeHistory(Golden.FooladInsCode), "tsetmc/client-type-history-folad.json");
        await Seed(endpoints.PriceAdjustList(Golden.FooladInsCode), "tsetmc/price-adjust-folad.json");
        await Seed(endpoints.InstrumentShareChange(Golden.FooladInsCode), "tsetmc/share-change-folad.json");
        await Seed(endpoints.IndexHistory(TsetmcEndpoints.TedpixInsCode), "tsetmc/index-b2-tedpix.json");
        await Seed(endpoints.MarketWatch(new MarketWatchQuery()), "tsetmc/market-watch-sample.json");
        await Seed(endpoints.ClosingPriceInfo(Golden.FooladInsCode), "tsetmc/closing-price-info-folad.json");
        await Seed(endpoints.ClientTypeAll(), "tsetmc/client-type-all-sample.json");
        await Seed(endpoints.IndexSnapshots(1), "tsetmc/index-b1-last-all.json");
        await Seed(endpoints.InstrumentsHistoryInDay(new DateOnly(2026, 10, 21)), "tsetmc/instruments-history-in-day-sample.json");

        services.GetRequiredService<MarketStore>().UpsertInstruments(
            [new Instrument { InsCode = Golden.FooladInsCode, Symbol = "فولاد", Isin = "IRO1FOLD0001", Class = InstrumentClass.TseStock }],
            new DateOnly(2026, 10, 17), "yval");
    }

    [TestCleanup]
    public void Cleanup()
    {
        services.Dispose();
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    [TestMethod]
    public async Task Backfill_from_cache_stores_classified_history_flows_and_both_adjustment_sources()
    {
        var collector = services.GetRequiredService<MarketCollector>();
        var store = services.GetRequiredService<MarketStore>();

        var result = await collector.BackfillHistoryAsync(10, force: false, withMessages: false, scope: null, CachePolicy.CacheOnly, CancellationToken.None);

        Assert.AreEqual("ok", result.Status);
        Assert.AreEqual(1, result.Items);
        var bars = store.GetDailyBars(Golden.FooladInsCode);
        Assert.HasCount(97, bars);
        Assert.IsTrue(bars.Where(b => b.Bar.Date >= new DateOnly(2026, 3, 2) && b.Bar.Date <= new DateOnly(2026, 5, 18)).All(b => b.Status == DayStatus.MarketClosed));
        Assert.AreEqual(DayStatus.QueueLockedUp, bars[^1].Status);
        Assert.HasCount(30, store.GetClientTypes(Golden.FooladInsCode));
        Assert.HasCount(23, store.GetPriceAdjustments(Golden.FooladInsCode, "published"));
        Assert.Contains(new DateOnly(2026, 3, 30), store.GetPriceAdjustments(Golden.FooladInsCode, "implied").Select(a => a.Date).ToList());
        Assert.AreEqual("ok", store.GetLatestRuns().Single(r => r.Job == "backfill").Status);
    }

    [TestMethod]
    public async Task End_of_day_appends_the_session_from_bulk_calls_and_estimates_flows()
    {
        var collector = services.GetRequiredService<MarketCollector>();
        var store = services.GetRequiredService<MarketStore>();
        await collector.BackfillHistoryAsync(10, force: false, withMessages: false, scope: null, CachePolicy.CacheOnly, CancellationToken.None);

        var result = await collector.EndOfDayAsync(force: true, CachePolicy.CacheOnly, CancellationToken.None);

        Assert.AreEqual("ok", result.Status, result.Message);
        var today = store.GetDailyBars(Golden.FooladInsCode, new DateOnly(2026, 10, 17)).Single();
        Assert.AreEqual("marketwatch", today.Source);
        Assert.AreEqual(4410m, today.Bar.Close);
        Assert.AreEqual(DayStatus.QueueLockedDown, today.Status); // whole day at the lower band, 4540 → 4410
        Assert.IsTrue(store.GetClientTypes(Golden.FooladInsCode).Single(c => c.Date == new DateOnly(2026, 10, 17)).ValuesEstimated);
        Assert.AreEqual(4, store.GetFreshness().Single(f => f.Dataset == "instrument").Rows);
    }

    [TestMethod]
    public async Task Repair_fills_missed_sessions_oldest_first_and_never_past_a_hole()
    {
        var collector = services.GetRequiredService<MarketCollector>();
        var store = services.GetRequiredService<MarketStore>();
        await collector.BackfillHistoryAsync(10, force: false, withMessages: false, scope: null, CachePolicy.CacheOnly, CancellationToken.None);

        // Thursday 2026-10-22, 10:00 Iran time. Another instrument stands for the sessions the store has; the last
        // collected day is 2026-10-19, so Tuesday 10-20 (not in the cache) and Wednesday 10-21 (cached) were missed.
        time.SetUtcNow(new DateTimeOffset(2026, 10, 22, 6, 30, 0, TimeSpan.Zero));
        var collected = Enumerable.Range(0, 30).Select(i => new DateOnly(2026, 9, 20).AddDays(i)).Where(d => d <= new DateOnly(2026, 10, 19));
        store.UpsertDailyBars(collected.Select(d => new ClassifiedBar(new DailyBar { InsCode = 1, Date = d, Close = 100, TradeCount = 1 }, DayStatus.Traded)), "marketwatch");

        // 10-19, the latest stored session, is not in the cache either: nothing is fetched or written.
        var stopped = await collector.RepairMissedSessionsAsync(30, CachePolicy.CacheOnly, CancellationToken.None);
        Assert.AreEqual("ok", stopped.Status, stopped.Message);
        Assert.AreEqual("0 of 2 missed sessions filled (2026-10-20..2026-10-21), 0 bars, 0 corporate actions", stopped.Message);
        Assert.IsEmpty(store.GetDailyBars(Golden.FooladInsCode, new DateOnly(2026, 10, 21)));

        store.UpsertDailyBars([new ClassifiedBar(new DailyBar { InsCode = 1, Date = new DateOnly(2026, 10, 20), Close = 100, TradeCount = 1 }, DayStatus.Traded)], "marketwatch");
        var filled = await collector.RepairMissedSessionsAsync(30, CachePolicy.CacheOnly, CancellationToken.None);

        Assert.AreEqual("ok", filled.Status, filled.Message);
        Assert.AreEqual(1, filled.Items); // only the known instrument of the four that traded
        var bar = store.GetDailyBars(Golden.FooladInsCode, new DateOnly(2026, 10, 21)).Single();
        Assert.AreEqual("marketwatch", bar.Source);
        Assert.AreEqual(4220m, bar.Bar.Close);
        Assert.AreEqual(4340m, bar.Bar.Yesterday);
        Assert.AreEqual(DayStatus.Traded, bar.Status);
        Assert.EndsWith("2026-10-21 rewritten from the official list", (await collector.RepairMissedSessionsAsync(30, CachePolicy.CacheOnly, CancellationToken.None)).Message!);
    }
}
