using System.Text;
using Boors.Market.Domain;
using Boors.Market.Sources;
using Boors.Market.Sources.Tsetmc;
using Boors.Market.Text;
using Microsoft.Extensions.Options;

namespace Boors.Market.Cli;

public sealed class VerifyCommands(TsetmcClient tsetmc, IOptions<MarketDataOptions> options, TimeProvider timeProvider)
{
    private sealed record FieldCheck(string Field, string MarketWatchKey, string Reference, Func<MarketWatchRow, Instrument, ClosingPriceSnapshot, (decimal Low, decimal High)?, BestLimitLevel?, (object? Actual, object? Expected)> Compare);

    // The inferred compact-key mapping (docs/development-plan.md §3.8), each checked against a long-name endpoint.
    private static readonly FieldCheck[] Checks =
    [
        new("last price", "pdv", "GetClosingPriceInfo.pDrCotVal", (m, _, c, _, _) => (m.Last, c.Last)),
        new("closing price", "pcl", "GetClosingPriceInfo.pClosing", (m, _, c, _, _) => (m.Close, c.Close)),
        new("first price", "pf", "GetClosingPriceInfo.priceFirst", (m, _, c, _, _) => (m.Open, c.Open)),
        new("day low", "pmn", "GetClosingPriceInfo.priceMin", (m, _, c, _, _) => (m.Low, c.Low)),
        new("day high", "pmx", "GetClosingPriceInfo.priceMax", (m, _, c, _, _) => (m.High, c.High)),
        new("yesterday", "py", "GetClosingPriceInfo.priceYesterday", (m, _, c, _, _) => (m.Yesterday, c.Yesterday)),
        new("trade count", "ztt", "GetClosingPriceInfo.zTotTran", (m, _, c, _, _) => (m.TradeCount, c.TradeCount)),
        new("volume", "qtj", "GetClosingPriceInfo.qTotTran5J", (m, _, c, _, _) => (m.Volume, c.Volume)),
        new("value", "qtc", "GetClosingPriceInfo.qTotCap", (m, _, c, _, _) => (m.Value, c.Value)),
        new("allowed high", "pMax", "GetInstrumentInfo.staticThreshold.psGelStaMax", (m, _, _, t, _) => (m.AllowedHigh, t?.High)),
        new("allowed low", "pMin", "GetInstrumentInfo.staticThreshold.psGelStaMin", (m, _, _, t, _) => (m.AllowedLow, t?.Low)),
        new("base volume", "bv", "GetInstrumentInfo.baseVol", (m, i, _, _, _) => (m.BaseVolume, i.BaseVolume ?? 0)),
        new("total shares", "ztd", "GetInstrumentInfo.zTitad", (m, i, _, _, _) => (m.TotalShares, i.TotalShares ?? 0)),
        new("sector code", "csv", "GetInstrumentInfo.sector.cSecVal", (m, i, _, _, _) => (m.SectorCode, i.SectorCode)),
        new("ISIN", "insID", "GetInstrumentInfo.instrumentID", (m, i, _, _, _) => (m.Isin, i.Isin)),
        new("best bid price", "blDs[0].pmd", "BestLimits[0].pMeDem", (m, _, _, _, b) => (m.BestLimits.FirstOrDefault()?.BidPrice, b?.BidPrice)),
        new("best bid volume", "blDs[0].qmd", "BestLimits[0].qTitMeDem", (m, _, _, _, b) => (m.BestLimits.FirstOrDefault()?.BidVolume, b?.BidVolume)),
        new("best ask price", "blDs[0].pmo", "BestLimits[0].pMeOf", (m, _, _, _, b) => (m.BestLimits.FirstOrDefault()?.AskPrice, b?.AskPrice)),
        new("best ask volume", "blDs[0].qmo", "BestLimits[0].qTitMeOf", (m, _, _, _, b) => (m.BestLimits.FirstOrDefault()?.AskVolume, b?.AskVolume)),
    ];

    public async Task<int> MarketWatchKeysAsync(CliArgs cli, CachePolicy policy, CancellationToken cancellationToken)
    {
        var count = cli.GetInt("count", 10);
        var startedIran = IranTime.Now(timeProvider);
        var snapshot = await tsetmc.GetMarketWatchAsync(new MarketWatchQuery { PaperTypes = [1] }, policy, cancellationToken);
        var sample = snapshot.Rows.Where(r => r.TradeCount > 0).OrderByDescending(r => r.Value).Take(count).ToList();

        var tallies = Checks.ToDictionary(c => c.Field, _ => (Match: 0, Total: 0, Example: (string?)null));
        foreach (var row in sample)
        {
            var infoBody = await tsetmc.GetBodyAsync(tsetmc.Endpoints.InstrumentInfo(row.InsCode), policy, cancellationToken);
            var instrument = TsetmcParser.ParseInstrumentInfo(infoBody, row.InsCode);
            var threshold = TsetmcParser.ParseStaticThreshold(infoBody);
            var closing = await tsetmc.GetClosingPriceInfoAsync(row.InsCode, policy, cancellationToken);
            var bestLimits = await tsetmc.GetBestLimitsAsync(row.InsCode, policy, cancellationToken);

            foreach (var check in Checks)
            {
                var (actual, expected) = check.Compare(row, instrument, closing, threshold, bestLimits.FirstOrDefault(l => l.Level == 1));
                var tally = tallies[check.Field];
                var same = Equals(Normalize(actual), Normalize(expected));
                tallies[check.Field] = (tally.Match + (same ? 1 : 0), tally.Total + 1, tally.Example ?? (same ? null : $"{row.Symbol}: mw={Output.Format(actual)} ref={Output.Format(expected)}"));
            }
        }

        var during = IsDuringSession(startedIran);
        Output.Table(["field", "mw key", "reference", "match"], Checks.Select(c =>
        {
            var t = tallies[c.Field];
            return (IReadOnlyList<object?>)[c.Field, c.MarketWatchKey, c.Reference, $"{t.Match}/{t.Total}"];
        }));

        var report = new StringBuilder()
            .AppendLine("# Market-watch compact-key verification")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"- Run: {startedIran:yyyy-MM-dd HH:mm} Iran time, node `{options.Value.Node}`")
            .AppendLine(CultureInfo.InvariantCulture, $"- Sample: top {sample.Count} traded shares by value: {string.Join("، ", sample.Select(s => s.Symbol))}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Market session in progress during the run: {(during ? "yes — live values move between calls, so small mismatches are expected" : "no — values are static")}")
            .AppendLine()
            .AppendLine("| Field | Market-watch key | Reference | Match | First mismatch |")
            .AppendLine("|---|---|---|---|---|");
        foreach (var check in Checks)
        {
            var t = tallies[check.Field];
            report.AppendLine(CultureInfo.InvariantCulture, $"| {check.Field} | `{check.MarketWatchKey}` | `{check.Reference}` | {t.Match}/{t.Total} | {t.Example ?? ""} |");
        }

        var path = Path.Combine(options.Value.DataRoot, "reports", $"mw-keys-{startedIran:yyyyMMdd-HHmm}.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, report.ToString(), cancellationToken);
        Console.WriteLine();
        Console.WriteLine($"Report: {Path.GetFullPath(path)}");
        return tallies.Values.All(t => t.Match == t.Total) ? 0 : 5;
    }

    private static object? Normalize(object? value) => value switch
    {
        decimal d => d,
        long l => (decimal)l,
        int i => (decimal)i,
        string s => s.Trim(),
        _ => value,
    };

    private static bool IsDuringSession(DateTimeOffset iranNow) =>
        iranNow.DayOfWeek is not (DayOfWeek.Thursday or DayOfWeek.Friday)
        && iranNow.TimeOfDay >= TimeSpan.FromHours(8.75)
        && iranNow.TimeOfDay <= TimeSpan.FromHours(13.25);
}
