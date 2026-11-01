using Boors.Market.Domain;
using Boors.Market.Rules;
using Boors.Market.Sources;
using Boors.Market.Sources.Codal;
using Boors.Market.Sources.Funds;
using Boors.Market.Sources.Macro;
using Boors.Market.Sources.Tsetmc;
using Boors.Market.Storage;
using Boors.Market.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Boors.Market.Ingestion;

public sealed record CollectionResult(string Job, string Status, int Items, int Failures, string? Message)
{
    public bool Succeeded => Status is "ok" or "skipped";
}

/// <summary>Instrument classes that get daily history by default (options and debt are opt-in: many, short-lived, low value).</summary>
public static class CollectionScope
{
    public static readonly IReadOnlySet<InstrumentClass> Default = new HashSet<InstrumentClass>
    {
        InstrumentClass.TseStock, InstrumentClass.IfbStock, InstrumentClass.IfbBaseYellow, InstrumentClass.IfbBaseOrange, InstrumentClass.IfbBaseRed,
        InstrumentClass.Rights, InstrumentClass.EtfEquity, InstrumentClass.EtfFixedIncome, InstrumentClass.EtfRealEstate, InstrumentClass.EtfMixed,
        InstrumentClass.FundLeveraged, InstrumentClass.FundGoldSilver, InstrumentClass.FundOfFunds,
    };
}

/// <summary>
/// Collection jobs. Every job fetches through the source clients (so responses land in the raw cache first), writes
/// idempotent merges into the <see cref="MarketStore"/>, and records a run row for the data-health report.
/// The same jobs run from the CLI, from Hangfire on the server, and with <see cref="CachePolicy.CacheOnly"/> for rebuilds.
/// </summary>
public sealed partial class MarketCollector(
    TsetmcClient tsetmc,
    CodalClient codal,
    TgjuClient tgju,
    FipiranClient fipiran,
    MarketStore store,
    RawCache.IRawCache rawCache,
    MarketRules rules,
    TimeProvider timeProvider,
    IOptions<MarketDataOptions> options,
    ILogger<MarketCollector> logger)
{
    /// <summary>Main indices whose history is collected (TEDPIX, equal-weight, price, free-float, first and second market).</summary>
    public static readonly IReadOnlyList<long> MainIndices = [TsetmcEndpoints.TedpixInsCode, 67130298613737946, 5798407779416661, 8384385859414435, 49579049405614711, 62752761908615603, 71704845530629737];

    private DateOnly TodayInIran
    {
        get
        {
            var now = IranTime.Now(timeProvider);
            return new DateOnly(now.Year, now.Month, now.Day);
        }
    }

    // ------------------------------------------------------------------ instruments and funds

    /// <summary>
    /// Refreshes the instrument master from the market watch, applies fipiran fund types to ETFs, and (optionally)
    /// fetches GetInstrumentInfo for up to <paramref name="maxDetails"/> instruments that have never been detailed.
    /// </summary>
    public Task<CollectionResult> SyncInstrumentsAsync(bool details, int maxDetails, CachePolicy policy, CancellationToken cancellationToken) =>
        RunAsync("instruments", async () =>
        {
            var today = TodayInIran;
            var snapshot = await tsetmc.GetMarketWatchAsync(new MarketWatchQuery(), policy, cancellationToken);
            UpsertFromMarketWatch(snapshot, today);
            var items = snapshot.Rows.Count;
            var failures = 0;

            failures += await TryAsync("sector names", async () => store.UpsertSectors(await tsetmc.GetStaticDataAsync(Prefer(policy), cancellationToken)));

            failures += await TryAsync("fund types", async () =>
            {
                var funds = await fipiran.GetFundsAsync(policy, cancellationToken);
                store.UpsertFunds(funds);
                store.ApplyFundTypes(funds);
            });

            if (details)
            {
                var pending = store.GetInstrumentsWithoutDetails();
                foreach (var insCode in pending.Take(maxDetails))
                {
                    failures += await TryAsync($"details {insCode}", async () =>
                    {
                        var instrument = await tsetmc.GetInstrumentInfoAsync(insCode, Prefer(policy), cancellationToken);
                        store.UpsertInstruments([instrument], today, "yval", timeProvider.GetUtcNow());
                        items++;
                    });
                }

                // yVal-based classes never override fund-based ones (rank), but re-apply in case funds were new.
                failures += await TryAsync("fund types (after details)", async () => store.ApplyFundTypes(await fipiran.GetFundsAsync(CachePolicy.PreferCache, cancellationToken)));
            }

            return (items, failures, $"{snapshot.Rows.Count} instruments in the market watch");
        });

    public Task<CollectionResult> SyncFundsAsync(CachePolicy policy, CancellationToken cancellationToken) =>
        RunAsync("funds", async () =>
        {
            var funds = await fipiran.GetFundsAsync(policy, cancellationToken);
            store.UpsertFunds(funds);
            var updated = store.ApplyFundTypes(funds);
            return (funds.Count, 0, $"{funds.Count} funds, {updated} instruments re-classified");
        });

    // ------------------------------------------------------------------ indices and calendar

    /// <summary>Index list (TSE and IFB) and, with <paramref name="history"/>, the full daily history of the main indices.</summary>
    public Task<CollectionResult> SyncIndicesAsync(bool history, CachePolicy policy, CancellationToken cancellationToken) =>
        RunAsync("indices", async () =>
        {
            var items = 0;
            var failures = 0;
            foreach (var flow in new[] { 1, 2 })
            {
                failures += await TryAsync($"index list flow {flow}", async () =>
                {
                    var snapshots = await tsetmc.GetIndexSnapshotsAsync(flow, policy, cancellationToken);
                    store.UpsertIndexInfo(snapshots.Select(s => (s.InsCode, s.Name, flow)));
                    items += snapshots.Count;
                });
            }

            if (history)
            {
                // The IFB main index is looked up by name: its code is only known from the IFB index list.
                var ifbMain = store.GetIndexInfo().Where(i => i.Flow == 2 && PersianText.MatchKey(i.Name) == PersianText.MatchKey("شاخص کل فرابورس")).Select(i => i.InsCode);
                foreach (var insCode in MainIndices.Concat(ifbMain).Distinct())
                {
                    failures += await TryAsync($"index history {insCode}", async () =>
                    {
                        var bars = await tsetmc.GetIndexHistoryAsync(insCode, policy, cancellationToken);
                        store.UpsertIndexBars(bars, "history");
                        items += bars.Count;
                    });
                }
            }

            RefreshCalendar();
            return (items, failures, null);
        });

    /// <summary>Rebuilds market_closure from rules.yaml plus days on which TEDPIX repeated its previous close exactly.</summary>
    public MarketCalendar RefreshCalendar()
    {
        var tedpix = store.GetIndexBars(TsetmcEndpoints.TedpixInsCode);
        var detected = MarketCalendar.DetectFlatIndexDays(tedpix);
        var closures = rules.KnownClosures
            .SelectMany(c => EachDay(c.From, c.To).Select(d => (Date: d, Reason: c.Reason, Origin: "rules")))
            .Concat(detected.Select(d => (Date: d, Reason: "TEDPIX unchanged from the previous row", Origin: "index")))
            .DistinctBy(c => c.Date)
            .ToList();
        store.ReplaceClosures(closures);
        return new MarketCalendar([], closures.Select(c => c.Date));
    }

    private MarketCalendar LoadCalendar() => new([], store.GetClosureDays());

    // ------------------------------------------------------------------ helpers shared by the jobs

    private async Task<CollectionResult> RunAsync(string job, Func<Task<(int Items, int Failures, string? Message)>> work)
    {
        var started = timeProvider.GetUtcNow();
        var runId = $"{started:yyyyMMddHHmmssfff}-{job}-{options.Value.Node}";
        store.RecordRun(new CollectionRun(runId, job, options.Value.Node, started, null, "running", 0, 0, null));

        CollectionResult result;
        try
        {
            var (items, failures, message) = await work();
            result = new CollectionResult(job, failures == 0 ? "ok" : "partial", items, failures, message);
        }
        catch (OperationCanceledException)
        {
            result = new CollectionResult(job, "cancelled", 0, 0, null);
            store.RecordRun(new CollectionRun(runId, job, options.Value.Node, started, timeProvider.GetUtcNow(), result.Status, 0, 0, null));
            throw;
        }
        catch (Exception e) when (e is SourceRequestException or NotCachedException or HttpRequestException or Polly.ExecutionRejectedException)
        {
            logger.LogError(e, "Collection job {Job} failed", job);
            result = new CollectionResult(job, "failed", 0, 1, e.Message);
        }

        store.RecordRun(new CollectionRun(runId, job, options.Value.Node, started, timeProvider.GetUtcNow(), result.Status, result.Items, result.Failures, result.Message));
        return result;
    }

    /// <summary>Runs one unit of work; logs and counts a failure instead of aborting the job.</summary>
    private async Task<int> TryAsync(string what, Func<Task> work)
    {
        try
        {
            await work();
            return 0;
        }
        catch (NotCachedException e)
        {
            // Expected during a rebuild: the cache simply does not hold this response.
            logger.LogDebug("{What} skipped: {Message}", what, e.Message);
            return 0;
        }
        catch (Exception e) when (e is SourceRequestException or HttpRequestException or Polly.ExecutionRejectedException or FormatException or JsonException)
        {
            logger.LogWarning("{What} failed: {Message}", what, e.Message);
            return 1;
        }
    }

    private static CachePolicy Prefer(CachePolicy policy) => policy == CachePolicy.Network ? CachePolicy.PreferCache : policy;

    /// <summary>Rows of the extended variant carry yVal and rank as "yval"; compact rows only have the ISIN.</summary>
    private void UpsertFromMarketWatch(MarketWatchSnapshot snapshot, DateOnly seenOn)
    {
        store.UpsertInstruments(snapshot.Rows.Where(r => r.YVal is null).Select(ToInstrument), seenOn, "isin");
        store.UpsertInstruments(snapshot.Rows.Where(r => r.YVal is not null).Select(ToInstrument), seenOn, "yval");
    }

    private static Instrument ToInstrument(MarketWatchRow row) => new()
    {
        InsCode = row.InsCode,
        Symbol = row.Symbol,
        Name = row.Name,
        Isin = row.Isin,
        SectorCode = row.SectorCode,
        YVal = row.YVal,
        Flow = row.Flow,
        Board = row.Board,
        BaseVolume = row.BaseVolume > 0 ? row.BaseVolume : null,
        TotalShares = row.TotalShares > 0 ? row.TotalShares : null,
        Class = row.Class,
    };

    private static IEnumerable<DateOnly> EachDay(DateOnly from, DateOnly to)
    {
        for (var d = from; d <= to; d = d.AddDays(1))
            yield return d;
    }

    /// <summary>Normalized symbol → primary listing (secondary listings resolve to their parent).</summary>
    private IReadOnlyDictionary<string, long> SymbolMap() =>
        store.GetInstruments()
            .GroupBy(i => PersianText.MatchKey(i.Symbol))
            .ToDictionary(g => g.Key, g => g.OrderBy(i => i.ParentInsCode is not null).ThenByDescending(i => i.Isin is not null).Select(i => i.ParentInsCode ?? i.InsCode).First());
}
