using System.Text.RegularExpressions;
using Boors.Market.Text;

namespace Boors.Market.Sources.Codal;

internal sealed record CodalCategoryDto(int Code, string? Name, List<CodalPublisherTypeDto>? PublisherTypes);

internal sealed record CodalPublisherTypeDto(int Code, string? Name, List<CodalLetterTypeDto>? LetterTypes);

internal sealed record CodalLetterTypeDto(int Id, string? Name);

internal sealed record CodalSearchDto(int Total, int Page, List<CodalLetterDto>? Letters);

internal sealed record CodalLetterDto
{
    public long TracingNo { get; init; }
    public string? Symbol { get; init; }
    public string? CompanyName { get; init; }
    public int UnderSupervision { get; init; }
    public string? Title { get; init; }
    public string? LetterCode { get; init; }
    public string? SentDateTime { get; init; }
    public string? PublishDateTime { get; init; }
    public bool HasHtml { get; init; }
    public bool IsEstimate { get; init; }
    public string? Url { get; init; }
    public bool HasExcel { get; init; }
    public bool HasPdf { get; init; }
    public bool HasXbrl { get; init; }
    public bool HasAttachment { get; init; }
    public string? AttachmentUrl { get; init; }
    public string? PdfUrl { get; init; }
    public string? ExcelUrl { get; init; }
}

// Codal's JSON uses PascalCase, so no naming policy.
[JsonSerializable(typeof(List<CodalCategoryDto>))]
[JsonSerializable(typeof(CodalSearchDto))]
internal sealed partial class CodalJsonContext : JsonSerializerContext;

/// <summary>Pure functions from Codal JSON bodies to domain records.</summary>
public static partial class CodalParser
{
    public static CodalTaxonomy ParseCategories(ReadOnlySpan<byte> json)
    {
        var categories = JsonSerializer.Deserialize(json, CodalJsonContext.Default.ListCodalCategoryDto) ?? [];
        return new CodalTaxonomy(categories
            .Where(c => c.Code >= 0) // -1 is the "all" pseudo-category
            .Select(c => new CodalCategory(
                c.Code,
                PersianText.Normalize(c.Name) ?? string.Empty,
                (c.PublisherTypes ?? [])
                    .SelectMany(p => p.LetterTypes ?? [])
                    .GroupBy(l => l.Id)
                    .Select(g => new CodalLetterType(g.Key, PersianText.Normalize(g.First().Name) ?? string.Empty))
                    .OrderBy(l => l.Id)
                    .ToList()))
            .ToList());
    }

    public static CodalSearchPage ParseSearch(ReadOnlySpan<byte> json, string codalBaseUrl = "https://codal.ir/")
    {
        var dto = JsonSerializer.Deserialize(json, CodalJsonContext.Default.CodalSearchDto)
            ?? throw new FormatException("Codal search response is empty.");

        var letters = (dto.Letters ?? [])
            .Select(l =>
            {
                var title = PersianText.Normalize(l.Title) ?? string.Empty;
                return new CodalLetter
                {
                    TracingNo = l.TracingNo,
                    Symbol = PersianText.Normalize(l.Symbol),
                    CompanyName = PersianText.Normalize(l.CompanyName),
                    Title = title,
                    LetterCode = PersianText.Normalize(l.LetterCode),
                    LetterTypeId = ParseLetterTypeId(l.Url),
                    SentAt = PersianText.ParseJalali(l.SentDateTime),
                    PublishedAt = PersianText.ParseJalali(l.PublishDateTime),
                    DisclosureGroup = ParseDisclosureGroup(title),
                    HasHtml = l.HasHtml,
                    HasExcel = l.HasExcel,
                    HasPdf = l.HasPdf,
                    HasXbrl = l.HasXbrl,
                    HasAttachment = l.HasAttachment,
                    IsEstimate = l.IsEstimate,
                    UnderSupervision = l.UnderSupervision,
                    ReportUrl = Absolute(codalBaseUrl, l.Url, "Reports/"),
                    ExcelUrl = Absolute(codalBaseUrl, l.ExcelUrl, "Reports/"),
                    PdfUrl = Absolute(codalBaseUrl, l.PdfUrl, "Reports/"),
                    AttachmentUrl = Absolute(codalBaseUrl, l.AttachmentUrl, "Reports/"),
                };
            })
            .ToList();

        return new CodalSearchPage(dto.Total, dto.Page, letters);
    }

    /// <summary>Extracts the <c>let=</c> LetterType id from a Decision.aspx URL.</summary>
    public static int? ParseLetterTypeId(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        var match = LetterTypeRegex().Match(url);
        return match.Success && int.TryParse(match.Groups[1].ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
    }

    /// <summary>
    /// Material-disclosure group from a title such as "افشای اطلاعات بااهمیت - (موضوع - گروه ب) ...".
    /// Tolerates "-" vs "–" and spacing variations.
    /// </summary>
    public static string? ParseDisclosureGroup(string? title)
    {
        if (string.IsNullOrEmpty(title))
            return null;

        var match = DisclosureGroupRegex().Match(title);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? Absolute(string baseUrl, string? url, string relativeRoot)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
            return absolute.ToString();

        var root = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
        return url.StartsWith('/') ? new Uri(root, url).ToString() : new Uri(new Uri(root, relativeRoot), url).ToString();
    }

    [GeneratedRegex(@"[?&]let=(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LetterTypeRegex();

    [GeneratedRegex(@"[-–]\s*گروه\s*(الف|ب)", RegexOptions.CultureInvariant)]
    private static partial Regex DisclosureGroupRegex();
}
