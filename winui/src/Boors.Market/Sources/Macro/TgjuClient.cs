using System.Text.RegularExpressions;
using Boors.Market.RawCache;
using Microsoft.Extensions.Logging;

namespace Boors.Market.Sources.Macro;

/// <summary>A daily OHLC bar of a macro series (free-market dollar, gold coin, gold gram, ounce, ...). Rial unless noted.</summary>
public sealed record MacroBar(string Series, DateOnly Date, decimal Open, decimal Low, decimal High, decimal Close, string Source);

/// <summary>tgju.org item codes used by Boors (verified live 2026-10-17).</summary>
public static class TgjuSeries
{
    public const string UsdFreeMarket = "price_dollar_rl";
    public const string EmamiCoin = "sekee";
    public const string Gold18k = "geram18";
    public const string GoldOunceUsd = "ons";
    public const string Eur = "price_eur";

    public static readonly IReadOnlyList<string> Default = [UsdFreeMarket, EmamiCoin, Gold18k, GoldOunceUsd, Eur];
}

/// <summary>tgju has no official API; these are the JSON endpoints behind its pages. Responses are cached like every source.</summary>
public sealed partial class TgjuClient(HttpClient http, IRawCache cache, TimeProvider timeProvider, ILogger<TgjuClient> logger)
    : RawSourceClient(http, cache, timeProvider, logger)
{
    public const string Source = "tgju";

    public static RawRequest HistoryRequest(string series) =>
        new(Source, "summary-table-data", $"https://api.tgju.org/v1/market/indicator/summary-table-data/{Uri.EscapeDataString(series)}", $"series={series}");

    public async Task<IReadOnlyList<MacroBar>> GetHistoryAsync(string series, CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default) =>
        ParseHistory(await GetBodyAsync(HistoryRequest(series), policy, cancellationToken), series);

    /// <summary>
    /// Parses summary-table-data. Each row is [open, low, high, close, change (HTML), change % (HTML), "yyyy/MM/dd", jalali date];
    /// the column order was checked on 2026-10-15: change 40,000 = close 2,679,000 − previous close 2,639,000.
    /// </summary>
    public static IReadOnlyList<MacroBar> ParseHistory(ReadOnlySpan<byte> json, string series)
    {
        using var document = JsonDocument.Parse(json.ToArray());
        var bars = new List<MacroBar>();
        foreach (var row in document.RootElement.GetProperty("data").EnumerateArray())
        {
            if (row.GetArrayLength() < 7)
                continue;

            var date = DateOnly.TryParseExact(row[6].GetString(), "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateOnly?)null;
            if (date is null || ParseNumber(row[0]) is not { } open || ParseNumber(row[1]) is not { } low || ParseNumber(row[2]) is not { } high || ParseNumber(row[3]) is not { } close)
                continue;

            bars.Add(new MacroBar(series, date.Value, open, low, high, close, Source));
        }

        return bars.OrderBy(b => b.Date).ToList();
    }

    private static decimal? ParseNumber(JsonElement element)
    {
        var text = element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = HtmlTagRegex().Replace(text, string.Empty).Replace(",", string.Empty, StringComparison.Ordinal).Trim();
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();
}
