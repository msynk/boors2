using Boors.Market.Services.Contracts;

namespace Boors.Market.Services.Jobs;

/// <summary>
/// One job's record over a window of runs. Outcomes: ok, skipped (ok, nothing to do), partial (finished with failed
/// items), failed, cancelled, interrupted (left "running" by a process that died; the caller decides, see
/// <see cref="JobRunStatistics.MarkInterrupted"/>) and running. <see cref="SuccessRate"/> is (ok + skipped) over the
/// finished runs (cancelled, interrupted and running ones left out); <see cref="Cancelled"/> counts cancelled and
/// interrupted runs; <see cref="FailureStreak"/> counts the failed runs since the last finished one that did not fail;
/// <see cref="Recent"/> holds the newest <see cref="JobRunStatistics.RecentRuns"/> outcomes, newest first.
/// </summary>
public sealed record JobRunSummary(
    string Job,
    int Runs,
    int Ok,
    int Skipped,
    int Partial,
    int Failed,
    int Cancelled,
    double? SuccessRate,
    TimeSpan? AverageDuration,
    TimeSpan? MaxDuration,
    JobRunDto? Last,
    JobRunDto? LastSuccess,
    JobRunDto? LastFailure,
    int FailureStreak,
    long Items,
    long ItemFailures,
    IReadOnlyList<string> Recent);

/// <summary>Runs that started on one day (in the zone the caller chose), by outcome; cancelled includes interrupted.</summary>
public sealed record RunDay(DateOnly Day, int Ok, int Skipped, int Partial, int Failed, int Cancelled)
{
    public int Total => Ok + Skipped + Partial + Failed + Cancelled;
}

/// <summary>Aggregates of the run history (<see cref="JobRunDto"/>) for the job monitor.</summary>
public static class JobRunStatistics
{
    public const int RecentRuns = 20;

    /// <summary>The run's outcome: its status, except an ok run whose message starts with "skipped" is skipped.</summary>
    public static string Outcome(JobRunDto run) =>
        run.Status == "ok" && run.Message?.StartsWith("skipped", StringComparison.OrdinalIgnoreCase) == true ? "skipped" : run.Status;

    public static bool IsSuccess(string outcome) => outcome is "ok" or "skipped";

    /// <summary>Partial, failed, cancelled and interrupted runs: the ones worth a look.</summary>
    public static bool IsProblem(string outcome) => outcome is "partial" or "failed" or "cancelled" or "interrupted";

    /// <summary>
    /// Runs still marked "running" that are not running: every one except a run of <paramref name="runningJob"/> that
    /// started at or after <paramref name="runningSince"/> (the job the queue runs now) becomes "interrupted".
    /// </summary>
    public static List<JobRunDto> MarkInterrupted(IEnumerable<JobRunDto> runs, string? runningJob, DateTimeOffset? runningSince) =>
        [.. runs.Select(r => r.Status == "running" && !(r.Job == runningJob && r.StartedAt >= runningSince?.AddSeconds(-1)) ? r with { Status = "interrupted" } : r)];

    /// <summary>
    /// One summary per job: every name in <paramref name="jobs"/> (in that order, even without runs), then any other
    /// job found in <paramref name="runs"/> (e.g. a CLI import), by name.
    /// </summary>
    public static IReadOnlyList<JobRunSummary> Summarize(IEnumerable<JobRunDto> runs, IEnumerable<string> jobs)
    {
        var byJob = runs.GroupBy(r => r.Job).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.StartedAt).ToList());
        var names = jobs.ToList();
        names.AddRange(byJob.Keys.Except(names).Order(StringComparer.Ordinal));
        return [.. names.Select(job => Summarize(job, byJob.GetValueOrDefault(job) ?? []))];
    }

    private static JobRunSummary Summarize(string job, List<JobRunDto> newestFirst)
    {
        var outcomes = newestFirst.Select(Outcome).ToList();
        int Count(string outcome) => outcomes.Count(o => o == outcome);

        var ok = Count("ok");
        var skipped = Count("skipped");
        var partial = Count("partial");
        var failed = Count("failed");
        var finished = ok + skipped + partial + failed;
        var durations = newestFirst.Where(r => r.FinishedAt is not null && Outcome(r) != "cancelled").Select(r => r.FinishedAt!.Value - r.StartedAt).ToList();
        var streak = outcomes.Where(o => o is not ("running" or "cancelled" or "interrupted")).TakeWhile(o => o == "failed").Count();

        return new JobRunSummary(
            job,
            newestFirst.Count,
            ok,
            skipped,
            partial,
            failed,
            Count("cancelled") + Count("interrupted"),
            finished == 0 ? null : (double)(ok + skipped) / finished,
            durations.Count == 0 ? null : TimeSpan.FromTicks((long)durations.Average(d => d.Ticks)),
            durations.Count == 0 ? null : durations.Max(),
            newestFirst.FirstOrDefault(),
            newestFirst.FirstOrDefault(r => IsSuccess(Outcome(r))),
            newestFirst.FirstOrDefault(r => Outcome(r) is "failed" or "partial"),
            streak,
            newestFirst.Sum(r => (long)r.Items),
            newestFirst.Sum(r => (long)r.Failures),
            [.. outcomes.Take(RecentRuns)]);
    }

    /// <summary>Runs per day from <paramref name="from"/> to <paramref name="to"/> (every day present, oldest first), by outcome.</summary>
    public static IReadOnlyList<RunDay> Daily(IEnumerable<JobRunDto> runs, DateOnly from, DateOnly to, TimeZoneInfo zone)
    {
        var byDay = runs
            .GroupBy(r => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.StartedAt, zone).DateTime))
            .ToDictionary(g => g.Key, g => g.Select(Outcome).ToList());
        var days = new List<RunDay>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var outcomes = byDay.GetValueOrDefault(day) ?? [];
            days.Add(new RunDay(day,
                outcomes.Count(o => o == "ok"), outcomes.Count(o => o == "skipped"), outcomes.Count(o => o == "partial"),
                outcomes.Count(o => o == "failed"), outcomes.Count(o => o is "cancelled" or "interrupted")));
        }

        return days;
    }
}
