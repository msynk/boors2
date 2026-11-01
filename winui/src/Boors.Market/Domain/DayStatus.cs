namespace Boors.Market.Domain;

/// <summary>
/// What a daily row actually means. Statistics use <see cref="Traded"/>-like days only; non-traded days get null
/// returns, never zero.
/// </summary>
public enum DayStatus
{
    /// <summary>Normal trading inside the daily band.</summary>
    Traded,

    /// <summary>Market open, symbol permitted, but nothing traded.</summary>
    NoTrade,

    /// <summary>Symbol suspended (cEtaval "IS" or a known halt) while the market was open.</summary>
    SymbolHalted,

    /// <summary>The whole market was closed; TSETMC still emits a flat, zero-volume row.</summary>
    MarketClosed,

    /// <summary>First print after a halt, or a day whose range broke the daily band: an unlimited-band auction.</summary>
    ReopenAuction,

    /// <summary>The whole day traded at the upper limit: buyers were queued and a buy order would most likely not fill.</summary>
    QueueLockedUp,

    /// <summary>The whole day traded at the lower limit: sellers were queued and a sell order would most likely not fill.</summary>
    QueueLockedDown,
}

public sealed record ClassifiedBar(DailyBar Bar, DayStatus Status)
{
    /// <summary>True when the day produced a real, comparable trading price.</summary>
    public bool HasPrice => Status is DayStatus.Traded or DayStatus.ReopenAuction or DayStatus.QueueLockedUp or DayStatus.QueueLockedDown;
}

/// <summary>
/// Classifies one symbol's daily bars. Inputs beyond the bars are optional so the classifier works on history alone;
/// when instrument-state or threshold data is available it is used in preference to inference.
/// </summary>
public sealed class DayStatusClassifier(MarketCalendar calendar, Func<DateOnly, decimal> priceLimit)
{
    /// <summary>
    /// Relative tolerance when comparing a price with the theoretical limit. TSETMC rounds the band to the tick
    /// (e.g. 4540 × 1.03 = 4676.2 is published as 4670), so an exact comparison would miss real limit days.
    /// </summary>
    public decimal LimitTolerance { get; init; } = 0.005m;

    public IReadOnlyList<ClassifiedBar> Classify(
        IEnumerable<DailyBar> bars,
        IReadOnlySet<DateOnly>? haltedDays = null,
        IReadOnlyDictionary<DateOnly, (decimal Low, decimal High)>? allowedRanges = null)
    {
        var ordered = bars.OrderBy(b => b.Date).ToList();
        var result = new List<ClassifiedBar>(ordered.Count);
        var previousOpenMarketDayWasHalted = false;

        foreach (var bar in ordered)
        {
            DayStatus status;

            if (calendar.IsMarketClosed(bar.Date))
            {
                status = DayStatus.MarketClosed;
            }
            else if (bar.TradeCount <= 0)
            {
                status = haltedDays?.Contains(bar.Date) == true ? DayStatus.SymbolHalted : DayStatus.NoTrade;
                previousOpenMarketDayWasHalted = status == DayStatus.SymbolHalted;
            }
            else
            {
                var band = allowedRanges is not null && allowedRanges.TryGetValue(bar.Date, out var range)
                    ? range
                    : TheoreticalBand(bar.Yesterday, priceLimit(bar.Date));

                if (previousOpenMarketDayWasHalted || BreaksBand(bar, band))
                    status = DayStatus.ReopenAuction;
                else if (bar.High == bar.Low && IsAtOrAbove(bar.Close, band.High) && bar.Close > bar.Yesterday)
                    status = DayStatus.QueueLockedUp;
                else if (bar.High == bar.Low && IsAtOrBelow(bar.Close, band.Low) && bar.Close < bar.Yesterday)
                    status = DayStatus.QueueLockedDown;
                else
                    status = DayStatus.Traded;

                previousOpenMarketDayWasHalted = false;
            }

            result.Add(new ClassifiedBar(bar, status));
        }

        return result;
    }

    private static (decimal Low, decimal High) TheoreticalBand(decimal yesterday, decimal limit) =>
        (yesterday * (1 - limit), yesterday * (1 + limit));

    private bool BreaksBand(DailyBar bar, (decimal Low, decimal High) band)
    {
        if (bar.Yesterday <= 0)
            return false;

        var slack = bar.Yesterday * LimitTolerance;
        return bar.High > band.High + slack || bar.Low < band.Low - slack;
    }

    private bool IsAtOrAbove(decimal price, decimal limit) => price >= limit - limit * LimitTolerance;

    private bool IsAtOrBelow(decimal price, decimal limit) => price <= limit + limit * LimitTolerance;
}
