using System.Text;
using Boors.Market.Sources.Funds;
using Boors.Market.Sources.Macro;
using Boors.Market.Sources.Tsetmc;

namespace Boors.Market.Tests;

[TestClass]
public sealed class Phase1ParserTests
{
    [TestMethod]
    public void ClientTypeAll_maps_volumes_and_counts_and_estimates_values_from_average_price()
    {
        var snapshots = TsetmcParser.ParseClientTypeAll(Golden.Read("tsetmc/client-type-all-sample.json"));
        var folad = snapshots.Single(s => s.InsCode == Golden.FooladInsCode);

        Assert.HasCount(31, snapshots);
        Assert.IsGreaterThan(0, folad.BuyIndividualCount);

        var day = folad.ToEstimatedDay(new DateOnly(2026, 10, 17), 4410m);
        Assert.IsTrue(day.ValuesEstimated);
        Assert.AreEqual(folad.BuyIndividualVolume * 4410m, day.BuyIndividualValue);
    }

    [TestMethod]
    public void IndexSnapshots_map_value_high_low_and_names()
    {
        var indices = TsetmcParser.ParseIndexSnapshots(Golden.Read("tsetmc/index-b1-last-all.json"));
        var steel = indices.Single(i => i.InsCode == 32453344048876642);

        Assert.HasCount(57, indices);
        Assert.AreEqual("27-فلزات اساسی", steel.Name); // Arabic ي normalized
        Assert.IsGreaterThanOrEqualTo(steel.Low, steel.Value);
        Assert.IsLessThanOrEqualTo(steel.High, steel.Value);
    }

    [TestMethod]
    public void Messages_are_classified_by_kind_reason_and_limit()
    {
        var messages = TsetmcParser.ParseMessages(Golden.Read("tsetmc/messages-folad-sample.json")).ToDictionary(m => m.Id);

        var disclosureHalt = messages[260551];
        Assert.AreEqual(MessageKind.Halt, disclosureHalt.Kind);
        Assert.AreEqual(HaltReason.Disclosure, disclosureHalt.Reason);
        Assert.Contains("فولاد1", disclosureHalt.MentionedSymbols);

        Assert.AreEqual(HaltReason.Underlying, messages[251499].Reason);            // futures halted because the underlying was
        Assert.AreEqual(MessageKind.Halt, messages[255362].Kind);                   // تعلیق = suspension
        Assert.AreEqual(HaltReason.Supervisory, messages[255362].Reason);
        Assert.AreEqual(MessageKind.Reopen, messages[259792].Kind);                 // بازگشایی ... پس از رفع تعلیق
        Assert.IsTrue(messages[259792].ReopensWithoutLimit);
        Assert.AreEqual(MessageKind.Reopen, messages[225554].Kind);                 // حراج مجدد
        Assert.AreEqual(MessageKind.Other, messages[262429].Kind);
        Assert.HasCount(6, messages[260679].MentionedSymbols);                     // (سپ)،(ولپارس)،(فولاد)،...
    }

    [TestMethod]
    public void Shareholders_use_the_snapshot_date_when_the_source_sends_zero()
    {
        var holders = TsetmcParser.ParseShareholders(Golden.Read("tsetmc/shareholders-folad.json"), Golden.FooladInsCode, new DateOnly(2026, 10, 17));

        Assert.HasCount(12, holders);
        Assert.IsTrue(holders.All(h => h.SnapshotDate == new DateOnly(2026, 10, 17)));
        Assert.AreEqual(16.709m, holders.Max(h => h.Percent));
    }

    [TestMethod]
    public void InstrumentState_reads_an_object_and_treats_an_empty_array_as_unknown()
    {
        var halted = TsetmcParser.ParseInstrumentState(Golden.Read("tsetmc/instrument-state-halted.json"), Golden.FooladInsCode, new DateOnly(2026, 5, 19));
        var unknown = TsetmcParser.ParseInstrumentState(Encoding.UTF8.GetBytes("""{"instrumentState":[]}"""), 1, new DateOnly(2008, 12, 6));

        Assert.AreEqual("IS", halted.State);
        Assert.IsTrue(halted.IsHalted);
        Assert.IsNull(unknown.State);
        Assert.IsFalse(unknown.IsHalted);
    }

    [TestMethod]
    public void Tgju_history_reads_open_low_high_close_and_strips_html()
    {
        var bars = TgjuClient.ParseHistory(Golden.Read("tgju/price-dollar-rl-sample.json"), TgjuSeries.UsdFreeMarket);
        var last = bars[^1];

        Assert.HasCount(43, bars);
        Assert.AreEqual(new DateOnly(2011, 11, 26), bars[0].Date);
        Assert.AreEqual(new DateOnly(2026, 10, 15), last.Date);
        Assert.AreEqual(2_674_050m, last.Open);
        Assert.AreEqual(2_669_000m, last.Low);
        Assert.AreEqual(2_679_000m, last.High);
        Assert.AreEqual(2_679_000m, last.Close);
    }

    [TestMethod]
    public void Fipiran_funds_map_types_classes_and_tolerate_placeholder_dates()
    {
        var funds = FipiranClient.ParseFundCompare(Golden.Read("fipiran/fund-compare-sample.json"));
        var leveraged = funds.Single(f => f.RegNo == "12288");
        var unknownStart = funds.Single(f => f.RegNo == "11276");

        Assert.AreEqual(35831329532779850L, leveraged.InsCode);
        Assert.AreEqual(22, leveraged.FundType);
        Assert.IsTrue(leveraged.IsExchangeTraded);
        Assert.AreEqual(InstrumentClass.FundLeveraged, leveraged.ImpliedClass);
        Assert.AreEqual(new DateOnly(2026, 10, 12), leveraged.Date);
        Assert.IsNull(unknownStart.InitiationDate);
        Assert.AreEqual(InstrumentClass.EtfFixedIncome, funds.First(f => f.FundType == 4).ImpliedClass);
    }
}
