using System.Text;
using Boors.Market.RawCache;

namespace Boors.Market.Tests;

[TestClass]
public sealed class FileSystemRawCacheTests
{
    private string root = null!;
    private FileSystemRawCache cache = null!;

    private static readonly RawRequest Request = new("tsetmc", "ClosingPrice/GetClosingPriceInfo", "https://cdn.tsetmc.com/api/ClosingPrice/GetClosingPriceInfo/1", "insCode=1");
    private static readonly DateTimeOffset T0 = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    [TestInitialize]
    public void Initialize()
    {
        root = Path.Combine(Path.GetTempPath(), "boors-rawcache-tests", Guid.NewGuid().ToString("N"));
        cache = new FileSystemRawCache(new RawCacheOptions { Root = root, Node = "test-node" });
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    [TestMethod]
    public async Task Stored_body_round_trips_and_becomes_latest()
    {
        var entry = await cache.StoreAsync(Request, Encoding.UTF8.GetBytes("{\"a\":1}"), 200, "application/json", T0);

        var latest = await cache.GetLatestAsync(Request);

        Assert.IsNotNull(latest);
        Assert.AreEqual(entry.BodyPath, latest.BodyPath);
        Assert.AreEqual("test-node", latest.Node);
        Assert.AreEqual("{\"a\":1}", Encoding.UTF8.GetString(await cache.ReadBodyAsync(latest)));
    }

    [TestMethod]
    public async Task Every_fetch_is_kept_and_errors_never_become_latest()
    {
        await cache.StoreAsync(Request, Encoding.UTF8.GetBytes("first"), 200, null, T0);
        await cache.StoreAsync(Request, Encoding.UTF8.GetBytes("second"), 200, null, T0.AddMinutes(1));
        await cache.StoreAsync(Request, Encoding.UTF8.GetBytes("blocked"), 403, null, T0.AddMinutes(2));

        var all = new List<RawEntry>();
        await foreach (var e in cache.ListAsync(Request.Source, Request.Endpoint))
            all.Add(e);
        var latest = await cache.GetLatestAsync(Request);

        Assert.HasCount(3, all);
        Assert.AreEqual("second", Encoding.UTF8.GetString(await cache.ReadBodyAsync(latest!)));
    }

    [TestMethod]
    public async Task Replaying_an_older_response_does_not_move_latest_backwards()
    {
        await cache.StoreAsync(Request, Encoding.UTF8.GetBytes("new"), 200, null, T0.AddHours(1));
        await cache.StoreAsync(Request, Encoding.UTF8.GetBytes("old"), 200, null, T0);

        var latest = await cache.GetLatestAsync(Request);

        Assert.AreEqual("new", Encoding.UTF8.GetString(await cache.ReadBodyAsync(latest!)));
    }

    [TestMethod]
    [DataRow("raw/../../evil/x.json.gz")] // beside the cache root, with a name the root is a prefix of
    [DataRow("raw/../market.duckdb.wal")] // inside the root but outside the raw cache
    [DataRow("raw/../latest/forged.json")]
    public async Task A_bundle_writes_only_under_the_raw_cache(string entryName)
    {
        var bundle = Path.Combine(root, "bundle.zip");
        Directory.CreateDirectory(root);
        using (var zip = System.IO.Compression.ZipFile.Open(bundle, System.IO.Compression.ZipArchiveMode.Create))
        {
            await using var writer = new StreamWriter(zip.CreateEntry(entryName).Open());
            await writer.WriteAsync("x");
        }

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => RawCacheBundle.ImportAsync(cache, bundle, CancellationToken.None));
        Assert.IsFalse(File.Exists(Path.GetFullPath(Path.Combine(root, entryName))));
    }

    [TestMethod]
    public async Task Importing_again_restores_the_latest_pointers_an_interrupted_import_left_out()
    {
        await cache.StoreAsync(Request, Encoding.UTF8.GetBytes("body"), 200, null, T0);
        var bundle = Path.Combine(root, "bundle.zip");
        await RawCacheBundle.ExportAsync(cache, bundle, null, CancellationToken.None);

        // A second node whose import stopped after writing the files, before refreshing the pointers.
        var otherRoot = root + "-other";
        try
        {
            var other = new FileSystemRawCache(new RawCacheOptions { Root = otherRoot, Node = "other" });
            await RawCacheBundle.ImportAsync(other, bundle, CancellationToken.None);
            Directory.Delete(Path.Combine(otherRoot, "latest"), recursive: true);
            Assert.IsNull(await other.GetLatestAsync(Request));

            var (imported, skipped) = await RawCacheBundle.ImportAsync(other, bundle, CancellationToken.None);

            Assert.AreEqual(0, imported);
            Assert.AreEqual(2, skipped);
            Assert.AreEqual("body", Encoding.UTF8.GetString(await other.ReadBodyAsync((await other.GetLatestAsync(Request))!)));
        }
        finally
        {
            if (Directory.Exists(otherRoot))
                Directory.Delete(otherRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task A_bad_sidecar_is_left_out_and_the_rest_of_the_bundle_imports()
    {
        await cache.StoreAsync(Request, Encoding.UTF8.GetBytes("body"), 200, null, T0);
        var bundle = Path.Combine(root, "bundle.zip");
        await RawCacheBundle.ExportAsync(cache, bundle, null, CancellationToken.None);
        using (var zip = System.IO.Compression.ZipFile.Open(bundle, System.IO.Compression.ZipArchiveMode.Update))
        {
            await using var writer = new StreamWriter(zip.CreateEntry("raw/tsetmc/x/2026/10/17/bad.json.meta.json").Open());
            await writer.WriteAsync("{ not json");
        }

        var otherRoot = root + "-other";
        try
        {
            var other = new FileSystemRawCache(new RawCacheOptions { Root = otherRoot, Node = "other" });
            var (imported, skipped) = await RawCacheBundle.ImportAsync(other, bundle, CancellationToken.None);

            Assert.AreEqual(2, imported);
            Assert.AreEqual(1, skipped);
            Assert.IsNotNull(await other.GetLatestAsync(Request));
            Assert.IsEmpty(Directory.EnumerateFiles(otherRoot, "*.tmp", SearchOption.AllDirectories).ToList());
        }
        finally
        {
            if (Directory.Exists(otherRoot))
                Directory.Delete(otherRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task Same_millisecond_writes_do_not_overwrite_each_other()
    {
        var a = await cache.StoreAsync(Request, Encoding.UTF8.GetBytes("a"), 200, null, T0);
        var b = await cache.StoreAsync(Request, Encoding.UTF8.GetBytes("b"), 200, null, T0);

        Assert.AreNotEqual(a.BodyPath, b.BodyPath);
        Assert.AreEqual("a", Encoding.UTF8.GetString(await cache.ReadBodyAsync(a)));
        Assert.AreEqual("b", Encoding.UTF8.GetString(await cache.ReadBodyAsync(b)));
    }
}
