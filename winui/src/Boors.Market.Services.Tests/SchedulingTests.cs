using Boors.Market.Services.Jobs;
using Boors.Market.Storage;
using Boors.Market.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Boors.Market.Services.Tests;

[TestClass]
public sealed class SchedulingTests
{
    private static readonly TimeZoneInfo Iran = IranTime.Zone;

    private static DateTimeOffset IranLocal(int y, int m, int d, int h, int min) => new(y, m, d, h, min, 0, TimeSpan.FromHours(3.5));

    private static MarketJobScheduler Scheduler(MarketStore store, MarketJobQueue queue, TimeProvider time, bool collect = true) =>
        new(queue, new ServiceCollection().AddSingleton(store).BuildServiceProvider(), time,
            Options.Create(new MarketDataOptions { Collect = collect, MaxBackfillPerRun = 400 }), NullLogger<MarketJobScheduler>.Instance);

    private static void Ran(MarketStore store, string job, DateTimeOffset at, string status = "ok", int items = 1) =>
        store.RecordRun(new CollectionRun($"{at:yyyyMMddHHmmss}-{job}", job, "test", at, at.AddMinutes(1), status, items, 0, null));

    private static readonly string[] AllJobs = ["indices", "repair", "eod", "instruments", "backfill", "halts", "messages", "codal", "macro", "funds"];

    [TestMethod]
    public void EndOfDaySkipsThursdayAndFriday()
    {
        // 2026-10-15 is a Thursday: the next "35 13 * * 6,0-3" run is Saturday 2026-10-17 13:35 Iran time.
        var next = CronExpression.Parse("35 13 * * 6,0-3").GetNextOccurrence(IranLocal(2026, 10, 15, 14, 0), Iran);
        Assert.AreEqual(IranLocal(2026, 10, 17, 13, 35), next);
    }

    [TestMethod]
    public void StepsAndRangesWithinTheDay()
    {
        Assert.AreEqual(IranLocal(2026, 10, 18, 7, 15), CronExpression.Parse("*/15 7-23 * * *").GetNextOccurrence(IranLocal(2026, 10, 18, 7, 1), Iran));
        Assert.AreEqual(IranLocal(2026, 10, 19, 9, 0), CronExpression.Parse("0 9,13,17,21 * * *").GetNextOccurrence(IranLocal(2026, 10, 18, 21, 0), Iran));
        Assert.AreEqual(IranLocal(2026, 10, 18, 8, 30), CronExpression.Parse("*/30 8-18 * * 6,0-3").GetNextOccurrence(IranLocal(2026, 10, 18, 8, 0), Iran));
    }

    [TestMethod]
    public void NextOccurrenceIsStrictlyAfterTheGivenTime()
    {
        var at = IranLocal(2026, 10, 18, 7, 30);
        Assert.AreEqual(IranLocal(2026, 10, 19, 7, 30), CronExpression.Parse("30 7 * * *").GetNextOccurrence(at, Iran));
    }

    [TestMethod]
    public void PreviousOccurrenceIsAtOrBeforeTheGivenTime()
    {
        // Thursday afternoon: the last end of day was Wednesday 13:35; Saturday 13:35 itself counts.
        Assert.AreEqual(IranLocal(2026, 10, 14, 13, 35), CronExpression.Parse("35 13 * * 6,0-3").GetPreviousOccurrence(IranLocal(2026, 10, 15, 14, 0), Iran));
        Assert.AreEqual(IranLocal(2026, 10, 17, 13, 35), CronExpression.Parse("35 13 * * 6,0-3").GetPreviousOccurrence(IranLocal(2026, 10, 17, 13, 35), Iran));
        Assert.AreEqual(IranLocal(2026, 10, 18, 7, 0), CronExpression.Parse("*/15 7-23 * * *").GetPreviousOccurrence(IranLocal(2026, 10, 18, 7, 14), Iran));
        Assert.AreEqual(IranLocal(2026, 10, 17, 23, 45), CronExpression.Parse("*/15 7-23 * * *").GetPreviousOccurrence(IranLocal(2026, 10, 18, 6, 59), Iran));
    }

    [TestMethod]
    public void EveryJobIsDueOnANewStoreInCatchUpOrder()
    {
        var time = new FakeTimeProvider(IranLocal(2026, 10, 18, 10, 0));
        using var store = MarketStore.OpenInMemory();
        var scheduler = Scheduler(store, new MarketJobQueue(time), time);

        Assert.AreSequenceEqual(AllJobs, scheduler.DueJobs(time.GetUtcNow()));
    }

    [TestMethod]
    public void JobsThatRanAfterTheirLatestScheduledTimeAreNotDue()
    {
        var now = IranLocal(2026, 10, 18, 14, 0);
        var time = new FakeTimeProvider(now);
        using var store = MarketStore.OpenInMemory();
        foreach (var job in AllJobs)
            Ran(store, job, IranLocal(2026, 10, 18, 13, 50));
        var scheduler = Scheduler(store, new MarketJobQueue(time), time);

        // Messages and Codal were scheduled again at 14:00; everything else last fell due before 13:50.
        Assert.AreSequenceEqual(new[] { "messages", "codal" }, scheduler.DueJobs(now));
    }

    [TestMethod]
    public void JobsMissedWhileClosedAreCaughtUpOnce()
    {
        // Closed from Wednesday 20:00 to Saturday 10:00: no session was missed (Thursday and Friday do not trade).
        var now = IranLocal(2026, 10, 17, 10, 0);
        var time = new FakeTimeProvider(now);
        using var store = MarketStore.OpenInMemory();
        foreach (var job in AllJobs)
            Ran(store, job, IranLocal(2026, 10, 14, 20, 0));
        var scheduler = Scheduler(store, new MarketJobQueue(time), time);

        Assert.AreSequenceEqual(AllJobs.Where(j => j != "eod").ToArray(), scheduler.DueJobs(now));
    }

    [TestMethod]
    public void BackfillRunsAgainWhileItHitsItsPerRunCap()
    {
        var now = IranLocal(2026, 10, 18, 10, 5);
        var time = new FakeTimeProvider(now);
        using var store = MarketStore.OpenInMemory();
        foreach (var job in AllJobs)
            Ran(store, job, now.AddMinutes(-5));
        var scheduler = Scheduler(store, new MarketJobQueue(time), time);

        store.RecordRun(new CollectionRun("capped-backfill", "backfill", "test", now.AddMinutes(-1), now, "partial", 397, 3, null));
        Assert.AreSequenceEqual(new[] { "backfill" }, scheduler.DueJobs(now));

        store.RecordRun(new CollectionRun("last-backfill", "backfill", "test", now, now, "ok", 120, 0, null));
        Assert.IsEmpty(scheduler.DueJobs(now));
    }

    [TestMethod]
    public void QueuedAndCancelledJobsAreLeftAloneButInterruptedRunsResume()
    {
        var now = IranLocal(2026, 10, 18, 10, 0);
        var time = new FakeTimeProvider(now);
        using var store = MarketStore.OpenInMemory();
        foreach (var job in AllJobs)
            Ran(store, job, now);
        var queue = new MarketJobQueue(time);
        var scheduler = Scheduler(store, queue, time);

        // A run the previous process left cancelled (closed mid-job) or "running" (crashed) does not count.
        Ran(store, "backfill", now.AddMinutes(1), "cancelled");
        Ran(store, "halts", now.AddMinutes(1), "running");
        Assert.AreSequenceEqual(new[] { "backfill", "halts" }, scheduler.DueJobs(now.AddMinutes(2)));

        // Waiting in the queue, or cancelled by the user since it fell due: not queued again.
        queue.Enqueue(MarketJobRequest.Collect("halts", "schedule"));
        var backfill = queue.Enqueue(MarketJobRequest.Collect("backfill", "schedule"));
        queue.Cancel(backfill.Id);
        Assert.IsEmpty(scheduler.DueJobs(now.AddMinutes(2)));
    }

    [TestMethod]
    public void SwitchingOffCancelsWaitingScheduledJobsAndSwitchingOnCatchesUp()
    {
        var now = IranLocal(2026, 10, 18, 10, 0);
        var time = new FakeTimeProvider(now);
        using var store = MarketStore.OpenInMemory();
        var queue = new MarketJobQueue(time);
        var scheduler = Scheduler(store, queue, time);
        var manual = queue.Enqueue(MarketJobRequest.Collect("macro", "manual"));
        foreach (var job in scheduler.DueJobs(now))
            queue.Enqueue(MarketJobRequest.Collect(job, "schedule"));

        scheduler.Enabled = false;
        Assert.IsTrue(queue.Snapshot().Where(j => j.Trigger == "schedule").All(j => j.State == "cancelled"));
        Assert.AreEqual("queued", queue.Find(manual.Id)!.State);

        time.Advance(TimeSpan.FromMinutes(1));
        scheduler.Enabled = true;
        Assert.AreSequenceEqual(AllJobs.Where(j => j != "macro").ToArray(), scheduler.DueJobs(time.GetUtcNow()));
    }

    [TestMethod]
    public void InvalidExpressionsAreRejected()
    {
        Assert.ThrowsExactly<FormatException>(() => CronExpression.Parse("61 * * * *"));
        Assert.ThrowsExactly<FormatException>(() => CronExpression.Parse("* * *"));
    }

    [TestMethod]
    public void ScheduleIsDescribedWithNextRunsOnlyWhenCollecting()
    {
        var time = new FakeTimeProvider(IranLocal(2026, 10, 18, 13, 0));
        using var store = MarketStore.OpenInMemory();
        var scheduler = Scheduler(store, new MarketJobQueue(time), time, collect: false);
        Assert.IsTrue(scheduler.Describe().All(s => s.NextRunAt is null));

        scheduler.Enabled = true;
        var eod = scheduler.Describe().Single(s => s.Id == "eod");
        Assert.AreEqual(IranLocal(2026, 10, 18, 13, 35), eod.NextRunAt);
        Assert.HasCount(MarketJobScheduler.Schedule.Count, scheduler.Describe());
    }

    [TestMethod]
    public void QueueDoesNotQueueAWaitingJobTwiceAndListsNewestFirst()
    {
        var queue = new MarketJobQueue(new FakeTimeProvider(DateTimeOffset.UtcNow));
        var first = queue.Enqueue(MarketJobRequest.Collect("eod", "manual"));
        var again = queue.Enqueue(MarketJobRequest.Collect("eod", "schedule"));
        var other = queue.Enqueue(MarketJobRequest.Collect("macro", "manual"));

        Assert.AreEqual(first.Id, again.Id);
        Assert.HasCount(2, queue.Snapshot());
        Assert.AreEqual(other.Id, queue.Snapshot()[0].Id);
    }

    [TestMethod]
    public void CancellingAQueuedJobFinishesIt()
    {
        var queue = new MarketJobQueue(new FakeTimeProvider(DateTimeOffset.UtcNow));
        var job = queue.Enqueue(MarketJobRequest.Collect("codal", "manual"));
        Assert.IsTrue(queue.Cancel(job.Id));
        Assert.AreEqual("cancelled", queue.Find(job.Id)!.State);
        Assert.IsFalse(queue.Cancel(job.Id));
    }
}
