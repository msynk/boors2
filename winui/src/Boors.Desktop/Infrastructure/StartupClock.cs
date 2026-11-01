using Microsoft.Extensions.Logging;

namespace Boors.Desktop.Infrastructure;

/// <summary>
/// Startup timings relative to the process start: window shown, first frame, first page with data. Logged and
/// written to %LOCALAPPDATA%\Boors\logs\startup.txt (one line per launch) for measurement scripts.
/// </summary>
public static class StartupClock
{
    private static readonly DateTime ProcessStart = Process.GetCurrentProcess().StartTime.ToUniversalTime();
    private static readonly List<(string Mark, double Ms)> marks = [];
    private static bool reported;

    public static double Elapsed => (DateTime.UtcNow - ProcessStart).TotalMilliseconds;

    public static void Mark(string name)
    {
        lock (marks)
        {
            if (marks.All(m => m.Mark != name))
                marks.Add((name, Elapsed));
        }
    }

    public static double? Get(string name)
    {
        lock (marks)
            return marks.FirstOrDefault(m => m.Mark == name) is { Mark: not null } m ? m.Ms : null;
    }

    public static void Report(ILogger logger)
    {
        string line;
        lock (marks)
        {
            if (reported)
                return;
            reported = true;
            line = string.Join(" ", marks.Select(m => $"{m.Mark}={m.Ms:0}ms"));
        }

        logger.LogInformation("Startup timings: {Timings}", line);
        try
        {
            var directory = Path.Combine(SettingsService.AppFolder, "logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "startup.txt"), $"{DateTimeOffset.Now:O} {line}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }
}
