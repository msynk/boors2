namespace Boors.Market.Services.Contracts;

// Operations read models: data health (same shape as the boilerplate's MarketDataHealthDto), the job queue and node sync.

/// <summary>Freshness of the market-data store, the last run of every collection job and the last reachability probe.</summary>
public record MarketDataHealthDto(
    DateTimeOffset GeneratedAt,
    string Node,
    DateOnly ExpectedSession,
    bool Healthy,
    IReadOnlyList<DatasetHealthDto> Datasets,
    IReadOnlyList<CollectionRunDto> Runs,
    DateTimeOffset? LastProbeAt,
    IReadOnlyList<SourceReachabilityDto> Sources);

/// <summary><paramref name="Status"/> is Ok, Stale, Empty or Info.</summary>
public record DatasetHealthDto(string Dataset, long Rows, DateOnly? LatestDate, DateOnly? ExpectedDate, string Status);

public record CollectionRunDto(string Job, string Node, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string Status, int Items, int Failures, string? Message);

public record SourceReachabilityDto(string Name, string Verdict, int? Status, string? Error);

/// <summary>
/// One entry of the single-writer job queue. <paramref name="State"/> is queued, running, succeeded, failed or cancelled;
/// <paramref name="Progress"/> is the latest progress line the job logged; <paramref name="Trigger"/> is manual, schedule or sync.
/// The counters cover the time the job ran: HTTP requests to the sources (each retry attempt counts), the ones that
/// failed (an error or a status of 400 and above), retries, and the warnings and errors it logged.
/// </summary>
public record JobInfoDto(
    string Id,
    string Job,
    string State,
    string Trigger,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int Items,
    int Failures,
    string? Message,
    string? Progress,
    int Requests = 0,
    int FailedRequests = 0,
    int Retries = 0,
    int Warnings = 0,
    int Errors = 0,
    double? AverageRequestMs = null);

/// <summary>
/// The queue: jobs that can be run by name, whether a scheduler is active, and recent/queued entries (newest first),
/// plus the HTTP traffic to each host since the process started (<paramref name="SessionStartedAt"/>).
/// </summary>
public record JobQueueDto(
    IReadOnlyList<string> Jobs,
    bool Scheduled,
    IReadOnlyList<ScheduledJobDto> Schedule,
    IReadOnlyList<JobInfoDto> Entries,
    IReadOnlyList<SourceTrafficDto>? Traffic = null,
    DateTimeOffset? SessionStartedAt = null);

/// <summary>
/// A queue entry with its log (the lines the job's collectors, source clients and retry policy wrote while it ran,
/// oldest first, the newest <see cref="Jobs.MarketJobQueue.LogLimit"/>) and its HTTP traffic per host.
/// </summary>
public record JobDetailDto(JobInfoDto Job, IReadOnlyList<JobLogLineDto> Log, IReadOnlyList<SourceTrafficDto> Traffic);

/// <summary><paramref name="Level"/> is Information, Warning, Error or Critical.</summary>
public record JobLogLineDto(DateTimeOffset At, string Level, string Category, string Message);

/// <summary>HTTP requests to one host: counts, latency (each attempt, in milliseconds) and the last status and error.</summary>
public record SourceTrafficDto(
    string Host,
    int Requests,
    int Failed,
    double AverageMs,
    double P95Ms,
    double MaxMs,
    int? LastStatus,
    string? LastError,
    DateTimeOffset? LastAt,
    DateTimeOffset? LastFailedAt);

/// <summary>
/// One row of the store's run history (collection_run): a collector run, from the desktop, a node or the CLI.
/// <paramref name="Status"/> is running, ok, partial, failed or cancelled; a run that decided there was nothing to do
/// is ok with a message starting "skipped".
/// </summary>
public record JobRunDto(string RunId, string Job, string Node, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string Status, int Items, int Failures, string? Message);

/// <summary>A recurring job in Iran time; <paramref name="NextRunAt"/> is null when scheduling is off.</summary>
public record ScheduledJobDto(string Id, string Job, string Cron, DateTimeOffset? NextRunAt);

/// <summary>Pull another node's raw-cache bundle: entries fetched on or after <paramref name="Since"/> (yyyy-MM-dd, default everything).</summary>
public record SyncPullRequestDto(string NodeUrl, string? Since, string? OperatorToken = null);
