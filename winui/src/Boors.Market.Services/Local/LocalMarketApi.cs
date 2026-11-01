using Boors.Market.Analytics;
using Boors.Market.Domain;
using Boors.Market.Filters;
using Boors.Market.Ingestion;
using Boors.Market.Services.Contracts;
using Boors.Market.Services.Jobs;
using Boors.Market.Sources;
using Boors.Market.Sources.Tsetmc;
using Boors.Market.Storage;
using Boors.Market.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Boors.Market.Services.Local;

/// <summary>
/// <see cref="IMarketApi"/> over the in-process DuckDB store. A port of the boilerplate's MarketController,
/// InstrumentController and MarketDataController: same parameters, clamps and results. Every call runs on the thread
/// pool so a UI thread never waits on DuckDB; reads may run concurrently, writes go through <see cref="MarketJobQueue"/>.
/// </summary>
public sealed class LocalMarketApi(
    IServiceProvider services,
    MarketJobQueue jobs,
    IOptions<MarketApiOptions> apiOptions,
    TimeProvider timeProvider,
    ILogger<LocalMarketApi> logger) : IMarketApi
{
    // Resolved lazily (and on a pool thread) so opening the store never happens on the caller's thread.
    private MarketStore Store => services.GetRequiredService<MarketStore>();

    private MarketAnalytics Analytics => services.GetRequiredService<MarketAnalytics>();

    private MarketReports Reports => services.GetRequiredService<MarketReports>();

    public Task<MarketOverviewDto?> GetOverviewAsync(string? date, CancellationToken cancellationToken)
    {
        var session = ParseDate(date);
        return Run(() => Analytics.GetOverview(session)?.ToDto(), cancellationToken);
    }

    public Task<List<BreadthDayDto>> GetBreadthAsync(int sessions, CancellationToken cancellationToken) =>
        Run(() => Analytics.GetBreadth(Math.Clamp(sessions == 0 ? 20 : sessions, 1, 250)).Select(b => b.ToDto()).ToList(), cancellationToken);

    public Task<List<SectorPerformanceDto>> GetSectorsAsync(string? date, CancellationToken cancellationToken)
    {
        var session = ParseDate(date);
        return Run(() => Analytics.GetSectors(session).Select(s => s.ToDto()).ToList(), cancellationToken);
    }

    public Task<List<MoverDto>> GetMoversAsync(string kind, int take, string? date, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<MoverKind>(kind, ignoreCase: true, out var moverKind))
            throw MarketApiException.BadRequest($"Unknown movers kind '{kind}'.");

        var session = ParseDate(date);
        return Run(() => Analytics.GetMovers(moverKind, Take(take), session).Select(m => m.ToDto()).ToList(), cancellationToken);
    }

    public Task<List<ScreenRowDto>> GetScreenAsync(string kind, int take, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ScreenKind>(kind, ignoreCase: true, out var screenKind))
            throw MarketApiException.BadRequest($"Unknown screen '{kind}'.");

        return Run(() => Analytics.Screen(screenKind, Take(take)).Select(r => r.ToDto()).ToList(), cancellationToken);
    }

    public Task<List<SeriesPointDto>> GetIndexHistoryAsync(long insCode, string? from, CancellationToken cancellationToken)
    {
        var start = ParseDate(from) ?? DateOnly.MinValue;
        return Run(() => Store.GetIndexBars(insCode).Where(b => b.Date >= start).Select(b => new SeriesPointDto(b.Date, b.Close)).ToList(), cancellationToken);
    }

    public Task<List<SeriesPointDto>> GetMacroHistoryAsync(string series, string? from, CancellationToken cancellationToken)
    {
        var start = ParseDate(from) ?? DateOnly.MinValue;
        return Run(() => Store.GetMacroDaily(series).Where(b => b.Date >= start).Select(b => new SeriesPointDto(b.Date, b.Close)).ToList(), cancellationToken);
    }

    public Task<List<InstrumentSearchResultDto>> SearchAsync(string text, int take, CancellationToken cancellationToken) =>
        Run(() => Analytics.Search(text ?? string.Empty, Take(take))
            .Select(i => new InstrumentSearchResultDto(i.InsCode, i.Symbol, i.Name, i.Class.ToRuleKey(), i.ParentInsCode))
            .ToList(), cancellationToken);

    public Task<MarketWatchResultDto> GetMarketWatchAsync(MarketWatchRequestDto request, CancellationToken cancellationToken)
    {
        var session = ParseDate(request.Date);
        return Run(() =>
        {
            try
            {
                var result = services.GetRequiredService<MarketWatchService>().Query(new MarketWatchRequest
                {
                    Session = session,
                    Filter = request.Filter,
                    Classes = request.Classes is { Count: > 0 } classes ? classes.ToHashSet() : null,
                    TradedOnly = request.TradedOnly,
                }, cancellationToken);

                return new MarketWatchResultDto(
                    result.Session,
                    [.. result.Entries.Select(e => e.ToDto())],
                    result.Filter?.Evaluated ?? result.Entries.Count,
                    [.. result.Filter?.Errors ?? []]);
            }
            catch (FilterSyntaxException e)
            {
                throw MarketApiException.BadRequest($"Filter error: {e.Message}");
            }
        }, cancellationToken);
    }

    public Task<InstrumentMetricsDto> GetMetricsAsync(long insCode, CancellationToken cancellationToken) =>
        Run(() => (Analytics.GetInstrumentMetrics(insCode) ?? throw MarketApiException.NotFound($"Instrument {insCode} is not in the store.")).ToDto(), cancellationToken);

    public Task<List<BarDto>> GetBarsAsync(long insCode, string? from, CancellationToken cancellationToken)
    {
        var start = ParseDate(from) ?? DateOnly.MinValue;
        return Run(() =>
        {
            // Adjust over the full history (point-in-time as of the last bar), then cut to the requested window.
            var bars = Store.GetDailyBars(insCode);
            if (bars.Count == 0)
                return new List<BarDto>();

            var adjusted = PointInTime.AdjustFromBars(bars.Select(b => (b.Bar, b.Status)).ToList(), bars[^1].Bar.Date);
            return bars.Zip(adjusted)
                .Where(x => x.First.Bar.Date >= start)
                .Select(x => new BarDto(x.First.Bar.Date, x.First.Bar.Open, x.First.Bar.High, x.First.Bar.Low, x.First.Bar.Close, x.Second.AdjustedClose,
                    x.First.Bar.Volume, x.First.Bar.Value, x.First.Bar.TradeCount, x.First.Status?.ToString(), x.Second.Return is { } r ? (double)r : null))
                .ToList();
        }, cancellationToken);
    }

    public Task<List<FlowDayDto>> GetFlowsAsync(long insCode, string? from, CancellationToken cancellationToken)
    {
        var start = ParseDate(from) ?? DateOnly.MinValue;
        return Run(() => Store.GetClientTypes(insCode)
            .Where(f => f.Date >= start)
            .Select(f => new FlowDayDto(f.Date, f.BuyIndividualValue, f.SellIndividualValue, f.BuyInstitutionalValue, f.SellInstitutionalValue,
                f.BuyIndividualCount, f.SellIndividualCount, f.IndividualNetValue, f.PerCapitaBuy, f.PerCapitaSell, f.BuyerPower, f.ValuesEstimated))
            .ToList(), cancellationToken);
    }

    public Task<List<CorporateEventDto>> GetEventsAsync(long insCode, CancellationToken cancellationToken) =>
        Run(() =>
        {
            var adjustments = new[] { "implied", "published" }
                .SelectMany(source => Store.GetPriceAdjustments(insCode, source)
                    .Select(a => new CorporateEventDto(a.Date, "adjustment", source, a.Factor, null, null)));
            var shareChanges = Store.GetShareChanges(insCode)
                .Select(s => new CorporateEventDto(s.Date, "share_change", null, null, s.SharesBefore, s.SharesAfter));
            return adjustments.Concat(shareChanges).OrderBy(e => e.Date).ToList();
        }, cancellationToken);

    public Task<List<ShareholderDto>> GetShareholdersAsync(long insCode, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            var now = IranTime.Now(timeProvider);
            var today = new DateOnly(now.Year, now.Month, now.Day);
            var holders = Store.GetLatestShareholders(insCode);
            if (!apiOptions.Value.Offline && (holders.Count == 0 || holders[0].SnapshotDate < today))
            {
                try
                {
                    // Bounded so a dead link degrades to the stored snapshot instead of a spinner for minutes.
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(apiOptions.Value.LiveRefreshTimeout);
                    var fresh = await services.GetRequiredService<TsetmcClient>().GetShareholdersAsync(insCode, today, CachePolicy.Network, timeout.Token);
                    Store.UpsertShareholders(fresh);
                    holders = Store.GetLatestShareholders(insCode);
                }
                catch (Exception e) when (e is SourceRequestException or HttpRequestException or Polly.ExecutionRejectedException
                    || (e is OperationCanceledException && !cancellationToken.IsCancellationRequested))
                {
                    // Serve the last snapshot we have; the page shows its date.
                    logger.LogInformation("Shareholders of {InsCode} not refreshed: {Reason}", insCode, e.Message);
                }
            }

            return holders.Select(h => new ShareholderDto(h.SnapshotDate, h.HolderName, h.Shares, h.Percent, h.Change, h.ChangeAmount)).ToList();
        }, cancellationToken);

    public Task<List<CodalLetterDto>> GetLettersAsync(long insCode, int take, CancellationToken cancellationToken) =>
        Run(() => Store.GetCodalLetters(insCode, Math.Clamp(take == 0 ? 50 : take, 1, 500))
            .Select(l => new CodalLetterDto(l.TracingNo, l.Title, l.LetterCode, l.LetterTypeId, l.PublishedAt, l.DisclosureGroup, l.HasExcel, l.ReportUrl, l.ExcelUrl, l.PdfUrl))
            .ToList(), cancellationToken);

    public Task<List<SupervisorMessageDto>> GetMessagesAsync(long insCode, int take, CancellationToken cancellationToken) =>
        Run(() => Store.GetMessages(insCode)
            .OrderByDescending(m => m.Date).ThenByDescending(m => m.Time)
            .Take(Math.Clamp(take == 0 ? 50 : take, 1, 500))
            .Select(m => new SupervisorMessageDto(m.Id, m.Date, m.Title, m.Body, m.Kind.ToString(), m.Reason.ToString(), m.ReopensWithoutLimit))
            .ToList(), cancellationToken);

    public Task<PerformanceReportDto> GetPerformanceReportAsync(string kind, string? period, int take, decimal minValue, string? asOf, string? from, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<PerformanceReportKind>(kind, ignoreCase: true, out var reportKind) || !Enum.IsDefined(reportKind))
            throw MarketApiException.BadRequest($"Unknown report '{kind}'.");

        var window = ParseWindow(period, asOf, from);
        var limit = Math.Clamp(take == 0 ? 50 : take, 1, 500);
        return Run(() => Reports.GetPerformance(reportKind, window.Period, Math.Max(0, minValue), limit, window.AsOf, window.From).ToDto(), cancellationToken);
    }

    public Task<SectorReturnsReportDto> GetSectorReturnsAsync(string? period, decimal minValue, string? asOf, string? from, CancellationToken cancellationToken)
    {
        var window = ParseWindow(period, asOf, from);
        return Run(() => Reports.GetSectorReturns(window.Period, Math.Max(0, minValue), window.AsOf, window.From).ToDto(), cancellationToken);
    }

    public Task<SignalTrackRecordDto> GetSignalTrackRecordAsync(int years, string? asOf, string? from, CancellationToken cancellationToken)
    {
        var window = ParseRange(asOf, from);
        return Run(() => Reports.GetSignalTrackRecord(Math.Clamp(years == 0 ? 5 : years, 1, 15), asOf: window.AsOf, from: window.From).ToDto(), cancellationToken);
    }

    public Task<SeasonalityReportDto> GetSeasonalityAsync(string? asOf, string? from, CancellationToken cancellationToken)
    {
        var window = ParseRange(asOf, from);
        return Run(() => Reports.GetSeasonality(window.AsOf, window.From).ToDto(), cancellationToken);
    }

    public Task<ShortlistDto> GetShortlistAsync(string? asOf, CancellationToken cancellationToken)
    {
        var end = ParseDate(asOf);
        return Run(() => Reports.GetShortlist(end).ToDto(), cancellationToken);
    }

    public Task<MarketDataHealthDto> GetHealthAsync(CancellationToken cancellationToken) =>
        Run(() =>
        {
            var report = services.GetRequiredService<DataHealthService>().GetReport();
            return new MarketDataHealthDto(
                report.GeneratedAt,
                report.Node,
                report.ExpectedSession,
                report.Healthy,
                [.. report.Datasets.Select(d => new DatasetHealthDto(d.Dataset, d.Rows, d.LatestDate, d.ExpectedDate, d.Status.ToString()))],
                [.. report.LatestRuns.Select(r => new CollectionRunDto(r.Job, r.Node, r.StartedAt, r.FinishedAt, r.Status, r.Items, r.Failures, r.Message))],
                report.LastProbeAt,
                [.. report.Sources.Select(s => new SourceReachabilityDto(s.Name, s.Verdict, s.Status, s.Error))]);
        }, cancellationToken);

    public Task<string> RunJobAsync(string job, CancellationToken cancellationToken)
    {
        if (!MarketJobs.Names.Contains(job))
            throw MarketApiException.BadRequest($"Unknown market-data job '{job}'. Jobs: {string.Join(", ", MarketJobs.Names)}.");

        return Task.FromResult(jobs.Enqueue(MarketJobRequest.Collect(job, "manual")).Id);
    }

    public Task<JobQueueDto> GetJobsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new JobQueueDto(
            MarketJobs.Names,
            services.GetService<MarketJobScheduler>()?.Enabled ?? false,
            services.GetService<MarketJobScheduler>()?.Describe() ?? [],
            jobs.Snapshot(),
            services.GetService<SourceTraffic>()?.Snapshot() ?? [],
            services.GetService<SourceTraffic>()?.StartedAt));

    public Task<JobDetailDto> GetJobAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(jobs.Detail(id) ?? throw MarketApiException.NotFound($"Job '{id}' is not in the queue (it keeps the last {MarketJobQueue.HistoryLimit} finished jobs)."));

    public Task<bool> CancelJobAsync(string id, CancellationToken cancellationToken) => Task.FromResult(jobs.Cancel(id));

    public Task<List<JobRunDto>> GetRunsAsync(string? job, string? since, int take, CancellationToken cancellationToken)
    {
        var from = ParseDate(since) ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(-30);
        var limit = Math.Clamp(take == 0 ? 5000 : take, 1, 50_000);
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return Run(() => Store.GetRuns(start, string.IsNullOrWhiteSpace(job) ? null : job, limit)
            .Select(r => new JobRunDto(r.RunId, r.Job, r.Node, r.StartedAt, r.FinishedAt, r.Status, r.Items, r.Failures, r.Message))
            .ToList(), cancellationToken);
    }

    public Task<string> PullFromNodeAsync(SyncPullRequestDto request, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.NodeUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw MarketApiException.BadRequest($"Invalid node URL '{request.NodeUrl}' (expected http(s)://host[:port]).");
        var since = ParseDate(request.Since);

        return Task.FromResult(jobs.Enqueue(MarketJobRequest.Pull(uri, since, request.OperatorToken)).Id);
    }

    private static Task<T> Run<T>(Func<T> work, CancellationToken cancellationToken) => Task.Run(work, cancellationToken);

    private static int Take(int take) => Math.Clamp(take == 0 ? 20 : take, 1, 500);

    /// <summary>A report window: a start date makes it custom; custom without one, or a start on or after the end, is a 400.</summary>
    private static (ReportPeriod Period, DateOnly? AsOf, DateOnly? From) ParseWindow(string? period, string? asOf, string? from)
    {
        var (end, start) = ParseRange(asOf, from);
        var reportPeriod = start is not null ? ReportPeriod.Custom
            : string.IsNullOrWhiteSpace(period) ? ReportPeriod.OneYear
            : ReportPeriods.Keys.TryGetValue(period, out var p) ? p
            : throw MarketApiException.BadRequest($"Unknown period '{period}' (expected {string.Join(", ", ReportPeriods.Keys.Keys)}).");
        if (reportPeriod == ReportPeriod.Custom && start is null)
            throw MarketApiException.BadRequest("A custom period needs its start date (from=yyyy-MM-dd).");
        return (reportPeriod, end, start);
    }

    private static (DateOnly? AsOf, DateOnly? From) ParseRange(string? asOf, string? from)
    {
        var end = ParseDate(asOf);
        var start = ParseDate(from);
        if (start is { } s && end is { } e && s >= e)
            throw MarketApiException.BadRequest($"The start date {from} must be before the end date {asOf}.");
        return (end, start);
    }

    public static DateOnly? ParseDate(string? date) =>
        string.IsNullOrWhiteSpace(date) ? null
        : DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d
        : throw MarketApiException.BadRequest($"Invalid date '{date}' (expected yyyy-MM-dd).");
}
