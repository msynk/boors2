using Boors.Market.Ingestion;
using Boors.Market.Services.Jobs;
using Boors.Market.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Boors.Market.Services.Tests;

[TestClass]
public sealed class MonitoringTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 6, 0, 0, TimeSpan.Zero);

    public TestContext TestContext { get; set; } = default!;

    private static JobRunDto Run(string job, int minutes, string status, string? message = null, int items = 10, int failures = 0, int seconds = 30) =>
        new($"{job}-{minutes}", job, "test", T0.AddMinutes(minutes), status == "running" ? null : T0.AddMinutes(minutes).AddSeconds(seconds), status, items, failures, message);

    [TestMethod]
    public void SummariesCountOutcomesRatesAndTheFailureStreak()
    {
        JobRunDto[] runs =
        [
            Run("eod", 0, "ok"),
            Run("eod", 10, "ok", "skipped: the session closes at 12:30"),
            Run("eod", 20, "partial", failures: 2),
            Run("eod", 30, "failed", seconds: 90),
            Run("eod", 40, "cancelled"),
            Run("eod", 50, "failed", seconds: 90),
            Run("eod", 60, "running"),
            Run("macro-import", 5, "ok"),
        ];

        var summaries = JobRunStatistics.Summarize(runs, ["indices", "eod"]);

        Assert.AreSequenceEqual(new[] { "indices", "eod", "macro-import" }, summaries.Select(s => s.Job).ToArray());
        Assert.AreEqual(0, summaries[0].Runs);
        Assert.IsNull(summaries[0].SuccessRate);

        var eod = summaries[1];
        Assert.AreEqual(7, eod.Runs);
        Assert.AreEqual((1, 1, 1, 2, 1), (eod.Ok, eod.Skipped, eod.Partial, eod.Failed, eod.Cancelled));
        Assert.AreEqual(2.0 / 5, eod.SuccessRate!.Value, 1e-9); // ok + skipped over the five finished runs
        Assert.AreEqual(2, eod.FailureStreak);                  // running and cancelled runs neither count nor break it
        Assert.AreEqual("running", eod.Last!.Status);
        Assert.AreEqual("eod-10", eod.LastSuccess!.RunId);
        Assert.AreEqual("eod-50", eod.LastFailure!.RunId);
        Assert.AreEqual(TimeSpan.FromSeconds(90), eod.MaxDuration);
        Assert.AreEqual(TimeSpan.FromSeconds(54), eod.AverageDuration); // (30 + 30 + 30 + 90 + 90) / 5
        Assert.AreSequenceEqual(new[] { "running", "failed", "cancelled", "failed", "partial", "skipped", "ok" }, eod.Recent.ToArray());
        Assert.AreEqual(2, eod.ItemFailures);
    }

    [TestMethod]
    public void RunsLeftRunningByADeadProcessAreInterrupted()
    {
        JobRunDto[] runs = [Run("halts", 0, "running"), Run("backfill", 10, "running"), Run("eod", 20, "ok")];

        var marked = JobRunStatistics.MarkInterrupted(runs, "backfill", T0.AddMinutes(10));

        Assert.AreSequenceEqual(new[] { "interrupted", "running", "ok" }, marked.Select(r => r.Status).ToArray());
        var halts = JobRunStatistics.Summarize(marked, ["halts"])[0];
        Assert.AreEqual((1, (double?)null), (halts.Cancelled, halts.SuccessRate));
        Assert.IsTrue(JobRunStatistics.IsProblem("interrupted"));
        Assert.HasCount(3, JobRunStatistics.MarkInterrupted(runs, null, null).Where(r => r.Status is "interrupted" or "ok"));
    }

    [TestMethod]
    public void DailyCountsCoverEveryDayInTheZone()
    {
        var tehran = Boors.Market.Text.IranTime.Zone;
        // 21:00 UTC on the 26th is 00:30 on the 27th in Tehran.
        JobRunDto[] runs = [Run("eod", -540, "ok"), Run("eod", 0, "failed"), Run("codal", 5, "ok", "skipped: nothing new")];

        var days = JobRunStatistics.Daily(runs, new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 27), tehran);

        Assert.HasCount(3, days);
        Assert.AreEqual(0, days[0].Total);
        Assert.AreEqual(0, days[1].Total);
        Assert.AreEqual((1, 1, 1), (days[2].Ok, days[2].Skipped, days[2].Failed));
    }

    [TestMethod]
    public void LogLinesAndRequestsBelongToTheRunningJobOnly()
    {
        var time = new FakeTimeProvider(T0);
        var queue = new MarketJobQueue(time);
        queue.ReportLog("Warning", "Polly", "before any job");
        queue.ReportRequest("cdn.tsetmc.com", 100, 200, null);

        var info = queue.Enqueue(MarketJobRequest.Collect("eod", "manual"));
        var entry = Running(queue);
        queue.ReportLog("Information", "Boors.Market.Ingestion.MarketCollector", "Backfill: 50/400");
        queue.ReportLog("Warning", "Polly", "Resilience event occurred. EventName: 'OnRetry'", retry: true);
        queue.ReportLog("Error", "Boors.Market.Services.Jobs.MarketJobWorker", "Job failed");
        queue.ReportRequest("cdn.tsetmc.com", 100, 200, null);
        queue.ReportRequest("cdn.tsetmc.com", 300, 502, null);
        queue.ReportRequest("search.codal.ir", 200, null, "connection_error");
        queue.Finish(entry, "failed", 0, 1, "boom");
        queue.ReportLog("Warning", "Polly", "after the job");

        var detail = queue.Detail(info.Id)!;
        Assert.HasCount(3, detail.Log);
        Assert.AreEqual((3, 2, 1, 1, 1), (detail.Job.Requests, detail.Job.FailedRequests, detail.Job.Retries, detail.Job.Warnings, detail.Job.Errors));
        Assert.AreEqual(200, detail.Job.AverageRequestMs!.Value, 1e-9);
        var tsetmc = detail.Traffic.Single(t => t.Host == "cdn.tsetmc.com");
        Assert.AreEqual((2, 1, 502, "HTTP 502"), (tsetmc.Requests, tsetmc.Failed, tsetmc.LastStatus, tsetmc.LastError));
        Assert.AreEqual("connection_error", detail.Traffic.Single(t => t.Host == "search.codal.ir").LastError);
        Assert.IsNull(queue.Detail("unknown"));
    }

    [TestMethod]
    public void TheLogKeepsTheNewestLines()
    {
        var queue = new MarketJobQueue(new FakeTimeProvider(T0));
        var info = queue.Enqueue(MarketJobRequest.Collect("backfill", "manual"));
        Running(queue);
        for (var i = 0; i < MarketJobQueue.LogLimit + 5; i++)
            queue.ReportLog("Information", "Boors.Market.Ingestion", $"line {i}");

        var log = queue.Detail(info.Id)!.Log;
        Assert.HasCount(MarketJobQueue.LogLimit, log);
        Assert.AreEqual("line 5", log[0].Message);
    }

    [TestMethod]
    public void TrafficStatsReportThe95thPercentilePerHost()
    {
        var stats = new TrafficStats();
        for (var i = 1; i <= 100; i++)
            stats.Add("a", i, 200, null, T0);
        stats.Add("b", 10, 404, null, T0);

        var rows = stats.Snapshot();
        Assert.AreEqual("a", rows[0].Host);
        Assert.AreEqual(95, rows[0].P95Ms);
        Assert.AreEqual(50.5, rows[0].AverageMs, 1e-9);
        Assert.AreEqual(100, rows[0].MaxMs);
        Assert.AreEqual((1, 1), (rows[1].Requests, rows[1].Failed));
        Assert.AreEqual((101, 1), (stats.Totals().Requests, stats.Totals().Failed));
    }

    [TestMethod]
    public async Task TheApiServesTheRunHistoryJobDetailsAndCancellation()
    {
        var root = Path.Combine(Path.GetTempPath(), "boors-services-tests", Guid.NewGuid().ToString("N"));
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddBoorsLocalApi(o =>
        {
            o.DataRoot = root;
            o.StorePath = ":memory:";
            o.Node = "test";
        });
        using var host = builder.Build();
        var api = host.Services.GetRequiredService<IMarketApi>();
        var store = host.Services.GetRequiredService<MarketStore>();
        var ct = TestContext.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        store.RecordRun(new CollectionRun("old", "eod", "test", now.AddDays(-40), now.AddDays(-40), "ok", 1, 0, null));
        store.RecordRun(new CollectionRun("a", "eod", "test", now.AddHours(-2), now.AddHours(-2), "failed", 0, 1, "boom"));
        store.RecordRun(new CollectionRun("b", "codal", "test", now.AddHours(-1), now.AddHours(-1), "ok", 3, 0, null));

        Assert.AreSequenceEqual(new[] { "b", "a" }, (await api.GetRunsAsync(null, null, 0, ct)).Select(r => r.RunId).ToArray());
        Assert.AreSequenceEqual(new[] { "a" }, (await api.GetRunsAsync("eod", null, 0, ct)).Select(r => r.RunId).ToArray());
        Assert.HasCount(3, await api.GetRunsAsync(null, now.AddDays(-50).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 0, ct));
        Assert.HasCount(1, await api.GetRunsAsync(null, null, 1, ct));

        var queued = await api.RunJobAsync("codal", ct);
        Assert.AreEqual("queued", (await api.GetJobAsync(queued, ct)).Job.State);
        Assert.IsTrue(await api.CancelJobAsync(queued, ct));
        Assert.IsFalse(await api.CancelJobAsync(queued, ct));
        var missing = await Assert.ThrowsExactlyAsync<MarketApiException>(() => api.GetJobAsync("nope", ct));
        Assert.AreEqual(404, missing.StatusCode);
        Assert.IsNotNull((await api.GetJobsAsync(ct)).Traffic);
    }

    private static MarketJobQueue.Entry Running(MarketJobQueue queue)
    {
        Assert.IsTrue(queue.Reader.TryRead(out var entry));
        queue.Start(entry);
        return entry;
    }
}
