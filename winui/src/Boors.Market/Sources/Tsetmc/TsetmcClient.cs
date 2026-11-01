using Boors.Market.Domain;
using Boors.Market.RawCache;
using Boors.Market.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Boors.Market.Sources.Tsetmc;

/// <summary>Typed access to cdn.tsetmc.com. Each call stores the raw response, then parses it.</summary>
public sealed class TsetmcClient(HttpClient http, IRawCache cache, TimeProvider timeProvider, ILogger<TsetmcClient> logger, IOptions<MarketDataOptions> options)
    : RawSourceClient(http, cache, timeProvider, logger)
{
    public TsetmcEndpoints Endpoints { get; } = new(options.Value.TsetmcBaseUrl);

    public async Task<Instrument> GetInstrumentInfoAsync(long insCode, CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default) =>
        TsetmcParser.ParseInstrumentInfo(await GetBodyAsync(Endpoints.InstrumentInfo(insCode), policy, cancellationToken), insCode);

    public async Task<IReadOnlyList<InstrumentSearchHit>> SearchAsync(string text, CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default) =>
        TsetmcParser.ParseInstrumentSearch(await GetBodyAsync(Endpoints.InstrumentSearch(text), policy, cancellationToken));

    /// <summary>
    /// Resolves a ticker (e.g. "فولاد") to the currently active instrument: exact normalized symbol match,
    /// preferring the row TSETMC marks active. Returns null when nothing matches exactly.
    /// </summary>
    public async Task<InstrumentSearchHit?> ResolveSymbolAsync(string symbol, CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default)
    {
        var key = PersianText.MatchKey(symbol);
        var persian = PersianText.Normalize(symbol) ?? symbol;

        // TSETMC stores names with Arabic ي/ك; try the Persian spelling first, then the Arabic one.
        foreach (var text in new[] { persian, PersianText.ToArabicLetters(persian) }.Distinct(StringComparer.Ordinal))
        {
            var hit = (await SearchAsync(text, policy, cancellationToken))
                .Where(h => PersianText.MatchKey(h.Symbol) == key)
                .OrderByDescending(h => h.IsActive)
                .ThenBy(h => h.Flow is 1 or 2 ? 0 : 1)
                .FirstOrDefault();
            if (hit is not null)
                return hit;
        }

        return null;
    }

    public async Task<IReadOnlyList<DailyBar>> GetDailyHistoryAsync(long insCode, CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default) =>
        TsetmcParser.ParseDailyHistory(await GetBodyAsync(Endpoints.ClosingPriceDailyList(insCode), policy, cancellationToken), insCode);

    public async Task<IReadOnlyList<DailyBar>> GetInstrumentsHistoryInDayAsync(DateOnly date, CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default) =>
        TsetmcParser.ParseInstrumentsHistoryInDay(await GetBodyAsync(Endpoints.InstrumentsHistoryInDay(date), policy, cancellationToken), date);

    public async Task<ClosingPriceSnapshot> GetClosingPriceInfoAsync(long insCode, CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default) =>
        TsetmcParser.ParseClosingPriceInfo(await GetBodyAsync(Endpoints.ClosingPriceInfo(insCode), policy, cancellationToken), insCode);

    public async Task<IReadOnlyList<ClientTypeDay>> GetClientTypeHistoryAsync(long insCode, CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default) =>
        TsetmcParser.ParseClientTypeHistory(await GetBodyAsync(Endpoints.ClientTypeHistory(insCode), policy, cancellationToken), insCode);

    public async Task<IReadOnlyList<PriceAdjustment>> GetPriceAdjustmentsAsync(long insCode, CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default) =>
        TsetmcParser.ParsePriceAdjustments(await GetBodyAsync(Endpoints.PriceAdjustList(insCode), policy, cancellationToken), insCode);

    public async Task<IReadOnlyList<ShareChange>> GetShareChangesAsync(long insCode, CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default) =>
        TsetmcParser.ParseShareChanges(await GetBodyAsync(Endpoints.InstrumentShareChange(insCode), policy, cancellationToken), insCode);

    public async Task<IReadOnlyList<BestLimitLevel>> GetBestLimitsAsync(long insCode, CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default) =>
        TsetmcParser.ParseBestLimits(await GetBodyAsync(Endpoints.BestLimits(insCode), policy, cancellationToken));

    public async Task<IReadOnlyList<IndexBar>> GetIndexHistoryAsync(long indexInsCode = TsetmcEndpoints.TedpixInsCode, CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default) =>
        TsetmcParser.ParseIndexHistory(await GetBodyAsync(Endpoints.IndexHistory(indexInsCode), policy, cancellationToken), indexInsCode);

    public async Task<IReadOnlyList<StaticDataItem>> GetStaticDataAsync(CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default) =>
        TsetmcParser.ParseStaticData(await GetBodyAsync(Endpoints.StaticData(), policy, cancellationToken));

    public async Task<IReadOnlyList<ClientTypeSnapshot>> GetClientTypeAllAsync(CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default) =>
        TsetmcParser.ParseClientTypeAll(await GetBodyAsync(Endpoints.ClientTypeAll(), policy, cancellationToken));

    public async Task<IReadOnlyList<IndexSnapshot>> GetIndexSnapshotsAsync(int flow = 1, CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default) =>
        TsetmcParser.ParseIndexSnapshots(await GetBodyAsync(Endpoints.IndexSnapshots(flow), policy, cancellationToken));

    public async Task<IReadOnlyList<SupervisorMessage>> GetMessagesAsync(long insCode, CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default) =>
        TsetmcParser.ParseMessages(await GetBodyAsync(Endpoints.MessagesByInstrument(insCode), policy, cancellationToken));

    public async Task<IReadOnlyList<SupervisorMessage>> GetLatestMessagesAsync(int flow, int top, CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default) =>
        TsetmcParser.ParseMessages(await GetBodyAsync(Endpoints.MessagesByFlow(flow, top), policy, cancellationToken));

    public async Task<IReadOnlyList<ShareholderPosition>> GetShareholdersAsync(long insCode, DateOnly snapshotDate, CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default) =>
        TsetmcParser.ParseShareholders(await GetBodyAsync(Endpoints.Shareholders(insCode), policy, cancellationToken), insCode, snapshotDate);

    public async Task<InstrumentStateOnDate> GetInstrumentStateAsync(long insCode, DateOnly date, CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default) =>
        TsetmcParser.ParseInstrumentState(await GetBodyAsync(Endpoints.InstrumentState(insCode, date), policy, cancellationToken), insCode, date);

    public async Task<IReadOnlyList<InstrumentStatistic>> GetInstrumentStatisticsAsync(CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default) =>
        TsetmcParser.ParseInstrumentStatistics(await GetBodyAsync(Endpoints.InstrumentStatistics(), policy, cancellationToken));

    public async Task<MarketWatchSnapshot> GetMarketWatchAsync(MarketWatchQuery? query = null, CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default) =>
        TsetmcParser.ParseMarketWatch(await GetBodyAsync(Endpoints.MarketWatch(query ?? new MarketWatchQuery()), policy, cancellationToken));
}
