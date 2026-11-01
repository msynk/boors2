namespace Boors.Market.Sources.Codal;

/// <summary>Well-known Codal LetterType ids (from search.codal.ir/api/search/v1/categories, Oct 2026).</summary>
public static class CodalLetterTypes
{
    /// <summary>Monthly activity report (گزارش فعالیت ماهانه), form ن-۳۰.</summary>
    public const int MonthlyActivity = 58;

    /// <summary>Interim and annual financial statements, form ن-۱۰. The management interpretive report is an attachment of these.</summary>
    public const int FinancialStatements = 6;

    /// <summary>Material disclosure (افشای اطلاعات بااهمیت), form ن-۲۰; group الف/ب is only in the title.</summary>
    public const int MaterialDisclosure = 11;

    /// <summary>Dividend (DPS) payment schedule, form ن-۱۳.</summary>
    public const int DividendSchedule = 30;

    public const int BoardActivityReport = 32;

    public const int PortfolioStatement = 8;

    public static readonly IReadOnlySet<int> AgmInvitations = new HashSet<int> { 16, 17, 18 };

    public static readonly IReadOnlySet<int> AgmDecisions = new HashSet<int> { 20, 21, 22 };

    /// <summary>Capital-increase chain, in the order the stages normally appear.</summary>
    public static readonly IReadOnlyList<int> CapitalIncreaseChain = [23, 24, 55, 80, 56, 45, 53, 57, 25, 27, 119, 28, 164];
}

public sealed record CodalLetterType(int Id, string Name);

public sealed record CodalCategory(int Code, string Name, IReadOnlyList<CodalLetterType> LetterTypes);

public sealed record CodalTaxonomy(IReadOnlyList<CodalCategory> Categories)
{
    public IReadOnlyDictionary<int, CodalLetterType> LetterTypesById { get; } = Categories
        .SelectMany(c => c.LetterTypes)
        .GroupBy(l => l.Id)
        .ToDictionary(g => g.Key, g => g.First());

    /// <summary>Category codes that list a letter type (a few types, e.g. 6, appear under more than one).</summary>
    public IReadOnlyList<int> CategoriesOf(int letterTypeId) =>
        [.. Categories.Where(c => c.LetterTypes.Any(l => l.Id == letterTypeId)).Select(c => c.Code)];
}

public sealed record CodalLetter
{
    public required long TracingNo { get; init; }
    public string? Symbol { get; init; }
    public string? CompanyName { get; init; }
    public required string Title { get; init; }

    /// <summary>Form code such as "ن-30" (digits normalized to ASCII).</summary>
    public string? LetterCode { get; init; }

    /// <summary>LetterType id parsed from the report URL's <c>let=</c> parameter.</summary>
    public int? LetterTypeId { get; init; }

    public DateTimeOffset? SentAt { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>"الف" or "ب" for material disclosures, parsed from the title; null otherwise.</summary>
    public string? DisclosureGroup { get; init; }

    public bool HasHtml { get; init; }
    public bool HasExcel { get; init; }
    public bool HasPdf { get; init; }
    public bool HasXbrl { get; init; }
    public bool HasAttachment { get; init; }
    public bool IsEstimate { get; init; }
    public int UnderSupervision { get; init; }

    public string? ReportUrl { get; init; }
    public string? ExcelUrl { get; init; }
    public string? PdfUrl { get; init; }
    public string? AttachmentUrl { get; init; }
}

public sealed record CodalSearchPage(int Total, int PageCount, IReadOnlyList<CodalLetter> Letters);

public sealed record CodalSearchQuery
{
    public string? Symbol { get; init; }
    public int LetterType { get; init; } = -1;
    public int Category { get; init; } = -1;
    public int PageNumber { get; init; } = 1;
}
