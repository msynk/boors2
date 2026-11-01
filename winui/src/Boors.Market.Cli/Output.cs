using System.Text;

namespace Boors.Market.Cli;

internal static class Output
{
    /// <summary>Writes a CSV with a UTF-8 BOM so spreadsheet tools show Persian text correctly.</summary>
    public static async Task WriteCsvAsync(string path, IReadOnlyList<string> header, IEnumerable<IReadOnlyList<object?>> rows, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await writer.WriteLineAsync(string.Join(',', header.Select(Escape)));
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(',', row.Select(v => Escape(Format(v)))));
        }
    }

    public static void Table(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<object?>> rows)
    {
        var data = rows.Select(r => r.Select(Format).ToArray()).ToList();
        var widths = header.Select((h, i) => Math.Max(h.Length, data.Count == 0 ? 0 : data.Max(r => i < r.Length ? r[i].Length : 0))).ToArray();

        Console.WriteLine(string.Join("  ", header.Select((h, i) => h.PadRight(widths[i]))));
        Console.WriteLine(string.Join("  ", widths.Select(w => new string('-', w))));
        foreach (var row in data)
            Console.WriteLine(string.Join("  ", row.Select((v, i) => v.PadRight(widths[i]))));
    }

    public static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly t => t.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:sszzz", CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.####", CultureInfo.InvariantCulture),
        double f => f.ToString("0.####", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
}
