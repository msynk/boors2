using Boors.Market.Analytics;
using Boors.Market.Sources.Tsetmc;

namespace Boors.Market.Tests;

[TestClass]
public sealed class PointInTimeTests
{
    private static DailyBar Bar(int day, decimal close, decimal yesterday, long trades = 10) => new()
    {
        InsCode = 1,
        Date = new DateOnly(2026, 1, day),
        Close = close,
        Yesterday = yesterday,
        TradeCount = trades,
        Open = close,
        High = close,
        Low = close,
        Last = close,
    };

    // 100 → 110, then a 2-for-1 style event (base 55 on day 3) → 60.
    private static readonly DailyBar[] Bars = [Bar(1, 100, 100), Bar(2, 110, 100), Bar(3, 60, 55), Bar(4, 63, 60)];

    [TestMethod]
    public void Daily_return_uses_the_exchange_base_price_and_is_null_without_a_price()
    {
        Assert.AreEqual(0.1m, PointInTime.DailyReturn(Bars[1], DayStatus.Traded));
        Assert.AreEqual(60m / 55m - 1, PointInTime.DailyReturn(Bars[2], DayStatus.Traded));
        Assert.IsNull(PointInTime.DailyReturn(Bar(5, 63, 63, trades: 0), DayStatus.NoTrade));
        Assert.IsNull(PointInTime.DailyReturn(Bars[1], DayStatus.MarketClosed));
    }

    [TestMethod]
    public void Implied_adjustments_come_from_base_price_breaks()
    {
        var implied = PointInTime.ImpliedAdjustments(Bars);

        var single = implied.Single();
        Assert.AreEqual(new DateOnly(2026, 1, 3), single.Date);
        Assert.AreEqual(0.5m, single.Factor);
    }

    [TestMethod]
    public void Adjustment_is_point_in_time()
    {
        var withStatus = Bars.Select(b => (b, (DayStatus?)DayStatus.Traded)).ToList();

        var before = PointInTime.AdjustFromBars(withStatus, new DateOnly(2026, 1, 2));
        var after = PointInTime.AdjustFromBars(withStatus, new DateOnly(2026, 1, 4));

        // As of day 2 the event is unknown: prices are raw.
        Assert.AreEqual(110m, before[^1].AdjustedClose);
        // As of day 4 everything before day 3 is halved, and the path matches compounded returns.
        Assert.AreEqual(55m, after[1].AdjustedClose);
        Assert.AreEqual(63m, after[^1].AdjustedClose);
        var compounded = after.Skip(1).Aggregate(1m, (acc, b) => acc * (1 + b.Return!.Value));
        Assert.AreEqual(after[^1].AdjustedClose / after[0].AdjustedClose, compounded);
    }

    [TestMethod]
    public void Real_history_finds_the_capital_increase_during_the_2026_closure()
    {
        var bars = TsetmcParser.ParseDailyHistory(Golden.Read("tsetmc/closing-price-daily-folad.json"), Golden.FooladInsCode);

        var implied = PointInTime.ImpliedAdjustments(bars).Single(a => a.Date == new DateOnly(2026, 3, 30));

        // Matches GetInstrumentShareChange: 1.5T → 1.935T shares on 2026-03-30 (absent from GetPriceAdjustList).
        Assert.AreEqual(2604m, implied.AdjustedClose);
        Assert.AreEqual(3359m, implied.UnadjustedClose);
    }
}
