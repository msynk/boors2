using Boors.Desktop.Controls;
using Boors.Desktop.Infrastructure;
using Boors.Market.Services;
using Boors.Market.Services.Contracts;
using Boors.Market.Services.Jobs;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Boors.Desktop.ViewModels;

/// <summary>A job in the monitor's table: its record over the chosen period, its schedule and next run.</summary>
public sealed record JobRow(string Job, string Name, JobRunSummary Summary, string Schedule, DateTimeOffset? NextRun, string Strip, bool Runnable);

/// <summary>A row of the run history.</summary>
public sealed record RunRow(JobRunDto Run, string Name, string Outcome, TimeSpan? Duration);

/// <summary>A label and value of the detail pane.</summary>
public sealed record DetailField(string Label, string Value);

/// <summary>A line of a job's log: time, level icon and message; problems (warnings, errors) carry their status tone.</summary>
public sealed record LogRow(string Time, string Icon, string Message, Tone Tone, bool IsProblem, string Plain);

/// <summary>
/// What the detail pane shows: a run from the history, or an entry of the queue (<see cref="EntryId"/>, whose log
/// follows the job while it runs).
/// </summary>
public sealed record JobDetail(
    string Title,
    string Job,
    string StatusText,
    Tone StatusTone,
    IReadOnlyList<DetailField> Fields,
    string? Message,
    string? EntryId,
    bool CanCancel,
    bool CanRunAgain);

/// <summary>
/// The job monitor: what runs now and what waits, every job's record over a period (runs, success rate, duration,
/// failure streak, the last 20 outcomes), runs per day and run time charts, the filterable run history with each run's
/// log, and the HTTP traffic to the sources. Local mode follows the in-process queue live; remote mode polls the node
/// while the page is open.
/// </summary>
public sealed partial class JobsViewModel : PageViewModel
{
    /// <summary>The history the page loads once; the period filter narrows it.</summary>
    public const int LoadedDays = 90;

    private readonly MarketJobQueue queue;
    private readonly SourceTraffic traffic;
    private readonly SettingsService settings;
    private readonly UiDispatcher dispatcher;
    private List<JobRunDto> allRuns = [];
    private JobQueueDto? snapshot;
    private List<LogRow> log = [];
    private int refreshQueued;
    private int finishedSeen = -1;
    private string? runningSeen;
    private CancellationTokenSource? detailLoad;
    private DateTimeOffset lastLogRefresh;
    private string? detailState;
    private IReadOnlyList<KpiItem> runKpis = [];

    public JobsViewModel(IMarketApi api, MarketJobQueue queue, SourceTraffic traffic, SettingsService settings, UiDispatcher dispatcher, ILogger<JobsViewModel> logger) : base(api, logger)
    {
        this.queue = queue;
        this.traffic = traffic;
        this.settings = settings;
        this.dispatcher = dispatcher;
        queue.Changed += (_, _) => OnQueueChanged();
    }

    public static IReadOnlyList<Choice> Periods => [new("1", Loc.T("24 hours")), new("7", Loc.T("7 days")), new("30", Loc.T("30 days")), new("90", Loc.T("90 days"))];

    public static IReadOnlyList<Choice> StatusFilters => [new("all", Loc.T("All runs")), new("problems", Loc.T("Problems")), new("failed", Loc.T("Failed"))];

    public bool IsLocal => settings.Current.Mode != "Remote";

    [ObservableProperty]
    public partial string Period { get; set; } = "7";

    [ObservableProperty]
    public partial string StatusFilter { get; set; } = "all";

    /// <summary>The job the history and the run-time chart show; empty for every job.</summary>
    [ObservableProperty]
    public partial string JobFilter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<KpiItem> Kpis { get; set; } = [];

    [ObservableProperty]
    public partial JobInfoDto? Running { get; set; }

    [ObservableProperty]
    public partial string RunningTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RunningProgress { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RunningCounters { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<JobInfoDto> Waiting { get; set; } = [];

    public bool HasWaiting => Waiting.Count > 0;

    /// <summary>Every entry the queue holds (this session's jobs), newest first.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<JobInfoDto> Entries { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<JobRow> JobRows { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<Choice> JobChoices { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<RunRow> History { get; set; } = [];

    [ObservableProperty]
    public partial string HistorySummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<ChartColumn> DailyColumns { get; set; } = [];

    [ObservableProperty]
    public partial string DailyTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<ChartColumn> DurationColumns { get; set; } = [];

    [ObservableProperty]
    public partial string DurationTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DurationNote { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<SourceTrafficDto> Traffic { get; set; } = [];

    [ObservableProperty]
    public partial string TrafficNote { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool Scheduled { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Notice { get; set; }

    [ObservableProperty]
    public partial JobDetail? Detail { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<LogRow> LogRows { get; set; } = [];

    [ObservableProperty]
    public partial string LogNote { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ProblemsOnly { get; set; }

    /// <summary>The jobs that can be queued by name (from the queue's own list).</summary>
    public IReadOnlyList<string> RunnableJobs => snapshot?.Jobs ?? MarketJobs.Names;

    private DateTimeOffset Now => DateTimeOffset.UtcNow;

    private DateTimeOffset PeriodStart => Now.AddDays(-int.Parse(Period, CultureInfo.InvariantCulture));

    public Task LoadAsync() => RunAsync(async ct =>
    {
        var jobsTask = Api.GetJobsAsync(ct);
        var runsTask = Api.GetRunsAsync(null, Now.AddDays(-LoadedDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 50_000, ct);
        snapshot = await jobsTask;
        allRuns = Interrupted(await runsTask);
        runningSeen = snapshot.Entries.FirstOrDefault(e => e.State == "running")?.Id;
        finishedSeen = snapshot.Entries.Count(e => e.FinishedAt is not null);
        HasData = true;
        Refresh();
    }, "Job monitor");

    /// <summary>Remote mode: the node's queue, every few seconds while the page is open; the history follows when a job finishes.</summary>
    public async Task PollAsync()
    {
        if (IsLocal || IsLoading)
            return;
        try
        {
            ApplyQueue(await Api.GetJobsAsync(CancellationToken.None));
        }
        catch (Exception e)
        {
            Logger.LogInformation("Job queue unavailable: {Message}", e.Message);
        }
    }

    public override void Refresh()
    {
        if (snapshot is null)
            return;
        ApplyQueue(snapshot, reloadOnFinish: false);
        ApplyRuns();
    }

    /// <summary>Once a second while a job runs: the elapsed time and counters of the running card.</summary>
    public void Tick()
    {
        if (Running is { } running)
            ShowRunning(running);
    }

    partial void OnWaitingChanged(IReadOnlyList<JobInfoDto> value) => OnPropertyChanged(nameof(HasWaiting));

    partial void OnPeriodChanged(string value) => ApplyRuns();

    partial void OnStatusFilterChanged(string value) => ApplyHistory();

    partial void OnJobFilterChanged(string value)
    {
        ApplyHistory();
        ApplyDurations();
    }

    partial void OnSearchChanged(string value) => ApplyHistory();

    partial void OnProblemsOnlyChanged(bool value) => LogRows = value ? [.. log.Where(l => l.IsProblem)] : log;

    // ---------------------------------------------------------------- actions

    public async Task RunJobAsync(string job)
    {
        try
        {
            var id = await Api.RunJobAsync(job, CancellationToken.None);
            Notice = Loc.F("Queued {0} (#{1})", HealthViewModel.JobName(job), id);
            if (!IsLocal)
                await PollAsync();
        }
        catch (Exception e)
        {
            Notice = Describe(e);
        }
    }

    public async Task CancelAsync(string id)
    {
        try
        {
            if (!await Api.CancelJobAsync(id, CancellationToken.None))
                Notice = Loc.T("The job had already finished.");
            if (!IsLocal)
                await PollAsync();
        }
        catch (Exception e)
        {
            Notice = Describe(e);
        }
    }

    /// <summary>Cancels every job that has not started yet.</summary>
    public async Task CancelWaitingAsync()
    {
        foreach (var entry in Waiting.ToList())
            await CancelAsync(entry.Id);
    }

    public void CloseDetail()
    {
        detailLoad?.Cancel();
        detailState = null;
        Detail = null;
        log = [];
        LogRows = [];
    }

    /// <summary>A run of the history: its fields and its log (from this session's queue entry, or from the app's log files).</summary>
    public async Task ShowRunAsync(RunRow row)
    {
        var cts = Restart();
        detailState = null;
        var run = row.Run;
        Detail = new JobDetail(row.Name, run.Job, StatusLabel(row.Outcome), StatusTone(row.Outcome),
        [
            new(Loc.T("Started"), Fmt.DateTime(run.StartedAt)),
            new(Loc.T("Finished"), run.FinishedAt is { } f ? Fmt.DateTime(f) : Loc.T("running")),
            new(Loc.T("Duration"), Fmt.Duration(row.Duration)),
            new(Loc.T("Items"), Fmt.Number(run.Items)),
            new(Loc.T("Failures"), Fmt.Number(run.Failures)),
            new(Loc.T("Node"), run.Node),
            new(Loc.T("Run id"), run.RunId),
        ], run.Message, null, false, RunnableJobs.Contains(run.Job));
        SetLog([], Loc.T("Loading the log…"));

        try
        {
            var end = run.FinishedAt ?? Now;
            var entry = Entries.FirstOrDefault(e => e.StartedAt is { } s && s <= run.StartedAt.AddSeconds(1) && (e.FinishedAt ?? Now) >= run.StartedAt);
            if (entry is not null)
            {
                var detail = await Api.GetJobAsync(entry.Id, cts.Token);
                var lines = detail.Log.Where(l => l.At >= run.StartedAt.AddSeconds(-1) && l.At <= end.AddSeconds(1)).ToList();
                SetLog(lines, Loc.F("From this session's job log ({0})", HealthViewModel.JobName(entry.Job)));
                return;
            }

            if (!IsLocal)
            {
                SetLog([], Loc.T("The node keeps the log of its last jobs only; this run is older."));
                return;
            }

            var fromFiles = await RunLogReader.ReadAsync(run.StartedAt, end, cts.Token);
            SetLog(fromFiles, fromFiles.Count > 0
                ? Loc.T("From the app's log files")
                : Loc.T("No log lines: the run was made elsewhere (a node or the CLI) or before the log files were kept."));
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            SetLog([], Describe(e));
        }
    }

    /// <summary>A queue entry: its fields, per-host traffic and log; while it runs, the log follows it.</summary>
    public async Task ShowEntryAsync(string id)
    {
        var cts = Restart();
        try
        {
            ShowEntry(await Api.GetJobAsync(id, cts.Token));
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            Notice = Describe(e);
        }
    }

    private void ShowEntry(JobDetailDto detail)
    {
        var job = detail.Job;
        var fields = new List<DetailField>
        {
            new(Loc.T("Trigger"), TriggerName(job.Trigger)),
            new(Loc.T("Queued"), Fmt.DateTime(job.EnqueuedAt)),
            new(Loc.T("Waited"), job.StartedAt is { } s ? Fmt.Duration(s - job.EnqueuedAt) : Fmt.Duration(Now - job.EnqueuedAt)),
            new(Loc.T("Started"), Fmt.DateTime(job.StartedAt)),
            new(Loc.T("Finished"), Fmt.DateTime(job.FinishedAt)),
            new(Loc.T("Duration"), job.StartedAt is { } started ? Fmt.Duration((job.FinishedAt ?? Now) - started) : Fmt.Dash),
            new(Loc.T("Items"), Fmt.Number(job.Items)),
            new(Loc.T("Failures"), Fmt.Number(job.Failures)),
            new(Loc.T("Requests"), $"{Fmt.Number(job.Requests)} · {Loc.F("{0} failed", Fmt.Number(job.FailedRequests))} · {Loc.F("{0} retries", Fmt.Number(job.Retries))}"),
            new(Loc.T("Average request"), Fmt.Milliseconds(job.AverageRequestMs)),
            new(Loc.T("Warnings / errors"), $"{Fmt.Number(job.Warnings)} / {Fmt.Number(job.Errors)}"),
        };
        fields.AddRange(detail.Traffic.Select(t => new DetailField(t.Host,
            $"{Fmt.Number(t.Requests)} · {Loc.F("{0} failed", Fmt.Number(t.Failed))} · {Fmt.Milliseconds(t.AverageMs)}")));

        var state = detailState = job.State;
        Detail = new JobDetail(HealthViewModel.JobName(job.Job), job.Job, StatusLabel(state), StatusTone(state), fields,
            job.Message ?? job.Progress, job.Id, state is "queued" or "running", RunnableJobs.Contains(job.Job));
        SetLog(detail.Log, detail.Log.Count == 0
            ? state switch { "queued" => Loc.T("Waiting to start."), "running" => Loc.T("No log lines yet."), _ => Loc.T("No log lines.") }
            : Loc.F("{0} lines kept for this job", Fmt.Number(detail.Log.Count)));
        lastLogRefresh = Now;
    }

    private CancellationTokenSource Restart()
    {
        detailLoad?.Cancel();
        return detailLoad = new CancellationTokenSource();
    }

    private void SetLog(IReadOnlyList<JobLogLineDto> lines, string note)
    {
        log = [.. lines.Select(l =>
        {
            var (icon, tone, problem) = l.Level switch
            {
                "Warning" => ("⚠", Tone.Warning, true),
                "Error" or "Critical" => ("✕", Tone.Critical, true),
                _ => ("ℹ", Tone.Neutral, false),
            };
            var time = l.At.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            return new LogRow(Fmt.Ltr(time), icon, l.Message, tone, problem, $"{time} {l.Level} {Short(l.Category)}: {l.Message}");
        })];
        LogRows = ProblemsOnly ? [.. log.Where(l => l.IsProblem)] : log;
        LogNote = note;
    }

    /// <summary>The log as text for the clipboard.</summary>
    public string LogText() => string.Join(Environment.NewLine, log.Select(l => l.Plain));

    private static string Short(string category) => category[(category.LastIndexOf('.') + 1)..];

    // ---------------------------------------------------------------- the queue

    private void OnQueueChanged()
    {
        if (!IsLocal || snapshot is null || Interlocked.Exchange(ref refreshQueued, 1) == 1)
            return;
        dispatcher.Post(() =>
        {
            Interlocked.Exchange(ref refreshQueued, 0);
            ApplyQueue(snapshot with { Entries = queue.Snapshot(), Traffic = traffic.Snapshot() });
        });
    }

    private void ApplyQueue(JobQueueDto queueDto, bool reloadOnFinish = true)
    {
        snapshot = queueDto;
        var entries = queueDto.Entries;
        Entries = entries;
        Waiting = [.. entries.Where(e => e.State == "queued").OrderBy(e => e.EnqueuedAt)];
        Running = entries.FirstOrDefault(e => e.State == "running");
        if (Running is { } running)
            ShowRunning(running);
        Scheduled = queueDto.Scheduled;
        Traffic = queueDto.Traffic ?? [];
        var since = queueDto.SessionStartedAt is { } s ? Loc.F("since {0}", Fmt.DateTime(s)) : string.Empty;
        TrafficNote = $"{Loc.F("{0} requests", Fmt.Number(Traffic.Sum(t => t.Requests)))} · {Loc.F("{0} failed", Fmt.Number(Traffic.Sum(t => t.Failed)))} {since}".Trim();
        StatusText = IsLocal
            ? Loc.T(Scheduled ? "Background download on" : "Background download off")
            : Loc.F("Jobs of the node at {0}", settings.Current.NodeUrl);

        // A job that started or finished changed the history: read it again (and with it the statistics and charts).
        var finished = entries.Count(e => e.FinishedAt is not null);
        if (reloadOnFinish && (finished != finishedSeen || Running?.Id != runningSeen))
        {
            finishedSeen = finished;
            runningSeen = Running?.Id;
            _ = ReloadRunsAsync();
        }

        // The detail pane follows a queue entry live (at most once a second).
        if (Detail?.EntryId is { } id && entries.FirstOrDefault(e => e.Id == id) is { } entry
            && (entry.State == "running" || entry.State != detailState) && Now - lastLogRefresh > TimeSpan.FromSeconds(1))
        {
            lastLogRefresh = Now;
            _ = ShowEntryAsync(id);
        }

        BuildKpis();
    }

    private void ShowRunning(JobInfoDto running)
    {
        var elapsed = running.StartedAt is { } s ? Fmt.Duration(Now - s) : string.Empty;
        RunningTitle = $"{HealthViewModel.JobName(running.Job)} · {TriggerName(running.Trigger)} · {elapsed}";
        RunningProgress = running.Progress ?? Loc.T("Working…");
        RunningCounters = string.Join(" · ",
            Loc.F("{0} requests", Fmt.Number(running.Requests)),
            Loc.F("{0} failed", Fmt.Number(running.FailedRequests)),
            Loc.F("{0} retries", Fmt.Number(running.Retries)),
            Loc.F("{0} warnings", Fmt.Number(running.Warnings)),
            Loc.F("avg {0}", Fmt.Milliseconds(running.AverageRequestMs)));
    }

    private async Task ReloadRunsAsync()
    {
        try
        {
            allRuns = Interrupted(await Api.GetRunsAsync(null, Now.AddDays(-LoadedDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 50_000, CancellationToken.None));
            ApplyRuns();
        }
        catch (Exception e)
        {
            Logger.LogInformation("Run history unavailable: {Message}", e.Message);
        }
    }

    /// <summary>Runs left "running" by a process that died are shown as interrupted; the queue's running job is not.</summary>
    private List<JobRunDto> Interrupted(List<JobRunDto> runs)
    {
        var running = snapshot?.Entries.FirstOrDefault(e => e.State == "running");
        return JobRunStatistics.MarkInterrupted(runs, running?.Job, running?.StartedAt);
    }

    // ---------------------------------------------------------------- the history and statistics

    private List<JobRunDto> PeriodRuns => [.. allRuns.Where(r => r.StartedAt >= PeriodStart)];

    private void ApplyRuns()
    {
        if (snapshot is null)
            return;
        var runs = PeriodRuns;
        var schedule = snapshot.Schedule;
        var collectorJobs = snapshot.Jobs.Where(j => j is not (MarketJobs.Reload or MarketJobs.Sync));
        JobRows = [.. JobRunStatistics.Summarize(runs, collectorJobs).Select(s =>
        {
            var entries = schedule.Where(e => e.Job == s.Job).ToList();
            var strip = Fmt.Ltr(string.Concat(s.Recent.AsEnumerable().Reverse().Select(Glyph)));
            return new JobRow(s.Job, HealthViewModel.JobName(s.Job), s,
                entries.Count == 0 ? Loc.T("On demand") : string.Join(" · ", entries.Select(e => ScheduleText.Describe(e.Cron))),
                entries.Select(e => e.NextRunAt).Where(t => t is not null).Min(), strip, snapshot.Jobs.Contains(s.Job));
        })];
        JobChoices = [new(string.Empty, Loc.T("All jobs")), .. JobRows.Select(r => new Choice(r.Job, r.Name))];
        ApplyDaily(runs);
        ApplyHistory();
        ApplyDurations();
        runKpis = RunKpis(runs);
        BuildKpis();
    }

    private void ApplyHistory()
    {
        IEnumerable<JobRunDto> runs = PeriodRuns;
        if (!string.IsNullOrEmpty(JobFilter))
            runs = runs.Where(r => r.Job == JobFilter);
        var text = Search.Trim();
        if (text.Length > 0)
        {
            runs = runs.Where(r => (r.Message?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
                || r.Job.Contains(text, StringComparison.OrdinalIgnoreCase)
                || HealthViewModel.JobName(r.Job).Contains(text, StringComparison.OrdinalIgnoreCase)
                || r.RunId.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        var rows = runs.Select(r => new RunRow(r, HealthViewModel.JobName(r.Job), JobRunStatistics.Outcome(r), r.FinishedAt - r.StartedAt))
            .Where(r => StatusFilter switch
            {
                "problems" => JobRunStatistics.IsProblem(r.Outcome),
                "failed" => r.Outcome == "failed",
                _ => true,
            })
            .ToList();
        History = rows;
        HistorySummary = string.Join(" · ",
            Runs(rows.Count),
            $"✕ {Loc.F("{0} failed", Fmt.Number(rows.Count(r => r.Outcome == "failed")))}",
            $"⚠ {Loc.F("{0} partial", Fmt.Number(rows.Count(r => r.Outcome == "partial")))}");
    }

    /// <summary>Runs per hour (24 hours) or per day, stacked by outcome.</summary>
    private void ApplyDaily(List<JobRunDto> runs)
    {
        var columns = new List<ChartColumn>();
        if (Period == "1")
        {
            DailyTitle = Loc.T("Runs per hour, by outcome");
            var start = new DateTimeOffset(Now.Year, Now.Month, Now.Day, Now.Hour, 0, 0, TimeSpan.Zero).AddHours(-23);
            for (var i = 0; i < 24; i++)
            {
                var from = start.AddHours(i);
                var hour = runs.Where(r => r.StartedAt >= from && r.StartedAt < from.AddHours(1)).Select(JobRunStatistics.Outcome).ToList();
                var label = i % 4 == 0 || i == 23 ? Fmt.Clock(from) : null;
                columns.Add(Column(Fmt.DateTime(from), label, hour));
            }
        }
        else
        {
            DailyTitle = Loc.T("Runs per day, by outcome");
            var zone = TimeZoneInfo.Local;
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now, zone).DateTime);
            var days = JobRunStatistics.Daily(runs, today.AddDays(1 - int.Parse(Period, CultureInfo.InvariantCulture)), today, zone);
            var every = Math.Max(1, (int)Math.Ceiling(days.Count / 6.0));
            for (var i = 0; i < days.Count; i++)
            {
                var d = days[i];
                var outcomes = Enumerable.Repeat("ok", d.Ok).Concat(Enumerable.Repeat("skipped", d.Skipped)).Concat(Enumerable.Repeat("partial", d.Partial))
                    .Concat(Enumerable.Repeat("failed", d.Failed)).Concat(Enumerable.Repeat("cancelled", d.Cancelled)).ToList();
                var label = i % every == 0 || i == days.Count - 1 ? Fmt.Digits(Fmt.DateText(d.Day)[5..]) : null;
                columns.Add(Column(Fmt.Date(d.Day), label, outcomes));
            }
        }

        DailyColumns = columns;
    }

    private static ChartColumn Column(string when, string? label, List<string> outcomes)
    {
        int Count(string o) => outcomes.Count(x => x == o || (o == "cancelled" && x == "interrupted"));
        var tip = $"{when}: {Runs(outcomes.Count)}\n"
            + $"✓ {Fmt.Number(Count("ok"))} {Loc.T("OK")} · ↷ {Fmt.Number(Count("skipped"))} {Loc.T("Skipped")} · ⚠ {Fmt.Number(Count("partial"))} {Loc.T("Partial")} · "
            + $"✕ {Fmt.Number(Count("failed"))} {Loc.T("Failed")} · ⊘ {Fmt.Number(Count("cancelled"))} {Loc.T("Cancelled")}";
        return new ChartColumn(
        [
            (Count("ok"), Tone.Good),
            (Count("skipped"), Tone.Neutral),
            (Count("partial"), Tone.Warning),
            (Count("failed"), Tone.Critical),
            (Count("cancelled"), Tone.Muted),
        ], label, tip);
    }

    /// <summary>Run time of the chosen job (or the busiest one), its newest 60 runs in the period, oldest first.</summary>
    private void ApplyDurations()
    {
        var job = !string.IsNullOrEmpty(JobFilter) ? JobFilter
            : PeriodRuns.GroupBy(r => r.Job).OrderByDescending(g => g.Sum(r => (r.FinishedAt - r.StartedAt)?.TotalSeconds ?? 0)).Select(g => g.Key).FirstOrDefault();
        if (job is null)
        {
            DurationTitle = Loc.T("Run time");
            DurationNote = Loc.T("No runs in this period.");
            DurationColumns = [];
            return;
        }

        var runs = PeriodRuns.Where(r => r.Job == job && r.FinishedAt is not null).Take(60).Reverse().ToList();
        DurationTitle = Loc.F("Run time: {0}", HealthViewModel.JobName(job));
        var durations = runs.Select(r => (r.FinishedAt!.Value - r.StartedAt).TotalSeconds).ToList();
        var note = new List<string>();
        if (durations.Count > 0)
        {
            note.Add(Runs(durations.Count));
            note.Add(Loc.F("average {0}", Fmt.Duration(TimeSpan.FromSeconds(durations.Average()))));
            note.Add(Loc.F("longest {0}", Fmt.Duration(TimeSpan.FromSeconds(durations.Max()))));
        }
        else
        {
            note.Add(Loc.T("No finished runs in this period."));
        }

        if (string.IsNullOrEmpty(JobFilter))
            note.Add(Loc.T("the job with the most run time; pick a job to change it"));
        DurationNote = string.Join(" · ", note);
        var every = Math.Max(1, (int)Math.Ceiling(runs.Count / 6.0));
        var oneDay = runs.Count > 0 && runs[^1].StartedAt - runs[0].StartedAt < TimeSpan.FromDays(1);
        DurationColumns = [.. runs.Select((r, i) =>
        {
            var outcome = JobRunStatistics.Outcome(r);
            var label = i % every == 0 || i == runs.Count - 1 ? (oneDay ? Fmt.Clock(r.StartedAt) : Fmt.Digits(Fmt.DateText(DateOnly.FromDateTime(r.StartedAt.ToLocalTime().DateTime))[5..])) : null;
            var tip = $"{Fmt.DateTime(r.StartedAt)} · {Fmt.Duration(r.FinishedAt - r.StartedAt)}\n{StatusLabel(outcome)} · {Loc.T("Items")} {Fmt.Number(r.Items)}";
            return new ChartColumn([(durations[i], Tone.Accent)], label, tip);
        })];
    }

    /// <summary>The tiles that depend on the run history of the period (runs, success rate, failing jobs, items).</summary>
    private IReadOnlyList<KpiItem> RunKpis(List<JobRunDto> runs)
    {
        var outcomes = runs.Select(JobRunStatistics.Outcome).ToList();
        int Count(string o) => outcomes.Count(x => x == o);
        var finished = outcomes.Count(o => o is "ok" or "skipped" or "partial" or "failed");
        var rate = finished == 0 ? (double?)null : (double)(Count("ok") + Count("skipped")) / finished;
        var failing = JobRows.Where(r => r.Summary.FailureStreak > 0).ToList();
        var period = Periods.First(p => p.Key == Period).Text;

        // Daily success rate for the sparkline (days without finished runs are left out).
        var spark = JobRunStatistics.Daily(runs, DateOnly.FromDateTime(PeriodStart.LocalDateTime), DateOnly.FromDateTime(Now.LocalDateTime), TimeZoneInfo.Local)
            .Where(d => d.Ok + d.Skipped + d.Partial + d.Failed > 0)
            .Select(d => (double)(d.Ok + d.Skipped) / (d.Ok + d.Skipped + d.Partial + d.Failed))
            .ToList();

        return
        [
            new KpiItem(Loc.F("Runs, {0}", period), Fmt.Number(outcomes.Count),
                Note: Fmt.Ltr($"✓ {Fmt.Number(Count("ok") + Count("skipped"))} · ⚠ {Fmt.Number(Count("partial"))} · ✕ {Fmt.Number(Count("failed"))} · ⊘ {Fmt.Number(Count("cancelled") + Count("interrupted"))}")),
            new KpiItem(Loc.F("Success rate, {0}", period), rate is null ? Fmt.Dash : $"{(rate >= 0.95 ? "✓" : "⚠")} {Fmt.Percent(rate)}",
                rate is null ? Tone.Neutral : rate >= 0.95 ? Tone.Good : Tone.Warning, Note: Loc.T("OK or skipped, of finished runs"), Spark: spark),
            new KpiItem(Loc.T("Failing jobs"), failing.Count == 0 ? $"✓ {Fmt.Number(0)}" : $"✕ {Fmt.Number(failing.Count)}",
                failing.Count == 0 ? Tone.Good : Tone.Critical,
                Note: failing.Count == 0 ? Loc.T("Every job's last run succeeded") : string.Join(Loc.IsPersian ? "، " : ", ", failing.Select(f => f.Name))),
            new KpiItem(Loc.F("Items, {0}", period), Fmt.Number(runs.Sum(r => (long)r.Items)), Note: Loc.F("{0} item failures", Fmt.Number(runs.Sum(r => (long)r.Failures)))),
        ];
    }

    /// <summary>All tiles: the live ones from the queue (cheap, on every queue event) around the cached run-history ones.</summary>
    private void BuildKpis()
    {
        if (snapshot is null)
            return;
        var next = snapshot.Schedule.Where(s => s.NextRunAt is not null).OrderBy(s => s.NextRunAt).FirstOrDefault();
        var traffic = snapshot.Traffic ?? [];
        var requests = traffic.Sum(t => t.Requests);
        var failedRequests = traffic.Sum(t => t.Failed);
        var averageMs = requests == 0 ? (double?)null : traffic.Sum(t => t.AverageMs * t.Requests) / requests;

        Kpis =
        [
            Running is { } running
                ? new KpiItem(Loc.T("Running now"), HealthViewModel.JobName(running.Job), Note: running.Progress ?? Loc.F("Started {0}", Fmt.Clock(running.StartedAt)))
                : new KpiItem(Loc.T("Running now"), Loc.T("Idle"), Note: IsLocal ? Loc.T(Scheduled ? "Background download on" : "Background download off") : Loc.T("The node is idle")),
            new KpiItem(Loc.T("Waiting"), Fmt.Number(Waiting.Count), Note: Waiting.Count > 0 ? Loc.F("Next: {0}", HealthViewModel.JobName(Waiting[0].Job)) : Loc.T("Nothing waiting")),
            .. runKpis,
            new KpiItem(Loc.T("Requests this session"), Fmt.Number(requests),
                Note: $"{Loc.F("{0} failed", Fmt.Number(failedRequests))} · {Loc.F("avg {0}", Fmt.Milliseconds(averageMs))}"),
            next is null
                ? new KpiItem(Loc.T("Next scheduled job"), Fmt.Dash, Note: Loc.T(IsLocal ? "Background download off" : "The node does not schedule jobs"))
                : new KpiItem(Loc.T("Next scheduled job"), HealthViewModel.JobName(next.Job), Note: $"{Fmt.Clock(next.NextRunAt)} · {Fmt.Relative(next.NextRunAt, Now)}"),
        ];
    }

    // ---------------------------------------------------------------- labels

    private static string Runs(int count) => Loc.F(count == 1 ? "{0} run" : "{0} runs", Fmt.Number(count));

    public static string Glyph(string outcome) => outcome switch
    {
        "ok" or "succeeded" => "✓",
        "skipped" => "↷",
        "partial" => "⚠",
        "failed" => "✕",
        "cancelled" => "⊘",
        "interrupted" => "⚡",
        "running" => "⟳",
        "queued" => "…",
        _ => "·",
    };

    public static Tone StatusTone(string outcome) => outcome switch
    {
        "ok" or "succeeded" => Tone.Good,
        "partial" or "running" or "queued" or "interrupted" => Tone.Warning,
        "failed" => Tone.Critical,
        "cancelled" => Tone.Neutral,
        _ => Tone.Info,
    };

    /// <summary>Icon and name of an outcome or a queue state ("✕ Failed"): never colour alone.</summary>
    public static string StatusLabel(string outcome) => $"{Glyph(outcome)} {HealthViewModel.StateName(outcome)}";

    public static string TriggerName(string trigger) => trigger switch
    {
        "manual" => Loc.T("Manual"),
        "schedule" => Loc.T("Schedule"),
        "sync" => Loc.T("Sync"),
        _ => trigger,
    };
}
