using System.Net.Http.Headers;
using Boors.Market.Ingestion;
using Boors.Market.RawCache;
using Boors.Market.Services.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Boors.Market.Services.Sync;

/// <summary>
/// Moves raw-cache entries between nodes over HTTP. The raw cache is the shared format of every Boors implementation
/// (raw/{source}/{endpoint}/{yyyy}/{MM}/{dd}/{time}-{hash}-{node}.json.gz + .meta.json), so a bundle exported by
/// Boors.Node, the boilerplate server or <c>boors cache export</c> imports here and vice versa.
/// </summary>
public sealed class NodeSync(
    IRawCache rawCache,
    IHttpClientFactory httpClientFactory,
    IOptions<MarketDataOptions> options,
    ILogger<NodeSync> logger)
{
    public const string HttpClientName = "boors-node";

    public const string BundleRoute = "api/v1/Sync/ExportBundle";

    private FileSystemRawCache Cache => rawCache as FileSystemRawCache
        ?? throw new InvalidOperationException("Node sync needs the file-system raw cache.");

    /// <summary>Writes a bundle of the entries fetched on or after <paramref name="since"/> to a temporary file and returns its path.</summary>
    public async Task<(string Path, int Files)> ExportAsync(DateOnly? since, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetFullPath(options.Value.DataRoot), "sync", "out");
        Directory.CreateDirectory(directory);
        foreach (var stale in Directory.EnumerateFiles(directory, "*.zip").Where(f => File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddHours(-1)))
            TryDelete(stale);

        var path = Path.Combine(directory, $"bundle-{options.Value.Node}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.zip");
        var files = await RawCacheBundle.ExportAsync(Cache, path, since, cancellationToken);
        logger.LogInformation("Exported {Files} raw-cache file(s) since {Since} to {Path}", files, since?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "the beginning", path);
        return (path, files);
    }

    /// <summary>Downloads another node's bundle, imports it (a union: existing files are skipped) and replays the cache into the store.</summary>
    public async Task<IReadOnlyList<CollectionResult>> PullAsync(Uri nodeUrl, DateOnly? since, string? operatorToken, MarketCollector collector, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetFullPath(options.Value.DataRoot), "sync", "in");
        Directory.CreateDirectory(directory);
        var bundle = Path.Combine(directory, $"pull-{nodeUrl.Host}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.zip");
        var started = DateTimeOffset.UtcNow;
        try
        {
            var query = since is { } s ? $"?since={s:yyyy-MM-dd}" : string.Empty;
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(nodeUrl, BundleRoute + query));
            if (!string.IsNullOrWhiteSpace(operatorToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", operatorToken);

            var http = httpClientFactory.CreateClient(HttpClientName);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return [new CollectionResult(MarketJobs.Sync, "failed", 0, 1, $"{nodeUrl} answered {(int)response.StatusCode} {response.ReasonPhrase}")];

            var total = response.Content.Headers.ContentLength;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = File.Create(bundle))
            {
                var buffer = new byte[1 << 16];
                long copied = 0, reported = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    copied += read;
                    if (copied - reported >= 4 << 20)
                    {
                        reported = copied;
                        logger.LogInformation("Downloading bundle: {Downloaded:N1} MB{OfTotal}", copied / 1048576.0, total is { } t ? $" of {t / 1048576.0:N1} MB" : string.Empty);
                    }
                }
            }

            logger.LogInformation("Importing bundle from {Node} ({Size:N1} MB)", nodeUrl, new FileInfo(bundle).Length / 1048576.0);
            var (imported, skipped) = await RawCacheBundle.ImportAsync(Cache, bundle, cancellationToken);
            var pulled = new CollectionResult(MarketJobs.Sync, "ok", imported, 0, $"{imported} file(s) imported, {skipped} already present from {nodeUrl}");
            await RecordAsync(nodeUrl, started, imported, skipped);

            if (imported == 0)
                return [pulled];

            logger.LogInformation("Replaying the raw cache into the store ({Imported} new file(s))", imported);
            return [pulled, .. await MarketJobs.ReloadFromCacheAsync(collector, cancellationToken)];
        }
        catch (Exception e) when (e is HttpRequestException or InvalidDataException or IOException || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning("Pull from {Node} failed: {Message}", nodeUrl, e.Message);
            return [new CollectionResult(MarketJobs.Sync, "failed", 0, 1, $"{nodeUrl}: {e.Message}")];
        }
        finally
        {
            TryDelete(bundle);
        }
    }

    /// <summary>The last successful pull from <paramref name="nodeUrl"/> (UTC), so the next pull can ask only for newer entries.</summary>
    public DateTimeOffset? LastPull(Uri nodeUrl)
    {
        var path = StatePath(nodeUrl);
        return File.Exists(path) && DateTimeOffset.TryParse(File.ReadAllText(path).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : null;
    }

    private async Task RecordAsync(Uri nodeUrl, DateTimeOffset started, int imported, int skipped)
    {
        var path = StatePath(nodeUrl);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, started.ToString("O", CultureInfo.InvariantCulture));
        logger.LogInformation("Pulled {Imported} new and {Skipped} known file(s) from {Node}", imported, skipped, nodeUrl);
    }

    private string StatePath(Uri nodeUrl) =>
        Path.Combine(Path.GetFullPath(options.Value.DataRoot), "sync", $"last-pull-{string.Concat(nodeUrl.Authority.Select(c => char.IsLetterOrDigit(c) ? c : '_'))}.txt");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
