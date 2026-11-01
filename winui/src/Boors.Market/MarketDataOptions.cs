namespace Boors.Market;

public sealed class MarketDataOptions
{
    public const string SectionName = "MarketData";

    /// <summary>Root for the raw cache and parsed outputs.</summary>
    public string DataRoot { get; set; } = ".boors-data";

    /// <summary>Path of the DuckDB market store; defaults to <c>{DataRoot}/market.duckdb</c>.</summary>
    public string? StorePath { get; set; }

    /// <summary>Name of this collector node (e.g. "iran-1", "eu-1"), recorded on every raw entry.</summary>
    public string Node { get; set; } = Environment.MachineName;

    public string TsetmcBaseUrl { get; set; } = "https://cdn.tsetmc.com/api/";

    public string CodalSearchBaseUrl { get; set; } = "https://search.codal.ir/api/search/";

    public string CodalBaseUrl { get; set; } = "https://codal.ir/";

    /// <summary>Requests per second to cdn.tsetmc.com. No limit is documented; ≤ 2 is the agreed conservative default.</summary>
    public double TsetmcRequestsPerSecond { get; set; } = 2;

    /// <summary>Codal answers HTTP 429 to bursts (seen 2026-10-17 after ~10 requests in a few seconds); one request every 2 s.</summary>
    public double CodalRequestsPerSecond { get; set; } = 0.5;

    /// <summary>tgju, fipiran and other secondary sources.</summary>
    public double OtherSourcesRequestsPerSecond { get; set; } = 1;

    /// <summary>A browser User-Agent. cdn.tsetmc.com answers 403 without one.</summary>
    public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36";

    /// <summary>Timeout for one attempt. Large exports (Excel market watch) need up to 90 s.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>Overall timeout including retries.</summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public int MaxRetryAttempts { get; set; } = 4;

    /// <summary>
    /// Server only: schedule the collection jobs on this node. Off by default so development machines and test runs do
    /// not start hitting external sources; turn it on per node (Iran node, abroad node).
    /// </summary>
    public bool Collect { get; set; }

    public int MaxInstrumentDetailsPerRun { get; set; } = 300;

    public int MaxBackfillPerRun { get; set; } = 400;

    public int MaxHaltQueriesPerRun { get; set; } = 500;

    /// <summary>Optional path to a rules.yaml that overrides the copy embedded in the assembly.</summary>
    public string? RulesPath { get; set; }
}
