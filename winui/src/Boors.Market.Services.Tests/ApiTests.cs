using System.Net;
using System.Text;
using Boors.Market.Ingestion;
using Boors.Market.RawCache;
using Boors.Market.Services.Local;
using Boors.Market.Services.Remote;
using Boors.Market.Services.Sync;
using Microsoft.Extensions.Hosting;

namespace Boors.Market.Services.Tests;

[TestClass]
public sealed class ApiTests
{
    public TestContext TestContext { get; set; } = default!;

    private static (IHost Host, string Root) LocalHost()
    {
        var root = Path.Combine(Path.GetTempPath(), "boors-services-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddBoorsLocalApi(o =>
        {
            o.DataRoot = root;
            o.StorePath = ":memory:";
            o.Node = "test";
        }, api => api.Offline = true);
        return (builder.Build(), root);
    }

    [TestMethod]
    public async Task EmptyStoreAnswersLikeTheBoilerplateApi()
    {
        var (host, _) = LocalHost();
        using var _ = host;
        var api = host.Services.GetRequiredService<IMarketApi>();
        var ct = TestContext.CancellationToken;

        Assert.IsNull(await api.GetOverviewAsync(null, ct));
        Assert.IsEmpty(await api.GetBreadthAsync(0, ct));
        Assert.IsEmpty(await api.GetMoversAsync("value", 0, null, ct));
        Assert.IsEmpty(await api.SearchAsync("فولاد", 5, ct));
        Assert.IsNull((await api.GetMarketWatchAsync(new MarketWatchRequestDto(null, null, null, true), ct)).Session);

        var unknownKind = Assert.ThrowsExactly<MarketApiException>(() => api.GetMoversAsync("nope", 0, null, ct));
        Assert.AreEqual(400, unknownKind.StatusCode);
        var notFound = await Assert.ThrowsExactlyAsync<MarketApiException>(() => api.GetMetricsAsync(1, ct));
        Assert.AreEqual(404, notFound.StatusCode);
        var badDate = Assert.ThrowsExactly<MarketApiException>(() => api.GetSectorsAsync("17-10-2026", ct));
        Assert.AreEqual(400, badDate.StatusCode);
        var badJob = Assert.ThrowsExactly<MarketApiException>(() => api.RunJobAsync("nope", ct));
        Assert.AreEqual(400, badJob.StatusCode);
    }

    [TestMethod]
    public async Task EmptyStoreAnswersEveryReportAndRejectsUnknownKindsAndPeriods()
    {
        var (host, _) = LocalHost();
        using var _ = host;
        var api = host.Services.GetRequiredService<IMarketApi>();
        var ct = TestContext.CancellationToken;

        var performance = await api.GetPerformanceReportAsync("steadycompounders", "3y", 0, 0, null, null, ct);
        Assert.IsNull(performance.AsOf);
        Assert.AreEqual("3y", performance.Period);
        Assert.IsEmpty(performance.Rows);
        Assert.AreEqual("1y", (await api.GetSectorReturnsAsync(null, 0, null, null, ct)).Period);
        Assert.AreEqual("custom", (await api.GetSectorReturnsAsync("6m", 0, "2026-10-17", "2026-01-01", ct)).Period);
        Assert.IsEmpty((await api.GetSignalTrackRecordAsync(0, null, null, ct)).Signals);
        Assert.IsEmpty((await api.GetSeasonalityAsync(null, null, ct)).Years);
        var shortlist = await api.GetShortlistAsync(null, ct);
        Assert.IsNull(shortlist.AsOf);
        Assert.IsEmpty(shortlist.LongTerm);

        Assert.AreEqual(400, Assert.ThrowsExactly<MarketApiException>(() => api.GetPerformanceReportAsync("Best", "1y", 0, 0, null, null, ct)).StatusCode);
        Assert.AreEqual(400, Assert.ThrowsExactly<MarketApiException>(() => api.GetPerformanceReportAsync("TopPerformers", "2y", 0, 0, null, null, ct)).StatusCode);
        Assert.AreEqual(400, Assert.ThrowsExactly<MarketApiException>(() => api.GetSectorReturnsAsync("week", 0, null, null, ct)).StatusCode);
        Assert.AreEqual(400, Assert.ThrowsExactly<MarketApiException>(() => api.GetPerformanceReportAsync("TopPerformers", "custom", 0, 0, null, null, ct)).StatusCode);
        Assert.AreEqual(400, Assert.ThrowsExactly<MarketApiException>(() => api.GetPerformanceReportAsync("TopPerformers", null, 0, 0, "2026-01-01", "2026-01-01", ct)).StatusCode);
        Assert.AreEqual(400, Assert.ThrowsExactly<MarketApiException>(() => api.GetSignalTrackRecordAsync(0, "2026-01-01", "2026-06-01", ct)).StatusCode);
        Assert.AreEqual(400, Assert.ThrowsExactly<MarketApiException>(() => api.GetSeasonalityAsync("1405/07/25", null, ct)).StatusCode);
        Assert.AreEqual(400, Assert.ThrowsExactly<MarketApiException>(() => api.GetShortlistAsync("27-10-2026", ct)).StatusCode);
    }

    [TestMethod]
    public void ParseDateAcceptsOnlyIsoDates()
    {
        Assert.AreEqual(new DateOnly(2026, 10, 17), LocalMarketApi.ParseDate("2026-10-17"));
        Assert.IsNull(LocalMarketApi.ParseDate(" "));
        Assert.ThrowsExactly<MarketApiException>(() => LocalMarketApi.ParseDate("1405/07/25"));
    }

    [TestMethod]
    public void JsonIsCamelCaseAndKeepsDictionaryKeys()
    {
        var metrics = new InstrumentMetricsDto(1, "فولاد", null, "tse_stock", null, null, new DateOnly(2026, 10, 17), "Traded", 4410m, -2.86, null, null, null, null,
            new Dictionary<string, double?> { ["1w"] = 0.07 }, null, null, null, null, null, null, null);
        var json = JsonSerializer.Serialize(metrics, MarketJsonContext.Default.InstrumentMetricsDto);

        Assert.Contains("\"insCode\":1", json);
        Assert.Contains("\"asOf\":\"2026-10-17\"", json);
        Assert.Contains("\"returns\":{\"1w\":0.07}", json);
        Assert.Contains("\"marketCap\":null", json);
    }

    [TestMethod]
    public async Task RemoteClientMapsNoContentProblemsAndTextIds()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/Market/GetOverview" => new HttpResponseMessage(HttpStatusCode.NoContent),
            "/api/v1/Market/GetMovers/nope" => Json(HttpStatusCode.BadRequest, """{"title":"Bad request","status":400,"detail":"Unknown movers kind 'nope'."}"""),
            "/api/v1/Report/GetPerformance/SteadyCompounders" => Json(HttpStatusCode.OK, """{"asOf":"2026-10-17","period":"5y","sessions":1138,"universe":1,"rows":[{"insCode":7,"symbol":"افق","return":18.99,"maxDrawdown":-0.21,"averageValue20":461000000000,"coverage":0.91,"score":99.2}]}"""),
            "/api/v1/MarketData/RunJob/eod" => request.Headers.Authorization?.Parameter == "t0ken"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("20261019-1-eod", Encoding.UTF8, "text/plain") }
                : Json(HttpStatusCode.Unauthorized, """{"title":"Unauthorized","status":401}"""),
            _ => Json(HttpStatusCode.OK, """[{"date":"2026-10-17","value":8265156.65}]"""),
        });
        var api = new RemoteMarketApi(new HttpClient(handler) { BaseAddress = new Uri("http://node/") }, new RemoteMarketApiOptions { OperatorToken = "t0ken" });
        var ct = TestContext.CancellationToken;

        Assert.IsNull(await api.GetOverviewAsync(null, ct));
        var problem = await Assert.ThrowsExactlyAsync<MarketApiException>(() => api.GetMoversAsync("nope", 0, null, ct));
        Assert.AreEqual(400, problem.StatusCode);
        Assert.AreEqual("Unknown movers kind 'nope'.", problem.Message);
        Assert.AreEqual("20261019-1-eod", await api.RunJobAsync("eod", ct));
        var steady = await api.GetPerformanceReportAsync("SteadyCompounders", "5y", 0, 50_000_000_000m, "2025-10-17", null, ct);
        Assert.AreEqual("/api/v1/Report/GetPerformance/SteadyCompounders?period=5y&minValue=50000000000&asOf=2025-10-17", handler.Requests[^1]);
        Assert.AreEqual(99.2, steady.Rows.Single().Score);
        Assert.IsNull(steady.Rows.Single().UsdReturn);
        var series = await api.GetIndexHistoryAsync(32097828799138957, "2026-10-01", ct);
        Assert.AreEqual(8265156.65m, series.Single().Value);
        Assert.AreEqual("/api/v1/Market/GetIndexHistory/32097828799138957?from=2026-10-01", handler.Requests[^1]);
    }

    [TestMethod]
    public async Task PullImportsAnotherNodesBundle()
    {
        var ct = TestContext.CancellationToken;

        // A source node with one cached response.
        var sourceRoot = Path.Combine(Path.GetTempPath(), "boors-services-tests", Guid.NewGuid().ToString("N"));
        var source = new FileSystemRawCache(new RawCacheOptions { Root = sourceRoot, Node = "iran-1" });
        await source.StoreAsync(new RawRequest("tgju", "price_dollar_rl", "https://example/price", "series=price_dollar_rl"), Encoding.UTF8.GetBytes("{}"), 200, "application/json",
            DateTimeOffset.UtcNow, ct);
        var bundle = Path.Combine(sourceRoot, "bundle.zip");
        var exported = await RawCacheBundle.ExportAsync(source, bundle, null, ct);
        Assert.AreEqual(2, exported); // body + sidecar

        var (host, root) = LocalHost();
        using var _ = host;
        var bytes = await File.ReadAllBytesAsync(bundle, ct);
        var factory = new StubFactory(new StubHandler(request => request.RequestUri!.AbsolutePath == "/" + NodeSync.BundleRoute
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
            : new HttpResponseMessage(HttpStatusCode.NotFound)));
        var sync = new NodeSync(host.Services.GetRequiredService<IRawCache>(), factory,
            host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MarketDataOptions>>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NodeSync>.Instance);

        var results = await sync.PullAsync(new Uri("http://iran-1:5080/"), null, null, host.Services.GetRequiredService<MarketCollector>(), ct);
        Assert.AreEqual("ok", results[0].Status, results[0].Message);
        Assert.AreEqual(2, results[0].Items);
        Assert.HasCount(2, Directory.EnumerateFiles(Path.Combine(root, "raw"), "*", SearchOption.AllDirectories).ToList());
        Assert.IsNotNull(sync.LastPull(new Uri("http://iran-1:5080/")));

        // Pulling again is a union: nothing new, so nothing is replayed.
        var again = await sync.PullAsync(new Uri("http://iran-1:5080/"), null, null, host.Services.GetRequiredService<MarketCollector>(), ct);
        Assert.HasCount(1, again);
        Assert.AreEqual(0, again[0].Items);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, status == HttpStatusCode.OK ? "application/json" : "application/problem+json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
