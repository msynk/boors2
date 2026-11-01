namespace Boors.Market.Services.Contracts;

/// <summary>
/// Source-generated JSON for every API payload: camelCase properties (as the boilerplate's AppJsonContext), dictionary
/// keys left as they are ("1w", "1m"...), case-insensitive reads. Shared by Boors.Node and the remote client.
/// </summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    AllowTrailingCommas = true,
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(MarketDataHealthDto))]
[JsonSerializable(typeof(MarketOverviewDto))]
[JsonSerializable(typeof(List<SeriesPointDto>))]
[JsonSerializable(typeof(List<BreadthDayDto>))]
[JsonSerializable(typeof(List<SectorPerformanceDto>))]
[JsonSerializable(typeof(List<MoverDto>))]
[JsonSerializable(typeof(List<ScreenRowDto>))]
[JsonSerializable(typeof(List<InstrumentSearchResultDto>))]
[JsonSerializable(typeof(MarketWatchRequestDto))]
[JsonSerializable(typeof(MarketWatchResultDto))]
[JsonSerializable(typeof(InstrumentMetricsDto))]
[JsonSerializable(typeof(List<BarDto>))]
[JsonSerializable(typeof(List<FlowDayDto>))]
[JsonSerializable(typeof(List<CorporateEventDto>))]
[JsonSerializable(typeof(List<ShareholderDto>))]
[JsonSerializable(typeof(List<CodalLetterDto>))]
[JsonSerializable(typeof(List<SupervisorMessageDto>))]
[JsonSerializable(typeof(PerformanceReportDto))]
[JsonSerializable(typeof(SectorReturnsReportDto))]
[JsonSerializable(typeof(SignalTrackRecordDto))]
[JsonSerializable(typeof(SeasonalityReportDto))]
[JsonSerializable(typeof(ShortlistDto))]
[JsonSerializable(typeof(JobQueueDto))]
[JsonSerializable(typeof(JobInfoDto))]
[JsonSerializable(typeof(JobDetailDto))]
[JsonSerializable(typeof(List<JobRunDto>))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(SyncPullRequestDto))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(ApiProblem))]
public sealed partial class MarketJsonContext : JsonSerializerContext;

/// <summary>The subset of RFC 9457 problem details the remote client reads back.</summary>
public sealed record ApiProblem(string? Type, string? Title, int? Status, string? Detail);
