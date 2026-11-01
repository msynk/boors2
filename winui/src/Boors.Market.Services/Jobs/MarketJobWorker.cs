using Boors.Market.Ingestion;
using Boors.Market.Services.Sync;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Boors.Market.Services.Jobs;

/// <summary>Runs <see cref="MarketJobQueue"/> entries one at a time (the store's single writer).</summary>
public sealed class MarketJobWorker(
    MarketJobQueue queue,
    IServiceScopeFactory scopes,
    IOptions<MarketDataOptions> options,
    ILogger<MarketJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var entry in queue.Reader.ReadAllAsync(stoppingToken))
        {
            if (entry.State != "queued")
                continue; // cancelled while waiting

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, entry.Cancellation.Token);
            queue.Start(entry);
            var started = Stopwatch.GetTimestamp();
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var collector = scope.ServiceProvider.GetRequiredService<MarketCollector>();
                IReadOnlyList<CollectionResult> results = entry.Request.Job == MarketJobs.Sync
                    ? await scope.ServiceProvider.GetRequiredService<NodeSync>().PullAsync(entry.Request.NodeUrl!, entry.Request.Since, entry.Request.OperatorToken, collector, linked.Token)
                    : await MarketJobs.RunAsync(collector, options.Value, entry.Request.Job, linked.Token);

                var failed = results.Where(r => r.Status == "failed").ToList();
                var message = string.Join("; ", results.Select(r => string.IsNullOrWhiteSpace(r.Message) ? $"{r.Job}: {r.Status}" : $"{r.Job}: {r.Message}"));
                queue.Finish(entry, failed.Count > 0 ? "failed" : "succeeded", results.Sum(r => r.Items), results.Sum(r => r.Failures), message);
                logger.LogInformation("Job {JobId} ({Job}, {Trigger}) finished in {Seconds:0.0} s: {State}, {Items} items, {Failures} failures",
                    entry.Id, entry.Request.Job, entry.Request.Trigger, Stopwatch.GetElapsedTime(started).TotalSeconds, failed.Count > 0 ? "failed" : "succeeded",
                    results.Sum(r => r.Items), results.Sum(r => r.Failures));
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                queue.Finish(entry, "cancelled", 0, 0, stoppingToken.IsCancellationRequested ? "stopped with the host" : "cancelled");
                if (stoppingToken.IsCancellationRequested)
                    return;
            }
            catch (Exception e)
            {
                // A job failure never stops the worker; the entry carries the reason.
                logger.LogError(e, "Job {JobId} ({Job}) failed", entry.Id, entry.Request.Job);
                queue.Finish(entry, "failed", 0, 1, e.Message);
            }
        }
    }
}
