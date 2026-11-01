using Boors.Market.RawCache;
using Boors.Market.Sources;
using Boors.Market.Sources.Codal;
using Boors.Market.Sources.Funds;
using Boors.Market.Sources.Macro;
using Boors.Market.Sources.Tsetmc;

namespace Boors.Market.Contracts;

/// <summary>
/// The endpoints Boors depends on, each with a fixed sample request (فولاد, TEDPIX). <c>boors verify contracts</c>
/// fetches them, compares their JSON shape with the committed baseline and reports drift.
/// </summary>
public sealed class SourceContracts(TsetmcClient tsetmc, CodalClient codal, TgjuClient tgju, FipiranClient fipiran)
{
    private const long Folad = 46348559193224090;

    /// <summary>
    /// Paths a source is known to send only sometimes. TSETMC's market watch alternates (per CDN cache period, not per
    /// request) between a compact variant and an extended one that adds these fields; seen 2026-10-17.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> KnownOptionalPaths = new Dictionary<string, IReadOnlyList<string>>
    {
        ["tsetmc.market-watch"] = ["$.marketwatch[].buyOP", "$.marketwatch[].cGrValCot", "$.marketwatch[].flow", "$.marketwatch[].pRedTran", "$.marketwatch[].yMarNSC", "$.marketwatch[].yVal"],
    };

    /// <summary>Marks the known optional paths of a contract in a shape (adding them when absent).</summary>
    public static SortedDictionary<string, string> WithKnownOptionals(string contract, SortedDictionary<string, string> shape)
    {
        foreach (var path in KnownOptionalPaths.GetValueOrDefault(contract) ?? [])
        {
            var kinds = shape.TryGetValue(path, out var existing) ? existing : "null";
            shape[path] = kinds.StartsWith(JsonShape.OptionalPrefix, StringComparison.Ordinal) ? kinds : JsonShape.OptionalPrefix + kinds;
        }

        return shape;
    }

    public IReadOnlyList<(string Name, RawSourceClient Client, RawRequest Request)> All()
    {
        var e = tsetmc.Endpoints;
        return
        [
            ("tsetmc.instrument-info", tsetmc, e.InstrumentInfo(Folad)),
            ("tsetmc.instrument-search", tsetmc, e.InstrumentSearch("فولاد")),
            ("tsetmc.closing-price-info", tsetmc, e.ClosingPriceInfo(Folad)),
            ("tsetmc.closing-price-daily", tsetmc, e.ClosingPriceDailyList(Folad, 10)),
            ("tsetmc.instruments-history-in-day", tsetmc, e.InstrumentsHistoryInDay(new DateOnly(2026, 10, 21))),
            ("tsetmc.client-type-history", tsetmc, e.ClientTypeHistory(Folad)),
            ("tsetmc.client-type-all", tsetmc, e.ClientTypeAll()),
            ("tsetmc.price-adjust", tsetmc, e.PriceAdjustList(Folad)),
            ("tsetmc.share-change", tsetmc, e.InstrumentShareChange(Folad)),
            ("tsetmc.best-limits", tsetmc, e.BestLimits(Folad)),
            ("tsetmc.index-history", tsetmc, e.IndexHistory(TsetmcEndpoints.TedpixInsCode)),
            ("tsetmc.index-snapshots", tsetmc, e.IndexSnapshots(1)),
            ("tsetmc.market-watch", tsetmc, e.MarketWatch(new MarketWatchQuery { PaperTypes = [1] })),
            ("tsetmc.messages-by-flow", tsetmc, e.MessagesByFlow(1, 20)),
            ("tsetmc.shareholders", tsetmc, e.Shareholders(Folad)),
            ("tsetmc.static-data", tsetmc, e.StaticData()),
            ("codal.categories", codal, codal.CategoriesRequest()),
            ("codal.search", codal, codal.SearchRequest(new CodalSearchQuery())),
            ("tgju.history", tgju, TgjuClient.HistoryRequest(TgjuSeries.UsdFreeMarket)),
            ("fipiran.fund-compare", fipiran, FipiranClient.FundCompareRequest()),
        ];
    }

    public static IReadOnlyDictionary<string, SortedDictionary<string, string>> LoadBaseline(string path) =>
        JsonSerializer.Deserialize<Dictionary<string, SortedDictionary<string, string>>>(File.ReadAllText(path))
        ?? throw new FormatException($"Empty contract baseline: {path}");

    public static void SaveBaseline(string path, IReadOnlyDictionary<string, SortedDictionary<string, string>> shapes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var ordered = new SortedDictionary<string, SortedDictionary<string, string>>(shapes.ToDictionary(k => k.Key, k => k.Value), StringComparer.Ordinal);
        File.WriteAllText(path, JsonSerializer.Serialize(ordered, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
    }
}
