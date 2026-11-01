using Boors.Market.Sources;
using Boors.Market.Sources.Codal;
using Boors.Market.Sources.Macro;
using Boors.Market.Storage;

namespace Boors.Market.Ingestion;

// Disclosures, supervisor messages and macro series.
public sealed partial class MarketCollector
{
    /// <summary>Pages through the newest Codal letters until a page contains only letters already stored.</summary>
    public Task<CollectionResult> SyncCodalAsync(int maxPages, CachePolicy policy, CancellationToken cancellationToken) =>
        RunAsync("codal", async () =>
        {
            var symbols = SymbolMap();
            var stored = 0;
            var pages = 0;
            for (var page = 1; page <= maxPages; page++)
            {
                var result = await codal.SearchAsync(new CodalSearchQuery { PageNumber = page }, policy, cancellationToken);
                pages++;
                if (result.Letters.Count == 0)
                    break;

                var known = store.GetKnownTracingNumbers(result.Letters.Select(l => l.TracingNo));
                store.UpsertCodalLetters(result.Letters, symbols);
                stored += result.Letters.Count - known.Count;
                if (known.Count == result.Letters.Count || policy == CachePolicy.CacheOnly)
                    break;
            }

            return (stored, 0, $"{stored} new letters from {pages} page(s)");
        });

    /// <summary>Latest supervisor messages of both markets, linked to instruments by the symbols they mention.</summary>
    public Task<CollectionResult> SyncMessagesAsync(int top, CachePolicy policy, CancellationToken cancellationToken) =>
        RunAsync("messages", async () =>
        {
            var symbols = SymbolMap();
            var items = 0;
            var failures = 0;
            foreach (var flow in new[] { 1, 2 })
            {
                failures += await TryAsync($"messages flow {flow}", async () =>
                {
                    var messages = await tsetmc.GetLatestMessagesAsync(flow, top, policy, cancellationToken);
                    store.UpsertMessages(messages, symbols);
                    items += messages.Count;
                });
            }

            return (items, failures, null);
        });

    /// <summary>Daily history of the tgju series (free-market dollar, Emami coin, 18k gold, ounce, euro).</summary>
    public Task<CollectionResult> SyncMacroAsync(IReadOnlyList<string>? series, CachePolicy policy, CancellationToken cancellationToken) =>
        RunAsync("macro", async () =>
        {
            var items = 0;
            var failures = 0;
            foreach (var code in series ?? TgjuSeries.Default)
            {
                failures += await TryAsync($"tgju {code}", async () =>
                {
                    var bars = await tgju.GetHistoryAsync(code, policy, cancellationToken);
                    store.UpsertMacroDaily(bars);
                    items += bars.Count;
                });
            }

            return (items, failures, null);
        });

    /// <summary>
    /// Imports monthly macro values (CPI from SCI and CBI, the Exchange Centre dollar, policy rates...) that have no
    /// machine-readable source. CSV columns: series,period,period_end,value,source[,note]. Example:
    /// <c>cpi_yoy,1405-06,2026-09-22,89.8,sci,point-to-point</c>.
    /// </summary>
    public Task<CollectionResult> ImportMacroMonthlyAsync(string csvPath, CancellationToken cancellationToken) =>
        RunAsync("macro-import", async () =>
        {
            var lines = await File.ReadAllLinesAsync(csvPath, cancellationToken);
            var values = new List<MacroMonthlyValue>();
            var failures = 0;
            foreach (var (line, number) in lines.Select((l, i) => (l.Trim(), i + 1)))
            {
                if (line.Length == 0 || line.StartsWith('#') || (number == 1 && line.StartsWith("series", StringComparison.OrdinalIgnoreCase)))
                    continue;

                var fields = line.Split(',');
                if (fields.Length < 5
                    || !DateOnly.TryParseExact(fields[2].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var periodEnd)
                    || !decimal.TryParse(fields[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    failures++;
                    continue;
                }

                values.Add(new MacroMonthlyValue(fields[0].Trim(), fields[1].Trim(), periodEnd, value, fields[4].Trim(), fields.Length > 5 ? string.Join(',', fields[5..]).Trim() : null));
            }

            store.UpsertMacroMonthly(values);
            return (values.Count, failures, failures > 0 ? $"{failures} malformed line(s) skipped" : null);
        });
}
