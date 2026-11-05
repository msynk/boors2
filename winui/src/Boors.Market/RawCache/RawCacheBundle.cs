using System.IO.Compression;

namespace Boors.Market.RawCache;

/// <summary>
/// Moves raw-cache entries between nodes as a single zip file, over whatever link is up (scp, object storage, a USB
/// stick during a blackout). Entries are write-once and uniquely named by time, request hash and node, so importing
/// is a union: existing files are skipped and latest-pointers only move forward.
/// </summary>
public static class RawCacheBundle
{
    public static async Task<int> ExportAsync(FileSystemRawCache cache, string bundlePath, DateOnly? since, CancellationToken cancellationToken)
    {
        var rawRoot = Path.Combine(cache.Root, "raw");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(bundlePath))!);
        var count = 0;

        await using var stream = new FileStream(bundlePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        if (!Directory.Exists(rawRoot))
            return 0;

        foreach (var file in Directory.EnumerateFiles(rawRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(cache.Root, file).Replace('\\', '/');
            if (relative.EndsWith(".tmp", StringComparison.Ordinal) || (since is { } s && DateFromPath(relative) is { } d && d < s))
                continue;

            // Bodies are already gzip-compressed; only the small JSON sidecars benefit from compression.
            var level = file.EndsWith(".gz", StringComparison.Ordinal) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
            await zip.CreateEntryFromFileAsync(file, relative, level, cancellationToken);
            count++;
        }

        return count;
    }

    public static async Task<(int Imported, int Skipped)> ImportAsync(FileSystemRawCache cache, string bundlePath, CancellationToken cancellationToken)
    {
        int imported = 0, skipped = 0;
        var entries = new List<RawEntry>();
        var rawRoot = Path.GetFullPath(Path.Combine(cache.Root, "raw")) + Path.DirectorySeparatorChar;

        await using (var stream = File.OpenRead(bundlePath))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("raw/", StringComparison.Ordinal) && !e.FullName.EndsWith('/')))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = Path.GetFullPath(Path.Combine(cache.Root, entry.FullName));
                if (!target.StartsWith(rawRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Bundle entry escapes the raw cache: {entry.FullName}");

                var isSidecar = target.EndsWith(".meta.json", StringComparison.Ordinal);
                if (File.Exists(target))
                {
                    // Known files still count for the pointers: an import cut short before them left them unset.
                    skipped++;
                    if (isSidecar && await ReadSidecarAsync(target, cancellationToken) is { } known)
                        entries.Add(known);
                    continue;
                }

                // Through a temporary name, so an interrupted import never leaves a partial file that a retry would take for a known one.
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var partial = target + ".tmp";
                await entry.ExtractToFileAsync(partial, overwrite: true, cancellationToken);
                var sidecar = isSidecar ? await ReadSidecarAsync(partial, cancellationToken) : null;
                if (isSidecar && sidecar is null)
                {
                    File.Delete(partial);
                    skipped++;
                    continue;
                }

                File.Move(partial, target);
                imported++;
                if (sidecar is not null)
                    entries.Add(sidecar);
            }
        }

        foreach (var rawEntry in entries.Where(e => e.IsSuccess).OrderBy(e => e.FetchedAtUtc))
            await cache.RefreshLatestPointerAsync(rawEntry, cancellationToken);

        return (imported, skipped);
    }

    /// <summary>The entry a sidecar describes, or null when it does not parse (a bad file must not stop the rest of the bundle).</summary>
    private static async Task<RawEntry?> ReadSidecarAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var meta = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<RawEntry>(meta, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // raw/{source}/{endpoint}/{yyyy}/{MM}/{dd}/{file}
    private static DateOnly? DateFromPath(string relative)
    {
        var parts = relative.Split('/');
        return parts.Length >= 7
            && int.TryParse(parts[3], CultureInfo.InvariantCulture, out var y)
            && int.TryParse(parts[4], CultureInfo.InvariantCulture, out var m)
            && int.TryParse(parts[5], CultureInfo.InvariantCulture, out var d)
            ? new DateOnly(y, m, d)
            : null;
    }
}
