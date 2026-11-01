using Boors.Market.RawCache;
using Microsoft.Extensions.Logging;

namespace Boors.Market.Sources;

public enum CachePolicy
{
    /// <summary>Always fetch from the source (the response is still stored in the raw cache).</summary>
    Network,

    /// <summary>Use the latest cached response if one exists; otherwise fetch.</summary>
    PreferCache,

    /// <summary>Never touch the network; fail if nothing is cached. Used for replay and offline work.</summary>
    CacheOnly,
}

/// <summary>Thrown when a source answers with a non-success status after the resilience pipeline gave up.</summary>
public sealed class SourceRequestException(RawEntry entry)
    : Exception($"{entry.Source} {entry.Endpoint} returned HTTP {entry.StatusCode} ({entry.Url}).")
{
    public RawEntry Entry { get; } = entry;
}

public sealed class NotCachedException(RawRequest request)
    : Exception($"No cached response for {request.Source} {request.Endpoint} [{request.ParamsKey}].")
{
    public RawRequest Request { get; } = request;
}

/// <summary>
/// Base for source clients: every response is written to the raw cache before anything parses it.
/// Rate limiting, retries and timeouts live in the HttpClient's resilience pipeline.
/// </summary>
public abstract class RawSourceClient(HttpClient http, IRawCache cache, TimeProvider timeProvider, ILogger logger)
{
    protected IRawCache Cache => cache;

    public async Task<byte[]> GetBodyAsync(RawRequest request, CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default)
    {
        if (policy != CachePolicy.Network && await cache.GetLatestAsync(request, cancellationToken) is { } cached)
            return await cache.ReadBodyAsync(cached, cancellationToken);

        if (policy == CachePolicy.CacheOnly)
            throw new NotCachedException(request);

        var started = timeProvider.GetTimestamp();
        using var message = new HttpRequestMessage(request.JsonBody is null ? HttpMethod.Get : HttpMethod.Post, request.Url);
        if (request.JsonBody is not null)
            message.Content = new StringContent(request.JsonBody, System.Text.Encoding.UTF8, "application/json");
        foreach (var (name, value) in request.Headers ?? new Dictionary<string, string>())
        {
            message.Headers.Remove(name);
            message.Headers.TryAddWithoutValidation(name, value);
        }

        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var entry = await cache.StoreAsync(request, body, (int)response.StatusCode, response.Content.Headers.ContentType?.ToString(), timeProvider.GetUtcNow(), cancellationToken);

        logger.LogDebug("Fetched {Source} {Endpoint} [{Params}] -> {Status} {Bytes} bytes in {Elapsed} ms",
            request.Source, request.Endpoint, request.ParamsKey, entry.StatusCode, entry.Length, timeProvider.GetElapsedTime(started).TotalMilliseconds);

        if (!entry.IsSuccess)
            throw new SourceRequestException(entry);

        return body;
    }
}
