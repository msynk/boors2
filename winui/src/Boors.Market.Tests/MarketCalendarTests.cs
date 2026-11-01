using Boors.Market.Sources.Tsetmc;

namespace Boors.Market.Tests;

[TestClass]
public sealed class MarketCalendarTests
{
    [TestMethod]
    public void Flat_index_rows_identify_both_war_closures()
    {
        var index = TsetmcParser.ParseIndexHistory(Golden.Read("tsetmc/index-b2-tedpix.json"), TsetmcEndpoints.TedpixInsCode);

        var flat = MarketCalendar.DetectFlatIndexDays(index);

        Assert.Contains(new DateOnly(2025, 6, 15), flat);
        Assert.Contains(new DateOnly(2025, 6, 25), flat);
        Assert.Contains(new DateOnly(2026, 2, 28), flat);
        Assert.Contains(new DateOnly(2026, 5, 18), flat);
        Assert.DoesNotContain(new DateOnly(2025, 6, 28), flat); // reopening day: the index fell 2.1%
        Assert.DoesNotContain(new DateOnly(2026, 5, 19), flat);
    }

    [TestMethod]
    public void Calendar_combines_seeded_and_detected_closures()
    {
        var calendar = new MarketCalendar(
            [new Rules.ClosureRange(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 3), "test")],
            [new DateOnly(2018, 7, 26)]);

        Assert.IsTrue(calendar.IsMarketClosed(new DateOnly(2026, 3, 2)));
        Assert.IsTrue(calendar.IsMarketClosed(new DateOnly(2018, 7, 26)));
        Assert.IsFalse(calendar.IsMarketClosed(new DateOnly(2026, 3, 4)));
    }
}
