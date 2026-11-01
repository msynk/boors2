using Boors.Market.Domain;

namespace Boors.Market.Analytics;

/// <summary>A daily bar with its point-in-time adjusted close and its return.</summary>
public sealed record AdjustedBar(DateOnly Date, DayStatus? Status, decimal Close, decimal AdjustedClose, decimal? Return);

/// <summary>
/// Point-in-time price handling. Raw prices are stored unchanged; adjusted series are built on demand "as of" a
/// date using only corporate actions known by then, so backtests never see future capital increases or dividends.
/// </summary>
public static class PointInTime
{
    /// <summary>
    /// Daily return of a day with a real price: <c>Close / Yesterday − 1</c>. TSETMC publishes <c>priceYesterday</c> as
    /// the exchange's base price for the day, which is already adjusted for a capital increase or dividend on that day,
    /// and after a halt or closure it is the last close. So this return is corporate-action-correct and carries the
    /// whole move across non-traded gaps. Days without a real price get null, never zero.
    /// </summary>
    public static decimal? DailyReturn(DailyBar bar, DayStatus? status)
    {
        var priced = status is null or DayStatus.Traded or DayStatus.ReopenAuction or DayStatus.QueueLockedUp or DayStatus.QueueLockedDown;
        return priced && bar.TradeCount > 0 && bar.Yesterday > 0 ? bar.Close / bar.Yesterday - 1 : null;
    }

    /// <summary>
    /// Adjusted closes as of <paramref name="asOf"/>: every close before an adjustment event dated on or before
    /// <paramref name="asOf"/> is multiplied by that event's factor (adjusted ÷ unadjusted close). Events after
    /// <paramref name="asOf"/> are ignored, and bars after <paramref name="asOf"/> are dropped.
    /// </summary>
    public static IReadOnlyList<AdjustedBar> Adjust(IEnumerable<(DailyBar Bar, DayStatus? Status)> bars, IEnumerable<PriceAdjustment> adjustments, DateOnly asOf)
    {
        var ordered = bars.Where(b => b.Bar.Date <= asOf).OrderBy(b => b.Bar.Date).ToList();
        var events = adjustments.Where(a => a.Date <= asOf && a.UnadjustedClose > 0).OrderByDescending(a => a.Date).ToList();

        var result = new AdjustedBar[ordered.Count];
        var factor = 1m;
        var nextEvent = 0;
        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            var (bar, status) = ordered[i];

            // An event dated D adjusts every price strictly before D.
            while (nextEvent < events.Count && events[nextEvent].Date > bar.Date)
                factor *= events[nextEvent++].Factor;

            result[i] = new AdjustedBar(bar.Date, status, bar.Close, bar.Close * factor, DailyReturn(bar, status));
        }

        return result;
    }

    /// <summary>
    /// Adjusted closes as of <paramref name="asOf"/> using the factors implied by the bars (see <see cref="ImpliedAdjustments"/>).
    /// This is the default: TSETMC's published adjustment list misses most capital increases since 2020 and dates
    /// events by meeting date rather than the day the new base price took effect (checked on فولاد, 2026-10-17).
    /// </summary>
    public static IReadOnlyList<AdjustedBar> AdjustFromBars(IReadOnlyCollection<(DailyBar Bar, DayStatus? Status)> bars, DateOnly asOf) =>
        Adjust(bars, ImpliedAdjustments(bars.Select(b => b.Bar)), asOf);

    /// <summary>
    /// Adjustment factors implied by the bars themselves: on a day where <c>Yesterday</c> differs from the previous
    /// row's close, factor = Yesterday ÷ previous close. Most real events fall on days without trades (the new base
    /// price takes effect while the symbol is still halted after its general meeting), so no trade filter is applied.
    /// Only meaningful for primary listings: TAL/block siblings roll their reference price every day and are excluded
    /// upstream (see <see cref="Instrument.ParentInsCode"/>).
    /// </summary>
    public static IReadOnlyList<PriceAdjustment> ImpliedAdjustments(IEnumerable<DailyBar> bars, decimal tolerance = 0.0005m)
    {
        var ordered = bars.OrderBy(b => b.Date).ToList();
        var implied = new List<PriceAdjustment>();
        for (var i = 1; i < ordered.Count; i++)
        {
            var previousClose = ordered[i - 1].Close;
            var yesterday = ordered[i].Yesterday;
            if (previousClose > 0 && yesterday > 0 && Math.Abs(yesterday / previousClose - 1) > tolerance)
            {
                implied.Add(new PriceAdjustment
                {
                    InsCode = ordered[i].InsCode,
                    Date = ordered[i].Date,
                    AdjustedClose = yesterday,
                    UnadjustedClose = previousClose,
                });
            }
        }

        return implied;
    }
}
