using Boors.Market.Rules;
using Boors.Market.Sources.Tsetmc;

namespace Boors.Market.Tests;

[TestClass]
public sealed class DayStatusClassifierTests
{
    private static readonly DateOnly Day1 = new(2026, 10, 11);
    private static readonly DateOnly Day2 = new(2026, 10, 12);

    private static DayStatusClassifier Classifier(MarketCalendar? calendar = null) =>
        new(calendar ?? new MarketCalendar([]), _ => 0.03m);

    private static DailyBar Bar(DateOnly date, decimal yesterday, decimal low, decimal high, decimal close, long trades = 100) => new()
    {
        InsCode = 1,
        Date = date,
        Yesterday = yesterday,
        Open = low,
        Low = low,
        High = high,
        Close = close,
        Last = close,
        TradeCount = trades,
        Volume = trades * 1000,
        Value = trades * 1000 * close,
    };

    [TestMethod]
    [DataRow(1000, 1030, DayStatus.QueueLockedUp)]
    [DataRow(4540, 4670, DayStatus.QueueLockedUp)] // band published as 4670, not 4676.2 (tick rounding)
    [DataRow(4540, 4410, DayStatus.QueueLockedDown)]
    [DataRow(1000, 1010, DayStatus.Traded)] // one price all day, but inside the band
    public void Single_price_days_are_locked_only_at_the_band_edge(double yesterday, double price, DayStatus expected)
    {
        var bar = Bar(Day1, (decimal)yesterday, (decimal)price, (decimal)price, (decimal)price);

        Assert.AreEqual(expected, Classifier().Classify([bar])[0].Status);
    }

    [TestMethod]
    public void A_range_beyond_the_band_is_an_unlimited_reopen_auction() =>
        Assert.AreEqual(DayStatus.ReopenAuction, Classifier().Classify([Bar(Day1, 1000, 1100, 1200, 1150)])[0].Status);

    [TestMethod]
    public void First_trade_after_a_known_halt_is_a_reopen_auction_even_inside_the_band()
    {
        var result = Classifier().Classify(
            [Bar(Day1, 1000, 1000, 1000, 1000, trades: 0), Bar(Day2, 1000, 990, 1010, 1000)],
            haltedDays: new HashSet<DateOnly> { Day1 });

        Assert.AreEqual(DayStatus.SymbolHalted, result[0].Status);
        Assert.AreEqual(DayStatus.ReopenAuction, result[1].Status);
    }

    [TestMethod]
    public void Zero_trades_without_halt_information_is_no_trade() =>
        Assert.AreEqual(DayStatus.NoTrade, Classifier().Classify([Bar(Day1, 1000, 1000, 1000, 1000, trades: 0)])[0].Status);

    [TestMethod]
    public void Known_allowed_range_takes_precedence_over_the_theoretical_band()
    {
        var bar = Bar(Day1, 1000, 1020, 1020, 1020);
        var ranges = new Dictionary<DateOnly, (decimal Low, decimal High)> { [Day1] = (980, 1020) };

        Assert.AreEqual(DayStatus.QueueLockedUp, Classifier().Classify([bar], allowedRanges: ranges)[0].Status);
    }

    [TestMethod]
    public void Real_history_flags_the_2026_closure_but_not_the_symbols_own_suspension_afterwards()
    {
        var rules = MarketRules.LoadDefault();
        var index = TsetmcParser.ParseIndexHistory(Golden.Read("tsetmc/index-b2-tedpix.json"), TsetmcEndpoints.TedpixInsCode);
        var bars = TsetmcParser.ParseDailyHistory(Golden.Read("tsetmc/closing-price-daily-folad.json"), Golden.FooladInsCode);
        var classifier = new DayStatusClassifier(MarketCalendar.FromRules(rules, index), d => rules.GetPriceLimit(InstrumentClass.TseStock, d).Value);

        var byDate = classifier.Classify(bars).ToDictionary(c => c.Bar.Date, c => c.Status);

        Assert.IsTrue(byDate.Where(kv => kv.Key >= new DateOnly(2026, 2, 28) && kv.Key <= new DateOnly(2026, 5, 18)).All(kv => kv.Value == DayStatus.MarketClosed));
        // The market reopened on 19 May 2026, but فولاد stayed suspended: open market, zero trades.
        Assert.AreEqual(DayStatus.NoTrade, byDate[new DateOnly(2026, 5, 19)]);
        Assert.AreEqual(DayStatus.QueueLockedUp, byDate[new DateOnly(2026, 10, 13)]);
        Assert.AreEqual(DayStatus.QueueLockedUp, byDate[new DateOnly(2026, 10, 14)]);
    }
}
