using System.Diagnostics.Metrics;
using Boors.Market.Services.Contracts;
using Microsoft.Extensions.Hosting;

namespace Boors.Market.Services.Jobs;

/// <summary>
/// HTTP traffic of this process, read from the runtime's own metrics: System.Net.Http records
/// "http.client.request.duration" once per attempt (retries included), tagged with the host, the status code and, when
/// the request failed without a response, the error type. Kept per host since the process started, and every request is
/// also counted against the running job (<see cref="MarketJobQueue.ReportRequest"/>). Requests served from the raw
/// cache never reach the network, so they are not counted.
/// </summary>
public sealed class SourceTraffic(MarketJobQueue queue, TimeProvider timeProvider) : IHostedService, IDisposable
{
    private readonly TrafficStats session = new();
    private MeterListener? listener;

    public DateTimeOffset StartedAt { get; } = timeProvider.GetUtcNow();

    public IReadOnlyList<SourceTrafficDto> Snapshot() => session.Snapshot();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "System.Net.Http" && instrument.Name == "http.client.request.duration")
                    meterListener.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<double>(OnRequest);
        listener.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        listener?.Dispose();
        listener = null;
        return Task.CompletedTask;
    }

    public void Dispose() => listener?.Dispose();

    private void OnRequest(Instrument instrument, double seconds, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        var host = "?";
        int? status = null;
        string? error = null;
        foreach (var (key, value) in tags)
        {
            switch (key)
            {
                case "server.address":
                    host = Convert.ToString(value, CultureInfo.InvariantCulture) ?? host;
                    break;
                case "http.response.status_code" when value is not null:
                    status = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    break;
                case "error.type":
                    error = Convert.ToString(value, CultureInfo.InvariantCulture);
                    break;
            }
        }

        var milliseconds = seconds * 1000;
        session.Add(host, milliseconds, status, error, timeProvider.GetUtcNow());
        queue.ReportRequest(host, milliseconds, status, error);
    }
}

/// <summary>
/// HTTP requests per host: counts, latency (the last <see cref="Window"/> attempts per host for the 95th percentile),
/// and the last status and error. Thread-safe.
/// </summary>
public sealed class TrafficStats
{
    public const int Window = 512;

    private readonly Lock gate = new();
    private readonly Dictionary<string, Host> hosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A failed request: an error without a response, or a status of 400 and above.</summary>
    public static bool IsFailure(int? status, string? error) => error is not null || status >= 400;

    public void Add(string host, double milliseconds, int? status, string? error, DateTimeOffset at)
    {
        lock (gate)
        {
            if (!hosts.TryGetValue(host, out var stats))
                hosts[host] = stats = new Host(host);
            stats.Requests++;
            stats.TotalMs += milliseconds;
            stats.MaxMs = Math.Max(stats.MaxMs, milliseconds);
            if (stats.Recent.Count == Window)
                stats.Recent.Dequeue();
            stats.Recent.Enqueue(milliseconds);
            stats.LastStatus = status;
            stats.LastAt = at;
            if (IsFailure(status, error))
            {
                stats.Failed++;
                stats.LastError = error ?? $"HTTP {status}";
                stats.LastFailedAt = at;
            }
        }
    }

    /// <summary>Requests and failures over every host, and the mean latency (null before the first request).</summary>
    public (int Requests, int Failed, double? AverageMs) Totals()
    {
        lock (gate)
        {
            var requests = hosts.Values.Sum(h => h.Requests);
            return (requests, hosts.Values.Sum(h => h.Failed), requests == 0 ? null : hosts.Values.Sum(h => h.TotalMs) / requests);
        }
    }

    /// <summary>One row per host, busiest first.</summary>
    public IReadOnlyList<SourceTrafficDto> Snapshot()
    {
        lock (gate)
        {
            return [.. hosts.Values.OrderByDescending(h => h.Requests).Select(h => new SourceTrafficDto(
                h.Name, h.Requests, h.Failed, h.Requests == 0 ? 0 : h.TotalMs / h.Requests, Percentile(h.Recent, 0.95), h.MaxMs,
                h.LastStatus, h.LastError, h.LastAt, h.LastFailedAt))];
        }
    }

    private static double Percentile(IEnumerable<double> values, double p)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];
    }

    private sealed class Host(string name)
    {
        public string Name { get; } = name;
        public int Requests { get; set; }
        public int Failed { get; set; }
        public double TotalMs { get; set; }
        public double MaxMs { get; set; }
        public Queue<double> Recent { get; } = new();
        public int? LastStatus { get; set; }
        public string? LastError { get; set; }
        public DateTimeOffset? LastAt { get; set; }
        public DateTimeOffset? LastFailedAt { get; set; }
    }
}
