using Boors.Market.Ingestion;
using Boors.Market.Sources;

namespace Boors.Market.Services.Jobs;

/// <summary>
/// The collection jobs a node or the desktop can run by name, mapped onto <see cref="MarketCollector"/> exactly as the
/// boilerplate's MarketDataJobRunner does, plus <c>repair</c>: fill sessions the end-of-day job missed, and
/// <c>reload</c>: replay the raw cache into the store (an in-place <c>boors rebuild</c>, used after importing another
/// node's bundle).
/// </summary>
public static class MarketJobs
{
    /// <summary>Jobs accepted by RunJob, in the boilerplate's order (repair and reload are additions).</summary>
    public static readonly IReadOnlyList<string> Names = ["instruments", "indices", "backfill", "eod", Repair, "funds", "macro", "codal", "messages", "halts", Reload];

    public const string Repair = "repair";

    /// <summary>How far back <c>repair</c> looks for sessions without any bar.</summary>
    public const int RepairLookbackDays = 30;

    public const string Reload = "reload";

    public const string Sync = "sync";

    public static async Task<IReadOnlyList<CollectionResult>> RunAsync(MarketCollector collector, MarketDataOptions options, string job, CancellationToken cancellationToken) => job switch
    {
        "instruments" => [await collector.SyncInstrumentsAsync(details: true, options.MaxInstrumentDetailsPerRun, CachePolicy.Network, cancellationToken)],
        "indices" => [await collector.SyncIndicesAsync(history: true, CachePolicy.Network, cancellationToken)],
        "backfill" => [await collector.BackfillHistoryAsync(options.MaxBackfillPerRun, force: false, withMessages: false, scope: null, CachePolicy.Network, cancellationToken)],
        "eod" => [await collector.EndOfDayAsync(force: false, CachePolicy.Network, cancellationToken)],
        "funds" => [await collector.SyncFundsAsync(CachePolicy.Network, cancellationToken)],
        "macro" => [await collector.SyncMacroAsync(null, CachePolicy.Network, cancellationToken)],
        "codal" => [await collector.SyncCodalAsync(maxPages: 3, CachePolicy.Network, cancellationToken)],
        "messages" => [await collector.SyncMessagesAsync(top: 200, CachePolicy.Network, cancellationToken)],
        "halts" => [await collector.ResolveHaltsAsync(options.MaxHaltQueriesPerRun, scope: null, CachePolicy.Network, cancellationToken)],
        Repair => [await collector.RepairMissedSessionsAsync(RepairLookbackDays, CachePolicy.Network, cancellationToken)],
        Reload => await ReloadFromCacheAsync(collector, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(job), job, $"Unknown market-data job. Jobs: {string.Join(", ", Names)}."),
    };

    /// <summary>
    /// The steps of <c>boors rebuild</c> (CLI: <c>collect all --force</c> with the cache only), run against the open store.
    /// Merges are idempotent, so this is a union of the store and everything the cache holds; nothing is downloaded.
    /// History is re-merged for every instrument (force) because an imported bundle may carry newer history files.
    /// </summary>
    public static async Task<IReadOnlyList<CollectionResult>> ReloadFromCacheAsync(MarketCollector collector, CancellationToken cancellationToken)
    {
        const CachePolicy cache = CachePolicy.CacheOnly;
        var results = new List<CollectionResult>
        {
            await collector.SyncIndicesAsync(history: true, cache, cancellationToken),
            await collector.SyncInstrumentsAsync(details: false, 0, cache, cancellationToken),
            await collector.BackfillHistoryAsync(int.MaxValue, force: true, withMessages: false, scope: null, cache, cancellationToken),
            await collector.EndOfDayAsync(force: true, cache, cancellationToken),
            await collector.RepairMissedSessionsAsync(RepairLookbackDays, cache, cancellationToken),
            await collector.SyncMessagesAsync(200, cache, cancellationToken),
            await collector.ResolveHaltsAsync(500, scope: null, cache, cancellationToken),
            await collector.SyncCodalAsync(5, cache, cancellationToken),
            await collector.SyncMacroAsync(null, cache, cancellationToken),
        };
        return results;
    }
}
