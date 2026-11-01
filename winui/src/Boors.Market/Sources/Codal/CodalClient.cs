using Boors.Market.RawCache;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Boors.Market.Sources.Codal;

/// <summary>Typed access to Codal's open search API. Each call stores the raw response, then parses it.</summary>
public sealed class CodalClient(HttpClient http, IRawCache cache, TimeProvider timeProvider, ILogger<CodalClient> logger, IOptions<MarketDataOptions> options)
    : RawSourceClient(http, cache, timeProvider, logger)
{
    public const string Source = "codal";

    private readonly string searchBase = options.Value.CodalSearchBaseUrl.EndsWith('/') ? options.Value.CodalSearchBaseUrl : options.Value.CodalSearchBaseUrl + "/";
    private readonly string codalBase = options.Value.CodalBaseUrl;

    public RawRequest CategoriesRequest() => new(Source, "search/v1/categories", $"{searchBase}v1/categories", string.Empty);

    public RawRequest SearchRequest(CodalSearchQuery query)
    {
        var isFiltered = query.Symbol is not null || query.LetterType != -1 || query.Category != -1;
        var parameters = new List<(string Key, string Value)>
        {
            ("Audited", "true"),
            ("AuditorRef", "-1"),
            ("Category", Inv(query.Category)),
            ("Childs", "true"),
            ("CompanyState", "-1"),
            ("CompanyType", "-1"),
            ("Consolidatable", "true"),
            ("IsNotAudited", "false"),
            ("Length", "-1"),
            ("LetterType", Inv(query.LetterType)),
            ("Mains", "true"),
            ("NotAudited", "true"),
            ("NotConsolidatable", "true"),
            ("PageNumber", Inv(query.PageNumber)),
            ("Publisher", "false"),
        };
        if (query.Symbol is not null)
            parameters.Add(("Symbol", query.Symbol));
        parameters.Add(("TracingNo", "-1"));
        parameters.Add(("search", isFiltered ? "true" : "false"));

        var queryString = string.Join('&', parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        var paramsKey = string.Join('&', parameters.Select(p => $"{p.Key}={p.Value}"));
        return new(Source, "search/v2/q", $"{searchBase}v2/q?{queryString}", paramsKey);
    }

    public async Task<CodalTaxonomy> GetTaxonomyAsync(CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default) =>
        CodalParser.ParseCategories(await GetBodyAsync(CategoriesRequest(), policy, cancellationToken));

    public async Task<CodalSearchPage> SearchAsync(CodalSearchQuery query, CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default) =>
        CodalParser.ParseSearch(await GetBodyAsync(SearchRequest(query), policy, cancellationToken), codalBase);

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);
}
