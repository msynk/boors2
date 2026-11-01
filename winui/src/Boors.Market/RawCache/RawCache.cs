using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Boors.Market.RawCache;

/// <summary>
/// Identifies one logical request. <see cref="ParamsKey"/> is a canonical, human-readable parameter string
/// (e.g. "insCode=46348559193224090"); together with source and endpoint it forms the cache key.
/// </summary>
public sealed record RawRequest(string Source, string Endpoint, string Url, string ParamsKey)
{
    public string ParamsHash { get; } = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ParamsKey)))[..16];

    /// <summary>JSON body for POST endpoints (e.g. fipiran fund comparison); null means GET. Must be reflected in <see cref="ParamsKey"/>.</summary>
    public string? JsonBody { get; init; }

    /// <summary>Extra request headers some sources require (e.g. fipiran's Referer).</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
}

/// <summary>Metadata of one stored response. The body lives next to it, gzip-compressed.</summary>
public sealed record RawEntry
{
    public required string Source { get; init; }
    public required string Endpoint { get; init; }
    public required string Url { get; init; }
    public required string ParamsKey { get; init; }
    public required DateTimeOffset FetchedAtUtc { get; init; }
    public required string Node { get; init; }
    public required int StatusCode { get; init; }
    public string? ContentType { get; init; }
    public required long Length { get; init; }
    public required string Sha256 { get; init; }

    /// <summary>Path of the compressed body, relative to the cache root.</summary>
    public required string BodyPath { get; init; }

    [JsonIgnore]
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}

/// <summary>
/// Write-once store of every response fetched from an external source, written before parsing. Parsers are pure
/// functions over this store, so a parser fix means re-parsing, not re-fetching, and the data survives source outages.
/// </summary>
public interface IRawCache
{
    Task<RawEntry> StoreAsync(RawRequest request, ReadOnlyMemory<byte> body, int statusCode, string? contentType, DateTimeOffset fetchedAt, CancellationToken cancellationToken = default);

    /// <summary>The most recent successful response for this request, or null if none was stored.</summary>
    Task<RawEntry?> GetLatestAsync(RawRequest request, CancellationToken cancellationToken = default);

    Task<byte[]> ReadBodyAsync(RawEntry entry, CancellationToken cancellationToken = default);

    IAsyncEnumerable<RawEntry> ListAsync(string source, string endpoint, CancellationToken cancellationToken = default);
}

public sealed class RawCacheOptions
{
    /// <summary>Root directory; raw bodies go under <c>raw/</c>, latest-pointers under <c>latest/</c>.</summary>
    public string Root { get; set; } = ".boors-data";

    /// <summary>Name of the node writing (e.g. "iran-1", "eu-1"); part of every entry so nodes can merge caches.</summary>
    public string Node { get; set; } = Environment.MachineName;
}

/// <summary>
/// File-system implementation. Layout:
/// <c>raw/{source}/{endpoint}/{yyyy}/{MM}/{dd}/{HHmmssfff}-{paramsHash}-{node}.json.gz</c> plus a <c>.meta.json</c> sidecar,
/// and <c>latest/{source}/{endpoint}/{paramsHash}.json</c> pointing at the newest successful entry.
/// Bodies and sidecars are never overwritten; only latest-pointers are.
/// </summary>
public sealed class FileSystemRawCache(RawCacheOptions options) : IRawCache
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string Root => Path.GetFullPath(options.Root);

    public async Task<RawEntry> StoreAsync(RawRequest request, ReadOnlyMemory<byte> body, int statusCode, string? contentType, DateTimeOffset fetchedAt, CancellationToken cancellationToken = default)
    {
        var utc = fetchedAt.ToUniversalTime();
        var node = Sanitize(options.Node);
        var directory = Path.Combine("raw", Sanitize(request.Source), Sanitize(request.Endpoint), utc.ToString("yyyy", CultureInfo.InvariantCulture), utc.ToString("MM", CultureInfo.InvariantCulture), utc.ToString("dd", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(Path.Combine(Root, directory));

        var baseName = $"{utc:HHmmssfff}-{request.ParamsHash}-{node}";
        var bodyRelativePath = await WriteOnceAsync(directory, baseName, body, cancellationToken);

        var entry = new RawEntry
        {
            Source = request.Source,
            Endpoint = request.Endpoint,
            Url = request.Url,
            ParamsKey = request.ParamsKey,
            FetchedAtUtc = utc,
            Node = options.Node,
            StatusCode = statusCode,
            ContentType = contentType,
            Length = body.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(body.Span)),
            BodyPath = bodyRelativePath.Replace('\\', '/'),
        };

        var metaPath = Path.Combine(Root, Path.ChangeExtension(bodyRelativePath, null) + ".meta.json");
        await using (var meta = new FileStream(metaPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(meta, entry, JsonOptions, cancellationToken);

        if (entry.IsSuccess)
            await WriteLatestPointerAsync(request, entry, cancellationToken);

        return entry;
    }

    public async Task<RawEntry?> GetLatestAsync(RawRequest request, CancellationToken cancellationToken = default)
    {
        var path = LatestPointerPath(request.Source, request.Endpoint, request.ParamsHash);
        if (!File.Exists(path))
            return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<RawEntry>(stream, JsonOptions, cancellationToken);
    }

    public async Task<byte[]> ReadBodyAsync(RawEntry entry, CancellationToken cancellationToken = default)
    {
        await using var file = File.OpenRead(Path.Combine(Root, entry.BodyPath));
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var buffer = new MemoryStream();
        await gzip.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    public async IAsyncEnumerable<RawEntry> ListAsync(string source, string endpoint, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(Root, "raw", Sanitize(source), Sanitize(endpoint));
        if (!Directory.Exists(directory))
            yield break;

        foreach (var metaPath in Directory.EnumerateFiles(directory, "*.meta.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(metaPath);
            var entry = await JsonSerializer.DeserializeAsync<RawEntry>(stream, JsonOptions, cancellationToken);
            if (entry is not null)
                yield return entry;
        }
    }

    /// <summary>Points "latest" at an entry that arrived from another node, if it is newer than the current one.</summary>
    public Task RefreshLatestPointerAsync(RawEntry entry, CancellationToken cancellationToken = default) =>
        entry.IsSuccess
            ? WriteLatestPointerAsync(new RawRequest(entry.Source, entry.Endpoint, entry.Url, entry.ParamsKey), entry, cancellationToken)
            : Task.CompletedTask;

    private async Task<string> WriteOnceAsync(string directory, string baseName, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        var temp = Path.Combine(Root, directory, $".{Guid.NewGuid():N}.tmp");
        await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        await using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
            await gzip.WriteAsync(body, cancellationToken);

        // Two writes for the same request in the same millisecond on the same node get a numeric suffix.
        for (var attempt = 0; ; attempt++)
        {
            var name = attempt == 0 ? baseName : $"{baseName}-{attempt}";
            var relative = Path.Combine(directory, name + ".json.gz");
            try
            {
                File.Move(temp, Path.Combine(Root, relative), overwrite: false);
                return relative;
            }
            catch (IOException) when (File.Exists(Path.Combine(Root, relative)) && attempt < 100)
            {
            }
        }
    }

    private async Task WriteLatestPointerAsync(RawRequest request, RawEntry entry, CancellationToken cancellationToken)
    {
        var path = LatestPointerPath(request.Source, request.Endpoint, request.ParamsHash);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Never move the pointer backwards in time (e.g. when a node replays older responses during sync).
        if (await GetLatestAsync(request, cancellationToken) is { } current && current.FetchedAtUtc > entry.FetchedAtUtc)
            return;

        var temp = path + $".{Guid.NewGuid():N}.tmp";
        await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(stream, entry, JsonOptions, cancellationToken);

        File.Move(temp, path, overwrite: true);
    }

    private string LatestPointerPath(string source, string endpoint, string paramsHash) =>
        Path.Combine(Root, "latest", Sanitize(source), Sanitize(endpoint), paramsHash + ".json");

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) || c is '/' or '\\' ? '_' : c).ToArray();
        return new string(chars);
    }
}
