namespace Boors.Market.Services.Contracts;

// Read models of the Reports page (Boors.Market.Analytics.MarketReports). Returns, shares and scores of 0–1 are
// fractions (0.25 = 25%); the reliability score is 0–100; values are rials. Periods: 1m, 3m, 6m, ytd, 1y, 3y, 5y, and
// custom (a window given by its start date). A window ends at its base date (AsOf); when that is earlier than the
// latest session (Latest), the SinceThen fields show what followed.

public record PerformanceRowDto(
    long InsCode,
    string Symbol,
    string? Name,
    string InstrumentClass,
    string? SectorCode,
    string? SectorName,
    decimal? Close,
    double Return,
    double? AnnualizedReturn,
    double? UsdReturn,
    double? ExcessReturn,
    double? Volatility,
    double? RiskAdjusted,
    double MaxDrawdown,
    double? PositiveMonths,
    double? UsdWinRate,
    double? FromHigh52Week,
    decimal AverageValue20,
    int PricedSessions,
    double Coverage,
    double? BuyQueueShare,
    double? Score,
    double? SinceThen);

public record PerformanceReportDto(
    DateOnly? AsOf,
    string Period,
    DateOnly? From,
    int Sessions,
    double? TedpixReturn,
    double? MedianReturn,
    double? UsdReturn,
    double? CoinReturn,
    int Universe,
    int ShortHistory,
    int Illiquid,
    List<PerformanceRowDto> Rows,
    DateOnly? Latest,
    double? TedpixSinceThen,
    double? UsdSinceThen);

public record SectorReturnDto(
    string SectorCode,
    string? SectorName,
    int Members,
    double? MedianReturn,
    double? AverageReturn,
    double? MedianUsdReturn,
    double PositiveShare,
    double? BeatTedpixShare,
    long? BestInsCode,
    string? BestSymbol,
    double? BestReturn,
    decimal Value20,
    double? MedianSinceThen);

public record SectorReturnsReportDto(
    DateOnly? AsOf,
    string Period,
    DateOnly? From,
    double? TedpixReturn,
    double? UsdReturn,
    List<SectorReturnDto> Sectors,
    DateOnly? Latest,
    double? TedpixSinceThen);

public record SignalMonthDto(DateOnly Date, double Top, double Universe, double Bottom, double InformationCoefficient, double? Turnover);

public record SignalPickDto(long InsCode, string Symbol, string? Name, string? SectorCode, double Value);

/// <summary><paramref name="Verdict"/>: works, eaten-by-costs, reverses, no-edge or insufficient.</summary>
public record SignalVerdictDto(
    string Signal,
    int Months,
    double? TopReturn,
    double? UniverseReturn,
    double? BottomReturn,
    double? Excess,
    double? HitRate,
    double? TStat,
    double? InformationCoefficient,
    double? Turnover,
    double? NetExcess,
    double? AnnualNetExcess,
    string Verdict,
    List<SignalMonthDto> History,
    List<SignalPickDto> Picks);

public record SignalTrackRecordDto(DateOnly? AsOf, DateOnly? From, int Years, double RoundTripCost, List<SignalVerdictDto> Signals);

/// <summary>Twelve monthly returns per series, index 0 = Farvardin; null where a month has no data.</summary>
public record SeasonalityYearDto(int Year, List<double?> Tedpix, List<double?> TedpixUsd, List<double?> EqualWeight, List<double?> Usd);

public record SeasonalityReportDto(DateOnly? AsOf, int? CurrentYear, int? CurrentMonth, List<SeasonalityYearDto> Years);

/// <summary>A share on a shortlist (Boors.Market.Analytics.ShortlistPick); <paramref name="AlsoIn"/> lists the other horizons (short, mid, long) holding it.</summary>
public record ShortlistPickDto(
    long InsCode,
    string Symbol,
    string? Name,
    string? SectorCode,
    string? SectorName,
    double Score,
    double? Return1M,
    double? Return3M,
    double? Return1Y,
    double? UsdReturn1Y,
    double? Momentum12To1,
    double? RiskAdjusted,
    double? Volatility,
    double? FromHigh52Week,
    double? AnnualizedReturn3Y,
    int? RecordYears,
    double? AnnualizedReturn,
    double? UsdReturn,
    double? UsdWinRate,
    double? PositiveMonths,
    double? MaxDrawdown,
    decimal AverageValue20,
    double? SinceThen,
    List<string> AlsoIn);

public record ShortlistDto(
    DateOnly? AsOf,
    DateOnly? Latest,
    List<ShortlistPickDto> ShortTerm,
    List<ShortlistPickDto> MidTerm,
    List<ShortlistPickDto> LongTerm,
    double? Tedpix3M,
    double? Usd3M,
    double? Tedpix1Y,
    double? Usd1Y,
    double? Tedpix5Y,
    double? Usd5Y,
    double? TedpixSinceThen,
    double? UsdSinceThen,
    int MissingHistory);
