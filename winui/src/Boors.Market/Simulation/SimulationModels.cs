namespace Boors.Market.Simulation;

/// <summary>Who decides what to trade: the strategy (<see cref="Automatic"/>) or the user's own orders (<see cref="Manual"/>).</summary>
public enum SimulationMode
{
    Automatic,

    /// <summary>Only the user's orders trade; the strategy's choices are offered as suggestions.</summary>
    Manual,
}

public enum OrderSide
{
    Buy,
    Sell,
}

/// <summary>How the strategy splits the invested amount between the shares it holds.</summary>
public enum SizingMethod
{
    /// <summary>The same amount in every share.</summary>
    Equal,

    /// <summary>More in calmer shares: weight ∝ 1 / volatility of the last 60 sessions, so each share adds similar risk.</summary>
    InverseVolatility,

    /// <summary>Inverse volatility tilted by the composite score: the best-ranked shares get the most.</summary>
    Conviction,
}

/// <summary>The price an order decided at one session's close fills at on the next session.</summary>
public enum FillPrice
{
    /// <summary>The next session's first trade (priceFirst).</summary>
    NextOpen,

    /// <summary>The next session's closing price — the day's VWAP on the main markets since 2025-12-22.</summary>
    NextClose,
}

/// <summary>
/// The measures a strategy ranks shares by. Every value is computed at a session's close from data known then
/// (adjusted, point-in-time prices; flows and EPS of that session or earlier) and is higher for a more attractive share.
/// </summary>
public enum FactorKind
{
    // Technical
    /// <summary>12-month return without the latest month (the classic momentum signal).</summary>
    Momentum,

    /// <summary>Distance of the adjusted price above its 200-session average (log).</summary>
    Trend,

    /// <summary>Closeness to the 52-week high (0 = at the high).</summary>
    High52Week,

    /// <summary>The close against the highest close of the previous 55 sessions (a Donchian breakout).</summary>
    Breakout,

    /// <summary>The opposite of the last month's return: last month's hottest shares tend to give some back.</summary>
    ShortReversal,

    /// <summary>A pullback inside an uptrend: shares above their 200-session average, lowest 14-session RSI first.</summary>
    RsiPullback,

    /// <summary>MACD histogram (12/26/9 EMAs of the log price): momentum turning up.</summary>
    Macd,

    /// <summary>Lower volatility of the last 60 sessions first.</summary>
    LowVolatility,

    // Fundamental
    /// <summary>EPS ÷ price (the inverse of P/E), from TSETMC's end-of-day market watch.</summary>
    EarningsYield,

    /// <summary>Earnings yield against the median of the share's sector (cheap for its industry).</summary>
    SectorValue,

    /// <summary>Change of the published EPS over the last 60 sessions (forecast revisions).</summary>
    EpsRevision,

    // Money flow
    /// <summary>Individual (حقیقی) net buying over 20 sessions ÷ value traded: money coming in.</summary>
    IndividualFlow,

    /// <summary>Average individual buyer power (per-capita buy ÷ per-capita sell) over 5 sessions, log.</summary>
    BuyerPower,

    // Quality
    /// <summary>Share of up months in the last 12.</summary>
    Consistency,

    /// <summary>12-month return per unit of volatility.</summary>
    RiskAdjusted,
}

public static class FactorFamilies
{
    public const string Technical = "technical";
    public const string Fundamental = "fundamental";
    public const string Flow = "flow";
    public const string Quality = "quality";

    public static string Of(FactorKind kind) => kind switch
    {
        FactorKind.EarningsYield or FactorKind.SectorValue or FactorKind.EpsRevision => Fundamental,
        FactorKind.IndividualFlow or FactorKind.BuyerPower => Flow,
        FactorKind.Consistency or FactorKind.RiskAdjusted => Quality,
        _ => Technical,
    };
}

/// <summary>
/// The adjustable parameters of a simulation. Fractions are 0–1 (0.12 = 12%), values are rials, lengths are
/// sessions. A zero turns an optional rule off (stop loss, trailing stop, take profit, holding limit, volatility target).
/// </summary>
public sealed record SimulationSettings
{
    public static readonly IReadOnlyDictionary<FactorKind, double> BalancedWeights = new Dictionary<FactorKind, double>
    {
        [FactorKind.Momentum] = 2,
        [FactorKind.Trend] = 1,
        [FactorKind.High52Week] = 1,
        [FactorKind.LowVolatility] = 1,
        [FactorKind.EarningsYield] = 1,
        [FactorKind.EpsRevision] = 1,
        [FactorKind.IndividualFlow] = 1,
        [FactorKind.RiskAdjusted] = 1,
    };

    // ---------------------------------------------------------------- universe

    /// <summary>Minimum average value traded per session over the last 20 sessions (default 5 billion toman).</summary>
    public decimal MinValue { get; init; } = 50_000_000_000m;

    /// <summary>Priced sessions a share needs since its listing before it can be bought.</summary>
    public int MinHistorySessions { get; init; } = 120;

    // ---------------------------------------------------------------- portfolio

    /// <summary>How many shares the strategy holds.</summary>
    public int Positions { get; init; } = 8;

    /// <summary>Largest share of the portfolio in one position.</summary>
    public double MaxWeight { get; init; } = 0.20;

    /// <summary>Most positions from one sector.</summary>
    public int PerSector { get; init; } = 2;

    public SizingMethod Sizing { get; init; } = SizingMethod.InverseVolatility;

    /// <summary>Share of the portfolio kept in cash (for costs and rounding).</summary>
    public double CashReserve { get; init; } = 0.02;

    // ---------------------------------------------------------------- rebalancing

    /// <summary>Sessions between two rankings (5 = weekly, 20 = monthly). Risk exits are checked every session.</summary>
    public int RebalanceSessions { get; init; } = 5;

    /// <summary>
    /// A holding is kept while it stays in this top share of the ranked universe (0.2 = the best 20%). Free slots are
    /// filled from the top of the ranking down (passing over full sectors, shares in a buy queue and cooldowns), never
    /// from below this limit. Buying from the top but holding to this limit is a buffer that saves round trips (1.4% each).
    /// </summary>
    public double HoldPercentile { get; init; } = 0.20;

    /// <summary>Sessions a new position is held before the ranking may replace it (risk exits still apply).</summary>
    public int MinHoldSessions { get; init; } = 5;

    /// <summary>A position is resized only when it is further than this from its target (0.25 = 25% of the target).</summary>
    public double RebalanceBand { get; init; } = 0.25;

    // ---------------------------------------------------------------- signals

    /// <summary>Weight of each factor in the composite score (0 or missing = not used).</summary>
    public IReadOnlyDictionary<FactorKind, double> Weights { get; init; } = BalancedWeights;

    /// <summary>
    /// Learn the weights walk-forward: at each ranking, the factors' rank correlation with the next 20 sessions'
    /// returns over <see cref="AdaptiveLookbackSessions"/> (only months already complete) moves weight towards what has
    /// been working; <see cref="AdaptiveShrink"/> keeps part of the chosen weights.
    /// </summary>
    public bool Adaptive { get; init; }

    public int AdaptiveLookbackSessions { get; init; } = 480;

    /// <summary>Share of the chosen weights kept in adaptive mode (1 = never learn, 0 = only what was learned).</summary>
    public double AdaptiveShrink { get; init; } = 0.5;

    // ---------------------------------------------------------------- risk

    /// <summary>Sell when the price falls this far below the entry price.</summary>
    public double StopLoss { get; init; } = 0.12;

    /// <summary>Sell when the price falls this far below its highest close since entry.</summary>
    public double TrailingStop { get; init; } = 0.15;

    /// <summary>Sell when the price rises this far above the entry price.</summary>
    public double TakeProfit { get; init; }

    /// <summary>Sell after this many sessions whatever the ranking says.</summary>
    public int MaxHoldSessions { get; init; }

    /// <summary>Sessions after a risk exit before the same share may be bought again.</summary>
    public int CooldownSessions { get; init; } = 10;

    /// <summary>Invest less while TEDPIX is below its average of <see cref="RegimeSessions"/> sessions.</summary>
    public bool RegimeFilter { get; init; } = true;

    public int RegimeSessions { get; init; } = 100;

    /// <summary>Share of the portfolio invested while the regime filter says risk-off.</summary>
    public double DefensiveExposure { get; init; } = 0.5;

    /// <summary>Scale exposure down when the portfolio's own volatility (last 60 sessions) is above this yearly level.</summary>
    public double TargetVolatility { get; init; }

    // ---------------------------------------------------------------- execution and costs

    public FillPrice Fill { get; init; } = FillPrice.NextClose;

    /// <summary>Extra price paid on a buy and given up on a sell, in basis points (10 = 0.1%).</summary>
    public double SlippageBps { get; init; } = 10;

    /// <summary>Largest part of a session's value traded one order may take (0 = no limit).</summary>
    public double ParticipationCap { get; init; } = 0.10;

    /// <summary>Brokerage per side; null = the list (maximum) rate of the rules in force.</summary>
    public decimal? Brokerage { get; init; }

    /// <summary>Spend sale proceeds at once (broker buying power) instead of after settlement (T+2).</summary>
    public bool InstantSaleProceeds { get; init; }

    /// <summary>
    /// Yearly return on settled idle cash, as if it sat in a fixed-income fund (about 30% in 1405); 0 = cash earns
    /// nothing. It is also the hurdle of the Sharpe and Sortino ratios: in rials at this inflation, beating zero means little.
    /// </summary>
    public double CashYield { get; init; } = 0.30;

    /// <summary>Instruments the strategy does not buy (a holding already in the portfolio is still managed).</summary>
    public IReadOnlySet<long> Excluded { get; init; } = new HashSet<long>();
}

/// <summary>Ready-made starting points for the parameters; every value stays adjustable.</summary>
public static class SimulationPresets
{
    public const string Balanced = "balanced";
    public const string Adaptive = "adaptive";
    public const string Trend = "trend";
    public const string Value = "value";
    public const string Flow = "flow";
    public const string Swing = "swing";

    public static IReadOnlyList<(string Key, SimulationSettings Settings)> All { get; } =
    [
        (Balanced, new SimulationSettings()),
        (Adaptive, new SimulationSettings
        {
            Adaptive = true,
            Weights = Enum.GetValues<FactorKind>().ToDictionary(k => k, _ => 1.0),
        }),
        (Trend, new SimulationSettings
        {
            Weights = new Dictionary<FactorKind, double>
            {
                [FactorKind.Momentum] = 3,
                [FactorKind.Trend] = 2,
                [FactorKind.High52Week] = 2,
                [FactorKind.Breakout] = 1,
                [FactorKind.RiskAdjusted] = 1,
            },
            TrailingStop = 0.15,
            StopLoss = 0.10,
            RebalanceSessions = 5,
        }),
        (Value, new SimulationSettings
        {
            Weights = new Dictionary<FactorKind, double>
            {
                [FactorKind.EarningsYield] = 3,
                [FactorKind.SectorValue] = 2,
                [FactorKind.EpsRevision] = 2,
                [FactorKind.LowVolatility] = 1,
                [FactorKind.Consistency] = 1,
                [FactorKind.Trend] = 1,
            },
            Positions = 10,
            RebalanceSessions = 20,
            StopLoss = 0.18,
            TrailingStop = 0.25,
            MinHoldSessions = 20,
        }),
        (Flow, new SimulationSettings
        {
            Weights = new Dictionary<FactorKind, double>
            {
                [FactorKind.IndividualFlow] = 3,
                [FactorKind.BuyerPower] = 2,
                [FactorKind.Trend] = 1,
                [FactorKind.Momentum] = 1,
            },
            Positions = 6,
            RebalanceSessions = 5,
            StopLoss = 0.08,
            TrailingStop = 0.10,
        }),
        (Swing, new SimulationSettings
        {
            Weights = new Dictionary<FactorKind, double>
            {
                [FactorKind.RsiPullback] = 3,
                [FactorKind.ShortReversal] = 2,
                [FactorKind.Trend] = 1,
                [FactorKind.LowVolatility] = 1,
            },
            Positions = 8,
            RebalanceSessions = 5,
            StopLoss = 0.07,
            TrailingStop = 0,
            TakeProfit = 0.10,
            MaxHoldSessions = 30,
            MinHoldSessions = 3,
            HoldPercentile = 0.3,
        }),
    ];

    public static SimulationSettings Get(string key) => All.FirstOrDefault(p => p.Key == key).Settings ?? new SimulationSettings();
}

/// <summary>Parameters in force from <see cref="From"/> (inclusive) until the next revision: editing a running simulation never rewrites its past.</summary>
public sealed record SettingsRevision(DateOnly From, SimulationSettings Settings);

/// <summary>
/// An order the user placed. It was decided with data up to <see cref="PlacedOn"/> (the latest session when it was
/// placed) and can fill on the sessions after it, for <see cref="GoodForSessions"/> sessions. A buy gives a
/// <see cref="Value"/> (rials) or <see cref="Shares"/>; a sell gives <see cref="Shares"/>, or neither to sell everything.
/// </summary>
public sealed record ManualOrder
{
    public required string Id { get; init; }
    public required DateOnly PlacedOn { get; init; }
    public required long InsCode { get; init; }
    public required OrderSide Side { get; init; }
    public decimal? Value { get; init; }
    public decimal? Shares { get; init; }

    /// <summary>Highest price for a buy, lowest for a sell; null = at the fill price of the session.</summary>
    public decimal? LimitPrice { get; init; }

    public int GoodForSessions { get; init; } = 5;

    /// <summary>The order no longer fills on sessions after this date.</summary>
    public DateOnly? CancelledOn { get; init; }
}

/// <summary>Everything that defines a simulation. Its results are a pure function of this and the market data.</summary>
public sealed record SimulationSpec
{
    public required string Id { get; init; }
    public SimulationMode Mode { get; init; }

    /// <summary>Starting cash in rials.</summary>
    public required decimal Capital { get; init; }

    /// <summary>The first session on or after this date starts the simulation (its first decision is taken at that close).</summary>
    public required DateOnly Start { get; init; }

    /// <summary>Tracking stops after this date (null = it follows every new session).</summary>
    public DateOnly? End { get; init; }

    /// <summary>At least one; the first applies from the start whatever its date.</summary>
    public required IReadOnlyList<SettingsRevision> Revisions { get; init; }

    public IReadOnlyList<ManualOrder> Orders { get; init; } = [];
}

/// <summary>Keys of trade reasons and journal events (the UI words them).</summary>
public static class SimulationEvents
{
    public const string Start = "start";
    public const string Entry = "entry";
    public const string Resize = "resize";
    public const string RankExit = "rank-exit";
    public const string StopLoss = "stop-loss";
    public const string TrailingStop = "trailing-stop";
    public const string TakeProfit = "take-profit";
    public const string TimeExit = "time-exit";
    public const string Manual = "manual";
    public const string Rebalance = "rebalance";
    public const string RiskOff = "risk-off";
    public const string RiskOn = "risk-on";
    public const string BlockedBuyQueue = "blocked-buy-queue";
    public const string BlockedSellQueue = "blocked-sell-queue";
    public const string BlockedNoTrade = "blocked-no-trade";
    public const string BlockedLimit = "blocked-limit";
    public const string Expired = "expired";
    public const string NoCash = "no-cash";
    public const string NotHeld = "not-held";
    public const string NotTradable = "not-tradable";
    public const string TooSmall = "too-small";
    public const string PartialFill = "partial-fill";
    public const string CorporateAction = "corporate-action";
    public const string SettingsChanged = "settings-changed";
    public const string End = "end";
}

/// <summary>States of a manual order.</summary>
public static class OrderStates
{
    public const string Pending = "pending";
    public const string Filled = "filled";
    public const string Partial = "partial";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";
    public const string Rejected = "rejected";
}

/// <summary>
/// One session of the simulation: the portfolio at the close and the benchmarks' levels (null where unknown); the average
/// share is a level that is 1 at the simulation's start.
/// </summary>
public sealed record EquityPoint(DateOnly Date, decimal Equity, decimal Cash, decimal Invested, double? Tedpix, double? EqualWeight, double? Usd);

/// <summary>
/// A position at the last simulated close. Shares and the cost per share follow capital increases and dividends
/// (reinvested, the total-return convention of the reports), so they can be fractional.
/// </summary>
public sealed record SimulationHolding
{
    public required long InsCode { get; init; }
    public required string Symbol { get; init; }
    public string? Name { get; init; }
    public string? SectorName { get; init; }
    public decimal Shares { get; init; }

    /// <summary>Cost per share including buy costs.</summary>
    public decimal AverageCost { get; init; }

    public decimal LastPrice { get; init; }
    public decimal Value { get; init; }
    public double Weight { get; init; }
    public decimal UnrealizedPnl { get; init; }
    public double UnrealizedReturn { get; init; }
    public double? DayChange { get; init; }
    public DateOnly OpenedOn { get; init; }
    public int SessionsHeld { get; init; }

    /// <summary>The price that would trigger the stop loss or the trailing stop, whichever is higher.</summary>
    public decimal? StopPrice { get; init; }

    public string? DayStatus { get; init; }

    /// <summary>Composite score (0–100) at the last ranking, when the share was ranked.</summary>
    public double? Score { get; init; }

    public int? Rank { get; init; }
}

public sealed record SimulationTrade
{
    public required DateOnly Date { get; init; }
    public required long InsCode { get; init; }
    public required string Symbol { get; init; }
    public required OrderSide Side { get; init; }
    public decimal Shares { get; init; }
    public decimal Price { get; init; }
    public decimal Value { get; init; }
    public decimal Costs { get; init; }

    /// <summary>On a sell: proceeds after costs minus the cost of the shares sold.</summary>
    public decimal? RealizedPnl { get; init; }

    public double? RealizedReturn { get; init; }
    public int? SessionsHeld { get; init; }
    public required string Reason { get; init; }
    public string? OrderId { get; init; }
}

/// <summary>Something the simulation did or could not do; <see cref="Value"/> depends on <see cref="Kind"/> (see <see cref="SimulationEvents"/>).</summary>
public sealed record JournalEntry(DateOnly Date, string Kind, long? InsCode = null, string? Symbol = null, double? Value = null, double? Value2 = null);

/// <summary>An order waiting for the next session (or, in manual mode, a suggestion of the strategy).</summary>
public sealed record PlannedOrder
{
    public required long InsCode { get; init; }
    public required string Symbol { get; init; }
    public string? Name { get; init; }
    public required OrderSide Side { get; init; }
    public decimal Shares { get; init; }

    /// <summary>Value at the last close.</summary>
    public decimal Value { get; init; }

    public decimal Price { get; init; }
    public required string Reason { get; init; }
    public double? Score { get; init; }
    public int? Rank { get; init; }
    public string? OrderId { get; init; }
}

public sealed record OrderStatus(string OrderId, string State, decimal FilledShares, decimal? AveragePrice, DateOnly? LastFillOn, string? Reason);

/// <summary>
/// A factor's weight at the last ranking: the chosen share and the share in use. They differ in adaptive mode, and a
/// factor that too few shares have a value for (<see cref="Coverage"/> under 30%, e.g. EPS before snapshots were
/// collected) is left out and its weight shared among the others. <see cref="MeanIc"/> is its average rank
/// correlation with the next 20 sessions' returns over <see cref="Months"/> formations (adaptive mode).
/// </summary>
public sealed record FactorWeight(FactorKind Factor, double Chosen, double Used, double? MeanIc, int Months, double Coverage = 1);

/// <summary>Performance of a simulation over its sessions. Returns are fractions; yearly figures use 240 sessions a year.</summary>
public sealed record SimulationStats
{
    public static readonly SimulationStats Empty = new();

    public int Sessions { get; init; }
    public decimal Equity { get; init; }
    public decimal Cash { get; init; }
    public decimal Invested { get; init; }
    public double TotalReturn { get; init; }

    /// <summary>Compound yearly return; null under 60 sessions (it would be noise).</summary>
    public double? AnnualizedReturn { get; init; }

    public double? DayReturn { get; init; }
    public double? WeekReturn { get; init; }
    public double? MonthReturn { get; init; }
    public double? Volatility { get; init; }

    /// <summary>Yearly excess over the cash yield ÷ volatility.</summary>
    public double? Sharpe { get; init; }

    public double? Sortino { get; init; }
    public double MaxDrawdown { get; init; }
    public double CurrentDrawdown { get; init; }
    public double? Calmar { get; init; }
    public double? Beta { get; init; }

    /// <summary>Yearly return not explained by TEDPIX × beta.</summary>
    public double? Alpha { get; init; }

    public double? Correlation { get; init; }
    public double? TedpixReturn { get; init; }

    /// <summary>(1 + return) ÷ (1 + TEDPIX return) − 1.</summary>
    public double? ExcessReturn { get; init; }

    public double? EqualWeightReturn { get; init; }

    /// <summary>The portfolio's return measured in free-market dollars.</summary>
    public double? UsdReturn { get; init; }

    public int Trades { get; init; }
    public int ClosedTrades { get; init; }
    public double? WinRate { get; init; }
    public double? ProfitFactor { get; init; }
    public double? AverageWin { get; init; }
    public double? AverageLoss { get; init; }
    public double? AverageHoldSessions { get; init; }

    /// <summary>Value traded per year ÷ average equity.</summary>
    public double? Turnover { get; init; }

    public decimal CostsPaid { get; init; }
    public decimal RealizedPnl { get; init; }
    public decimal UnrealizedPnl { get; init; }

    /// <summary>Average share of the portfolio invested in shares.</summary>
    public double Exposure { get; init; }

    public double? BestDay { get; init; }
    public double? WorstDay { get; init; }
}

/// <summary>A simulation replayed from its start to the latest session (or its end date).</summary>
public sealed record SimulationResult
{
    public required string Id { get; init; }

    /// <summary>The first simulated session; null while waiting for the first session on or after the start date.</summary>
    public DateOnly? Start { get; init; }

    /// <summary>The last simulated session.</summary>
    public DateOnly? AsOf { get; init; }

    /// <summary>The latest session in the store (later than <see cref="AsOf"/> for a simulation that stopped tracking).</summary>
    public DateOnly? Latest { get; init; }

    public SimulationStats Stats { get; init; } = SimulationStats.Empty;
    public IReadOnlyList<EquityPoint> Equity { get; init; } = [];
    public IReadOnlyList<SimulationHolding> Holdings { get; init; } = [];
    public IReadOnlyList<SimulationTrade> Trades { get; init; } = [];
    public IReadOnlyList<JournalEntry> Journal { get; init; } = [];

    /// <summary>Orders that will try to fill on the next session.</summary>
    public IReadOnlyList<PlannedOrder> Planned { get; init; } = [];

    /// <summary>Manual mode: what the strategy would trade now with the simulation's parameters.</summary>
    public IReadOnlyList<PlannedOrder> Suggestions { get; init; } = [];

    public IReadOnlyList<OrderStatus> Orders { get; init; } = [];
    public IReadOnlyList<FactorWeight> Factors { get; init; } = [];

    /// <summary>The regime filter's state at the last close (true when it is off).</summary>
    public bool RiskOn { get; init; } = true;

    /// <summary>Share of the portfolio the strategy aims to invest at the last close.</summary>
    public double TargetExposure { get; init; } = 1;

    /// <summary>Sessions until the next ranking (automatic mode).</summary>
    public int? NextRebalanceIn { get; init; }

    /// <summary>Shares that passed the universe filters at the last ranking.</summary>
    public int Universe { get; init; }

    /// <summary>The first session with EPS data (fundamental factors are unavailable before it).</summary>
    public DateOnly? FundamentalsSince { get; init; }
}

/// <summary>What <see cref="MarketSimulator.Tune"/> ranks the parameter sets by, on the in-sample part.</summary>
public enum TuningObjective
{
    /// <summary>Compound yearly return.</summary>
    Return,

    /// <summary>Return per unit of volatility.</summary>
    Sharpe,

    /// <summary>Yearly return ÷ deepest fall.</summary>
    Calmar,
}

/// <summary>One parameter set of a tuning run, measured on the in-sample and the out-of-sample part of the same run.</summary>
public sealed record TuningRow
{
    public required SimulationSettings Settings { get; init; }
    public double Objective { get; init; }
    public double InReturn { get; init; }
    public double? InAnnualized { get; init; }
    public double? InSharpe { get; init; }
    public double InMaxDrawdown { get; init; }
    public double OutReturn { get; init; }
    public double? OutAnnualized { get; init; }
    public double? OutSharpe { get; init; }
    public double OutMaxDrawdown { get; init; }
    public int Trades { get; init; }
    public double? Turnover { get; init; }
}

public sealed record SimulationTuning
{
    public static readonly SimulationTuning Empty = new();

    public DateOnly? From { get; init; }

    /// <summary>The first out-of-sample session: rows are ranked on the sessions before it only.</summary>
    public DateOnly? Split { get; init; }

    public DateOnly? To { get; init; }
    public TuningObjective Objective { get; init; }
    public double? TedpixIn { get; init; }
    public double? TedpixOut { get; init; }
    public IReadOnlyList<TuningRow> Rows { get; init; } = [];
}
