using System.Threading.Channels;
using Boors.Market.Services.Contracts;

namespace Boors.Market.Services.Jobs;

/// <summary>What to run: a named collection job, or a pull from another node (download bundle → import → reload).</summary>
public sealed record MarketJobRequest(string Job, string Trigger, Uri? NodeUrl = null, DateOnly? Since = null, string? OperatorToken = null)
{
    public static MarketJobRequest Collect(string job, string trigger) => new(job, trigger);

    public static MarketJobRequest Pull(Uri nodeUrl, DateOnly? since, string? operatorToken) => new(MarketJobs.Sync, "sync", nodeUrl, since, operatorToken);
}

/// <summary>
/// The single-writer job queue. DuckDB allows one writer, so every job that writes the store (collection, reload,
/// node sync) goes through this queue and <see cref="MarketJobWorker"/> runs them one at a time, in order. Reads never
/// queue. The queue keeps the last <see cref="HistoryLimit"/> finished entries for the operations UI, each with the log
/// lines and HTTP traffic recorded while it ran (one job runs at a time, so whatever happens then belongs to it).
/// </summary>
public sealed class MarketJobQueue(TimeProvider timeProvider)
{
    public const int HistoryLimit = 50;

    /// <summary>Log lines kept per entry (the newest).</summary>
    public const int LogLimit = 1000;

    private readonly Channel<Entry> channel = Channel.CreateUnbounded<Entry>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Lock gate = new();
    private readonly List<Entry> entries = [];
    private long sequence;

    /// <summary>Raised (on a pool thread) whenever an entry is queued, starts, reports progress or finishes.</summary>
    public event EventHandler? Changed;

    internal ChannelReader<Entry> Reader => channel.Reader;

    /// <summary>Queues a job. A collection job that is already waiting (not yet running) is not queued twice.</summary>
    public JobInfoDto Enqueue(MarketJobRequest request)
    {
        Entry entry;
        lock (gate)
        {
            var waiting = entries.FirstOrDefault(e => e.State == "queued" && e.Request.Job == request.Job && request.Job != MarketJobs.Sync);
            if (waiting is not null)
                return waiting.ToDto();

            entry = new Entry($"{timeProvider.GetUtcNow():yyyyMMddHHmmss}-{Interlocked.Increment(ref sequence)}-{request.Job}", request, timeProvider.GetUtcNow());
            entries.Add(entry);
            Trim();
        }

        channel.Writer.TryWrite(entry);
        OnChanged();
        return entry.ToDto();
    }

    /// <summary>Cancels a queued or running job. Returns false when it already finished or is unknown.</summary>
    public bool Cancel(string id)
    {
        lock (gate)
        {
            var entry = entries.FirstOrDefault(e => e.Id == id);
            if (entry is null || entry.State is not ("queued" or "running"))
                return false;

            entry.Cancellation.Cancel();
            if (entry.State == "queued")
                Finish(entry, "cancelled", 0, 0, "cancelled before it started", raise: false);
        }

        OnChanged();
        return true;
    }

    /// <summary>All known entries, newest first.</summary>
    public IReadOnlyList<JobInfoDto> Snapshot()
    {
        lock (gate)
            return [.. entries.AsEnumerable().Reverse().Select(e => e.ToDto())];
    }

    public JobInfoDto? Find(string id)
    {
        lock (gate)
            return entries.FirstOrDefault(e => e.Id == id)?.ToDto();
    }

    /// <summary>An entry with its log and per-host traffic; null when the queue no longer holds it.</summary>
    public JobDetailDto? Detail(string id)
    {
        lock (gate)
        {
            var entry = entries.FirstOrDefault(e => e.Id == id);
            return entry is null ? null : new JobDetailDto(entry.ToDto(), [.. entry.Log], entry.Traffic.Snapshot());
        }
    }

    /// <summary>The running entry, if any.</summary>
    public JobInfoDto? Current
    {
        get
        {
            lock (gate)
                return entries.FirstOrDefault(e => e.State == "running")?.ToDto();
        }
    }

    /// <summary>Records a progress line for the running job (fed by <see cref="JobProgressLoggerProvider"/> and the sync step).</summary>
    public void ReportProgress(string message)
    {
        lock (gate)
        {
            var running = entries.FirstOrDefault(e => e.State == "running");
            if (running is null)
                return;
            running.Progress = message;
        }

        OnChanged();
    }

    /// <summary>Adds a log line to the running job (fed by <see cref="JobProgressLoggerProvider"/>); ignored when idle.</summary>
    public void ReportLog(string level, string category, string message, bool retry = false)
    {
        lock (gate)
        {
            var running = entries.FirstOrDefault(e => e.State == "running");
            if (running is null)
                return;
            if (running.Log.Count == LogLimit)
                running.Log.RemoveAt(0);
            running.Log.Add(new JobLogLineDto(timeProvider.GetUtcNow(), level, category, message));
            if (level == "Warning")
                running.Warnings++;
            else if (level is "Error" or "Critical")
                running.Errors++;
            if (retry)
                running.Retries++;
        }

        OnChanged();
    }

    /// <summary>Counts an HTTP request (one attempt) against the running job (fed by <see cref="SourceTraffic"/>); ignored when idle.</summary>
    public void ReportRequest(string host, double milliseconds, int? status, string? error)
    {
        lock (gate)
        {
            var running = entries.FirstOrDefault(e => e.State == "running");
            if (running is null)
                return;
            running.Traffic.Add(host, milliseconds, status, error, timeProvider.GetUtcNow());
        }

        OnChanged();
    }

    internal void Start(Entry entry)
    {
        lock (gate)
        {
            entry.State = "running";
            entry.StartedAt = timeProvider.GetUtcNow();
        }

        OnChanged();
    }

    internal void Finish(Entry entry, string state, int items, int failures, string? message, bool raise = true)
    {
        lock (gate)
        {
            entry.State = state;
            entry.FinishedAt = timeProvider.GetUtcNow();
            entry.Items = items;
            entry.Failures = failures;
            entry.Message = message;
            Trim();
        }

        if (raise)
            OnChanged();
    }

    private void Trim()
    {
        var finished = entries.Where(e => e.FinishedAt is not null).ToList();
        foreach (var old in finished.Take(Math.Max(0, finished.Count - HistoryLimit)))
            entries.Remove(old);
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    internal sealed class Entry(string id, MarketJobRequest request, DateTimeOffset enqueuedAt)
    {
        public string Id { get; } = id;
        public MarketJobRequest Request { get; } = request;
        public DateTimeOffset EnqueuedAt { get; } = enqueuedAt;
        public CancellationTokenSource Cancellation { get; } = new();
        public string State { get; set; } = "queued";
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public int Items { get; set; }
        public int Failures { get; set; }
        public string? Message { get; set; }
        public string? Progress { get; set; }
        public List<JobLogLineDto> Log { get; } = [];
        public TrafficStats Traffic { get; } = new();
        public int Warnings { get; set; }
        public int Errors { get; set; }
        public int Retries { get; set; }

        public JobInfoDto ToDto()
        {
            var (requests, failed, averageMs) = Traffic.Totals();
            return new(Id, Request.Job, State, Request.Trigger, EnqueuedAt, StartedAt, FinishedAt, Items, Failures, Message, Progress,
                requests, failed, Retries, Warnings, Errors, averageMs);
        }
    }
}
