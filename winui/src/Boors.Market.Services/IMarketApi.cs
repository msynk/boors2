using Boors.Market.Services.Contracts;

namespace Boors.Market.Services;

/// <summary>
/// Everything the workstation reads and the operations it can trigger. The same contract is served over HTTP by
/// Boors.Node (routes in the comments) and consumed in-process by the desktop (<see cref="Local.LocalMarketApi"/>) or
/// remotely (<see cref="Remote.RemoteMarketApi"/>). Dates are yyyy-MM-dd strings; null means the latest session.
/// Implementations never block the caller's thread: store and analytics work runs on the thread pool.
/// </summary>
public interface IMarketApi
{
    /// <summary>GET api/v1/Market/GetOverview?date=</summary>
    Task<MarketOverviewDto?> GetOverviewAsync(string? date, CancellationToken cancellationToken);

    /// <summary>GET api/v1/Market/GetBreadth?sessions= (default 20, 1–250)</summary>
    Task<List<BreadthDayDto>> GetBreadthAsync(int sessions, CancellationToken cancellationToken);

    /// <summary>GET api/v1/Market/GetSectors?date=</summary>
    Task<List<SectorPerformanceDto>> GetSectorsAsync(string? date, CancellationToken cancellationToken);

    /// <summary>GET api/v1/Market/GetMovers/{kind}?take=&amp;date= — kind: gainers, losers, value, inflow, outflow.</summary>
    Task<List<MoverDto>> GetMoversAsync(string kind, int take, string? date, CancellationToken cancellationToken);

    /// <summary>GET api/v1/Market/GetScreen/{kind}?take= — kind: Momentum6To1, High52WeekProximity, IndividualFlowIntensity20, BuyerPower5.</summary>
    Task<List<ScreenRowDto>> GetScreenAsync(string kind, int take, CancellationToken cancellationToken);

    /// <summary>GET api/v1/Market/GetIndexHistory/{insCode}?from=</summary>
    Task<List<SeriesPointDto>> GetIndexHistoryAsync(long insCode, string? from, CancellationToken cancellationToken);

    /// <summary>GET api/v1/Market/GetMacroHistory/{series}?from= — price_dollar_rl, sekee, geram18, ons, price_eur.</summary>
    Task<List<SeriesPointDto>> GetMacroHistoryAsync(string series, string? from, CancellationToken cancellationToken);

    /// <summary>GET api/v1/Market/Search?text=&amp;take=</summary>
    Task<List<InstrumentSearchResultDto>> SearchAsync(string text, int take, CancellationToken cancellationToken);

    /// <summary>POST api/v1/Market/GetMarketWatch — a filter syntax error is a <see cref="MarketApiException"/> with status 400.</summary>
    Task<MarketWatchResultDto> GetMarketWatchAsync(MarketWatchRequestDto request, CancellationToken cancellationToken);

    /// <summary>GET api/v1/Instrument/GetMetrics/{insCode} — 404 when the instrument is unknown.</summary>
    Task<InstrumentMetricsDto> GetMetricsAsync(long insCode, CancellationToken cancellationToken);

    /// <summary>GET api/v1/Instrument/GetBars/{insCode}?from=</summary>
    Task<List<BarDto>> GetBarsAsync(long insCode, string? from, CancellationToken cancellationToken);

    /// <summary>GET api/v1/Instrument/GetFlows/{insCode}?from=</summary>
    Task<List<FlowDayDto>> GetFlowsAsync(long insCode, string? from, CancellationToken cancellationToken);

    /// <summary>GET api/v1/Instrument/GetEvents/{insCode}</summary>
    Task<List<CorporateEventDto>> GetEventsAsync(long insCode, CancellationToken cancellationToken);

    /// <summary>GET api/v1/Instrument/GetShareholders/{insCode} — refreshed from TSETMC at most once a day (unless offline).</summary>
    Task<List<ShareholderDto>> GetShareholdersAsync(long insCode, CancellationToken cancellationToken);

    /// <summary>GET api/v1/Instrument/GetLetters/{insCode}?take=</summary>
    Task<List<CodalLetterDto>> GetLettersAsync(long insCode, int take, CancellationToken cancellationToken);

    /// <summary>GET api/v1/Instrument/GetMessages/{insCode}?take=</summary>
    Task<List<SupervisorMessageDto>> GetMessagesAsync(long insCode, int take, CancellationToken cancellationToken);

    /// <summary>
    /// GET api/v1/Report/GetPerformance/{kind}?period=&amp;take=&amp;minValue=&amp;asOf=&amp;from= — kind: TopPerformers,
    /// SteadyCompounders, QualityOnDip; period: 1m, 3m, 6m, ytd, 1y, 3y, 5y (default 1y), or custom with
    /// <paramref name="from"/> (giving <paramref name="from"/> makes the window custom); <paramref name="asOf"/>: the
    /// base date the window ends on (default the latest session; a non-session date falls back to the session before);
    /// take default 50, 1–500; minValue: rials of average value traded per session over the 20 sessions up to the base
    /// date (default 0). A start on or after the end is a 400.
    /// </summary>
    Task<PerformanceReportDto> GetPerformanceReportAsync(string kind, string? period, int take, decimal minValue, string? asOf, string? from, CancellationToken cancellationToken);

    /// <summary>GET api/v1/Report/GetSectorReturns?period=&amp;minValue=&amp;asOf=&amp;from= — the window as in <see cref="GetPerformanceReportAsync"/>.</summary>
    Task<SectorReturnsReportDto> GetSectorReturnsAsync(string? period, decimal minValue, string? asOf, string? from, CancellationToken cancellationToken);

    /// <summary>
    /// GET api/v1/Report/GetSignalTrackRecord?years=&amp;asOf=&amp;from= — the monthly walk-forward test of every ranking
    /// rule over <paramref name="years"/> (default 5, 1–15) or the whole months since <paramref name="from"/>, ending
    /// (with the picks) at <paramref name="asOf"/>.
    /// </summary>
    Task<SignalTrackRecordDto> GetSignalTrackRecordAsync(int years, string? asOf, string? from, CancellationToken cancellationToken);

    /// <summary>GET api/v1/Report/GetSeasonality?asOf=&amp;from= — monthly returns by Jalali month, up to <paramref name="asOf"/>, from the month of <paramref name="from"/>.</summary>
    Task<SeasonalityReportDto> GetSeasonalityAsync(string? asOf, string? from, CancellationToken cancellationToken);

    /// <summary>
    /// GET api/v1/Report/GetShortlist?asOf= — the short-, mid- and long-term shortlists on the base date
    /// <paramref name="asOf"/> (default the latest session), with what each pick did since for a past date.
    /// </summary>
    Task<ShortlistDto> GetShortlistAsync(string? asOf, CancellationToken cancellationToken);

    /// <summary>GET api/v1/MarketData/GetHealth</summary>
    Task<MarketDataHealthDto> GetHealthAsync(CancellationToken cancellationToken);

    /// <summary>POST api/v1/MarketData/RunJob/{job} — queues a job; returns its id.</summary>
    Task<string> RunJobAsync(string job, CancellationToken cancellationToken);

    /// <summary>GET api/v1/MarketData/GetJobs</summary>
    Task<JobQueueDto> GetJobsAsync(CancellationToken cancellationToken);

    /// <summary>GET api/v1/MarketData/GetJob/{id} — a queue entry with its log and traffic; 404 when the queue no longer holds it.</summary>
    Task<JobDetailDto> GetJobAsync(string id, CancellationToken cancellationToken);

    /// <summary>POST api/v1/MarketData/CancelJob/{id} — cancels a queued or running job; false when it already finished.</summary>
    Task<bool> CancelJobAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// GET api/v1/MarketData/GetRuns?job=&amp;since=&amp;take= — the run history, newest first: runs started on or after
    /// <paramref name="since"/> (yyyy-MM-dd, default 30 days ago), optionally of one job, at most <paramref name="take"/>
    /// (default 5,000, 1–50,000).
    /// </summary>
    Task<List<JobRunDto>> GetRunsAsync(string? job, string? since, int take, CancellationToken cancellationToken);

    /// <summary>POST api/v1/Sync/Pull — queues "download another node's bundle, import, reload"; returns the job id.</summary>
    Task<string> PullFromNodeAsync(SyncPullRequestDto request, CancellationToken cancellationToken);
}
