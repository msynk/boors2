using Boors.Market.Analytics;
using Boors.Market.Services.Contracts;

namespace Boors.Market.Services.Local;

/// <summary>Boors.Market read models → API DTOs. Plain code: the shapes match one to one (the boilerplate used Mapperly).</summary>
public static class DtoMapping
{
    public static MarketOverviewDto ToDto(this MarketOverview s) => new(
        s.Session,
        [.. s.Indices.Select(i => new IndexMoveDto(i.InsCode, i.Name, i.Value, i.ChangePercent))],
        s.EquityValue,
        s.FundValue,
        s.EquityTrades,
        s.Advancers,
        s.Decliners,
        s.Unchanged,
        s.NotTraded,
        s.Halted,
        s.BuyQueues,
        s.BuyQueueValue,
        s.SellQueues,
        s.SellQueueValue,
        s.IndividualNetFlow,
        s.IndividualPerCapitaBuy,
        s.IndividualPerCapitaSell,
        s.FlowValuesEstimated,
        s.Usd?.ToDto(),
        s.Coin?.ToDto(),
        s.TedpixUsdChangePercent);

    public static MacroQuoteDto ToDto(this MacroQuote s) => new(s.Series, s.Date, s.Value, s.ChangePercent);

    public static BreadthDayDto ToDto(this BreadthDay s) =>
        new(s.Date, s.Advancers, s.Decliners, s.Unchanged, s.NotTraded, s.Halted, s.LockedUp, s.LockedDown, s.EquityValue);

    public static SectorPerformanceDto ToDto(this SectorPerformance s) =>
        new(s.SectorCode, s.SectorName, s.Instruments, s.Traded, s.Value, s.WeightedReturn, s.MedianReturn, s.IndividualNetFlow, s.Advancers, s.Decliners);

    public static MoverDto ToDto(this Mover s) =>
        new(s.InsCode, s.Symbol, s.Name, s.InstrumentClass, s.DayStatus, s.Close, s.ChangePercent, s.Value, s.IndividualNetFlow);

    public static ScreenRowDto ToDto(this ScreenRow s) =>
        new(s.InsCode, s.Symbol, s.Name, s.InstrumentClass, s.SectorCode, s.Score, s.Observations);

    public static PerformanceReportDto ToDto(this PerformanceReport s) => new(
        s.AsOf, s.Period.Key(), s.From, s.Sessions, s.TedpixReturn, s.MedianReturn, s.UsdReturn, s.CoinReturn, s.Universe, s.ShortHistory, s.Illiquid,
        [.. s.Rows.Select(r => new PerformanceRowDto(
            r.InsCode, r.Symbol, r.Name, r.InstrumentClass, r.SectorCode, r.SectorName, r.Close, r.Return, r.AnnualizedReturn, r.UsdReturn, r.ExcessReturn,
            r.Volatility, r.RiskAdjusted, r.MaxDrawdown, r.PositiveMonths, r.UsdWinRate, r.FromHigh52Week, r.AverageValue20, r.PricedSessions, r.Coverage,
            r.BuyQueueShare, r.Score, r.SinceThen))],
        s.Latest, s.TedpixSinceThen, s.UsdSinceThen);

    public static SectorReturnsReportDto ToDto(this SectorReturnsReport s) => new(
        s.AsOf, s.Period.Key(), s.From, s.TedpixReturn, s.UsdReturn,
        [.. s.Sectors.Select(x => new SectorReturnDto(
            x.SectorCode, x.SectorName, x.Members, x.MedianReturn, x.AverageReturn, x.MedianUsdReturn, x.PositiveShare, x.BeatTedpixShare,
            x.BestInsCode, x.BestSymbol, x.BestReturn, x.Value20, x.MedianSinceThen))],
        s.Latest, s.TedpixSinceThen);

    public static SignalTrackRecordDto ToDto(this SignalTrackRecord s) => new(
        s.AsOf, s.From, s.Years, s.RoundTripCost,
        [.. s.Signals.Select(v => new SignalVerdictDto(
            v.Signal.ToString(), v.Months, v.TopReturn, v.UniverseReturn, v.BottomReturn, v.Excess, v.HitRate, v.TStat, v.InformationCoefficient,
            v.Turnover, v.NetExcess, v.AnnualNetExcess, v.Verdict,
            [.. v.History.Select(m => new SignalMonthDto(m.Date, m.Top, m.Universe, m.Bottom, m.InformationCoefficient, m.Turnover))],
            [.. v.Picks.Select(p => new SignalPickDto(p.InsCode, p.Symbol, p.Name, p.SectorCode, p.Value))]))]);

    public static SeasonalityReportDto ToDto(this SeasonalityReport s) => new(
        s.AsOf, s.CurrentYear, s.CurrentMonth,
        [.. s.Years.Select(y => new SeasonalityYearDto(y.Year, [.. y.Tedpix], [.. y.TedpixUsd], [.. y.EqualWeight], [.. y.Usd]))]);

    public static ShortlistDto ToDto(this Shortlist s) => new(
        s.AsOf, s.Latest, [.. s.ShortTerm.Select(ToDto)], [.. s.MidTerm.Select(ToDto)], [.. s.LongTerm.Select(ToDto)],
        s.Tedpix3M, s.Usd3M, s.Tedpix1Y, s.Usd1Y, s.Tedpix5Y, s.Usd5Y, s.TedpixSinceThen, s.UsdSinceThen, s.MissingHistory);

    public static ShortlistPickDto ToDto(this ShortlistPick p) => new(
        p.InsCode, p.Symbol, p.Name, p.SectorCode, p.SectorName, p.Score, p.Return1M, p.Return3M, p.Return1Y, p.UsdReturn1Y, p.Momentum12To1,
        p.RiskAdjusted, p.Volatility, p.FromHigh52Week, p.AnnualizedReturn3Y, p.RecordYears, p.AnnualizedReturn, p.UsdReturn, p.UsdWinRate,
        p.PositiveMonths, p.MaxDrawdown, p.AverageValue20, p.SinceThen, [.. p.AlsoIn]);

    public static MarketWatchEntryDto ToDto(this MarketWatchEntry s) => new(
        s.InsCode, s.Symbol, s.Name, s.InstrumentClass, s.SectorCode, s.DayStatus, s.Close, s.ChangePercent, s.Last, s.LastChangePercent,
        s.Volume, s.Value, s.Trades, s.AllowedLow, s.AllowedHigh, s.Queue, s.QueueValue, s.PriceEarnings, s.MarketCap, s.IndividualNetValue,
        s.PerCapitaBuy, s.PerCapitaSell, s.BuyerPower, s.IndividualBuyShare);

    public static InstrumentMetricsDto ToDto(this InstrumentMetrics s) => new(
        s.InsCode, s.Symbol, s.Name, s.InstrumentClass, s.SectorCode, s.SectorName, s.AsOf, s.DayStatus, s.Close, s.ChangePercent, s.MarketCap,
        s.High52Week, s.Low52Week, s.FromHigh52WeekPercent, s.Returns.ToDictionary(), s.UsdReturn1Year, s.AverageValue20, s.IndividualNetFlow20,
        s.PerCapitaBuy, s.PerCapitaSell, s.BuyerPower, s.BuyerPowerPercentile);
}
