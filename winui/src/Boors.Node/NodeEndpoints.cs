using Boors.Market.Services;
using Boors.Market.Services.Contracts;
using Boors.Market.Services.Local;
using Boors.Market.Services.Sync;

namespace Boors.Node;

/// <summary>
/// The boilerplate's routes (MarketController, InstrumentController, MarketDataController — "api/v{v}/[controller]/[action]")
/// as minimal APIs over <see cref="IMarketApi"/>, plus GetJobs and the node-sync endpoints.
/// </summary>
public static class NodeEndpoints
{
    public static IEndpointRouteBuilder MapMarketApi(this IEndpointRouteBuilder app)
    {
        var market = app.MapGroup("api/v1/Market").WithTags("Market");
        market.MapGet("GetOverview", async (string? date, IMarketApi api, CancellationToken ct) =>
            await api.GetOverviewAsync(date, ct) is { } overview ? Results.Ok(overview) : Results.NoContent());
        market.MapGet("GetBreadth", (int? sessions, IMarketApi api, CancellationToken ct) => api.GetBreadthAsync(sessions ?? 0, ct));
        market.MapGet("GetSectors", (string? date, IMarketApi api, CancellationToken ct) => api.GetSectorsAsync(date, ct));
        market.MapGet("GetMovers/{kind}", (string kind, int? take, string? date, IMarketApi api, CancellationToken ct) => api.GetMoversAsync(kind, take ?? 0, date, ct));
        market.MapGet("GetScreen/{kind}", (string kind, int? take, IMarketApi api, CancellationToken ct) => api.GetScreenAsync(kind, take ?? 0, ct));
        market.MapGet("GetIndexHistory/{insCode:long}", (long insCode, string? from, IMarketApi api, CancellationToken ct) => api.GetIndexHistoryAsync(insCode, from, ct));
        market.MapGet("GetMacroHistory/{series}", (string series, string? from, IMarketApi api, CancellationToken ct) => api.GetMacroHistoryAsync(series, from, ct));
        market.MapGet("Search", (string? text, int? take, IMarketApi api, CancellationToken ct) => api.SearchAsync(text ?? string.Empty, take ?? 0, ct));
        market.MapPost("GetMarketWatch", (MarketWatchRequestDto request, IMarketApi api, CancellationToken ct) => api.GetMarketWatchAsync(request, ct));

        var instrument = app.MapGroup("api/v1/Instrument").WithTags("Instrument");
        instrument.MapGet("GetMetrics/{insCode:long}", (long insCode, IMarketApi api, CancellationToken ct) => api.GetMetricsAsync(insCode, ct));
        instrument.MapGet("GetBars/{insCode:long}", (long insCode, string? from, IMarketApi api, CancellationToken ct) => api.GetBarsAsync(insCode, from, ct));
        instrument.MapGet("GetFlows/{insCode:long}", (long insCode, string? from, IMarketApi api, CancellationToken ct) => api.GetFlowsAsync(insCode, from, ct));
        instrument.MapGet("GetEvents/{insCode:long}", (long insCode, IMarketApi api, CancellationToken ct) => api.GetEventsAsync(insCode, ct));
        instrument.MapGet("GetShareholders/{insCode:long}", (long insCode, IMarketApi api, CancellationToken ct) => api.GetShareholdersAsync(insCode, ct));
        instrument.MapGet("GetLetters/{insCode:long}", (long insCode, int? take, IMarketApi api, CancellationToken ct) => api.GetLettersAsync(insCode, take ?? 0, ct));
        instrument.MapGet("GetMessages/{insCode:long}", (long insCode, int? take, IMarketApi api, CancellationToken ct) => api.GetMessagesAsync(insCode, take ?? 0, ct));

        var report = app.MapGroup("api/v1/Report").WithTags("Report");
        report.MapGet("GetPerformance/{kind}", (string kind, string? period, int? take, decimal? minValue, string? asOf, string? from, IMarketApi api, CancellationToken ct) =>
            api.GetPerformanceReportAsync(kind, period, take ?? 0, minValue ?? 0, asOf, from, ct));
        report.MapGet("GetSectorReturns", (string? period, decimal? minValue, string? asOf, string? from, IMarketApi api, CancellationToken ct) =>
            api.GetSectorReturnsAsync(period, minValue ?? 0, asOf, from, ct));
        report.MapGet("GetSignalTrackRecord", (int? years, string? asOf, string? from, IMarketApi api, CancellationToken ct) => api.GetSignalTrackRecordAsync(years ?? 0, asOf, from, ct));
        report.MapGet("GetSeasonality", (string? asOf, string? from, IMarketApi api, CancellationToken ct) => api.GetSeasonalityAsync(asOf, from, ct));
        report.MapGet("GetShortlist", (string? asOf, IMarketApi api, CancellationToken ct) => api.GetShortlistAsync(asOf, ct));

        var marketData = app.MapGroup("api/v1/MarketData").WithTags("MarketData");
        marketData.MapGet("GetHealth", (IMarketApi api, CancellationToken ct) => api.GetHealthAsync(ct));
        marketData.MapGet("GetJobs", (IMarketApi api, CancellationToken ct) => api.GetJobsAsync(ct));
        marketData.MapGet("GetJob/{id}", (string id, IMarketApi api, CancellationToken ct) => api.GetJobAsync(id, ct));
        marketData.MapGet("GetRuns", (string? job, string? since, int? take, IMarketApi api, CancellationToken ct) => api.GetRunsAsync(job, since, take ?? 0, ct));
        marketData.MapPost("RunJob/{job}", (string job, IMarketApi api, CancellationToken ct) => api.RunJobAsync(job, ct))
            .AddEndpointFilter<OperatorTokenFilter>();
        marketData.MapPost("CancelJob/{id}", (string id, IMarketApi api, CancellationToken ct) => api.CancelJobAsync(id, ct))
            .AddEndpointFilter<OperatorTokenFilter>();

        var sync = app.MapGroup("api/v1/Sync").WithTags("Sync").AddEndpointFilter<OperatorTokenFilter>();
        sync.MapGet("ExportBundle", async (string? since, NodeSync nodeSync, CancellationToken ct) =>
        {
            var (path, files) = await nodeSync.ExportAsync(LocalMarketApi.ParseDate(since), ct);

            // The temporary bundle deletes itself once the response stream is closed.
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1 << 16, FileOptions.DeleteOnClose | FileOptions.SequentialScan);
            return Results.Stream(stream, "application/zip", Path.GetFileName(path), enableRangeProcessing: false);
        });
        sync.MapPost("Pull", (SyncPullRequestDto request, IMarketApi api, CancellationToken ct) => api.PullFromNodeAsync(request, ct));

        return app;
    }
}
