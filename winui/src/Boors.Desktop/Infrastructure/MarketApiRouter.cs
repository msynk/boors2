using Boors.Market.Services;
using Boors.Market.Services.Contracts;
using Boors.Market.Services.Local;
using Boors.Market.Services.Remote;

namespace Boors.Desktop.Infrastructure;

/// <summary>
/// The desktop's <see cref="IMarketApi"/>: the in-process store (Local, the offline replica) or a Boors.Node over HTTP
/// (Remote), chosen per call from the current settings so switching modes needs no restart.
/// </summary>
public sealed class MarketApiRouter(IServiceProvider services, SettingsService settings, IHttpClientFactory httpClientFactory) : IMarketApi
{
    private readonly Lock gate = new();
    private (string Url, string? Token, RemoteMarketApi Api)? remote;

    public bool IsRemote => settings.Current.Mode == "Remote";

    private IMarketApi Api
    {
        get
        {
            if (!IsRemote)
                return services.GetRequiredService<LocalMarketApi>();

            lock (gate)
            {
                var url = settings.Current.NodeUrl;
                var token = settings.Current.OperatorToken;
                if (remote is not { } r || r.Url != url || r.Token != token)
                {
                    var http = httpClientFactory.CreateClient("remote-node");
                    http.BaseAddress = new Uri(url.EndsWith('/') ? url : url + "/");
                    http.Timeout = TimeSpan.FromMinutes(2);
                    remote = (url, token, new RemoteMarketApi(http, new RemoteMarketApiOptions { BaseAddress = http.BaseAddress, OperatorToken = token }));
                }

                return remote.Value.Api;
            }
        }
    }

    public Task<MarketOverviewDto?> GetOverviewAsync(string? date, CancellationToken cancellationToken) => Api.GetOverviewAsync(date, cancellationToken);

    public Task<List<BreadthDayDto>> GetBreadthAsync(int sessions, CancellationToken cancellationToken) => Api.GetBreadthAsync(sessions, cancellationToken);

    public Task<List<SectorPerformanceDto>> GetSectorsAsync(string? date, CancellationToken cancellationToken) => Api.GetSectorsAsync(date, cancellationToken);

    public Task<List<MoverDto>> GetMoversAsync(string kind, int take, string? date, CancellationToken cancellationToken) => Api.GetMoversAsync(kind, take, date, cancellationToken);

    public Task<List<ScreenRowDto>> GetScreenAsync(string kind, int take, CancellationToken cancellationToken) => Api.GetScreenAsync(kind, take, cancellationToken);

    public Task<List<SeriesPointDto>> GetIndexHistoryAsync(long insCode, string? from, CancellationToken cancellationToken) => Api.GetIndexHistoryAsync(insCode, from, cancellationToken);

    public Task<List<SeriesPointDto>> GetMacroHistoryAsync(string series, string? from, CancellationToken cancellationToken) => Api.GetMacroHistoryAsync(series, from, cancellationToken);

    public Task<List<InstrumentSearchResultDto>> SearchAsync(string text, int take, CancellationToken cancellationToken) => Api.SearchAsync(text, take, cancellationToken);

    public Task<MarketWatchResultDto> GetMarketWatchAsync(MarketWatchRequestDto request, CancellationToken cancellationToken) => Api.GetMarketWatchAsync(request, cancellationToken);

    public Task<InstrumentMetricsDto> GetMetricsAsync(long insCode, CancellationToken cancellationToken) => Api.GetMetricsAsync(insCode, cancellationToken);

    public Task<List<BarDto>> GetBarsAsync(long insCode, string? from, CancellationToken cancellationToken) => Api.GetBarsAsync(insCode, from, cancellationToken);

    public Task<List<FlowDayDto>> GetFlowsAsync(long insCode, string? from, CancellationToken cancellationToken) => Api.GetFlowsAsync(insCode, from, cancellationToken);

    public Task<List<CorporateEventDto>> GetEventsAsync(long insCode, CancellationToken cancellationToken) => Api.GetEventsAsync(insCode, cancellationToken);

    public Task<List<ShareholderDto>> GetShareholdersAsync(long insCode, CancellationToken cancellationToken) => Api.GetShareholdersAsync(insCode, cancellationToken);

    public Task<List<CodalLetterDto>> GetLettersAsync(long insCode, int take, CancellationToken cancellationToken) => Api.GetLettersAsync(insCode, take, cancellationToken);

    public Task<List<SupervisorMessageDto>> GetMessagesAsync(long insCode, int take, CancellationToken cancellationToken) => Api.GetMessagesAsync(insCode, take, cancellationToken);

    public Task<PerformanceReportDto> GetPerformanceReportAsync(string kind, string? period, int take, decimal minValue, string? asOf, string? from, CancellationToken cancellationToken) =>
        Api.GetPerformanceReportAsync(kind, period, take, minValue, asOf, from, cancellationToken);

    public Task<SectorReturnsReportDto> GetSectorReturnsAsync(string? period, decimal minValue, string? asOf, string? from, CancellationToken cancellationToken) =>
        Api.GetSectorReturnsAsync(period, minValue, asOf, from, cancellationToken);

    public Task<SignalTrackRecordDto> GetSignalTrackRecordAsync(int years, string? asOf, string? from, CancellationToken cancellationToken) =>
        Api.GetSignalTrackRecordAsync(years, asOf, from, cancellationToken);

    public Task<SeasonalityReportDto> GetSeasonalityAsync(string? asOf, string? from, CancellationToken cancellationToken) => Api.GetSeasonalityAsync(asOf, from, cancellationToken);

    public Task<ShortlistDto> GetShortlistAsync(string? asOf, CancellationToken cancellationToken) => Api.GetShortlistAsync(asOf, cancellationToken);

    public Task<MarketDataHealthDto> GetHealthAsync(CancellationToken cancellationToken) => Api.GetHealthAsync(cancellationToken);

    public Task<string> RunJobAsync(string job, CancellationToken cancellationToken) => Api.RunJobAsync(job, cancellationToken);

    public Task<JobQueueDto> GetJobsAsync(CancellationToken cancellationToken) => Api.GetJobsAsync(cancellationToken);

    public Task<JobDetailDto> GetJobAsync(string id, CancellationToken cancellationToken) => Api.GetJobAsync(id, cancellationToken);

    public Task<bool> CancelJobAsync(string id, CancellationToken cancellationToken) => Api.CancelJobAsync(id, cancellationToken);

    public Task<List<JobRunDto>> GetRunsAsync(string? job, string? since, int take, CancellationToken cancellationToken) => Api.GetRunsAsync(job, since, take, cancellationToken);

    public Task<string> PullFromNodeAsync(SyncPullRequestDto request, CancellationToken cancellationToken) => Api.PullFromNodeAsync(request, cancellationToken);
}
