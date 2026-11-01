namespace Boors.Market.Analytics;

/// <summary>The latest value of an index on a session, with its change from the previous index row.</summary>
public sealed record IndexMove(long InsCode, string Name, decimal Value, double? ChangePercent);

/// <summary>A macro quote (free-market dollar, Emami coin...) on or before a session.</summary>
public sealed record MacroQuote(string Series, DateOnly Date, decimal Value, double? ChangePercent);

/// <summary>Headline numbers of one session (the home screen's KPI row).</summary>
public sealed record MarketOverview
{
    public required DateOnly Session { get; init; }
    public IReadOnlyList<IndexMove> Indices { get; init; } = [];

    /// <summary>Rial value traded in equities (primary listings of TSE, IFB and base-market shares).</summary>
    public decimal EquityValue { get; init; }

    /// <summary>Rial value traded in ETFs and other exchange-traded funds.</summary>
    public decimal FundValue { get; init; }

    public long EquityTrades { get; init; }
    public int Advancers { get; init; }
    public int Decliners { get; init; }
    public int Unchanged { get; init; }
    public int NotTraded { get; init; }
    public int Halted { get; init; }

    /// <summary>Equities whose best bid sat at the upper band with no offers at the close (or, without a close snapshot, whose whole day traded at the upper band).</summary>
    public int BuyQueues { get; init; }

    /// <summary>Rial value waiting in buy queues at the close; null when only history (no close snapshot) is available.</summary>
    public decimal? BuyQueueValue { get; init; }

    public int SellQueues { get; init; }
    public decimal? SellQueueValue { get; init; }

    /// <summary>Individual (حقیقی) buy value minus sell value across equities. Includes same-day estimates when official values are not in yet.</summary>
    public decimal IndividualNetFlow { get; init; }

    public decimal? IndividualPerCapitaBuy { get; init; }
    public decimal? IndividualPerCapitaSell { get; init; }
    public bool FlowValuesEstimated { get; init; }
    public MacroQuote? Usd { get; init; }
    public MacroQuote? Coin { get; init; }

    /// <summary>TEDPIX change measured in free-market dollars.</summary>
    public double? TedpixUsdChangePercent { get; init; }
}

public sealed record BreadthDay(DateOnly Date, int Advancers, int Decliners, int Unchanged, int NotTraded, int Halted, int LockedUp, int LockedDown, decimal EquityValue);

public sealed record SectorPerformance
{
    public required string SectorCode { get; init; }
    public string? SectorName { get; init; }
    public int Instruments { get; init; }
    public int Traded { get; init; }
    public decimal Value { get; init; }

    /// <summary>Market-cap-weighted return of traded members (weight = yesterday's price × shares).</summary>
    public double? WeightedReturn { get; init; }

    public double? MedianReturn { get; init; }
    public decimal IndividualNetFlow { get; init; }
    public int Advancers { get; init; }
    public int Decliners { get; init; }
}

public enum MoverKind
{
    Gainers,
    Losers,
    Value,
    Inflow,
    Outflow,
}

public sealed record Mover(long InsCode, string Symbol, string? Name, string InstrumentClass, string? DayStatus, decimal Close, double? ChangePercent, decimal Value, decimal? IndividualNetFlow);

/// <summary>Everything the symbol page's header and returns card need, computed point-in-time as of <see cref="AsOf"/>.</summary>
public sealed record InstrumentMetrics
{
    public required long InsCode { get; init; }
    public required string Symbol { get; init; }
    public string? Name { get; init; }
    public required string InstrumentClass { get; init; }
    public string? SectorCode { get; init; }
    public string? SectorName { get; init; }
    public DateOnly? AsOf { get; init; }
    public string? DayStatus { get; init; }
    public decimal? Close { get; init; }
    public double? ChangePercent { get; init; }
    public decimal? MarketCap { get; init; }

    /// <summary>Adjusted 52-week closing high/low and the distance of the last close from the high (0 = at the high).</summary>
    public decimal? High52Week { get; init; }
    public decimal? Low52Week { get; init; }
    public double? FromHigh52WeekPercent { get; init; }

    /// <summary>Total returns (rial) over calendar windows ending at <see cref="AsOf"/>: 1w, 1m, 3m, 6m, 1y.</summary>
    public IReadOnlyDictionary<string, double?> Returns { get; init; } = new Dictionary<string, double?>();

    /// <summary>One-year return in free-market dollars.</summary>
    public double? UsdReturn1Year { get; init; }

    public decimal? AverageValue20 { get; init; }
    public decimal? IndividualNetFlow20 { get; init; }
    public decimal? PerCapitaBuy { get; init; }
    public decimal? PerCapitaSell { get; init; }
    public decimal? BuyerPower { get; init; }

    /// <summary>Where the latest buyer power sits within the instrument's own last ~250 sessions (0–1).</summary>
    public double? BuyerPowerPercentile { get; init; }
}

public enum ScreenKind
{
    /// <summary>Return from 6 months to 1 month ago (skips the last month, the usual short-term reversal window).</summary>
    Momentum6To1,

    /// <summary>Last adjusted close relative to the 52-week closing high (1 = at the high).</summary>
    High52WeekProximity,

    /// <summary>Individual net flow over 20 sessions divided by value traded over the same sessions.</summary>
    IndividualFlowIntensity20,

    /// <summary>Average buyer power over the last 5 sessions.</summary>
    BuyerPower5,
}

public sealed record ScreenRow(long InsCode, string Symbol, string? Name, string InstrumentClass, string? SectorCode, double Score, int Observations);
