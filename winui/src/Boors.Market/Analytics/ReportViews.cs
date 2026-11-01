namespace Boors.Market.Analytics;

/// <summary>
/// A report window ending at a base date (the latest session unless one is chosen). The API keys are 1m, 3m, 6m, ytd,
/// 1y, 3y, 5y and custom (a start date chosen by the caller).
/// </summary>
public enum ReportPeriod
{
    OneMonth,
    ThreeMonths,
    SixMonths,

    /// <summary>Since the start of the current Jalali year (1 Farvardin).</summary>
    YearToDate,

    OneYear,
    ThreeYears,
    FiveYears,

    /// <summary>From a start date chosen by the caller.</summary>
    Custom,
}

public static class ReportPeriods
{
    private static readonly PersianCalendar Jalali = new();

    public static readonly IReadOnlyDictionary<string, ReportPeriod> Keys = new Dictionary<string, ReportPeriod>(StringComparer.OrdinalIgnoreCase)
    {
        ["1m"] = ReportPeriod.OneMonth,
        ["3m"] = ReportPeriod.ThreeMonths,
        ["6m"] = ReportPeriod.SixMonths,
        ["ytd"] = ReportPeriod.YearToDate,
        ["1y"] = ReportPeriod.OneYear,
        ["3y"] = ReportPeriod.ThreeYears,
        ["5y"] = ReportPeriod.FiveYears,
        ["custom"] = ReportPeriod.Custom,
    };

    public static string Key(this ReportPeriod period) => Keys.First(k => k.Value == period).Key;

    /// <summary>
    /// The base date of the window: returns cover the sessions after it, measured from the last close on or before it
    /// (so a one-year return compares today's close with the close a year ago, like the symbol page's returns card).
    /// </summary>
    public static DateOnly Start(this ReportPeriod period, DateOnly asOf) => period switch
    {
        ReportPeriod.OneMonth => asOf.AddMonths(-1),
        ReportPeriod.ThreeMonths => asOf.AddMonths(-3),
        ReportPeriod.SixMonths => asOf.AddMonths(-6),
        ReportPeriod.YearToDate => JalaliYearStart(asOf).AddDays(-1),
        ReportPeriod.OneYear => asOf.AddYears(-1),
        ReportPeriod.ThreeYears => asOf.AddYears(-3),
        ReportPeriod.FiveYears => asOf.AddYears(-5),
        ReportPeriod.Custom => throw new InvalidOperationException("A custom window carries its own start date."),
        _ => throw new ArgumentOutOfRangeException(nameof(period)),
    };

    public static DateOnly JalaliYearStart(DateOnly date) =>
        DateOnly.FromDateTime(Jalali.ToDateTime(Jalali.GetYear(date.ToDateTime(TimeOnly.MinValue)), 1, 1, 0, 0, 0, 0));

    public static (int Year, int Month) JalaliMonth(DateOnly date)
    {
        var dt = date.ToDateTime(TimeOnly.MinValue);
        return (Jalali.GetYear(dt), Jalali.GetMonth(dt));
    }
}

public enum PerformanceReportKind
{
    /// <summary>Highest total return over the period.</summary>
    TopPerformers,

    /// <summary>Highest reliability score: return, risk-adjusted return, up months, drawdown and dollar-beating holds together.</summary>
    SteadyCompounders,

    /// <summary>Reliable long-term performers (score ≥ 60, beat TEDPIX) that now trade at least 15% below their 52-week high.</summary>
    QualityOnDip,
}

/// <summary>
/// One equity over a report window. Returns are fractions (0.25 = 25%) and compound <c>Close / Yesterday − 1</c> over
/// the priced sessions of the window, so they include capital increases, dividends and the moves across halts.
/// </summary>
public sealed record PerformanceRow
{
    public required long InsCode { get; init; }
    public required string Symbol { get; init; }
    public string? Name { get; init; }
    public required string InstrumentClass { get; init; }
    public string? SectorCode { get; init; }
    public string? SectorName { get; init; }
    public decimal? Close { get; init; }

    public double Return { get; init; }

    /// <summary>Compound annual growth rate; null for windows shorter than a year.</summary>
    public double? AnnualizedReturn { get; init; }

    /// <summary>The return measured in free-market dollars.</summary>
    public double? UsdReturn { get; init; }

    /// <summary>Return relative to TEDPIX over the same window: (1 + r) / (1 + TEDPIX) − 1.</summary>
    public double? ExcessReturn { get; init; }

    /// <summary>Annualised standard deviation of daily returns.</summary>
    public double? Volatility { get; init; }

    /// <summary>Annualised mean daily return divided by annualised volatility (a Sharpe ratio without a risk-free rate).</summary>
    public double? RiskAdjusted { get; init; }

    /// <summary>Deepest fall from a running peak inside the window (−0.3 = a 30% fall); 0 when it never fell.</summary>
    public double MaxDrawdown { get; init; }

    /// <summary>Share of calendar months with a positive return; null for windows under six months.</summary>
    public double? PositiveMonths { get; init; }

    /// <summary>
    /// Share of one-year holds, started at each month-end of the window, that beat the free-market dollar; null for
    /// windows under 18 months (fewer than six such holds).
    /// </summary>
    public double? UsdWinRate { get; init; }

    /// <summary>Last adjusted close relative to the 52-week closing high (−0.2 = 20% below the high).</summary>
    public double? FromHigh52Week { get; init; }

    /// <summary>Average rial value traded per session over the last 20 market sessions (non-traded sessions count as 0).</summary>
    public decimal AverageValue20 { get; init; }

    public int PricedSessions { get; init; }

    /// <summary>Priced sessions of the instrument divided by the market's sessions in the window (halts lower it).</summary>
    public double Coverage { get; init; }

    /// <summary>Share of priced sessions spent in an all-day buy queue: part of the return could not be bought.</summary>
    public double? BuyQueueShare { get; init; }

    /// <summary>
    /// 0–100: the average percentile, within the report's universe, of the annualised return, the risk-adjusted
    /// return, the share of up months, the drawdown (shallower is better) and the dollar-beating holds.
    /// </summary>
    public double? Score { get; init; }

    /// <summary>
    /// The return from the end of the window to the latest session, when the window ends in the past (what happened
    /// next — never used by the ranking); null when the window ends at the latest session or the share has not traded since.
    /// </summary>
    public double? SinceThen { get; init; }
}

public sealed record PerformanceReport
{
    public static readonly PerformanceReport Empty = new() { Period = ReportPeriod.OneYear };

    public DateOnly? AsOf { get; init; }
    public required ReportPeriod Period { get; init; }
    public DateOnly? From { get; init; }

    /// <summary>Market sessions in the window.</summary>
    public int Sessions { get; init; }

    public double? TedpixReturn { get; init; }

    /// <summary>The median return of the universe (all equities that passed the history and liquidity filters).</summary>
    public double? MedianReturn { get; init; }

    public double? UsdReturn { get; init; }
    public double? CoinReturn { get; init; }

    /// <summary>Equities ranked: listed for the whole window and liquid enough.</summary>
    public int Universe { get; init; }

    /// <summary>Equities left out because they were listed (or their history starts) inside the window.</summary>
    public int ShortHistory { get; init; }

    /// <summary>Equities left out by the liquidity filter.</summary>
    public int Illiquid { get; init; }

    public IReadOnlyList<PerformanceRow> Rows { get; init; } = [];

    /// <summary>The latest session in the store; later than <see cref="AsOf"/> when the window ends in the past.</summary>
    public DateOnly? Latest { get; init; }

    /// <summary>TEDPIX and the dollar from the end of the window to <see cref="Latest"/> (null when the window ends there).</summary>
    public double? TedpixSinceThen { get; init; }

    public double? UsdSinceThen { get; init; }
}

/// <summary>Equal-weighted view of one sector over a report window (members are the report's universe).</summary>
public sealed record SectorReturn
{
    public required string SectorCode { get; init; }
    public string? SectorName { get; init; }
    public int Members { get; init; }
    public double? MedianReturn { get; init; }
    public double? AverageReturn { get; init; }
    public double? MedianUsdReturn { get; init; }
    public double PositiveShare { get; init; }
    public double? BeatTedpixShare { get; init; }
    public long? BestInsCode { get; init; }
    public string? BestSymbol { get; init; }
    public double? BestReturn { get; init; }

    /// <summary>Sum of the members' average value traded per session (last 20 sessions).</summary>
    public decimal Value20 { get; init; }

    /// <summary>The members' median return from the end of the window to the latest session (see <see cref="PerformanceRow.SinceThen"/>).</summary>
    public double? MedianSinceThen { get; init; }
}

public sealed record SectorReturnsReport(
    DateOnly? AsOf,
    ReportPeriod Period,
    DateOnly? From,
    double? TedpixReturn,
    double? UsdReturn,
    IReadOnlyList<SectorReturn> Sectors,
    DateOnly? Latest = null,
    double? TedpixSinceThen = null);

/// <summary>A ranking rule tested in <see cref="MarketReports.GetSignalTrackRecord"/>; higher value = ranked higher.</summary>
public enum SignalKind
{
    Return1M,
    Return3M,
    Return6M,
    Return12M,
    Momentum12To1,
    Momentum6To1,
    High52WeekProximity,
    LowVolatility,
    RiskAdjusted12M,
    Consistency12M,
    IndividualFlow20,
}

/// <summary>The verdict keys of a tested signal.</summary>
public static class SignalVerdicts
{
    /// <summary>The top fifth beat the universe with t ≥ 2 and kept an edge after trading costs.</summary>
    public const string Works = "works";

    /// <summary>The top fifth beat the universe with t ≥ 2, but turnover × round-trip cost ate the edge.</summary>
    public const string EatenByCosts = "eaten-by-costs";

    /// <summary>The top fifth lagged the universe with t ≤ −2: the opposite ranking worked.</summary>
    public const string Reverses = "reverses";

    /// <summary>|t| &lt; 2: no difference that could be told apart from luck.</summary>
    public const string NoEdge = "no-edge";

    /// <summary>Fewer than 12 tested months.</summary>
    public const string Insufficient = "insufficient";
}

/// <summary>One monthly formation of a signal test: the next month's average return of each group.</summary>
public sealed record SignalMonth(DateOnly Date, double Top, double Universe, double Bottom, double InformationCoefficient, double? Turnover);

/// <summary>
/// A current pick of a signal. <see cref="Value"/> is in the signal's natural unit: a return (fraction) for the return,
/// momentum and 52-week-high signals, annualised volatility for low volatility, a ratio for risk-adjusted return, the
/// share of up months for consistency and net inflow ÷ value traded for individual inflow.
/// </summary>
public sealed record SignalPick(long InsCode, string Symbol, string? Name, string? SectorCode, double Value);

/// <summary>
/// The walk-forward record of one signal. Monthly averages are simple returns per month; <see cref="TStat"/> is the
/// t-statistic of the monthly excess return (top fifth minus universe); <see cref="NetExcess"/> subtracts the average
/// turnover times the round-trip cost.
/// </summary>
public sealed record SignalVerdict
{
    public required SignalKind Signal { get; init; }
    public int Months { get; init; }
    public double? TopReturn { get; init; }
    public double? UniverseReturn { get; init; }
    public double? BottomReturn { get; init; }
    public double? Excess { get; init; }
    public double? HitRate { get; init; }
    public double? TStat { get; init; }
    public double? InformationCoefficient { get; init; }
    public double? Turnover { get; init; }
    public double? NetExcess { get; init; }
    public double? AnnualNetExcess { get; init; }
    public required string Verdict { get; init; }
    public IReadOnlyList<SignalMonth> History { get; init; } = [];
    public IReadOnlyList<SignalPick> Picks { get; init; } = [];
}

public sealed record SignalTrackRecord(DateOnly? AsOf, DateOnly? From, int Years, double RoundTripCost, IReadOnlyList<SignalVerdict> Signals)
{
    public static readonly SignalTrackRecord Empty = new(null, null, 0, 0, []);
}

/// <summary>Monthly returns of one Jalali year (index 0 = Farvardin); null where a month has no data.</summary>
public sealed record SeasonalityYear(int Year, IReadOnlyList<double?> Tedpix, IReadOnlyList<double?> TedpixUsd, IReadOnlyList<double?> EqualWeight, IReadOnlyList<double?> Usd);

/// <summary>
/// Calendar returns by Jalali month, newest year first. <see cref="CurrentMonth"/> of <see cref="CurrentYear"/> is still
/// in progress: its value is month-to-date.
/// </summary>
public sealed record SeasonalityReport(DateOnly? AsOf, int? CurrentYear, int? CurrentMonth, IReadOnlyList<SeasonalityYear> Years)
{
    public static readonly SeasonalityReport Empty = new(null, null, null, []);
}

/// <summary>The horizons of <see cref="MarketReports.GetShortlist"/>.</summary>
public static class ShortlistHorizons
{
    /// <summary>One to three months.</summary>
    public const string Short = "short";

    /// <summary>Six to twelve months.</summary>
    public const string Mid = "mid";

    /// <summary>Three to five years.</summary>
    public const string Long = "long";
}

/// <summary>
/// A share on a shortlist. Which measures are filled depends on the list: the short list uses the trend, the
/// return per unit of risk, the volatility and the distance from the 52-week high; the mid list the 1-year score,
/// returns (rial and dollar) and the 3-year yearly return; the long list its 5- or 3-year record.
/// <see cref="PositiveMonths"/> and <see cref="MaxDrawdown"/> cover the list's own window (1 year for the short and
/// mid lists, the record for the long list).
/// </summary>
public sealed record ShortlistPick
{
    public required long InsCode { get; init; }
    public required string Symbol { get; init; }
    public string? Name { get; init; }
    public string? SectorCode { get; init; }
    public string? SectorName { get; init; }

    /// <summary>0–100: the list's composite (mean percentile of its measures within the candidates).</summary>
    public double Score { get; init; }

    public double? Return1M { get; init; }
    public double? Return3M { get; init; }
    public double? Return1Y { get; init; }
    public double? UsdReturn1Y { get; init; }

    /// <summary>The 12-month return without the latest month: (1 + 1Y) / (1 + 1M) − 1.</summary>
    public double? Momentum12To1 { get; init; }

    /// <summary>Over one year (see <see cref="PerformanceRow.RiskAdjusted"/>).</summary>
    public double? RiskAdjusted { get; init; }

    /// <summary>Over one year, annualised.</summary>
    public double? Volatility { get; init; }

    public double? FromHigh52Week { get; init; }
    public double? AnnualizedReturn3Y { get; init; }

    /// <summary>The long list's record: 5 years, or 3 for a share with a shorter history.</summary>
    public int? RecordYears { get; init; }

    public double? AnnualizedReturn { get; init; }
    public double? UsdReturn { get; init; }
    public double? UsdWinRate { get; init; }
    public double? PositiveMonths { get; init; }
    public double? MaxDrawdown { get; init; }
    public decimal AverageValue20 { get; init; }

    /// <summary>The return from the base date to the latest session, when the shortlist was taken in the past.</summary>
    public double? SinceThen { get; init; }

    /// <summary>The other horizons (<see cref="ShortlistHorizons"/>) whose list also holds this share.</summary>
    public IReadOnlyList<string> AlsoIn { get; init; } = [];
}

/// <summary>
/// The three horizon lists taken on one base date, with the benchmarks each list is measured against and, for a
/// past base date, what TEDPIX and the dollar did since. <see cref="MissingHistory"/> counts shares traded in the
/// last five years whose history does not reach back far enough for the long list.
/// </summary>
public sealed record Shortlist
{
    public static readonly Shortlist Empty = new();

    public DateOnly? AsOf { get; init; }
    public DateOnly? Latest { get; init; }
    public IReadOnlyList<ShortlistPick> ShortTerm { get; init; } = [];
    public IReadOnlyList<ShortlistPick> MidTerm { get; init; } = [];
    public IReadOnlyList<ShortlistPick> LongTerm { get; init; } = [];
    public double? Tedpix3M { get; init; }
    public double? Usd3M { get; init; }
    public double? Tedpix1Y { get; init; }
    public double? Usd1Y { get; init; }
    public double? Tedpix5Y { get; init; }
    public double? Usd5Y { get; init; }
    public double? TedpixSinceThen { get; init; }
    public double? UsdSinceThen { get; init; }
    public int MissingHistory { get; init; }
}
