using Microsoft.Extensions.Logging;

namespace Boors.Market.Services.Jobs;

/// <summary>
/// Turns the log lines written while a job runs into the job's live progress and log, so the operations UI shows them
/// without the collectors knowing about the queue. The collectors' Information lines ("Backfill: 150/1161
/// instruments ...") become the progress text; those lines, the source clients' and the worker's warnings and errors,
/// and the retry policy's events (Polly, Warning and above) go into the running entry's log. One job runs at a time,
/// so a line written while it runs is attributed to it.
/// </summary>
public sealed class JobProgressLoggerProvider(MarketJobQueue queue) : ILoggerProvider
{
    private static readonly string[] ProgressCategories = ["Boors.Market.Ingestion", "Boors.Market.Services.Sync"];

    private static readonly string[] LogCategories =
        ["Boors.Market.Ingestion", "Boors.Market.Services.Sync", "Boors.Market.Sources", "Boors.Market.RawCache", "Boors.Market.Services.Jobs.MarketJobWorker", "Polly"];

    public ILogger CreateLogger(string categoryName) =>
        LogCategories.Any(c => categoryName.StartsWith(c, StringComparison.Ordinal))
            ? new JobLogger(queue, categoryName, ProgressCategories.Any(c => categoryName.StartsWith(c, StringComparison.Ordinal)))
            : NullJobLogger.Instance;

    public void Dispose()
    {
    }

    private sealed class JobLogger(MarketJobQueue queue, string category, bool progress) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            if (exception is not null && !message.Contains(exception.Message, StringComparison.Ordinal))
                message = $"{message}: {exception.Message}";
            if (progress && logLevel == LogLevel.Information)
                queue.ReportProgress(message);

            // Polly's "Resilience event occurred. EventName: 'OnRetry'" is one retry.
            var retry = state is IReadOnlyList<KeyValuePair<string, object?>> properties
                && properties.Any(p => p.Key == "EventName" && Convert.ToString(p.Value, CultureInfo.InvariantCulture) == "OnRetry");
            queue.ReportLog(logLevel.ToString(), category, message, retry);
        }
    }

    private sealed class NullJobLogger : ILogger
    {
        public static readonly NullJobLogger Instance = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}
