using System.Text.RegularExpressions;
using Jint;
using Jint.Native;
using Jint.Runtime;

namespace Boors.Market.Filters;

/// <summary>
/// Everything a TSETMC market-watch filter can reference for one instrument. Names follow TSETMC's filter help
/// (old.tsetmc.com, ParTree=151715, LnkIdn 3196–3199).
/// </summary>
public sealed record FilterRow
{
    public required long InsCode { get; init; }
    public required string Symbol { get; init; }
    public string? Name { get; init; }
    public long TradeCount { get; init; }
    public long Volume { get; init; }
    public decimal Value { get; init; }
    public decimal Yesterday { get; init; }
    public decimal First { get; init; }
    public decimal Low { get; init; }
    public decimal High { get; init; }
    public decimal Last { get; init; }
    public decimal Close { get; init; }
    public decimal? Eps { get; init; }
    public decimal? PriceEarnings { get; init; }
    public decimal AllowedLow { get; init; }
    public decimal AllowedHigh { get; init; }
    public decimal TotalShares { get; init; }
    public long BaseVolume { get; init; }
    public int SectorCode { get; init; }

    /// <summary>Order-book levels 1..5 (only level 1 is known from the end-of-day snapshot).</summary>
    public IReadOnlyList<Domain.BestLimitLevel> BestLimits { get; init; } = [];

    public Domain.ClientTypeDay? ClientType { get; init; }

    /// <summary>Previous trading days, newest first: <c>[ih][0]</c> is the day before this row.</summary>
    public IReadOnlyList<Domain.DailyBar> History { get; init; } = [];

    /// <summary>TSETMC statistics by data type: <c>[isN]</c> reads data type N.</summary>
    public IReadOnlyDictionary<int, double> Statistics { get; init; } = new Dictionary<int, double>();
}

public sealed record FilterResult(IReadOnlyList<long> Matches, int Evaluated, IReadOnlyList<string> Errors);

public sealed class FilterSyntaxException(string message) : Exception(message);

/// <summary>
/// A TSETMC market-watch filter, executed by the Jint JavaScript interpreter so expressions — including the
/// <c>true==function(){...}()</c> code mode — behave as on TSETMC. The engine runs sandboxed: no .NET access,
/// a statement cap, a recursion cap, a memory cap and a timeout per instrument.
/// <para>TSETMC's <c>[ih]</c> and <c>[isN]</c> tokens are rewritten to identifiers before evaluation; everything else
/// (<c>(pl)</c>, <c>(ct).Buy_I_Volume</c>, <c>pd1</c>…) is already valid JavaScript.</para>
/// </summary>
public sealed partial class TsetmcFilter
{
    private static readonly string[] Scalars =
    [
        "l18", "l30", "tno", "tvol", "tval", "py", "pf", "pmin", "pmax", "pl", "plc", "plp", "pc", "pcc", "pcp",
        "eps", "pe", "tmin", "tmax", "z", "mv", "bvol", "cs", "buyop", "predtran",
    ];

    private static readonly string[] BookSides = ["pd", "zd", "qd", "po", "zo", "qo"];

    private readonly Engine engine;
    private readonly JsValue function;
    private readonly IReadOnlyList<int> statistics;

    private TsetmcFilter(string source, Engine engine, JsValue function, bool usesHistory, IReadOnlyList<int> statistics)
    {
        Source = source;
        this.engine = engine;
        this.function = function;
        UsesHistory = usesHistory;
        this.statistics = statistics;
    }

    public string Source { get; }

    /// <summary>True when the expression reads <c>[ih]</c>, so callers only load ~60 days of history when needed.</summary>
    public bool UsesHistory { get; }

    /// <summary>The <c>[isN]</c> data types the expression reads.</summary>
    public IReadOnlyList<int> StatisticTypes => statistics;

    public static TsetmcFilter Compile(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new FilterSyntaxException("The filter is empty.");

        var body = IhTokenRegex().Replace(expression.Trim().TrimEnd(';').Trim(), "ih");
        var statistics = IsTokenRegex().Matches(body).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().Order().ToList();
        body = IsTokenRegex().Replace(body, "is$1");

        var parameters = Scalars
            .Concat(BookSides.SelectMany(side => Enumerable.Range(1, 5).Select(level => $"{side}{level}")))
            .Concat(["ct", "ih"])
            .Concat(statistics.Select(s => $"is{s}"));

        var engine = new Engine(options => options
            .Strict(false)
            .TimeoutInterval(TimeSpan.FromMilliseconds(250))
            .MaxStatements(20_000)
            .LimitRecursion(64)
            .LimitMemory(16_000_000));

        try
        {
            var function = engine.Evaluate($"(function({string.Join(", ", parameters)}) {{ return ({body}\n); }})");
            return new TsetmcFilter(expression, engine, function, IhIdentifierRegex().IsMatch(body), statistics);
        }
        catch (JavaScriptException e)
        {
            throw new FilterSyntaxException(e.Message);
        }
    }

    /// <summary>
    /// Evaluates the filter for every row; a row matches when the result is JavaScript-truthy. Errors in single rows
    /// (e.g. reading <c>[ih][59]</c> for a new listing) skip that row and are reported, up to 20 distinct messages.
    /// </summary>
    public FilterResult Apply(IEnumerable<FilterRow> rows, CancellationToken cancellationToken = default)
    {
        var matches = new List<long>();
        var errors = new List<string>();
        var evaluated = 0;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            evaluated++;
            try
            {
                if (TypeConverter.ToBoolean(engine.Invoke(function, Arguments(row))))
                    matches.Add(row.InsCode);
            }
            catch (Exception e) when (e is JavaScriptException or StatementsCountOverflowException or TimeoutException or MemoryLimitExceededException or RecursionDepthOverflowException)
            {
                var message = $"{row.Symbol}: {e.Message}";
                if (errors.Count < 20 && !errors.Any(m => m.EndsWith(e.Message, StringComparison.Ordinal)))
                    errors.Add(message);
            }
        }

        return new FilterResult(matches, evaluated, errors);
    }

    private JsValue[] Arguments(FilterRow row)
    {
        var lastChange = row.Last - row.Yesterday;
        var closeChange = row.Close - row.Yesterday;
        var arguments = new List<JsValue>(Scalars.Length + 32 + statistics.Count)
        {
            new JsString(row.Symbol),
            new JsString(row.Name ?? string.Empty),
            Number(row.TradeCount),
            Number(row.Volume),
            Number(row.Value),
            Number(row.Yesterday),
            Number(row.First),
            Number(row.Low),
            Number(row.High),
            Number(row.Last),
            Number(lastChange),
            Number(row.Yesterday == 0 ? 0 : Math.Round(lastChange / row.Yesterday * 100, 2)),
            Number(row.Close),
            Number(closeChange),
            Number(row.Yesterday == 0 ? 0 : Math.Round(closeChange / row.Yesterday * 100, 2)),
            Number(row.Eps ?? 0),
            Number(row.PriceEarnings ?? 0),
            Number(row.AllowedLow),
            Number(row.AllowedHigh),
            Number(row.TotalShares),
            Number(row.Close * row.TotalShares),
            Number(row.BaseVolume),
            Number(row.SectorCode),
            Number(0), // buyop: not available
            Number(0), // predtran: not available
        };

        foreach (var side in BookSides)
        {
            for (var level = 1; level <= 5; level++)
            {
                var l = row.BestLimits.FirstOrDefault(b => b.Level == level);
                arguments.Add(Number(l is null ? 0 : side switch
                {
                    "pd" => l.BidPrice,
                    "zd" => l.BidOrders,
                    "qd" => l.BidVolume,
                    "po" => l.AskPrice,
                    "zo" => l.AskOrders,
                    _ => l.AskVolume,
                }));
            }
        }

        arguments.Add(ClientType(row.ClientType));
        arguments.Add(UsesHistory ? History(row.History) : new JsArray(engine));
        foreach (var type in statistics)
            arguments.Add(JsNumber.Create(row.Statistics.TryGetValue(type, out var value) ? value : 0));

        return [.. arguments];
    }

    private JsObject ClientType(Domain.ClientTypeDay? flows)
    {
        var ct = new JsObject(engine);
        ct.Set("Buy_CountI", Number(flows?.BuyIndividualCount ?? 0));
        ct.Set("Buy_CountN", Number(flows?.BuyInstitutionalCount ?? 0));
        ct.Set("Buy_I_Volume", Number(flows?.BuyIndividualVolume ?? 0));
        ct.Set("Buy_N_Volume", Number(flows?.BuyInstitutionalVolume ?? 0));
        ct.Set("Sell_CountI", Number(flows?.SellIndividualCount ?? 0));
        ct.Set("Sell_CountN", Number(flows?.SellInstitutionalCount ?? 0));
        ct.Set("Sell_I_Volume", Number(flows?.SellIndividualVolume ?? 0));
        ct.Set("Sell_N_Volume", Number(flows?.SellInstitutionalVolume ?? 0));
        return ct;
    }

    private JsArray History(IReadOnlyList<Domain.DailyBar> bars)
    {
        var items = new JsValue[bars.Count];
        for (var i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            var day = new JsObject(engine);
            day.Set("PClosing", Number(bar.Close));
            day.Set("PDrCotVal", Number(bar.Last));
            day.Set("ZTotTran", Number(bar.TradeCount));
            day.Set("QTotTran5J", Number(bar.Volume));
            day.Set("QTotCap", Number(bar.Value));
            day.Set("PriceMin", Number(bar.Low));
            day.Set("PriceMax", Number(bar.High));
            day.Set("PriceYesterday", Number(bar.Yesterday));
            day.Set("PriceFirst", Number(bar.Open));
            items[i] = day;
        }

        return new JsArray(engine, items);
    }

    private static JsNumber Number(decimal value) => JsNumber.Create((double)value);

    private static JsNumber Number(long value) => JsNumber.Create(value);

    [GeneratedRegex(@"\[\s*ih\s*\]", RegexOptions.CultureInvariant)]
    private static partial Regex IhTokenRegex();

    [GeneratedRegex(@"\bih\b", RegexOptions.CultureInvariant)]
    private static partial Regex IhIdentifierRegex();

    [GeneratedRegex(@"\[\s*is(\d{1,3})\s*\]", RegexOptions.CultureInvariant)]
    private static partial Regex IsTokenRegex();
}
