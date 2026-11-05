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
    public async Task End_of_day_compares_with_the_previous_session_only()
    {
        var collector = services.GetRequiredService<MarketCollector>();
        var store = services.GetRequiredService<MarketStore>();

        // Wednesday 10-14 is the session before Saturday 10-17: a different close is a real corporate action.
        store.UpsertDailyBars([new ClassifiedBar(new DailyBar { InsCode = Golden.FooladInsCode, Date = new DateOnly(2026, 10, 14), Close = 2_000, Yesterday = 2_000, TradeCount = 1 }, DayStatus.Traded)], "history");
        await collector.EndOfDayAsync(force: true, CachePolicy.CacheOnly, CancellationToken.None);
        var implied = store.GetPriceAdjustments(Golden.FooladInsCode, "implied");
        Assert.AreEqual(new DateOnly(2026, 10, 17), implied.Single().Date);
    }

    [TestMethod]
    public async Task End_of_day_after_a_missed_session_calls_nothing_a_corporate_action()
    {
        var collector = services.GetRequiredService<MarketCollector>();
        var store = services.GetRequiredService<MarketStore>();

        // The last stored session is Sunday 10-11: Monday to Wednesday were never collected, and the snapshot's
        // reference price is Wednesday's close, not an adjusted one.
        store.UpsertDailyBars([new ClassifiedBar(new DailyBar { InsCode = Golden.FooladInsCode, Date = new DateOnly(2026, 10, 11), Close = 2_000, Yesterday = 2_000, TradeCount = 1 }, DayStatus.Traded)], "history");
        var result = await collector.EndOfDayAsync(force: true, CachePolicy.CacheOnly, CancellationToken.None);

        Assert.AreEqual("ok", result.Status, result.Message);
        Assert.Contains("0 corporate actions", result.Message!);
        Assert.IsEmpty(store.GetPriceAdjustments(Golden.FooladInsCode, "implied"));
        Assert.AreEqual(4410m, store.GetDailyBars(Golden.FooladInsCode, new DateOnly(2026, 10, 17)).Single().Bar.Close);
    }

    [TestMethod]
    public async Task A_replay_does_not_date_a_mid_session_snapshot_from_older_closing_prices()
    {
        var collector = services.GetRequiredService<MarketCollector>();
        var store = services.GetRequiredService<MarketStore>();
        var endpoints = services.GetRequiredService<TsetmcClient>().Endpoints;

        // Sunday 10-18, 10:30 Iran time: the instruments job took a snapshot mid-session; the closing prices in the cache are Saturday's.
        var midSession = new DateTimeOffset(2026, 10, 18, 7, 0, 0, TimeSpan.Zero);
        time.SetUtcNow(midSession.AddHours(1));
        await services.GetRequiredService<IRawCache>().StoreAsync(endpoints.MarketWatch(new MarketWatchQuery()), Golden.Read("tsetmc/market-watch-sample.json"), 200, "application/json", midSession);

        var result = await collector.EndOfDayAsync(force: true, CachePolicy.CacheOnly, CancellationToken.None);

        Assert.AreEqual("ok", result.Status, result.Message);
        Assert.StartsWith("skipped", result.Message!);
        Assert.IsEmpty(store.GetDailyBars(Golden.FooladInsCode, new DateOnly(2026, 10, 17)));
    }

    [TestMethod]
    public async Task A_job_that_fails_unexpectedly_is_recorded_as_failed_not_left_running()
    {
        var collector = services.GetRequiredService<MarketCollector>();
        var store = services.GetRequiredService<MarketStore>();
        var endpoints = services.GetRequiredService<TsetmcClient>().Endpoints;

        // A source answering 200 with a page instead of JSON: left "running", the scheduler would queue the job every minute.
        await services.GetRequiredService<IRawCache>().StoreAsync(endpoints.MarketWatch(new MarketWatchQuery()), System.Text.Encoding.UTF8.GetBytes("<html>maintenance</html>"), 200, "text/html", time.GetUtcNow().AddMinutes(1));

        var thrown = false;
        try
        {
            await collector.EndOfDayAsync(force: true, CachePolicy.CacheOnly, CancellationToken.None);
        }
        catch (Exception e) when (e is JsonException or FormatException)
        {
            thrown = true;
        }

        Assert.IsTrue(thrown);
        var run = store.GetRuns(time.GetUtcNow().AddDays(-1), "eod", 1).Single();
        Assert.AreEqual("failed", run.Status);
        Assert.IsNotNull(run.FinishedAt);
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
