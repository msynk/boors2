using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Boors.Desktop.Infrastructure;

/// <summary>
/// Structured JSON-lines log in %LOCALAPPDATA%\Boors\logs\desktop-yyyyMMdd.jsonl, in the Compact Log Event Format
/// (CLEF: @t, @l, @m, @mt, @x, SourceContext) with every named template argument as its own property, so Seq or
/// any CLEF reader can load it. Writes happen on a background thread.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly BlockingCollection<string> lines = new(boundedCapacity: 10_000);
    private readonly Thread writer;
    private readonly string directory;

    public FileLoggerProvider(string directory)
    {
        this.directory = directory;
        Directory.CreateDirectory(directory);
        writer = new Thread(Drain) { IsBackground = true, Name = "Boors log writer" };
        writer.Start();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    public void Dispose()
    {
        lines.CompleteAdding();
        writer.Join(TimeSpan.FromSeconds(2));
    }

    private void Enqueue(string line) => lines.TryAdd(line);

    private void Drain()
    {
        foreach (var line in lines.GetConsumingEnumerable())
        {
            try
            {
                File.AppendAllText(Path.Combine(directory, $"desktop-{DateTime.UtcNow:yyyyMMdd}.jsonl"), line + "\n", Encoding.UTF8);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class FileLogger(string category, FileLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            using var buffer = new MemoryStream();
            using (var json = new Utf8JsonWriter(buffer))
            {
                json.WriteStartObject();
                // Compact Log Event Format (CLEF) envelope: @t time, @l level, @m rendered message, @x exception.
                json.WriteString("@t", DateTimeOffset.UtcNow);
                json.WriteString("@l", logLevel.ToString());
                json.WriteString("@m", formatter(state, exception));
                json.WriteString("SourceContext", category);
                if (state is IReadOnlyList<KeyValuePair<string, object?>> properties)
                {
                    foreach (var (key, value) in properties)
                    {
                        if (key == "{OriginalFormat}")
                        {
                            json.WriteString("@mt", Convert.ToString(value, CultureInfo.InvariantCulture));
                            continue;
                        }
                        json.WriteString(key, Convert.ToString(value, CultureInfo.InvariantCulture));
                    }
                }

                if (exception is not null)
                    json.WriteString("@x", exception.ToString());
                json.WriteEndObject();
            }

            provider.Enqueue(Encoding.UTF8.GetString(buffer.ToArray()));
        }
    }
}
