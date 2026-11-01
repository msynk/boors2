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
        var newEntries = new List<RawEntry>();

        await using (var stream = File.OpenRead(bundlePath))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("raw/", StringComparison.Ordinal) && !e.FullName.EndsWith('/')))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = Path.GetFullPath(Path.Combine(cache.Root, entry.FullName));
                if (!target.StartsWith(Path.GetFullPath(cache.Root), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Bundle entry escapes the cache root: {entry.FullName}");

                if (File.Exists(target))
                {
                    skipped++;
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await entry.ExtractToFileAsync(target, overwrite: false, cancellationToken);
                imported++;

                if (target.EndsWith(".meta.json", StringComparison.Ordinal))
                {
                    await using var meta = File.OpenRead(target);
                    if (await JsonSerializer.DeserializeAsync<RawEntry>(meta, cancellationToken: cancellationToken) is { } rawEntry)
                        newEntries.Add(rawEntry);
                }
            }
        }

        foreach (var rawEntry in newEntries.Where(e => e.IsSuccess).OrderBy(e => e.FetchedAtUtc))
            await cache.RefreshLatestPointerAsync(rawEntry, cancellationToken);

        return (imported, skipped);
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
