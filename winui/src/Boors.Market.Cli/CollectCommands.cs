using Boors.Market.Analytics;
using Boors.Market.Contracts;
using Boors.Market.Domain;
using Boors.Market.Ingestion;
using Boors.Market.RawCache;
using Boors.Market.Sources;
using Boors.Market.Sources.Tsetmc;
using Boors.Market.Storage;

namespace Boors.Market.Cli;

/// <summary>Phase 1 commands: collection jobs, rebuild, health, ad-hoc queries, cache sync and contract checks.</summary>
public sealed class CollectCommands(IServiceProvider services)
{
    private MarketCollector Collector => services.GetRequiredService<MarketCollector>();

    private MarketStore Store => services.GetRequiredService<MarketStore>();

    public async Task<int> CollectAsync(CliArgs cli, CachePolicy policy, CancellationToken cancellationToken)
    {
        var job = cli.At(1) ?? throw new ArgumentException("Usage: boors collect <job>");
        var scope = ParseScope(cli.Get("classes"));
        var max = cli.GetInt("max", int.MaxValue);

        IReadOnlyList<Func<Task<CollectionResult>>> steps = job switch
        {
            "instruments" => [() => Collector.SyncInstrumentsAsync(cli.Has("details"), cli.GetInt("max-details", 300), policy, cancellationToken)],
            "indices" => [() => Collector.SyncIndicesAsync(cli.Has("history"), policy, cancellationToken)],
            "calendar" => [() => Task.FromResult(CalendarResult(Collector.RefreshCalendar()))],
            "backfill" => [() => Collector.BackfillHistoryAsync(max, cli.Has("force"), cli.Has("with-messages"), scope, policy, cancellationToken)],
            "eod" => [() => Collector.EndOfDayAsync(cli.Has("force"), policy, cancellationToken)],
            "funds" => [() => Collector.SyncFundsAsync(policy, cancellationToken)],
            "macro" => [() => Collector.SyncMacroAsync(null, policy, cancellationToken)],
            "codal" => [() => Collector.SyncCodalAsync(cli.GetInt("pages", 5), policy, cancellationToken)],
            "messages" => [() => Collector.SyncMessagesAsync(cli.GetInt("top", 200), policy, cancellationToken)],
            "halts" => [() => Collector.ResolveHaltsAsync(cli.GetInt("max", 500), scope, policy, cancellationToken)],
            "all" => AllSteps(cli, policy, max, scope, cancellationToken),
            _ => throw new ArgumentException($"Unknown job '{job}'. Jobs: instruments, indices, calendar, backfill, eod, funds, macro, codal, messages, halts, all."),
        };

        var results = new List<CollectionResult>();
        foreach (var step in steps)
        {
            var result = await step();
            results.Add(result);
            Console.WriteLine($"{result.Status,-9} {result.Job,-12} items={result.Items,-8} failures={result.Failures,-4} {result.Message}");
        }

        return results.All(r => r.Succeeded) ? 0 : 6;
    }

    /// <summary>
    /// Deletes the store and rebuilds it from the raw cache only (no network): the store is a projection of the cache.
    /// Limitation: per-day end-of-day snapshots between history fetches are not replayed; only the latest response of
    /// each request is used.
    /// </summary>
    public async Task<int> RebuildAsync(CliArgs cli, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var exitCode = await CollectAsync(new CliArgs(["collect", "all", "--force", .. cli.Has("max") ? new[] { "--max", cli.Get("max")! } : []]), CachePolicy.CacheOnly, cancellationToken);
        Console.WriteLine($"Rebuilt {Store.Path_} from the raw cache in {(DateTimeOffset.UtcNow - started).TotalSeconds:0} s.");
        return exitCode;
    }

    public int Health()
    {
        var report = services.GetRequiredService<DataHealthService>().GetReport();
        Console.WriteLine($"Node {report.Node}; expected latest session {report.ExpectedSession:yyyy-MM-dd}; overall: {(report.Healthy ? "HEALTHY" : "ATTENTION")}");
        Console.WriteLine();
        Output.Table(["dataset", "rows", "latest", "expected", "status"], report.Datasets
            .Select(d => (IReadOnlyList<object?>)[d.Dataset, d.Rows, d.LatestDate, d.ExpectedDate, d.Status]));
        Console.WriteLine();
        Output.Table(["job", "last run (UTC)", "status", "items", "failures", "message"], report.LatestRuns
            .Select(r => (IReadOnlyList<object?>)[r.Job, r.StartedAt, r.Status, r.Items, r.Failures, r.Message]));
        Console.WriteLine();
        if (report.LastProbeAt is null)
        {
            Console.WriteLine("No reachability probe on record for this node. Run 'boors probe'.");
        }
        else
        {
            var problems = report.Sources.Where(s => s.Verdict is not ("ok" or "reachable")).ToList();
            Console.WriteLine($"Last probe {report.LastProbeAt:yyyy-MM-dd HH:mm} UTC: {report.Sources.Count - problems.Count}/{report.Sources.Count} sources reachable.");
            foreach (var p in problems)
                Console.WriteLine($"  {p.Verdict}: {p.Name} {p.Status} {p.Error}");
        }

        return report.Healthy ? 0 : 7;
    }

    public int Query(CliArgs cli)
    {
        var sql = cli.At(1) ?? throw new ArgumentException("Usage: boors query \"SELECT ...\"");
        var (columns, rows) = Store.RunQuery(sql, cli.GetInt("max", 200));
        Output.Table(columns, rows.Select(r => (IReadOnlyList<object?>)r));
        Console.WriteLine($"({rows.Count} row(s))");
        return 0;
    }

    public async Task<int> CacheAsync(CliArgs cli, CancellationToken cancellationToken)
    {
        var cache = services.GetRequiredService<IRawCache>() as FileSystemRawCache
            ?? throw new InvalidOperationException("Cache bundles need the file-system raw cache.");
        var path = cli.At(2) ?? throw new ArgumentException("Usage: boors cache export|import <bundle.zip> [--since yyyy-MM-dd]");

        switch (cli.At(1))
        {
            case "export":
                DateOnly? since = DateOnly.TryParseExact(cli.Get("since"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var s) ? s : null;
                var exported = await RawCacheBundle.ExportAsync(cache, path, since, cancellationToken);
                Console.WriteLine($"Exported {exported} file(s) to {Path.GetFullPath(path)} ({new FileInfo(path).Length / 1024} KiB).");
                return 0;
            case "import":
                var (imported, skipped) = await RawCacheBundle.ImportAsync(cache, path, cancellationToken);
                Console.WriteLine($"Imported {imported} file(s), skipped {skipped} already present. Run 'boors rebuild' or the collection jobs with --cache to load them into the store.");
                return 0;
            default:
                throw new ArgumentException("Usage: boors cache export|import <bundle.zip> [--since yyyy-MM-dd]");
        }
    }

    public async Task<int> ImportAsync(CliArgs cli, CancellationToken cancellationToken)
    {
        if (cli.At(1) != "macro" || cli.At(2) is not { } path)
            throw new ArgumentException("Usage: boors import macro <file.csv>   (columns: series,period,period_end,value,source[,note])");

        var result = await Collector.ImportMacroMonthlyAsync(path, cancellationToken);
        Console.WriteLine($"{result.Status}: {result.Items} value(s) imported. {result.Message}");
        return result.Succeeded ? 0 : 6;
    }

    /// <summary>
    /// Compares TSETMC's published adjustment list with the factors implied by the bars, and checks that compounding
    /// daily returns reproduces the point-in-time adjusted price path.
    /// </summary>
    public async Task<int> VerifyAdjustmentsAsync(CliArgs cli, CachePolicy policy, CancellationToken cancellationToken)
    {
        var tsetmc = services.GetRequiredService<TsetmcClient>();
        var target = cli.At(2) ?? throw new ArgumentException("Usage: boors verify adjustments <symbol|insCode>");
        var insCode = long.TryParse(target, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) ? code
            : (await tsetmc.ResolveSymbolAsync(target, policy, cancellationToken))?.InsCode ?? throw new ArgumentException($"Unknown symbol '{target}'.");

        var bars = await tsetmc.GetDailyHistoryAsync(insCode, policy, cancellationToken);
        var published = await tsetmc.GetPriceAdjustmentsAsync(insCode, policy, cancellationToken);
        var implied = PointInTime.ImpliedAdjustments(bars);

        var publishedByFactor = published.Select(p => Math.Round(p.Factor, 4)).ToHashSet();
        Output.Table(["implied date", "factor", "in published list?"], implied.Select(i => (IReadOnlyList<object?>)[i.Date, Math.Round(i.Factor, 4),
            published.Any(p => p.Date == i.Date) ? "same date" : publishedByFactor.Contains(Math.Round(i.Factor, 4)) ? "different date" : "MISSING"]));

        var adjusted = PointInTime.AdjustFromBars(bars.Select(b => (b, (DayStatus?)null)).ToList(), bars[^1].Date);
        var compounded = 1m;
        var worst = 0m;
        for (var i = 1; i < adjusted.Count; i++)
        {
            if (adjusted[i].Return is { } r)
                compounded *= 1 + r;
            var expected = adjusted[i].AdjustedClose / adjusted[0].AdjustedClose;
            if (adjusted[i].Return is not null && expected != 0)
                worst = Math.Max(worst, Math.Abs(compounded / expected - 1));
        }

        Console.WriteLine();
        Console.WriteLine($"{implied.Count} implied events vs {published.Count} published. Worst gap between compounded returns and the adjusted path: {worst:P4}.");
        return 0;
    }

    /// <summary>
    /// Phase 1 exit check: for the most-traded equities, compares what the store holds with what TSETMC serves now —
    /// the latest stored bar against GetClosingPriceInfo, and stored bars of the last days against a fresh daily history.
    /// The second check is what proves that end-of-day rows built from the market watch match the official history.
    /// </summary>
    public async Task<int> VerifyReconcileAsync(CliArgs cli, CancellationToken cancellationToken)
    {
        var tsetmc = services.GetRequiredService<TsetmcClient>();
        var count = cli.GetInt("count", 20);
        var days = cli.GetInt("days", 5);
        var equities = new HashSet<string> { "tse_stock", "ifb_stock" };
        var latestSession = Store.RunQuery("SELECT max(d) FROM daily_bar").Rows.Single()[0] as DateOnly?
            ?? throw new InvalidOperationException("The store has no bars. Run 'boors collect all' first.");
        var sample = Store.RunQuery($"""
            SELECT b.ins_code, i.symbol FROM daily_bar b JOIN instrument i USING (ins_code)
            WHERE b.d = DATE '{latestSession:yyyy-MM-dd}' AND i.instrument_class IN ('tse_stock', 'ifb_stock') AND i.parent_ins_code IS NULL AND b.trade_count > 0
            ORDER BY b.value DESC LIMIT {count}
            """).Rows.Select(r => (InsCode: (long)r[0]!, Symbol: (string)r[1]!)).ToList();

        var checks = new List<IReadOnlyList<object?>>();
        int mismatches = 0, compared = 0;
        foreach (var (insCode, symbol) in sample)
        {
            var stored = Store.GetDailyBars(insCode).ToDictionary(b => b.Bar.Date);
            var live = await tsetmc.GetClosingPriceInfoAsync(insCode, CachePolicy.Network, cancellationToken);
            var history = await tsetmc.GetDailyHistoryAsync(insCode, CachePolicy.Network, cancellationToken);

            var references = history.Where(h => stored.ContainsKey(h.Date)).TakeLast(days).Select(h => (Bar: h, Source: "history")).ToList();
            if (live.Date is { } liveDate && stored.ContainsKey(liveDate) && references.All(r => r.Bar.Date != liveDate))
            {
                references.Add((new DailyBar
                {
                    InsCode = insCode, Date = liveDate, Close = live.Close, Last = live.Last, Low = live.Low, High = live.High, Open = live.Open,
                    Yesterday = live.Yesterday, TradeCount = live.TradeCount, Volume = live.Volume, Value = live.Value,
                }, "closing-price-info"));
            }

            foreach (var (reference, source) in references)
            {
                var mine = stored[reference.Date];
                var differences = new List<string>();
                if (mine.Bar.Close != reference.Close) differences.Add($"close {mine.Bar.Close} vs {reference.Close}");
                if (mine.Bar.Yesterday != reference.Yesterday) differences.Add($"yesterday {mine.Bar.Yesterday} vs {reference.Yesterday}");
                if (mine.Bar.Volume != reference.Volume) differences.Add($"volume {mine.Bar.Volume} vs {reference.Volume}");
                if (mine.Bar.Value != reference.Value) differences.Add($"value {mine.Bar.Value} vs {reference.Value}");
                if (mine.Bar.TradeCount != reference.TradeCount) differences.Add($"trades {mine.Bar.TradeCount} vs {reference.TradeCount}");
                compared++;
                mismatches += differences.Count > 0 ? 1 : 0;
                checks.Add([symbol, reference.Date, mine.Source, source, differences.Count == 0 ? "match" : string.Join("; ", differences)]);
            }
        }

        Output.Table(["symbol", "date", "stored from", "checked against", "result"], checks);
        Console.WriteLine();
        Console.WriteLine($"{compared - mismatches}/{compared} symbol-days match ({sample.Count} symbols, latest session {latestSession:yyyy-MM-dd}).");
        return mismatches == 0 ? 0 : 9;
    }

    public async Task<int> VerifyContractsAsync(CliArgs cli, CancellationToken cancellationToken)
    {
        var baselinePath = cli.Get("baseline") ?? Path.Combine("src", "Market", "Boors.Market", "Contracts", "baseline.json");
        var contracts = services.GetRequiredService<SourceContracts>();
        var baseline = File.Exists(baselinePath) ? SourceContracts.LoadBaseline(baselinePath) : new Dictionary<string, SortedDictionary<string, string>>();

        // Some endpoints alternate between response variants (TSETMC's market watch does), so an update takes several
        // samples, and a check re-samples before calling a contract broken.
        var samples = cli.Has("update") ? cli.GetInt("samples", 4) : 1;
        var current = new Dictionary<string, SortedDictionary<string, string>>();
        foreach (var (name, client, request) in contracts.All())
        {
            try
            {
                for (var i = 0; i < samples; i++)
                {
                    var shape = JsonShape.Describe(await client.GetBodyAsync(request, CachePolicy.Network, cancellationToken));
                    current[name] = current.TryGetValue(name, out var earlier) ? JsonShape.Merge(earlier, shape) : shape;
                }

                for (var retry = 0; !cli.Has("update") && retry < 3 && baseline.TryGetValue(name, out var expected) && JsonShape.Compare(name, expected, current[name]).IsBreaking; retry++)
                {
                    var again = JsonShape.Describe(await client.GetBodyAsync(request, CachePolicy.Network, cancellationToken));
                    if (!JsonShape.Compare(name, expected, again).IsBreaking)
                        current[name] = again;
                }
            }
            catch (Exception e) when (e is SourceRequestException or HttpRequestException or Polly.ExecutionRejectedException or JsonException)
            {
                Console.WriteLine($"FAIL     {name}: {e.Message}");
            }
        }

        if (cli.Has("update"))
        {
            // Merge rather than overwrite, so fields a source sends only sometimes become optional instead of flapping.
            var merged = baseline.Keys.Union(current.Keys)
                .ToDictionary(k => k, k => SourceContracts.WithKnownOptionals(k, current.TryGetValue(k, out var sample)
                    ? JsonShape.Merge(baseline.GetValueOrDefault(k), sample)
                    : baseline[k]));
            SourceContracts.SaveBaseline(baselinePath, merged);
            Console.WriteLine($"Baseline written: {Path.GetFullPath(baselinePath)} ({merged.Count} contracts).");
            return 0;
        }

        var breaking = 0;
        foreach (var name in baseline.Keys.Union(current.Keys).Order(StringComparer.Ordinal))
        {
            if (!current.TryGetValue(name, out var now))
                continue;
            if (!baseline.TryGetValue(name, out var before))
            {
                Console.WriteLine($"NEW      {name} (not in baseline)");
                continue;
            }

            var diff = JsonShape.Compare(name, before, now);
            breaking += diff.IsBreaking ? 1 : 0;
            Console.WriteLine($"{(diff.IsBreaking ? "BREAKING" : diff.Added.Count > 0 ? "ADDED" : "ok"),-8} {name}");
            foreach (var m in diff.Missing)
                Console.WriteLine($"           missing  {m}");
            foreach (var k in diff.KindChanged)
                Console.WriteLine($"           changed  {k}");
            foreach (var a in diff.Added.Take(10))
                Console.WriteLine($"           added    {a}");
        }

        return breaking == 0 && current.Count == baseline.Count ? 0 : 8;
    }

    private IReadOnlyList<Func<Task<CollectionResult>>> AllSteps(CliArgs cli, CachePolicy policy, int max, IReadOnlySet<InstrumentClass>? scope, CancellationToken cancellationToken) =>
    [
        () => Collector.SyncIndicesAsync(history: true, policy, cancellationToken),
        () => Collector.SyncInstrumentsAsync(cli.Has("details"), cli.GetInt("max-details", 300), policy, cancellationToken),
        () => Collector.BackfillHistoryAsync(max, force: false, cli.Has("with-messages"), scope, policy, cancellationToken),
        () => Collector.EndOfDayAsync(cli.Has("force"), policy, cancellationToken),
        () => Collector.SyncMessagesAsync(200, policy, cancellationToken),
        () => Collector.ResolveHaltsAsync(cli.GetInt("max-halts", 500), scope, policy, cancellationToken),
        () => Collector.SyncCodalAsync(cli.GetInt("pages", 5), policy, cancellationToken),
        () => Collector.SyncMacroAsync(null, policy, cancellationToken),
    ];

    private static CollectionResult CalendarResult(MarketCalendar calendar) =>
        new("calendar", "ok", calendar.DetectedClosedDays.Count, 0, $"{calendar.DetectedClosedDays.Count} closure days recorded");

    private static IReadOnlySet<InstrumentClass>? ParseScope(string? classes) =>
        string.IsNullOrWhiteSpace(classes)
            ? null
            : classes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(InstrumentClassifier.FromRuleKey).ToHashSet();
}
