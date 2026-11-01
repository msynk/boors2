using Boors.Market.Services.Contracts;

namespace Boors.Desktop.Infrastructure;

/// <summary>
/// The log of a past run, read back from this app's own log files (%LOCALAPPDATA%\Boors\logs\desktop-yyyyMMdd.jsonl,
/// one file per UTC day): the collectors', source clients', worker's and retry policy's lines written between the
/// run's start and end. Jobs run one at a time, so those lines are the run's. Runs made by a node or the CLI, or
/// before the files were written, have no lines here.
/// </summary>
public static class RunLogReader
{
    public const int MaxLines = 2000;

    private static readonly string[] Categories =
        ["Boors.Market.Ingestion", "Boors.Market.Services.Sync", "Boors.Market.Sources", "Boors.Market.RawCache", "Boors.Market.Services.Jobs.MarketJobWorker", "Polly"];

    public static string Folder => Path.Combine(SettingsService.AppFolder, "logs");

    /// <summary>Lines from <paramref name="from"/> to <paramref name="to"/> (plus a second either side), oldest first, on a pool thread.</summary>
    public static Task<IReadOnlyList<JobLogLineDto>> ReadAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<JobLogLineDto>>(() =>
        {
            var start = from.AddSeconds(-1);
            var end = to.AddSeconds(1);
            var lines = new List<JobLogLineDto>();
            for (var day = start.UtcDateTime.Date; day <= end.UtcDateTime.Date && lines.Count < MaxLines; day = day.AddDays(1))
            {
                var path = Path.Combine(Folder, $"desktop-{day:yyyyMMdd}.jsonl");
                if (!File.Exists(path))
                    continue;

                // The log writer appends while this reads.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                while (reader.ReadLine() is { } line && lines.Count < MaxLines)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (Parse(line) is { } entry && entry.At >= start && entry.At <= end)
                        lines.Add(entry);
                }
            }

            return lines;
        }, cancellationToken);

    private static JobLogLineDto? Parse(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var category = root.TryGetProperty("SourceContext", out var c) ? c.GetString() ?? string.Empty : string.Empty;
            if (!Categories.Any(p => category.StartsWith(p, StringComparison.Ordinal)))
                return null;
            return new JobLogLineDto(
                root.GetProperty("@t").GetDateTimeOffset(),
                root.TryGetProperty("@l", out var l) ? l.GetString() ?? "Information" : "Information",
                category,
                root.TryGetProperty("@m", out var m) ? m.GetString() ?? string.Empty : string.Empty);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            return null; // a line cut short by a crash
        }
    }
}
