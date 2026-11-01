using Boors.Market.Rules;
using YamlDotNet.Core;

namespace Boors.Market.Tests;

[TestClass]
public sealed class MarketRulesTests
{
    private static readonly MarketRules Rules = MarketRules.LoadDefault();

    [TestMethod]
    [DataRow(InstrumentClass.TseStock, "2026-10-17", "0.03")]
    [DataRow(InstrumentClass.TseStock, "2010-01-01", "0.05")]
    [DataRow(InstrumentClass.FundLeveraged, "2026-10-17", "0.04")]
    [DataRow(InstrumentClass.FundGoldSilver, "2026-10-17", "0.10")]
    [DataRow(InstrumentClass.IfbBaseRed, "2026-10-17", "0.01")]
    [DataRow(InstrumentClass.Option, "2026-10-17", "0.03")] // no explicit entry -> default
    public void Price_limit_resolves_by_class_and_date(InstrumentClass instrumentClass, string date, string expected) =>
        Assert.AreEqual(decimal.Parse(expected, CultureInfo.InvariantCulture), Rules.GetPriceLimit(instrumentClass, DateOnly.Parse(date, CultureInfo.InvariantCulture)).Value);

    [TestMethod]
    public void Placeholder_era_is_flagged_low_confidence() =>
        Assert.AreEqual(RuleConfidence.Low, Rules.GetPriceLimit(InstrumentClass.TseStock, new DateOnly(2010, 1, 1)).Confidence);

    [TestMethod]
    public void Close_method_switches_to_vwap_on_main_markets_only()
    {
        Assert.AreEqual(CloseMethod.BaseVolumeWeighted, Rules.GetCloseMethod(InstrumentClass.TseStock, new DateOnly(2025, 12, 21)).Value);
        Assert.AreEqual(CloseMethod.Vwap, Rules.GetCloseMethod(InstrumentClass.TseStock, new DateOnly(2025, 12, 22)).Value);
        Assert.AreEqual(CloseMethod.BaseVolumeWeighted, Rules.GetCloseMethod(InstrumentClass.IfbBaseYellow, new DateOnly(2025, 9, 22)).Value);
        Assert.AreEqual(CloseMethod.DynamicBaseVolume, Rules.GetCloseMethod(InstrumentClass.IfbBaseYellow, new DateOnly(2025, 9, 23)).Value);
        Assert.AreEqual(CloseMethod.DynamicBaseVolume, Rules.GetCloseMethod(InstrumentClass.IfbBaseYellow, new DateOnly(2026, 10, 17)).Value);
    }

    [TestMethod]
    public void Sessions_add_tal_in_june_2026_and_extend_non_equity_etfs_in_mehr_1405()
    {
        var beforeTal = Rules.GetSession(InstrumentClass.TseStock, new DateOnly(2026, 6, 5)).Value;
        var withTal = Rules.GetSession(InstrumentClass.TseStock, new DateOnly(2026, 10, 17)).Value;
        var etfBefore = Rules.GetSession(InstrumentClass.EtfFixedIncome, new DateOnly(2026, 10, 9)).Value;
        var etfAfter = Rules.GetSession(InstrumentClass.EtfFixedIncome, new DateOnly(2026, 10, 10)).Value;

        Assert.IsNull(beforeTal.TalStart);
        Assert.AreEqual(new TimeOnly(12, 30), beforeTal.Close);
        Assert.AreEqual(new TimeOnly(12, 45), withTal.TalStart);
        Assert.AreEqual(new TimeOnly(13, 0), withTal.Close);
        Assert.AreEqual(new TimeOnly(13, 0), etfBefore.Close);
        Assert.AreEqual(new TimeOnly(18, 0), etfAfter.Close);
    }

    [TestMethod]
    public void Costs_give_list_and_discounted_round_trips()
    {
        var costs = Rules.GetCosts(new DateOnly(2026, 10, 17)).Value;

        Assert.AreEqual(0.01428m, costs.ListRoundTrip);
        Assert.AreEqual(0.0125m, costs.RoundTrip(0.00055m));
        Assert.AreEqual(costs.BuyCost(0.001m) + 0.005m, costs.SellCost(0.001m));
    }

    [TestMethod]
    public void Known_closures_cover_both_wars()
    {
        Assert.HasCount(2, Rules.KnownClosures);
        Assert.IsTrue(Rules.IsKnownClosure(new DateOnly(2026, 4, 1)));
        Assert.IsTrue(Rules.IsKnownClosure(new DateOnly(2025, 6, 20)));
        Assert.IsFalse(Rules.IsKnownClosure(new DateOnly(2026, 5, 19)));
        Assert.AreEqual(2, Rules.SettlementDays);
    }

    [TestMethod]
    public void A_later_default_overrides_an_earlier_class_entry()
    {
        var rules = MarketRules.Parse("""
            blocks:
              - from: 2020-01-01
                price_limit: { default: 0.05, tse_stock: 0.04 }
              - from: 2021-01-01
                price_limit: { default: 0.02 }
            """);

        Assert.AreEqual(0.04m, rules.GetPriceLimit(InstrumentClass.TseStock, new DateOnly(2020, 6, 1)).Value);
        Assert.AreEqual(0.02m, rules.GetPriceLimit(InstrumentClass.TseStock, new DateOnly(2021, 6, 1)).Value);
    }

    [TestMethod]
    public void Unknown_close_method_is_rejected() =>
        Assert.ThrowsExactly<FormatException>(() => MarketRules.Parse("""
            blocks:
              - from: 2020-01-01
                close_method: { default: magic }
            """));

    [TestMethod]
    public void Misspelled_keys_are_rejected() =>
        Assert.Throws<YamlException>(() => MarketRules.Parse("""
            blocks:
              - from: 2020-01-01
                price_limits: { default: 0.05 }
            """));
}
