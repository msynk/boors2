using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using Boors.Market.Services.Contracts;

namespace Boors.Market.Services.Remote;

/// <summary>
/// <see cref="IMarketApi"/> over HTTP against a Boors.Node (or any server with the same routes). Problem-details
/// responses come back as <see cref="MarketApiException"/> with the server's status and detail.
/// </summary>
public sealed class RemoteMarketApi(HttpClient http, RemoteMarketApiOptions options) : IMarketApi
{
    private static readonly MarketJsonContext Json = MarketJsonContext.Default;

    public Task<MarketOverviewDto?> GetOverviewAsync(string? date, CancellationToken cancellationToken) =>
        GetAsync($"api/v1/Market/GetOverview{Query(("date", date))}", Json.MarketOverviewDto, cancellationToken, allowEmpty: true);

    public Task<List<BreadthDayDto>> GetBreadthAsync(int sessions, CancellationToken cancellationToken) =>
        GetListAsync($"api/v1/Market/GetBreadth{Query(("sessions", Int(sessions)))}", Json.ListBreadthDayDto, cancellationToken);

    public Task<List<SectorPerformanceDto>> GetSectorsAsync(string? date, CancellationToken cancellationToken) =>
        GetListAsync($"api/v1/Market/GetSectors{Query(("date", date))}", Json.ListSectorPerformanceDto, cancellationToken);

    public Task<List<MoverDto>> GetMoversAsync(string kind, int take, string? date, CancellationToken cancellationToken) =>
        GetListAsync($"api/v1/Market/GetMovers/{Uri.EscapeDataString(kind)}{Query(("take", Int(take)), ("date", date))}", Json.ListMoverDto, cancellationToken);

    public Task<List<ScreenRowDto>> GetScreenAsync(string kind, int take, CancellationToken cancellationToken) =>
        GetListAsync($"api/v1/Market/GetScreen/{Uri.EscapeDataString(kind)}{Query(("take", Int(take)))}", Json.ListScreenRowDto, cancellationToken);

    public Task<List<SeriesPointDto>> GetIndexHistoryAsync(long insCode, string? from, CancellationToken cancellationToken) =>
        GetListAsync($"api/v1/Market/GetIndexHistory/{insCode}{Query(("from", from))}", Json.ListSeriesPointDto, cancellationToken);

    public Task<List<SeriesPointDto>> GetMacroHistoryAsync(string series, string? from, CancellationToken cancellationToken) =>
        GetListAsync($"api/v1/Market/GetMacroHistory/{Uri.EscapeDataString(series)}{Query(("from", from))}", Json.ListSeriesPointDto, cancellationToken);

    public Task<List<InstrumentSearchResultDto>> SearchAsync(string text, int take, CancellationToken cancellationToken) =>
        GetListAsync($"api/v1/Market/Search{Query(("text", text), ("take", Int(take)))}", Json.ListInstrumentSearchResultDto, cancellationToken);

    public async Task<MarketWatchResultDto> GetMarketWatchAsync(MarketWatchRequestDto request, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("api/v1/Market/GetMarketWatch", request, Json.MarketWatchRequestDto, cancellationToken);
        return (await ReadAsync(response, Json.MarketWatchResultDto, cancellationToken))!;
    }

    public async Task<InstrumentMetricsDto> GetMetricsAsync(long insCode, CancellationToken cancellationToken) =>
        (await GetAsync($"api/v1/Instrument/GetMetrics/{insCode}", Json.InstrumentMetricsDto, cancellationToken))!;

    public Task<List<BarDto>> GetBarsAsync(long insCode, string? from, CancellationToken cancellationToken) =>
        GetListAsync($"api/v1/Instrument/GetBars/{insCode}{Query(("from", from))}", Json.ListBarDto, cancellationToken);

    public Task<List<FlowDayDto>> GetFlowsAsync(long insCode, string? from, CancellationToken cancellationToken) =>
        GetListAsync($"api/v1/Instrument/GetFlows/{insCode}{Query(("from", from))}", Json.ListFlowDayDto, cancellationToken);

    public Task<List<CorporateEventDto>> GetEventsAsync(long insCode, CancellationToken cancellationToken) =>
        GetListAsync($"api/v1/Instrument/GetEvents/{insCode}", Json.ListCorporateEventDto, cancellationToken);

    public Task<List<ShareholderDto>> GetShareholdersAsync(long insCode, CancellationToken cancellationToken) =>
        GetListAsync($"api/v1/Instrument/GetShareholders/{insCode}", Json.ListShareholderDto, cancellationToken);

    public Task<List<CodalLetterDto>> GetLettersAsync(long insCode, int take, CancellationToken cancellationToken) =>
        GetListAsync($"api/v1/Instrument/GetLetters/{insCode}{Query(("take", Int(take)))}", Json.ListCodalLetterDto, cancellationToken);

    public Task<List<SupervisorMessageDto>> GetMessagesAsync(long insCode, int take, CancellationToken cancellationToken) =>
        GetListAsync($"api/v1/Instrument/GetMessages/{insCode}{Query(("take", Int(take)))}", Json.ListSupervisorMessageDto, cancellationToken);

    public async Task<PerformanceReportDto> GetPerformanceReportAsync(string kind, string? period, int take, decimal minValue, string? asOf, string? from, CancellationToken cancellationToken) =>
        (await GetAsync($"api/v1/Report/GetPerformance/{Uri.EscapeDataString(kind)}{Query(("period", period), ("take", Int(take)), ("minValue", Dec(minValue)), ("asOf", asOf), ("from", from))}", Json.PerformanceReportDto, cancellationToken))!;

    public async Task<SectorReturnsReportDto> GetSectorReturnsAsync(string? period, decimal minValue, string? asOf, string? from, CancellationToken cancellationToken) =>
        (await GetAsync($"api/v1/Report/GetSectorReturns{Query(("period", period), ("minValue", Dec(minValue)), ("asOf", asOf), ("from", from))}", Json.SectorReturnsReportDto, cancellationToken))!;

    public async Task<SignalTrackRecordDto> GetSignalTrackRecordAsync(int years, string? asOf, string? from, CancellationToken cancellationToken) =>
        (await GetAsync($"api/v1/Report/GetSignalTrackRecord{Query(("years", Int(years)), ("asOf", asOf), ("from", from))}", Json.SignalTrackRecordDto, cancellationToken))!;

    public async Task<SeasonalityReportDto> GetSeasonalityAsync(string? asOf, string? from, CancellationToken cancellationToken) =>
        (await GetAsync($"api/v1/Report/GetSeasonality{Query(("asOf", asOf), ("from", from))}", Json.SeasonalityReportDto, cancellationToken))!;

    public async Task<ShortlistDto> GetShortlistAsync(string? asOf, CancellationToken cancellationToken) =>
        (await GetAsync($"api/v1/Report/GetShortlist{Query(("asOf", asOf))}", Json.ShortlistDto, cancellationToken))!;

    public async Task<MarketDataHealthDto> GetHealthAsync(CancellationToken cancellationToken) =>
        (await GetAsync("api/v1/MarketData/GetHealth", Json.MarketDataHealthDto, cancellationToken))!;

    public async Task<string> RunJobAsync(string job, CancellationToken cancellationToken)
    {
        using var request = Operator(new HttpRequestMessage(HttpMethod.Post, $"api/v1/MarketData/RunJob/{Uri.EscapeDataString(job)}"));
        using var response = await http.SendAsync(request, cancellationToken);
        return await ReadTextAsync(response, cancellationToken);
    }

    public async Task<JobQueueDto> GetJobsAsync(CancellationToken cancellationToken) =>
        (await GetAsync("api/v1/MarketData/GetJobs", Json.JobQueueDto, cancellationToken))!;

    public async Task<JobDetailDto> GetJobAsync(string id, CancellationToken cancellationToken) =>
        (await GetAsync($"api/v1/MarketData/GetJob/{Uri.EscapeDataString(id)}", Json.JobDetailDto, cancellationToken))!;

    public async Task<bool> CancelJobAsync(string id, CancellationToken cancellationToken)
    {
        using var request = Operator(new HttpRequestMessage(HttpMethod.Post, $"api/v1/MarketData/CancelJob/{Uri.EscapeDataString(id)}"));
        using var response = await http.SendAsync(request, cancellationToken);
        return await ReadAsync(response, Json.Boolean, cancellationToken);
    }

    public Task<List<JobRunDto>> GetRunsAsync(string? job, string? since, int take, CancellationToken cancellationToken) =>
        GetListAsync($"api/v1/MarketData/GetRuns{Query(("job", job), ("since", since), ("take", Int(take)))}", Json.ListJobRunDto, cancellationToken);

    public async Task<string> PullFromNodeAsync(SyncPullRequestDto request, CancellationToken cancellationToken)
    {
        using var message = Operator(new HttpRequestMessage(HttpMethod.Post, "api/v1/Sync/Pull") { Content = JsonContent.Create(request, Json.SyncPullRequestDto) });
        using var response = await http.SendAsync(message, cancellationToken);
        return await ReadTextAsync(response, cancellationToken);
    }

    // RunJob and Pull answer with the job id as text/plain, as MVC does for a string result in the boilerplate.
    private static async Task<string> ReadTextAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
            await ReadAsync(response, Json.String, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return text.StartsWith('"') ? JsonSerializer.Deserialize(text, Json.String) ?? text : text;
    }

    private HttpRequestMessage Operator(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(options.OperatorToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.OperatorToken);
        return request;
    }

    private async Task<List<T>> GetListAsync<T>(string url, JsonTypeInfo<List<T>> type, CancellationToken cancellationToken) =>
        await GetAsync(url, type, cancellationToken) ?? [];

    private async Task<T?> GetAsync<T>(string url, JsonTypeInfo<T> type, CancellationToken cancellationToken, bool allowEmpty = false)
    {
        using var response = await http.GetAsync(url, cancellationToken);
        if (allowEmpty && response.StatusCode == HttpStatusCode.NoContent)
            return default;
        return await ReadAsync(response, type, cancellationToken);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> type, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return response.StatusCode == HttpStatusCode.NoContent ? default : await response.Content.ReadFromJsonAsync(type, cancellationToken);

        ApiProblem? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync(Json.ApiProblem, cancellationToken);
        }
        catch (JsonException)
        {
        }

        throw new MarketApiException((int)response.StatusCode, problem?.Detail ?? problem?.Title ?? $"{(int)response.StatusCode} {response.ReasonPhrase}");
    }

    private static string? Int(int value) => value == 0 ? null : value.ToString(CultureInfo.InvariantCulture);

    private static string? Dec(decimal value) => value == 0 ? null : value.ToString(CultureInfo.InvariantCulture);

    private static string Query(params (string Name, string? Value)[] parameters)
    {
        var present = parameters.Where(p => !string.IsNullOrEmpty(p.Value)).Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value!)}").ToList();
        return present.Count == 0 ? string.Empty : "?" + string.Join('&', present);
    }
}

public sealed class RemoteMarketApiOptions
{
    /// <summary>Base URL of the node, e.g. http://localhost:5080/.</summary>
    public Uri BaseAddress { get; set; } = new("http://localhost:5080/");

    /// <summary>Bearer token for RunJob and Sync when the node sets BOORS_OPERATOR_TOKEN.</summary>
    public string? OperatorToken { get; set; }
}
