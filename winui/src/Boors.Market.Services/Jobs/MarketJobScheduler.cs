using Boors.Market.Services.Contracts;
using Boors.Market.Storage;
using Boors.Market.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Boors.Market.Services.Jobs;

/// <summary>
/// The boilerplate's Hangfire schedule, in Iran time (Asia/Tehran), with catch-up. Trading days are Saturday–Wednesday
/// (cron 6,0-3); equities close at 13:00 and non-equity ETFs at 18:00, so end of day runs twice. An entry is due when
/// its latest scheduled time has passed since its job last ran (the run history in the store), so jobs missed while the
/// process was not running or the machine slept run once as soon as it is back, and a backfill that hit its per-run
/// cap runs again until the history is complete. Active while <see cref="Enabled"/> (initially
/// <see cref="MarketDataOptions.Collect"/>); jobs land in the same single-writer <see cref="MarketJobQueue"/>.
/// </summary>
public sealed class MarketJobScheduler(
    MarketJobQueue queue,
    IServiceProvider services,
    TimeProvider timeProvider,
    IOptions<MarketDataOptions> options,
    ILogger<MarketJobScheduler> logger) : BackgroundService
{
    /// <summary>
    /// In catch-up order: index history (the calendar) first, missed sessions before end of day (so the end-of-day
    /// append never compares a session with a hole), end of day before the long backfill (a new store shows the market
    /// within minutes), the instrument master before the backfill that walks it, then the rest.
    /// </summary>
    public static readonly IReadOnlyList<(string Id, string Job, string Cron)> Schedule =
    [
        ("indices", "indices", "15 1 * * *"),
        ("repair", MarketJobs.Repair, "30 1 * * *"),
        ("eod", "eod", "35 13 * * 6,0-3"),
        ("eod.etf", "eod", "35 18 * * 6,0-3"),
        ("instruments", "instruments", "30 7 * * *"),
        ("backfill", "backfill", "0 2 * * *"),
        ("halts", "halts", "0 3 * * *"),
        ("messages", "messages", "*/30 8-18 * * 6,0-3"),
        ("codal", "codal", "*/15 7-23 * * *"),
        ("macro", "macro", "0 9,13,17,21 * * *"),
        ("funds", "funds", "0 20 * * *"),
    ];

    /// <summary>Longest wait between two checks; bounds how late a job starts after the machine resumes from sleep.</summary>
    public static readonly TimeSpan MaxSleep = TimeSpan.FromMinutes(1);

    private static readonly IReadOnlyList<(string Id, string Job, CronExpression Cron)> Parsed =
        [.. Schedule.Select(s => (s.Id, s.Job, CronExpression.Parse(s.Cron)))];

    private volatile bool enabled = options.Value.Collect;
    private long enabledAtUtcTicks;
    private CancellationTokenSource wake = new();

    /// <summary>
    /// Whether jobs are scheduled. Switching it off cancels the scheduled jobs that have not started (a running one
    /// finishes); switching it on catches up at once, including those.
    /// </summary>
    public bool Enabled
    {
        get => enabled;
        set
        {
            if (enabled == value)
                return;
            enabled = value;
            logger.LogInformation("Market-data collection switched {State}", value ? "on" : "off");
            if (value)
            {
                Interlocked.Exchange(ref enabledAtUtcTicks, timeProvider.GetUtcNow().UtcTicks);
            }
            else
            {
                foreach (var waiting in queue.Snapshot().Where(j => j is { State: "queued", Trigger: "schedule" }))
                    queue.Cancel(waiting.Id);
            }

            Interlocked.Exchange(ref wake, new CancellationTokenSource()).Cancel();
        }
    }

    /// <summary>The schedule with the next run of each entry (null when scheduling is off).</summary>
    public IReadOnlyList<ScheduledJobDto> Describe()
    {
        var now = timeProvider.GetUtcNow();
        return [.. Schedule.Zip(Parsed).Select(x => new ScheduledJobDto(
            x.First.Id, x.First.Job, x.First.Cron, Enabled ? x.Second.Cron.GetNextOccurrence(now, IranTime.Zone) : null))];
    }

    /// <summary>
    /// The jobs to queue at <paramref name="now"/>, in schedule order: an entry is due when its latest scheduled time is
    /// later than the start of its job's last run, or the job never ran (a run that was cancelled, or left "running" by
    /// a crash, does not count); backfill is also due while its last run hit the per-run cap. A job that is queued or
    /// running, or was cancelled from the queue after both its latest scheduled time and the last switch-on, is left
    /// alone.
    /// </summary>
    public IReadOnlyList<string> DueJobs(DateTimeOffset now)
    {
        var runs = services.GetRequiredService<MarketStore>().GetLatestRuns().ToDictionary(r => r.Job);
        var entries = queue.Snapshot();
        var enabledAt = new DateTimeOffset(Interlocked.Read(ref enabledAtUtcTicks), TimeSpan.Zero);
        var due = new List<string>();
        foreach (var (_, job, cron) in Parsed)
        {
            if (due.Contains(job) || cron.GetPreviousOccurrence(now, IranTime.Zone) is not { } scheduledAt)
                continue;

            var latest = entries.FirstOrDefault(e => e.Job == job);
            if (latest is { State: "queued" or "running" } || (latest is { State: "cancelled" } && latest.FinishedAt >= scheduledAt && latest.FinishedAt > enabledAt))
                continue;

            var last = runs.GetValueOrDefault(job);
            var ran = last is { Status: not ("cancelled" or "running") } && last.StartedAt >= scheduledAt;
            var unfinished = job == "backfill" && last is { Status: "ok" or "partial", Items: > 0 } && last.Items + last.Failures >= options.Value.MaxBackfillPerRun;
            if (!ran || unfinished)
                due.Add(job);
        }

        return due;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (enabled)
            logger.LogInformation("Scheduling {Count} market-data jobs in {Zone}", Parsed.Count, IranTime.Zone.Id);
        else
            logger.LogInformation("Market-data collection is off (MarketData:Collect=false); no jobs are scheduled until it is switched on.");

        while (!stoppingToken.IsCancellationRequested)
        {
            // Read the wake token before the switch, so a switch made after this point always ends the wait below.
            var woken = Volatile.Read(ref wake).Token;
            var delay = Timeout.InfiniteTimeSpan;
            if (enabled)
            {
                var now = timeProvider.GetUtcNow();
                QueueDueJobs(now);
                var next = Parsed.Min(p => p.Cron.GetNextOccurrence(now, IranTime.Zone)) ?? now + MaxSleep;
                delay = TimeSpan.FromTicks(Math.Clamp((next - now).Ticks, 0, MaxSleep.Ticks));
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, woken);
            try
            {
                await Task.Delay(delay, timeProvider, linked.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Switched on or off: check again at once.
            }
        }
    }

    private void QueueDueJobs(DateTimeOffset now)
    {
        try
        {
            foreach (var job in DueJobs(now))
            {
                logger.LogInformation("Scheduled job {Job} is due; queuing it", job);
                queue.Enqueue(MarketJobRequest.Collect(job, "schedule"));
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // An unreadable store must not stop the scheduler (or, through the host, the app); the next check retries.
            logger.LogWarning(e, "Due market-data jobs could not be determined: {Message}", e.Message);
        }
    }
}

/// <summary>
/// A five-field cron expression (minute hour day-of-month month day-of-week) with <c>*</c>, <c>*/n</c>, ranges,
/// stepped ranges and lists; day-of-week 0 or 7 is Sunday. Evaluated in a given time zone's wall-clock time.
/// </summary>
public sealed class CronExpression
{
    private readonly bool[] minutes;
    private readonly bool[] hours;
    private readonly bool[] days;
    private readonly bool[] months;
    private readonly bool[] weekdays;
    private readonly bool anyDay;
    private readonly bool anyWeekday;

    private CronExpression(bool[] minutes, bool[] hours, bool[] days, bool[] months, bool[] weekdays, bool anyDay, bool anyWeekday)
    {
        (this.minutes, this.hours, this.days, this.months, this.weekdays, this.anyDay, this.anyWeekday) = (minutes, hours, days, months, weekdays, anyDay, anyWeekday);
    }

    public static CronExpression Parse(string expression)
    {
        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
            throw new FormatException($"Cron '{expression}' must have 5 fields.");

        var weekdays = Field(fields[4], 0, 7);
        weekdays[0] |= weekdays[7];
        return new CronExpression(Field(fields[0], 0, 59), Field(fields[1], 0, 23), Field(fields[2], 1, 31), Field(fields[3], 1, 12), weekdays[..7],
            fields[2] == "*", fields[4] == "*");
    }

    /// <summary>The first matching minute strictly after <paramref name="after"/>, within the next 366 days.</summary>
    public DateTimeOffset? GetNextOccurrence(DateTimeOffset after, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(after, zone);
        var start = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Unspecified).AddMinutes(1);
        for (var day = start.Date; day < start.Date.AddDays(366); day = day.AddDays(1))
        {
            if (!months[day.Month] || !DayMatches(day))
                continue;

            for (var h = day == start.Date ? start.Hour : 0; h < 24; h++)
            {
                if (!hours[h])
                    continue;
                for (var m = day == start.Date && h == start.Hour ? start.Minute : 0; m < 60; m++)
                {
                    if (minutes[m])
                    {
                        var wallClock = day.AddHours(h).AddMinutes(m);
                        if (zone.IsInvalidTime(wallClock))
                            continue;
                        return new DateTimeOffset(wallClock, zone.GetUtcOffset(wallClock));
                    }
                }
            }
        }

        return null;
    }

    /// <summary>The last matching minute at or before <paramref name="at"/>, within the previous 366 days.</summary>
    public DateTimeOffset? GetPreviousOccurrence(DateTimeOffset at, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(at, zone);
        var end = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Unspecified);
        for (var day = end.Date; day > end.Date.AddDays(-366); day = day.AddDays(-1))
        {
            if (!months[day.Month] || !DayMatches(day))
                continue;

            for (var h = day == end.Date ? end.Hour : 23; h >= 0; h--)
            {
                if (!hours[h])
                    continue;
                for (var m = day == end.Date && h == end.Hour ? end.Minute : 59; m >= 0; m--)
                {
                    if (minutes[m])
                    {
                        var wallClock = day.AddHours(h).AddMinutes(m);
                        if (zone.IsInvalidTime(wallClock))
                            continue;
                        return new DateTimeOffset(wallClock, zone.GetUtcOffset(wallClock));
                    }
                }
            }
        }

        return null;
    }

    // Standard cron: when both day fields are restricted, either may match.
    private bool DayMatches(DateTime day) =>
        anyDay && anyWeekday ? true
        : anyDay ? weekdays[(int)day.DayOfWeek]
        : anyWeekday ? days[day.Day]
        : days[day.Day] || weekdays[(int)day.DayOfWeek];

    private static bool[] Field(string field, int min, int max)
    {
        var set = new bool[max + 1];
        foreach (var part in field.Split(','))
        {
            var (range, step) = part.Split('/') is [var r, var s] ? (r, int.Parse(s, CultureInfo.InvariantCulture)) : (part, 1);
            var (from, to) = range == "*" ? (min, max)
                : range.Split('-') is [var a, var b] ? (int.Parse(a, CultureInfo.InvariantCulture), int.Parse(b, CultureInfo.InvariantCulture))
                : (int.Parse(range, CultureInfo.InvariantCulture), int.Parse(range, CultureInfo.InvariantCulture));
            if (from < min || to > max || from > to || step < 1)
                throw new FormatException($"Cron field '{field}' is out of range {min}-{max}.");
            for (var v = from; v <= to; v += step)
                set[v] = true;
        }

        return set;
    }
}
