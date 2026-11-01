using Boors.Market.Domain;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Boors.Market.Rules;

public enum CloseMethod
{
    /// <summary>Pre-2025-12-22 main-market rule: the close only moves toward the VWAP in proportion to volume / base volume.</summary>
    BaseVolumeWeighted,

    /// <summary>Base volume = 1 share, so the close is the day's volume-weighted average price.</summary>
    Vwap,

    /// <summary>IFB base market (from Mehr 1404): base volume recalculated dynamically.</summary>
    DynamicBaseVolume,
}

public enum RuleConfidence
{
    Low,
    Medium,
    High,
}

/// <summary>A resolved rule value plus where it came from, so research can tell sourced rules from placeholders.</summary>
public sealed record RuleValue<T>(T Value, DateOnly EffectiveFrom, RuleConfidence Confidence, string? Note);

public sealed record TradingSession(TimeOnly ContinuousStart, TimeOnly ContinuousEnd, TimeOnly? TalStart, TimeOnly? TalEnd)
{
    public TimeOnly Close => TalEnd ?? ContinuousEnd;
}

/// <summary>Per-side trading costs as fractions of trade value.</summary>
public sealed record TradingCosts(decimal Exchange, decimal Csd, decimal Technology, decimal BrokerageMax, decimal SaleTax)
{
    public decimal BuyCost(decimal brokerage) => Exchange + Csd + Technology + Math.Clamp(brokerage, 0, BrokerageMax);

    public decimal SellCost(decimal brokerage) => BuyCost(brokerage) + SaleTax;

    public decimal RoundTrip(decimal brokerage) => BuyCost(brokerage) + SellCost(brokerage);

    /// <summary>Round trip at the full (list) brokerage rate.</summary>
    public decimal ListRoundTrip => RoundTrip(BrokerageMax);
}

public sealed record ClosureRange(DateOnly From, DateOnly To, string Reason)
{
    public bool Contains(DateOnly date) => date >= From && date <= To;
}

/// <summary>
/// Dated market rules loaded from <c>rules/rules.yaml</c>. Blocks apply in date order; for a given instrument class,
/// the latest block (on or before the date) that names the class or sets <c>default</c> wins, and within one block
/// an explicit class entry beats <c>default</c>.
/// </summary>
public sealed class MarketRules
{
    private const string DefaultKey = "default";
    private readonly IReadOnlyList<RuleBlock> blocks;

    private MarketRules(IReadOnlyList<RuleBlock> blocks, int settlementDays, IReadOnlyList<ClosureRange> knownClosures)
    {
        this.blocks = blocks;
        SettlementDays = settlementDays;
        KnownClosures = knownClosures;
    }

    public int SettlementDays { get; }

    public IReadOnlyList<ClosureRange> KnownClosures { get; }

    public static MarketRules LoadDefault()
    {
        using var stream = typeof(MarketRules).Assembly.GetManifestResourceStream("Boors.Market.rules.yaml")
            ?? throw new InvalidOperationException("The embedded rules.yaml resource is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static MarketRules LoadFile(string path) => Parse(File.ReadAllText(path));

    public static MarketRules Parse(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        var document = deserializer.Deserialize<RulesDocument>(yaml)
            ?? throw new FormatException("rules.yaml is empty.");

        var blocks = document.Blocks
            .Select(b => b.ToBlock())
            .OrderBy(b => b.From)
            .ToList();

        if (blocks.Count == 0)
            throw new FormatException("rules.yaml defines no blocks.");

        var closures = document.KnownClosures
            .Select(c => new ClosureRange(ParseDate(c.From), ParseDate(c.To), c.Reason ?? string.Empty))
            .ToList();

        return new MarketRules(blocks, document.SettlementDays, closures);
    }

    public RuleValue<decimal> GetPriceLimit(InstrumentClass instrumentClass, DateOnly date) =>
        Resolve(b => b.PriceLimit, instrumentClass, date)
        ?? throw new InvalidOperationException($"No price limit defined for {instrumentClass} on {date:yyyy-MM-dd}.");

    public RuleValue<CloseMethod> GetCloseMethod(InstrumentClass instrumentClass, DateOnly date) =>
        Resolve(b => b.CloseMethod, instrumentClass, date)
        ?? throw new InvalidOperationException($"No close method defined for {instrumentClass} on {date:yyyy-MM-dd}.");

    public RuleValue<TradingSession> GetSession(InstrumentClass instrumentClass, DateOnly date) =>
        Resolve(b => b.Session, instrumentClass, date)
        ?? throw new InvalidOperationException($"No session defined for {instrumentClass} on {date:yyyy-MM-dd}.");

    public RuleValue<TradingCosts> GetCosts(DateOnly date)
    {
        RuleValue<TradingCosts>? result = null;
        foreach (var block in blocks)
        {
            if (block.From > date)
                break;
            if (block.Costs is { } costs)
                result = new(costs, block.From, block.Confidence, block.Note);
        }

        return result ?? throw new InvalidOperationException($"No costs defined on {date:yyyy-MM-dd}.");
    }

    public bool IsKnownClosure(DateOnly date) => KnownClosures.Any(c => c.Contains(date));

    private RuleValue<T>? Resolve<T>(Func<RuleBlock, IReadOnlyDictionary<string, T>?> selector, InstrumentClass instrumentClass, DateOnly date)
    {
        var key = instrumentClass.ToRuleKey();
        RuleValue<T>? result = null;

        foreach (var block in blocks)
        {
            if (block.From > date)
                break;

            var map = selector(block);
            if (map is null)
                continue;

            if (map.TryGetValue(key, out var specific))
                result = new(specific, block.From, block.Confidence, block.Note);
            else if (map.TryGetValue(DefaultKey, out var fallback))
                result = new(fallback, block.From, block.Confidence, block.Note);
        }

        return result;
    }

    private static DateOnly ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new FormatException($"Invalid date '{value}' in rules.yaml (expected yyyy-MM-dd).");

    private static TimeOnly ParseTime(string value) =>
        TimeOnly.ParseExact(value.Trim(), "HH:mm", CultureInfo.InvariantCulture);

    private static (TimeOnly Start, TimeOnly End) ParseRange(string value)
    {
        var parts = value.Split('-', 2);
        if (parts.Length != 2)
            throw new FormatException($"Invalid time range '{value}' in rules.yaml (expected HH:mm-HH:mm).");
        return (ParseTime(parts[0]), ParseTime(parts[1]));
    }

    private sealed record RuleBlock(
        DateOnly From,
        RuleConfidence Confidence,
        string? Note,
        IReadOnlyDictionary<string, decimal>? PriceLimit,
        IReadOnlyDictionary<string, CloseMethod>? CloseMethod,
        IReadOnlyDictionary<string, TradingSession>? Session,
        TradingCosts? Costs);

    // YAML document model (snake_case in the file).
    private sealed class RulesDocument
    {
        public int Version { get; set; }
        public List<BlockDocument> Blocks { get; set; } = [];
        public int SettlementDays { get; set; } = 2;
        public ReopenDocument? Reopen { get; set; }
        public List<ClosureDocument> KnownClosures { get; set; } = [];
    }

    private sealed class BlockDocument
    {
        public string? From { get; set; }
        public string? Confidence { get; set; }
        public string? Note { get; set; }
        public Dictionary<string, decimal>? PriceLimit { get; set; }
        public Dictionary<string, string>? CloseMethod { get; set; }
        public Dictionary<string, SessionDocument>? Session { get; set; }
        public CostsDocument? Costs { get; set; }

        public RuleBlock ToBlock() => new(
            ParseDate(From),
            Confidence?.ToLowerInvariant() switch
            {
                "high" => RuleConfidence.High,
                "medium" => RuleConfidence.Medium,
                "low" or null => RuleConfidence.Low,
                var other => throw new FormatException($"Unknown confidence '{other}' in rules.yaml."),
            },
            Note?.Trim(),
            PriceLimit,
            CloseMethod?.ToDictionary(kv => kv.Key, kv => ParseCloseMethod(kv.Value)),
            Session?.ToDictionary(kv => kv.Key, kv => kv.Value.ToSession()),
            Costs?.ToCosts());

        private static CloseMethod ParseCloseMethod(string value) => value switch
        {
            "base_volume_weighted" => Rules.CloseMethod.BaseVolumeWeighted,
            "vwap" => Rules.CloseMethod.Vwap,
            "dynamic_base_volume" => Rules.CloseMethod.DynamicBaseVolume,
            _ => throw new FormatException($"Unknown close_method '{value}' in rules.yaml."),
        };
    }

    private sealed class SessionDocument
    {
        public string Continuous { get; set; } = string.Empty;
        public string? Tal { get; set; }

        public TradingSession ToSession()
        {
            var continuous = ParseRange(Continuous);
            if (Tal is null)
                return new(continuous.Start, continuous.End, null, null);

            var tal = ParseRange(Tal);
            return new(continuous.Start, continuous.End, tal.Start, tal.End);
        }
    }

    private sealed class CostsDocument
    {
        public decimal Exchange { get; set; }
        public decimal Csd { get; set; }
        public decimal Technology { get; set; }
        public decimal BrokerageMax { get; set; }
        public decimal SaleTax { get; set; }

        public TradingCosts ToCosts() => new(Exchange, Csd, Technology, BrokerageMax, SaleTax);
    }

    private sealed class ReopenDocument
    {
        public string? Method { get; set; }
        public int DisclosureHaltMaxBusinessDays { get; set; }
    }

    private sealed class ClosureDocument
    {
        public string? From { get; set; }
        public string? To { get; set; }
        public string? Reason { get; set; }
    }
}
