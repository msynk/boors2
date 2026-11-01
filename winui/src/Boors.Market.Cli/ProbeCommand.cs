using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Boors.Market.Cli;

/// <summary>
/// One request per external dependency, reporting whether this node can reach it. Run it on every node (Iran, abroad)
/// after deployment and whenever connectivity changes; the JSON report feeds the data-health page later.
/// </summary>
public sealed class ProbeCommand(IHttpClientFactory httpClientFactory, IOptions<MarketDataOptions> options, TimeProvider timeProvider)
{
    public const string HttpClientName = "probe";

    private sealed record Target(string Group, string Name, string Url, string? Referer = null, bool ExpectJson = true);

    private sealed record ProbeResult(string Group, string Name, string Url, int? Status, long Bytes, long ElapsedMs, string Verdict, string? Error);

    private static readonly Target[] Targets =
    [
        new("market-data", "TSETMC cdn API (instrument info)", "https://cdn.tsetmc.com/api/Instrument/GetInstrumentInfo/46348559193224090", "https://www.tsetmc.com/"),
        new("market-data", "TSETMC cdn API (market watch, shares)", "https://cdn.tsetmc.com/api/ClosingPrice/GetMarketWatch?market=0&industrialGroup=&paperTypes%5B0%5D=1&showTraded=false&withBestLimits=true&hEven=0&RefID=0", "https://www.tsetmc.com/"),
        new("market-data", "TSETMC legacy (old.tsetmc.com)", "https://old.tsetmc.com/tsev2/data/InstTradeHistory.aspx?i=46348559193224090&Top=5&A=0", "https://old.tsetmc.com/", ExpectJson: false),
        new("market-data", "TSE gateway (webgw.tse.ir)", "https://webgw.tse.ir/InstrumentProvider/api/v1/Instrument/MarketSummary/fa"),
        new("market-data", "Codal search API", "https://search.codal.ir/api/search/v1/categories"),
        new("market-data", "Codal main site", "https://codal.ir/", ExpectJson: false),
        new("market-data", "fipiran fund API", "https://www.fipiran.com/services/fund/fundtype", "https://www.fipiran.com/"),
        new("market-data", "tgju live feed", "https://call1.tgju.org/ajax.json"),
        new("market-data", "BrsApi", "https://brsapi.ir/", ExpectJson: false),
        new("market-data", "Iran Mercantile Exchange", "https://www.ime.co.ir/", ExpectJson: false),
        new("messaging", "Telegram Bot API", "https://api.telegram.org/", ExpectJson: false),
        new("messaging", "Bale Bot API", "https://tapi.bale.ai/", ExpectJson: false),
        new("build", "nuget.org", "https://api.nuget.org/v3/index.json"),
        new("build", "ParsPack NuGet mirror", "https://mirror.abrha.net/repository/nuget/index.json"),
        new("ai", "AvalAI gateway", "https://api.avalai.ir/v1/models"),
    ];

    /// <summary>Plain client without retries; old.tsetmc.com's self-signed certificate is accepted for that host only.</summary>
    public static HttpMessageHandler CreateHandler() => new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
            errors == System.Net.Security.SslPolicyErrors.None
            || string.Equals(request.RequestUri?.Host, "old.tsetmc.com", StringComparison.OrdinalIgnoreCase),
    };

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(30);

        var results = new List<ProbeResult>();
        foreach (var target in Targets)
        {
            results.Add(await ProbeAsync(client, target, cancellationToken));
            var r = results[^1];
            Console.WriteLine($"{r.Verdict,-12} {r.Group,-12} {r.Name,-40} {(r.Status?.ToString(CultureInfo.InvariantCulture) ?? "-"),4} {r.ElapsedMs,6} ms  {r.Error}");
        }

        var now = timeProvider.GetUtcNow();
        var path = Path.Combine(options.Value.DataRoot, "reports", $"probe-{options.Value.Node}-{now:yyyyMMdd-HHmmss}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { node = options.Value.Node, probedAtUtc = now, results }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        Console.WriteLine();
        Console.WriteLine($"Report: {Path.GetFullPath(path)}");

        // Non-zero exit when any market-data source is unusable, so schedulers can alert on it.
        return results.Any(r => r.Group == "market-data" && r.Verdict != "ok") ? 3 : 0;
    }

    private async Task<ProbeResult> ProbeAsync(HttpClient client, Target target, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, target.Url);
        request.Headers.TryAddWithoutValidation("User-Agent", options.Value.UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", target.ExpectJson ? "application/json, text/plain, */*" : "text/html, */*");
        if (target.Referer is not null)
            request.Headers.TryAddWithoutValidation("Referer", target.Referer);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var status = (int)response.StatusCode;

            // Any HTTP answer proves reachability; for data sources only a 2xx counts as usable.
            var verdict = response.IsSuccessStatusCode ? "ok"
                : target.Group is "market-data" ? "http-error"
                : "reachable";
            return new(target.Group, target.Name, target.Url, status, body.Length, stopwatch.ElapsedMilliseconds, verdict, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            var message = e is TaskCanceledException ? "timeout" : e.InnerException?.Message ?? e.Message;
            return new(target.Group, target.Name, target.Url, null, 0, stopwatch.ElapsedMilliseconds, "unreachable", message);
        }
    }
}
