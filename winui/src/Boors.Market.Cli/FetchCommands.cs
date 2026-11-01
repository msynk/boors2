using Boors.Market.Domain;
using Boors.Market.Rules;
using Boors.Market.Sources;
using Boors.Market.Sources.Codal;
using Boors.Market.Sources.Tsetmc;
using Microsoft.Extensions.Options;

namespace Boors.Market.Cli;

public sealed class FetchCommands(TsetmcClient tsetmc, CodalClient codal, MarketRules rules, IOptions<MarketDataOptions> options)
{
    private string ParsedDir => Path.Combine(options.Value.DataRoot, "parsed");

    public async Task<int> InstrumentsAsync(CliArgs cli, CachePolicy policy, CancellationToken cancellationToken)
    {
        var snapshot = await tsetmc.GetMarketWatchAsync(new MarketWatchQuery(), policy, cancellationToken);
        var rows = snapshot.Rows.OrderBy(r => r.Symbol, StringComparer.Ordinal).ToList();
        Console.WriteLine($"Market watch: {rows.Count} instruments.");

        Output.Table(["class (by ISIN)", "count"], rows
            .GroupBy(r => r.Class)
            .OrderByDescending(g => g.Count())
            .Select(g => (IReadOnlyList<object?>)[g.Key.ToRuleKey(), g.Count()]));

        var path = Path.Combine(ParsedDir, "instruments.csv");
        await Output.WriteCsvAsync(path,
            ["ins_code", "symbol", "name", "isin", "sector", "class_by_isin", "close", "value"],
            rows.Select(r => (IReadOnlyList<object?>)[r.InsCode, r.Symbol, r.Name, r.Isin, r.SectorCode, r.Class.ToRuleKey(), r.Close, r.Value]),
            cancellationToken);
        Console.WriteLine($"Wrote {Path.GetFullPath(path)}");

        if (!cli.Has("details"))
            return 0;

        // GetInstrumentInfo per instrument gives yVal/flow/board. Rate-limited, so this takes ~N/2 seconds;
        // rerun with --cache to resume from what is already in the raw cache.
        var limit = cli.GetInt("limit", rows.Count);
        var detailed = new List<Instrument>();
        var failures = 0;
        var processed = 0;
        foreach (var row in rows.Take(limit))
        {
            if (++processed % 100 == 0)
                Console.WriteLine($"  {processed}/{limit} instruments...");

            try
            {
                detailed.Add(await tsetmc.GetInstrumentInfoAsync(row.InsCode, policy == CachePolicy.Network ? CachePolicy.PreferCache : policy, cancellationToken));
            }
            catch (Exception e) when (e is SourceRequestException or Polly.ExecutionRejectedException or HttpRequestException)
            {
                // One bad instrument must not end a 30-minute run; an open circuit means the source is struggling,
                // so the next iterations will fail fast until it closes again.
                failures++;
                Console.Error.WriteLine($"  {row.Symbol} ({row.InsCode}): {e.Message}");
            }
        }

        var detailedPath = Path.Combine(ParsedDir, "instruments-detailed.csv");
        await Output.WriteCsvAsync(detailedPath,
            ["ins_code", "symbol", "name", "isin", "company_isin", "y_val", "flow", "board", "board_title", "sector", "sector_name", "base_volume", "total_shares", "free_float", "class"],
            detailed.Select(i => (IReadOnlyList<object?>)[i.InsCode, i.Symbol, i.Name, i.Isin, i.CompanyIsin, i.YVal, i.Flow, i.Board, i.BoardTitle, i.SectorCode, i.SectorName, i.BaseVolume, i.TotalShares, i.FreeFloatPercent, i.Class.ToRuleKey()]),
            cancellationToken);

        Console.WriteLine();
        Output.Table(["y_val", "flow", "class", "count"], detailed
            .GroupBy(i => (i.YVal, i.Flow, i.Class))
            .OrderByDescending(g => g.Count())
            .Select(g => (IReadOnlyList<object?>)[g.Key.YVal, g.Key.Flow, g.Key.Class.ToRuleKey(), g.Count()]));
        Console.WriteLine($"Wrote {Path.GetFullPath(detailedPath)} ({detailed.Count} rows, {failures} failures)");
        return failures == 0 ? 0 : 4;
    }

    public async Task<int> HistoryAsync(CliArgs cli, CachePolicy policy, CancellationToken cancellationToken)
    {
        var target = cli.At(2) ?? throw new ArgumentException("Usage: boors fetch history <symbol|insCode>");
        var insCode = await ResolveAsync(target, policy, cancellationToken);
        if (insCode is null)
        {
            Console.Error.WriteLine($"No active instrument matches '{target}'.");
            return 1;
        }

        var instrument = await tsetmc.GetInstrumentInfoAsync(insCode.Value, policy, cancellationToken);
        var bars = await tsetmc.GetDailyHistoryAsync(insCode.Value, policy, cancellationToken);
        var flows = await tsetmc.GetClientTypeHistoryAsync(insCode.Value, policy, cancellationToken);
        var adjustments = await tsetmc.GetPriceAdjustmentsAsync(insCode.Value, policy, cancellationToken);
        var shareChanges = await tsetmc.GetShareChangesAsync(insCode.Value, policy, cancellationToken);
        var index = await tsetmc.GetIndexHistoryAsync(TsetmcEndpoints.TedpixInsCode, policy == CachePolicy.Network ? CachePolicy.PreferCache : policy, cancellationToken);

        var calendar = MarketCalendar.FromRules(rules, index);
        var classifier = new DayStatusClassifier(calendar, date => rules.GetPriceLimit(instrument.Class, date).Value);
        var classified = classifier.Classify(bars);
        var flowsByDate = flows.ToDictionary(f => f.Date);

        Console.WriteLine($"{instrument.Symbol} — {instrument.Name} ({instrument.InsCode}, {instrument.Isin}, yVal {instrument.YVal}, flow {instrument.Flow}, board {instrument.Board}, class {instrument.Class.ToRuleKey()})");
        Console.WriteLine($"Daily rows: {bars.Count} ({bars.FirstOrDefault()?.Date:yyyy-MM-dd} .. {bars.LastOrDefault()?.Date:yyyy-MM-dd}); client-type rows: {flows.Count}; adjustments: {adjustments.Count}; share changes: {shareChanges.Count}");
        Console.WriteLine();
        Output.Table(["day status", "rows"], classified
            .GroupBy(c => c.Status)
            .OrderBy(g => g.Key)
            .Select(g => (IReadOnlyList<object?>)[g.Key, g.Count()]));
        Console.WriteLine();
        Output.Table(["date", "status", "close", "yesterday", "low", "high", "trades", "value", "indiv. net value"], classified
            .TakeLast(8)
            .Select(c => (IReadOnlyList<object?>)[c.Bar.Date, c.Status, c.Bar.Close, c.Bar.Yesterday, c.Bar.Low, c.Bar.High, c.Bar.TradeCount, c.Bar.Value, flowsByDate.GetValueOrDefault(c.Bar.Date)?.IndividualNetValue]));

        var path = Path.Combine(ParsedDir, "history", $"{insCode}.csv");
        await Output.WriteCsvAsync(path,
            ["date", "status", "open", "high", "low", "close", "last", "yesterday", "trades", "volume", "value",
             "buy_i_value", "sell_i_value", "buy_i_count", "sell_i_count", "buy_n_value", "sell_n_value", "buyer_power"],
            classified.Select(c =>
            {
                var f = flowsByDate.GetValueOrDefault(c.Bar.Date);
                return (IReadOnlyList<object?>)[c.Bar.Date, c.Status, c.Bar.Open, c.Bar.High, c.Bar.Low, c.Bar.Close, c.Bar.Last, c.Bar.Yesterday, c.Bar.TradeCount, c.Bar.Volume, c.Bar.Value,
                    f?.BuyIndividualValue, f?.SellIndividualValue, f?.BuyIndividualCount, f?.SellIndividualCount, f?.BuyInstitutionalValue, f?.SellInstitutionalValue, f?.BuyerPower];
            }),
            cancellationToken);

        var eventsPath = Path.Combine(ParsedDir, "history", $"{insCode}-events.csv");
        await Output.WriteCsvAsync(eventsPath,
            ["date", "kind", "adjusted_close", "unadjusted_close", "factor", "shares_before", "shares_after"],
            adjustments.Select(a => (IReadOnlyList<object?>)[a.Date, "price_adjustment", a.AdjustedClose, a.UnadjustedClose, a.Factor, null, null])
                .Concat(shareChanges.Select(s => (IReadOnlyList<object?>)[s.Date, "share_change", null, null, null, s.SharesBefore, s.SharesAfter]))
                .OrderBy(r => (DateOnly)r[0]!),
            cancellationToken);

        Console.WriteLine();
        Console.WriteLine($"Wrote {Path.GetFullPath(path)}");
        Console.WriteLine($"Wrote {Path.GetFullPath(eventsPath)}");
        return 0;
    }

    public async Task<int> MarketWatchAsync(CachePolicy policy, CancellationToken cancellationToken)
    {
        var snapshot = await tsetmc.GetMarketWatchAsync(new MarketWatchQuery(), policy, cancellationToken);
        var traded = snapshot.Rows.Where(r => r.TradeCount > 0).ToList();

        // Queue = best bid pinned at the upper band with no offers (buy queue), or the mirror image (sell queue).
        var buyQueues = snapshot.Rows.Where(r => r.BestLimits.FirstOrDefault() is { } l && r.AllowedHigh > 0 && l.BidPrice == r.AllowedHigh && l.AskVolume == 0).ToList();
        var sellQueues = snapshot.Rows.Where(r => r.BestLimits.FirstOrDefault() is { } l && r.AllowedLow > 0 && l.AskPrice == r.AllowedLow && l.BidVolume == 0).ToList();

        Console.WriteLine($"Instruments: {snapshot.Rows.Count}, traded today: {traded.Count}");
        Console.WriteLine($"Buy queues: {buyQueues.Count} ({buyQueues.Sum(r => r.BestLimits[0].BidVolume * r.BestLimits[0].BidPrice) / 10_000_000_000_000m:0.##} hemat), sell queues: {sellQueues.Count} ({sellQueues.Sum(r => r.BestLimits[0].AskVolume * r.BestLimits[0].AskPrice) / 10_000_000_000_000m:0.##} hemat)");
        Console.WriteLine($"Max hEven (for incremental polling): {snapshot.MaxHEven}");
        Console.WriteLine();
        Output.Table(["class (by ISIN)", "traded", "value (hemat = 10^13 rial)"], traded
            .GroupBy(r => r.Class)
            .OrderByDescending(g => g.Sum(r => r.Value))
            .Select(g => (IReadOnlyList<object?>)[g.Key.ToRuleKey(), g.Count(), Math.Round(g.Sum(r => r.Value) / 10_000_000_000_000m, 2)]));
        Console.WriteLine();
        Output.Table(["symbol", "class", "close", "change %", "value (B rial)", "trades"], traded
            .Where(r => r.Class is InstrumentClass.TseStock or InstrumentClass.IfbStock or InstrumentClass.Rights)
            .OrderByDescending(r => r.Value)
            .Take(15)
            .Select(r => (IReadOnlyList<object?>)[r.Symbol, r.Class.ToRuleKey(), r.Close, r.Yesterday == 0 ? null : Math.Round((r.Close / r.Yesterday - 1) * 100, 2), Math.Round(r.Value / 1_000_000_000m, 1), r.TradeCount]));
        return 0;
    }

    public async Task<int> IndexAsync(CliArgs cli, CachePolicy policy, CancellationToken cancellationToken)
    {
        var insCode = long.TryParse(cli.At(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) ? code : TsetmcEndpoints.TedpixInsCode;
        var bars = await tsetmc.GetIndexHistoryAsync(insCode, policy, cancellationToken);
        var flat = MarketCalendar.DetectFlatIndexDays(bars);

        Console.WriteLine($"Index {insCode}: {bars.Count} rows ({bars.FirstOrDefault()?.Date:yyyy-MM-dd} .. {bars.LastOrDefault()?.Date:yyyy-MM-dd}), last close {bars.LastOrDefault()?.Close:N0}");
        Console.WriteLine($"Rows identical to the previous close (closure candidates): {flat.Count}");
        Console.WriteLine();
        Output.Table(["from", "to", "rows", "known closure in rules.yaml"], CollapseRuns(flat, bars.Select(b => b.Date).ToList())
            .Where(r => r.Rows >= 2)
            .Select(r => (IReadOnlyList<object?>)[r.From, r.To, r.Rows, rules.IsKnownClosure(r.From) ? "yes" : "NO"]));

        var path = Path.Combine(ParsedDir, "index", $"{insCode}.csv");
        await Output.WriteCsvAsync(path, ["date", "close", "low", "high", "flat"],
            bars.Select(b => (IReadOnlyList<object?>)[b.Date, b.Close, b.Low, b.High, flat.Contains(b.Date) ? 1 : 0]), cancellationToken);
        Console.WriteLine();
        Console.WriteLine($"Wrote {Path.GetFullPath(path)}");
        return 0;
    }

    public async Task<int> StaticDataAsync(CachePolicy policy, CancellationToken cancellationToken)
    {
        var items = await tsetmc.GetStaticDataAsync(policy, cancellationToken);
        Output.Table(["type", "code", "name"], items.OrderBy(i => i.Type).ThenBy(i => i.Code).Select(i => (IReadOnlyList<object?>)[i.Type, i.Code, i.Name]));
        return 0;
    }

    public async Task<int> CodalCategoriesAsync(CachePolicy policy, CancellationToken cancellationToken)
    {
        var taxonomy = await codal.GetTaxonomyAsync(policy, cancellationToken);
        foreach (var category in taxonomy.Categories)
        {
            Console.WriteLine($"[{category.Code}] {category.Name}");
            foreach (var letterType in category.LetterTypes)
                Console.WriteLine($"    {letterType.Id,5}  {letterType.Name}");
        }

        var path = Path.Combine(ParsedDir, "codal-taxonomy.json");
        Directory.CreateDirectory(ParsedDir);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(taxonomy.Categories, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), cancellationToken);
        Console.WriteLine();
        Console.WriteLine($"{taxonomy.LetterTypesById.Count} letter types in {taxonomy.Categories.Count} categories. Wrote {Path.GetFullPath(path)}");
        return 0;
    }

    public async Task<int> CodalLettersAsync(CliArgs cli, CachePolicy policy, CancellationToken cancellationToken)
    {
        var symbol = cli.At(2) ?? throw new ArgumentException("Usage: boors fetch codal-letters <symbol> [--type id] [--page n]");
        var page = await codal.SearchAsync(new CodalSearchQuery
        {
            Symbol = symbol,
            LetterType = cli.GetInt("type", -1),
            PageNumber = cli.GetInt("page", 1),
        }, policy, cancellationToken);

        Console.WriteLine($"{page.Total} letters ({page.PageCount} pages).");
        Output.Table(["tracing", "published", "type", "code", "group", "excel", "title"], page.Letters
            .Select(l => (IReadOnlyList<object?>)[l.TracingNo, l.PublishedAt, l.LetterTypeId, l.LetterCode, l.DisclosureGroup, l.HasExcel ? "y" : "", l.Title.Length > 70 ? l.Title[..70] + "…" : l.Title]));
        return 0;
    }

    private async Task<long?> ResolveAsync(string target, CachePolicy policy, CancellationToken cancellationToken)
    {
        if (long.TryParse(target, NumberStyles.Integer, CultureInfo.InvariantCulture, out var insCode))
            return insCode;

        return (await tsetmc.ResolveSymbolAsync(target, policy, cancellationToken))?.InsCode;
    }

    private static IEnumerable<(DateOnly From, DateOnly To, int Rows)> CollapseRuns(IReadOnlyList<DateOnly> flatDays, IReadOnlyList<DateOnly> allDays)
    {
        // Group flat days that are consecutive rows of the index (not calendar days: weekends have no rows).
        var position = allDays.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);
        DateOnly? start = null, end = null;
        var count = 0;
        foreach (var day in flatDays.Order())
        {
            if (end is { } e && position[day] == position[e] + 1)
            {
                end = day;
                count++;
                continue;
            }

            if (start is { } s)
                yield return (s, end!.Value, count);
            start = end = day;
            count = 1;
        }

        if (start is { } last)
            yield return (last, end!.Value, count);
    }
}
