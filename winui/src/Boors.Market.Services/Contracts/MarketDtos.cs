namespace Boors.Market.Services.Contracts;

// Read models for the workstation pages. They mirror Boors.Market.Analytics records (mapped in LocalMarketApi on the
// server); prices and values are rials, percentages are percent (2.5 = 2.5%), fractions are 0–1.

/// <summary>One point of a daily series (an index level, the free-market dollar...).</summary>
public record SeriesPointDto(DateOnly Date, decimal Value);

public record IndexMoveDto(long InsCode, string Name, decimal Value, double? ChangePercent);

public record MacroQuoteDto(string Series, DateOnly Date, decimal Value, double? ChangePercent);

public record MarketOverviewDto(
    DateOnly Session,
    List<IndexMoveDto> Indices,
    decimal EquityValue,
    decimal FundValue,
    long EquityTrades,
    int Advancers,
    int Decliners,
    int Unchanged,
    int NotTraded,
    int Halted,
    int BuyQueues,
    decimal? BuyQueueValue,
    int SellQueues,
    decimal? SellQueueValue,
    decimal IndividualNetFlow,
    decimal? IndividualPerCapitaBuy,
    decimal? IndividualPerCapitaSell,
    bool FlowValuesEstimated,
    MacroQuoteDto? Usd,
    MacroQuoteDto? Coin,
    double? TedpixUsdChangePercent);

public record BreadthDayDto(DateOnly Date, int Advancers, int Decliners, int Unchanged, int NotTraded, int Halted, int LockedUp, int LockedDown, decimal EquityValue);

public record SectorPerformanceDto(
    string SectorCode,
    string? SectorName,
    int Instruments,
    int Traded,
    decimal Value,
    double? WeightedReturn,
    double? MedianReturn,
    decimal IndividualNetFlow,
    int Advancers,
    int Decliners);

public record MoverDto(long InsCode, string Symbol, string? Name, string InstrumentClass, string? DayStatus, decimal Close, double? ChangePercent, decimal Value, decimal? IndividualNetFlow);

public record ScreenRowDto(long InsCode, string Symbol, string? Name, string InstrumentClass, string? SectorCode, double Score, int Observations);

public record InstrumentSearchResultDto(long InsCode, string Symbol, string? Name, string InstrumentClass, long? ParentInsCode);

public record MarketWatchRequestDto(string? Date, string? Filter, List<string>? Classes, bool TradedOnly);

public record MarketWatchEntryDto(
    long InsCode,
    string Symbol,
    string? Name,
    string InstrumentClass,
    string? SectorCode,
    string? DayStatus,
    decimal Close,
    double? ChangePercent,
    decimal Last,
    double? LastChangePercent,
    long Volume,
    decimal Value,
    long Trades,
    decimal? AllowedLow,
    decimal? AllowedHigh,
    string? Queue,
    decimal? QueueValue,
    decimal? PriceEarnings,
    decimal? MarketCap,
    decimal? IndividualNetValue,
    decimal? PerCapitaBuy,
    decimal? PerCapitaSell,
    decimal? BuyerPower,
    double? IndividualBuyShare);

/// <summary><paramref name="FilterErrors"/> lists per-instrument evaluation errors (e.g. too little history for [ih][59]).</summary>
public record MarketWatchResultDto(DateOnly? Session, List<MarketWatchEntryDto> Entries, int Evaluated, List<string> FilterErrors);

public record InstrumentMetricsDto(
    long InsCode,
    string Symbol,
    string? Name,
    string InstrumentClass,
    string? SectorCode,
    string? SectorName,
    DateOnly? AsOf,
    string? DayStatus,
    decimal? Close,
    double? ChangePercent,
    decimal? MarketCap,
    decimal? High52Week,
    decimal? Low52Week,
    double? FromHigh52WeekPercent,
    Dictionary<string, double?> Returns,
    double? UsdReturn1Year,
    decimal? AverageValue20,
    decimal? IndividualNetFlow20,
    decimal? PerCapitaBuy,
    decimal? PerCapitaSell,
    decimal? BuyerPower,
    double? BuyerPowerPercentile);

/// <summary>A daily bar for charts; <paramref name="AdjustedClose"/> is point-in-time as of the last bar returned.</summary>
public record BarDto(DateOnly Date, decimal Open, decimal High, decimal Low, decimal Close, decimal AdjustedClose, long Volume, decimal Value, long Trades, string? Status, double? Return);

public record FlowDayDto(
    DateOnly Date,
    decimal BuyIndividualValue,
    decimal SellIndividualValue,
    decimal BuyInstitutionalValue,
    decimal SellInstitutionalValue,
    int BuyIndividualCount,
    int SellIndividualCount,
    decimal IndividualNetValue,
    decimal? PerCapitaBuy,
    decimal? PerCapitaSell,
    decimal? BuyerPower,
    bool ValuesEstimated);

/// <summary><paramref name="Kind"/>: "adjustment" (factor = adjusted ÷ unadjusted) or "share_change".</summary>
public record CorporateEventDto(DateOnly Date, string Kind, string? Source, decimal? Factor, decimal? SharesBefore, decimal? SharesAfter);

public record ShareholderDto(DateOnly SnapshotDate, string HolderName, decimal Shares, decimal Percent, int Change, decimal ChangeAmount);

public record CodalLetterDto(long TracingNo, string Title, string? LetterCode, int? LetterTypeId, DateTimeOffset? PublishedAt, string? DisclosureGroup, bool HasExcel, string? ReportUrl, string? ExcelUrl, string? PdfUrl);

public record SupervisorMessageDto(long Id, DateOnly Date, string Title, string? Body, string Kind, string Reason, bool ReopensWithoutLimit);
