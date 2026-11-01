using Boors.Market.Text;

namespace Boors.Market.Tests;

[TestClass]
public sealed class TextAndCodesTests
{
    [TestMethod]
    public void Normalize_fixes_arabic_letters_digits_and_padding() =>
        Assert.AreEqual("فولاد مبارکه 1405", PersianText.Normalize("  فولاد مباركه ۱۴۰۵  "));

    [TestMethod]
    public void ToArabicLetters_produces_tsetmc_internal_spelling() =>
        Assert.AreEqual("كرمان", PersianText.ToArabicLetters("کرمان"));

    [TestMethod]
    public void MatchKey_ignores_zwnj_and_spaces() =>
        Assert.AreEqual(PersianText.MatchKey("سیمان آبیک"), PersianText.MatchKey("سيمان‌آبيك"));

    [TestMethod]
    [DataRow("۱۴۰۵/۰۷/۰۷ ۱۸:۵۵:۳۴", 2026, 9, 29, 18, 55, 34)]
    [DataRow("1405/01/01", 2026, 3, 21, 0, 0, 0)]
    [DataRow("1405/07/18", 2026, 10, 10, 0, 0, 0)]
    public void ParseJalali_returns_iran_local_time(string jalali, int year, int month, int day, int hour, int minute, int second) =>
        Assert.AreEqual(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.FromHours(3.5)), PersianText.ParseJalali(jalali));

    [TestMethod]
    public void ParseJalali_rejects_garbage() => Assert.IsNull(PersianText.ParseJalali("not a date"));

    [TestMethod]
    public void DEven_converts_dates_and_times()
    {
        Assert.AreEqual(new DateOnly(2026, 10, 14), DEven.ToDate(20261014));
        Assert.AreEqual(20261014, DEven.FromDate(new DateOnly(2026, 10, 14)));
        Assert.AreEqual(new TimeOnly(9, 15, 30), DEven.ToTime(91530));
        Assert.IsNull(DEven.ToTime(0));
        Assert.IsNull(DEven.ToDateOrNull(0));
    }

    [TestMethod]
    [DataRow(300, 1, "IRO1FOLD0001", InstrumentClass.TseStock)]
    [DataRow(303, 2, "IRO3KRMZ0001", InstrumentClass.IfbStock)]
    [DataRow(305, 1, "IRT1BLOT0001", InstrumentClass.EtfEquity)]
    [DataRow(320, 3, "IROAZGDZ0281", InstrumentClass.Option)]
    [DataRow(208, 3, "IRB6AF130811", InstrumentClass.Debt)]
    [DataRow(400, 1, "IRR1DAML0101", InstrumentClass.Rights)]
    public void Verified_yval_codes_classify(int yVal, int flow, string isin, InstrumentClass expected) =>
        Assert.AreEqual(expected, InstrumentClassifier.Classify(yVal, flow, isin));

    [TestMethod]
    [DataRow("IRO1FOLD0001", InstrumentClass.TseStock)]
    [DataRow("IRO3SAIZ0001", InstrumentClass.IfbStock)]
    [DataRow("IRB3TR000001", InstrumentClass.Debt)]
    [DataRow("IRO9ABCD0001", InstrumentClass.Option)]
    [DataRow("XX", InstrumentClass.Other)]
    public void Isin_prefix_is_the_fallback(string isin, InstrumentClass expected) =>
        Assert.AreEqual(expected, InstrumentClassifier.Classify(null, null, isin));
}
