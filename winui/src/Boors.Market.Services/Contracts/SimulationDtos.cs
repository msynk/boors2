namespace Boors.Market.Services.Contracts;

// Simulations (Boors.Market.Simulation.MarketSimulator). The client owns the definitions (SimulationSpecDto, which the
// desktop saves as JSON) and the engine replays them over the store from their start to the latest session, so a result
// is a pure function of the definition and the data. Fractions are 0–1, values rials, lengths sessions, dates
// yyyy-MM-dd. Enumerations travel as their names: mode Automatic | Manual, side Buy | Sell, sizing Equal |
// InverseVolatility | Conviction, fill NextOpen | NextClose, objective Return | Sharpe | Calmar, factors as in
// Boors.Market.Simulation.FactorKind. Trade reasons and journal kinds are the keys of SimulationEvents.

/// <summary>
/// The adjustable parameters (see Boors.Market.Simulation.SimulationSettings for their meaning). The defaults are the
/// engine's, so a definition saved before a parameter existed reads back with that parameter's default (settable
/// properties: the JSON source generator fills init-only ones through an initializer and would reset them to 0).
/// </summary>
public sealed record SimulationSettingsDto
{
    public decimal MinValue { get; set; } = 50_000_000_000m;
    public int MinHistorySessions { get; set; } = 120;
    public int Positions { get; set; } = 8;
    public double MaxWeight { get; set; } = 0.20;
    public int PerSector { get; set; } = 2;
    public string Sizing { get; set; } = "InverseVolatility";
    public double CashReserve { get; set; } = 0.02;
    public int RebalanceSessions { get; set; } = 5;
    public double HoldPercentile { get; set; } = 0.20;
    public int MinHoldSessions { get; set; } = 5;
    public double RebalanceBand { get; set; } = 0.25;

    public Dictionary<string, double> Weights { get; set; } = new()
    {
        ["Momentum"] = 2,
        ["Trend"] = 1,
        ["High52Week"] = 1,
        ["LowVolatility"] = 1,
        ["EarningsYield"] = 1,
        ["EpsRevision"] = 1,
        ["IndividualFlow"] = 1,
        ["RiskAdjusted"] = 1,
    };

    public bool Adaptive { get; set; }
    public int AdaptiveLookbackSessions { get; set; } = 480;
    public double AdaptiveShrink { get; set; } = 0.5;
    public double StopLoss { get; set; } = 0.12;
    public double TrailingStop { get; set; } = 0.15;
    public double TakeProfit { get; set; }
    public int MaxHoldSessions { get; set; }
    public int CooldownSessions { get; set; } = 10;
    public bool RegimeFilter { get; set; } = true;
    public int RegimeSessions { get; set; } = 100;
    public double DefensiveExposure { get; set; } = 0.5;
    public double TargetVolatility { get; set; }
    public string Fill { get; set; } = "NextClose";
    public double SlippageBps { get; set; } = 10;
    public double ParticipationCap { get; set; } = 0.10;
    public decimal? Brokerage { get; set; }
    public bool InstantSaleProceeds { get; set; }
    public double CashYield { get; set; } = 0.30;

    /// <summary>Instruments the strategy does not buy (a holding already in the portfolio is still managed).</summary>
    public List<long> Excluded { get; set; } = [];
}

/// <summary>Parameters in force from <paramref name="From"/> on (the first revision applies from the start).</summary>
public sealed record SettingsRevisionDto(DateOnly From, SimulationSettingsDto Settings);

/// <summary>
/// An order the user placed on the latest session <paramref name="PlacedOn"/>; it can fill on the sessions after it, for
/// <paramref name="GoodForSessions"/> sessions. A buy gives a value (rials) or shares; a sell gives shares, or neither
/// to sell everything.
/// </summary>
public sealed record ManualOrderDto(
    string Id,
    DateOnly PlacedOn,
    long InsCode,
    string Symbol,
    string Side,
    decimal? Value,
    decimal? Shares,
    decimal? LimitPrice,
    int GoodForSessions,
    DateOnly? CancelledOn,
    DateTimeOffset? PlacedAt);

/// <summary>A simulation's definition: everything its results depend on, plus its name and notes.</summary>
public sealed record SimulationSpecDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Mode { get; set; } = "Automatic";

    /// <summary>Starting cash, rials.</summary>
    public decimal Capital { get; set; }

    public DateOnly Start { get; set; }

    /// <summary>Tracking stopped after this date; null = it follows every new session.</summary>
    public DateOnly? End { get; set; }

    public List<SettingsRevisionDto> Revisions { get; set; } = [];
    public List<ManualOrderDto> Orders { get; set; } = [];

    /// <summary>The preset the parameters started from (balanced, adaptive, trend, value, flow, swing), for display.</summary>
    public string? Preset { get; set; }

    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>POST api/v1/Simulation/Run — without <paramref name="Detail"/> the trades and the journal are left out.</summary>
public sealed record SimulationRunRequestDto(SimulationSpecDto Spec, bool Detail);

/// <summary>POST api/v1/Simulation/Tune — <paramref name="Objective"/>: Return, Sharpe or Calmar.</summary>
public sealed record SimulationTuneRequestDto(SimulationSpecDto Spec, int Years, string Objective);

public sealed record SimulationPresetDto(string Key, SimulationSettingsDto Settings);

/// <summary>The portfolio at one session's close; <paramref name="Cash"/> includes sale proceeds still settling.</summary>
public record SimulationPointDto(DateOnly Date, decimal Equity, decimal Cash, decimal Invested, double? Tedpix, double? EqualWeight, double? Usd);

public record SimulationHoldingDto(
    long InsCode,
    string Symbol,
    string? Name,
    string? SectorName,
    decimal Shares,
    decimal AverageCost,
    decimal LastPrice,
    decimal Value,
    double Weight,
    decimal UnrealizedPnl,
    double UnrealizedReturn,
    double? DayChange,
    DateOnly OpenedOn,
    int SessionsHeld,
    decimal? StopPrice,
    string? DayStatus,
    double? Score,
    int? Rank);

public record SimulationTradeDto(
    DateOnly Date,
    long InsCode,
    string Symbol,
    string Side,
    decimal Shares,
    decimal Price,
    decimal Value,
    decimal Costs,
    decimal? RealizedPnl,
    double? RealizedReturn,
    int? SessionsHeld,
    string Reason,
    string? OrderId);

/// <summary>A journal line; the meaning of the values depends on <paramref name="Kind"/>.</summary>
public record SimulationEventDto(DateOnly Date, string Kind, long? InsCode, string? Symbol, double? Value, double? Value2);

/// <summary>An order waiting for the next session, or a suggestion of the strategy (manual mode).</summary>
public record SimulationOrderPlanDto(long InsCode, string Symbol, string? Name, string Side, decimal Shares, decimal Value, decimal Price, string Reason, double? Score, int? Rank, string? OrderId);

/// <summary><paramref name="State"/>: pending, filled, partial, expired, cancelled or rejected.</summary>
public record SimulationOrderStatusDto(string OrderId, string State, decimal FilledShares, decimal? AveragePrice, DateOnly? LastFillOn, string? Reason);

/// <summary><paramref name="Family"/>: technical, fundamental, flow or quality.</summary>
public record SimulationFactorDto(string Factor, string Family, double Chosen, double Used, double? MeanIc, int Months, double Coverage);

public record SimulationStatsDto(
    int Sessions,
    decimal Equity,
    decimal Cash,
    decimal Invested,
    double TotalReturn,
    double? AnnualizedReturn,
    double? DayReturn,
    double? WeekReturn,
    double? MonthReturn,
    double? Volatility,
    double? Sharpe,
    double? Sortino,
    double MaxDrawdown,
    double CurrentDrawdown,
    double? Calmar,
    double? Beta,
    double? Alpha,
    double? Correlation,
    double? TedpixReturn,
    double? ExcessReturn,
    double? EqualWeightReturn,
    double? UsdReturn,
    int Trades,
    int ClosedTrades,
    double? WinRate,
    double? ProfitFactor,
    double? AverageWin,
    double? AverageLoss,
    double? AverageHoldSessions,
    double? Turnover,
    decimal CostsPaid,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    double Exposure,
    double? BestDay,
    double? WorstDay);

public record SimulationResultDto(
    string Id,
    DateOnly? Start,
    DateOnly? AsOf,
    DateOnly? Latest,
    SimulationStatsDto Stats,
    List<SimulationPointDto> Equity,
    List<SimulationHoldingDto> Holdings,
    List<SimulationTradeDto> Trades,
    List<SimulationEventDto> Journal,
    List<SimulationOrderPlanDto> Planned,
    List<SimulationOrderPlanDto> Suggestions,
    List<SimulationOrderStatusDto> Orders,
    List<SimulationFactorDto> Factors,
    bool RiskOn,
    double TargetExposure,
    int? NextRebalanceIn,
    int Universe,
    DateOnly? FundamentalsSince);

public record SimulationTuneRowDto(
    SimulationSettingsDto Settings,
    double Objective,
    double InReturn,
    double? InAnnualized,
    double? InSharpe,
    double InMaxDrawdown,
    double OutReturn,
    double? OutAnnualized,
    double? OutSharpe,
    double OutMaxDrawdown,
    int Trades,
    double? Turnover);

/// <summary>Rows are ranked by the objective on the sessions before <paramref name="Split"/>; the out-of-sample figures cover the rest.</summary>
public record SimulationTuningDto(DateOnly? From, DateOnly? Split, DateOnly? To, string Objective, double? TedpixIn, double? TedpixOut, List<SimulationTuneRowDto> Rows);
