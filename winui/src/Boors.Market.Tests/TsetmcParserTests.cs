using System.Text;
using Boors.Market.Sources.Tsetmc;

namespace Boors.Market.Tests;

[TestClass]
public sealed class TsetmcParserTests
{
    [TestMethod]
    public void DailyHistory_is_sorted_oldest_first_and_maps_closing_price_fields()
    {
        var bars = TsetmcParser.ParseDailyHistory(Golden.Read("tsetmc/closing-price-daily-folad.json"), Golden.FooladInsCode);

        Assert.HasCount(97, bars);
        Assert.AreEqual(new DateOnly(2007, 3, 11), bars[0].Date);
        Assert.AreSequenceEqual(bars.OrderBy(b => b.Date), bars);

        var last = bars[^1];
        Assert.AreEqual(new DateOnly(2026, 10, 14), last.Date);
        Assert.AreEqual(Golden.FooladInsCode, last.InsCode);
        Assert.AreEqual(4540m, last.Close);
        Assert.AreEqual(4410m, last.Yesterday);
        Assert.AreEqual(4540m, last.Low);
        Assert.AreEqual(4540m, last.High);
        Assert.AreEqual(13401L, last.TradeCount);
        Assert.AreEqual(1563050927L, last.Volume);
        Assert.AreEqual(7096251208580m, last.Value);
        Assert.AreEqual(new TimeOnly(12, 29, 54), last.LastTradeTime);
    }

    [TestMethod]
    public void InstrumentsHistoryInDay_dates_every_row_with_the_requested_day()
    {
        var day = new DateOnly(2026, 10, 21);
        var bars = TsetmcParser.ParseInstrumentsHistoryInDay(Golden.Read("tsetmc/instruments-history-in-day-sample.json"), day);

        Assert.HasCount(4, bars);
        Assert.IsTrue(bars.All(b => b.Date == day && b.InsCode > 0 && b.LastTradeTime is null));
        var folad = bars.Single(b => b.InsCode == Golden.FooladInsCode);
        Assert.AreEqual(4340m, folad.Open);
        Assert.AreEqual(4210m, folad.Low);
        Assert.AreEqual(4220m, folad.Close);
        Assert.AreEqual(4230m, folad.Last);
        Assert.AreEqual(4340m, folad.Yesterday);
        Assert.AreEqual(48279L, folad.TradeCount);
        Assert.AreEqual(4864252101L, folad.Volume);
        Assert.AreEqual(20519267355350m, folad.Value);
    }

    [TestMethod]
    public void DailyHistory_contains_flat_zero_trade_rows_during_the_2026_closure()
    {
        var bars = TsetmcParser.ParseDailyHistory(Golden.Read("tsetmc/closing-price-daily-folad.json"), Golden.FooladInsCode);
        var closure = bars.Where(b => b.Date >= new DateOnly(2026, 3, 2) && b.Date <= new DateOnly(2026, 5, 18)).ToList();

        Assert.IsNotEmpty(closure);
        Assert.IsTrue(closure.All(b => b.TradeCount == 0 && b.Volume == 0));
    }

    [TestMethod]
    public void ClientTypeHistory_maps_individual_and_institutional_flows()
    {
        var days = TsetmcParser.ParseClientTypeHistory(Golden.Read("tsetmc/client-type-history-folad.json"), Golden.FooladInsCode);
        var day = days.Single(d => d.Date == new DateOnly(2026, 10, 14));

        Assert.AreEqual(1091991421L, day.BuyIndividualVolume);
        Assert.AreEqual(1517, day.BuyIndividualCount);
        Assert.AreEqual(2887, day.SellIndividualCount);
        Assert.AreEqual(91, day.BuyInstitutionalCount);
        Assert.AreEqual(88499622860m, day.IndividualNetValue);
        Assert.AreEqual(4957641051340m / 1517, day.PerCapitaBuy);
        Assert.IsNotNull(day.BuyerPower);
    }

    [TestMethod]
    public void PriceAdjustments_read_numeric_inscode_beyond_double_precision()
    {
        var adjustments = TsetmcParser.ParsePriceAdjustments(Golden.Read("tsetmc/price-adjust-folad.json"), 0);
        var latest = adjustments[^1];

        // insCode is sent as a JSON number larger than 2^53; it must survive exactly.
        Assert.AreEqual(Golden.FooladInsCode, latest.InsCode);
        Assert.AreEqual(new DateOnly(2026, 8, 3), latest.Date);
        Assert.AreEqual(2549m, latest.AdjustedClose);
        Assert.AreEqual(2604m, latest.UnadjustedClose);
        Assert.AreEqual(2549m / 2604m, latest.Factor);
    }

    [TestMethod]
    public void ShareChanges_map_old_and_new_share_counts()
    {
        var changes = TsetmcParser.ParseShareChanges(Golden.Read("tsetmc/share-change-folad.json"), Golden.FooladInsCode);
        var latest = changes[^1];

        Assert.AreEqual(new DateOnly(2026, 3, 30), latest.Date);
        Assert.AreEqual(1_500_000_000_000m, latest.SharesBefore);
        Assert.AreEqual(1_935_000_000_000m, latest.SharesAfter);
    }

    [TestMethod]
    public void ClosingPriceInfo_uses_the_requested_inscode_and_trims_state()
    {
        var snapshot = TsetmcParser.ParseClosingPriceInfo(Golden.Read("tsetmc/closing-price-info-folad.json"), Golden.FooladInsCode);

        Assert.AreEqual(Golden.FooladInsCode, snapshot.InsCode);
        Assert.AreEqual(new DateOnly(2026, 10, 17), snapshot.Date);
        Assert.AreEqual(4410m, snapshot.Close);
        Assert.AreEqual(4540m, snapshot.Yesterday);
        Assert.AreEqual(17246L, snapshot.TradeCount);
        Assert.AreEqual("A", snapshot.State);
        Assert.IsFalse(snapshot.IsHalted);
    }

    [TestMethod]
    public void InstrumentInfo_maps_codes_normalizes_persian_and_classifies()
    {
        var bytes = Golden.Read("tsetmc/instrument-info-folad.json");
        var instrument = TsetmcParser.ParseInstrumentInfo(bytes, Golden.FooladInsCode);

        Assert.AreEqual("فولاد", instrument.Symbol);
        Assert.AreEqual("فولاد مبارکه اصفهان", instrument.Name); // Arabic ك normalized to Persian ک
        Assert.AreEqual(300, instrument.YVal);
        Assert.AreEqual(1, instrument.Flow);
        Assert.AreEqual("N1", instrument.Board);
        Assert.AreEqual("27", instrument.SectorCode);
        Assert.AreEqual("IRO1FOLD0001", instrument.Isin);
        Assert.AreEqual("IRO1FOLD0009", instrument.CompanyIsin);
        Assert.AreEqual(1_935_000_000_000m, instrument.TotalShares);
        Assert.AreEqual(36m, instrument.FreeFloatPercent);
        Assert.AreEqual(InstrumentClass.TseStock, instrument.Class);
        Assert.AreEqual((4410m, 4670m), TsetmcParser.ParseStaticThreshold(bytes));
    }

    [TestMethod]
    [DataRow("zob", 71483646978964608L, 300, 1, "N2", InstrumentClass.TseStock)]
    [DataRow("kerman", 50792786683910016L, 303, 2, "Z1", InstrumentClass.IfbStock)]
    [DataRow("balout-etf", 4769396408895066L, 305, 1, "H1", InstrumentClass.EtfEquity)]
    [DataRow("option", 33107199324373968L, 320, 3, "13", InstrumentClass.Option)]
    [DataRow("sukuk", 6503819370387370L, 208, 3, "N4", InstrumentClass.Debt)]
    [DataRow("rights", 21533807502850906L, 400, 1, "N2", InstrumentClass.Rights)]
    public void InstrumentInfo_classifies_every_verified_instrument_type(string file, long insCode, int yVal, int flow, string board, InstrumentClass expected)
    {
        var instrument = TsetmcParser.ParseInstrumentInfo(Golden.Read($"tsetmc/instrument-info-{file}.json"), insCode);

        Assert.AreEqual(insCode, instrument.InsCode);
        Assert.AreEqual(yVal, instrument.YVal);
        Assert.AreEqual(flow, instrument.Flow);
        Assert.AreEqual(board, instrument.Board);
        Assert.AreEqual(expected, instrument.Class);
    }

    [TestMethod]
    public void InstrumentIdentity_is_accepted_and_falls_back_to_requested_inscode()
    {
        var instrument = TsetmcParser.ParseInstrumentInfo(Golden.Read("tsetmc/instrument-identity-folad.json"), Golden.FooladInsCode);

        Assert.AreEqual(Golden.FooladInsCode, instrument.InsCode);
        Assert.AreEqual(2710, instrument.SubSectorCode);
        Assert.IsNull(instrument.Flow);
        Assert.AreEqual(InstrumentClass.TseStock, instrument.Class);
    }

    [TestMethod]
    public void InstrumentSearch_marks_active_rows_and_collects_alternate_codes()
    {
        var hits = TsetmcParser.ParseInstrumentSearch(Golden.Read("tsetmc/instrument-search-folad.json"));
        var folad = hits.Single(h => h.Symbol == "فولاد");

        Assert.AreEqual(Golden.FooladInsCode, folad.InsCode);
        Assert.IsTrue(folad.IsActive);
        Assert.HasCount(3, folad.AlternateInsCodes);
        Assert.Contains(61934586397353104L, folad.AlternateInsCodes);
    }

    [TestMethod]
    public void BestLimits_map_bid_and_ask_sides()
    {
        var levels = TsetmcParser.ParseBestLimits(Golden.Read("tsetmc/best-limits-folad.json"));

        Assert.HasCount(5, levels);
        Assert.AreEqual(1, levels[0].Level);
        Assert.AreEqual(4400m, levels[0].BidPrice);
        Assert.AreEqual(4410m, levels[0].AskPrice);
        Assert.AreEqual(2232275616L, levels[0].AskVolume);
        Assert.AreEqual(9999, levels[0].AskOrders);
    }

    [TestMethod]
    public void IndexHistory_maps_close_low_high()
    {
        var bars = TsetmcParser.ParseIndexHistory(Golden.Read("tsetmc/index-b2-tedpix.json"), TsetmcEndpoints.TedpixInsCode);
        var day = bars.Single(b => b.Date == new DateOnly(2026, 3, 2));

        Assert.AreEqual(3713955.9m, day.Close);
        Assert.AreEqual(3713950m, day.Low);
        Assert.AreEqual(3713950m, day.High);
    }

    [TestMethod]
    public void StaticData_trims_fixed_width_padding()
    {
        var items = TsetmcParser.ParseStaticData(Golden.Read("tsetmc/static-data.json"));
        var shares = items.Single(i => i.Type == "PaperType" && i.Code == 1);

        Assert.AreEqual("سهام", shares.Name);
        Assert.HasCount(9, items.Where(i => i.Type == "PaperType").ToList());
    }

    [TestMethod]
    public void MarketWatch_maps_verified_compact_keys_and_best_limits()
    {
        var snapshot = TsetmcParser.ParseMarketWatch(Golden.Read("tsetmc/market-watch-sample.json"));
        var folad = snapshot.Rows.Single(r => r.InsCode == Golden.FooladInsCode);

        Assert.HasCount(4, snapshot.Rows);
        Assert.AreEqual("فولاد", folad.Symbol);
        Assert.AreEqual(4410m, folad.Close);
        Assert.AreEqual(4540m, folad.Yesterday);
        Assert.AreEqual(4670m, folad.AllowedHigh);
        Assert.AreEqual(4410m, folad.AllowedLow);
        Assert.AreEqual(8.5m, folad.PriceEarnings);
        Assert.AreEqual(518m, folad.Eps);
        Assert.HasCount(5, folad.BestLimits);
        Assert.AreEqual(InstrumentClass.TseStock, folad.Class);
        Assert.AreEqual(snapshot.Rows.Max(r => r.HEven), snapshot.MaxHEven);
    }

    [TestMethod]
    public void MarketWatch_reads_placeholder_pe_as_null()
    {
        const string json = """{"marketwatch":[{"insCode":"1","lva":"x","pe":"-","eps":null,"pcl":10.0,"blDs":[]}]}""";

        var row = TsetmcParser.ParseMarketWatch(Encoding.UTF8.GetBytes(json)).Rows.Single();

        Assert.IsNull(row.PriceEarnings);
        Assert.IsNull(row.Eps);
        Assert.AreEqual(10m, row.Close);
    }
}
