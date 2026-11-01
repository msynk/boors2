using Boors.Market.Sources.Codal;

namespace Boors.Market.Tests;

[TestClass]
public sealed class CodalParserTests
{
    [TestMethod]
    public void Categories_build_letter_type_lookup_without_the_all_pseudo_category()
    {
        var taxonomy = CodalParser.ParseCategories(Golden.Read("codal/categories.json"));

        Assert.DoesNotContain(c => c.Code < 0, taxonomy.Categories);
        Assert.AreEqual("گزارش فعالیت ماهانه", taxonomy.LetterTypesById[CodalLetterTypes.MonthlyActivity].Name);
        Assert.AreSequenceEqual(new[] { 3 }, taxonomy.CategoriesOf(CodalLetterTypes.MonthlyActivity));
        Assert.IsTrue(taxonomy.LetterTypesById.ContainsKey(CodalLetterTypes.FinancialStatements));
        Assert.IsTrue(taxonomy.LetterTypesById.ContainsKey(CodalLetterTypes.MaterialDisclosure));
    }

    [TestMethod]
    public void Search_parses_letter_type_jalali_timestamps_and_absolute_urls()
    {
        var page = CodalParser.ParseSearch(Golden.Read("codal/search-folad.json"));
        var monthly = page.Letters[0];

        Assert.AreEqual(1471, page.Total);
        Assert.AreEqual(1605314L, monthly.TracingNo);
        Assert.AreEqual(CodalLetterTypes.MonthlyActivity, monthly.LetterTypeId);
        Assert.AreEqual("ن-30", monthly.LetterCode);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 29, 18, 55, 34, TimeSpan.FromHours(3.5)), monthly.PublishedAt);
        Assert.StartsWith("https://codal.ir/Reports/Decision.aspx?", monthly.ReportUrl!);
        Assert.StartsWith("https://codal.ir/Reports/DownloadFile.aspx?", monthly.PdfUrl!);
        Assert.StartsWith("https://excel.codal.ir/service/Excel/GetAll/", monthly.ExcelUrl!);
        Assert.IsTrue(monthly.HasExcel);
        Assert.IsFalse(monthly.HasXbrl);

        Assert.AreEqual(CodalLetterTypes.FinancialStatements, page.Letters.Single(l => l.LetterCode == "ن-10").LetterTypeId);
    }

    [TestMethod]
    [DataRow("افشای اطلاعات بااهمیت - (توقف تولید - گروه ب) منتهی به سال مالی ۱۴۰۵/۱۲/۲۹", "ب")]
    [DataRow("افشای اطلاعات بااهمیت - (افزایش سرمایه – گروه الف)", "الف")]
    [DataRow("افشای اطلاعات بااهمیت - (موضوع -گروه الف)", "الف")]
    [DataRow("گزارش فعالیت ماهانه دوره ۱ ماهه منتهی به ۱۴۰۵/۰۶/۳۱", null)]
    public void DisclosureGroup_is_parsed_from_the_title(string title, string? expected) =>
        Assert.AreEqual(expected, CodalParser.ParseDisclosureGroup(title));

    [TestMethod]
    public void LetterTypeId_is_read_from_the_report_url() =>
        Assert.AreEqual(58, CodalParser.ParseLetterTypeId("/Reports/Decision.aspx?LetterSerial=abc%3d&rt=0&let=58&ct=0&ft=-1"));
}
