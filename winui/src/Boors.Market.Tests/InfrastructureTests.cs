using System.Text;
using Boors.Market.Contracts;
using Boors.Market.Ingestion;
using Boors.Market.RawCache;

namespace Boors.Market.Tests;

[TestClass]
public sealed class JsonShapeTests
{
    [TestMethod]
    public void Shape_lists_paths_with_value_kinds_across_array_items()
    {
        var shape = JsonShape.Describe(Encoding.UTF8.GetBytes("""{"rows":[{"a":1,"b":"x"},{"a":null}]}"""));

        Assert.AreEqual("null|number", shape["$.rows[].a"]);
        Assert.AreEqual("string", shape["$.rows[].b"]);
    }

    [TestMethod]
    public void Missing_fields_and_new_kinds_are_breaking_but_added_fields_are_not()
    {
        var baseline = JsonShape.Describe(Encoding.UTF8.GetBytes("""{"a":1,"b":"x"}"""));

        var added = JsonShape.Compare("c", baseline, JsonShape.Describe(Encoding.UTF8.GetBytes("""{"a":1,"b":"x","c":true}""")));
        var missing = JsonShape.Compare("c", baseline, JsonShape.Describe(Encoding.UTF8.GetBytes("""{"a":1}""")));
        var retyped = JsonShape.Compare("c", baseline, JsonShape.Describe(Encoding.UTF8.GetBytes("""{"a":"1","b":"x"}""")));

        Assert.IsFalse(added.IsBreaking);
        Assert.HasCount(1, added.Added);
        Assert.IsTrue(missing.IsBreaking);
        Assert.IsTrue(retyped.IsBreaking);
    }
}

[TestClass]
public sealed class RawCacheBundleTests
{
    private readonly List<string> roots = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var root in roots.Where(Directory.Exists))
            Directory.Delete(root, recursive: true);
    }

    private FileSystemRawCache NewCache(string node)
    {
        var root = Path.Combine(Path.GetTempPath(), "boors-bundle-tests", Guid.NewGuid().ToString("N"));
        roots.Add(root);
        return new FileSystemRawCache(new RawCacheOptions { Root = root, Node = node });
    }

    [TestMethod]
    public async Task Import_is_a_union_and_moves_latest_forward_only()
    {
        var request = new RawRequest("tsetmc", "X", "https://example/x", "k=1");
        var iran = NewCache("iran-1");
        var abroad = NewCache("eu-1");
        await iran.StoreAsync(request, Encoding.UTF8.GetBytes("old"), 200, null, new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        await abroad.StoreAsync(request, Encoding.UTF8.GetBytes("new"), 200, null, new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));

        var bundle = Path.Combine(roots[0], "bundle.zip");
        await RawCacheBundle.ExportAsync(abroad, bundle, since: null, CancellationToken.None);
        var (imported, skipped) = await RawCacheBundle.ImportAsync(iran, bundle, CancellationToken.None);
        var again = await RawCacheBundle.ImportAsync(iran, bundle, CancellationToken.None);

        Assert.AreEqual(2, imported); // body + sidecar
        Assert.AreEqual(0, skipped);
        Assert.AreEqual(2, again.Skipped);
        Assert.AreEqual("new", Encoding.UTF8.GetString(await iran.ReadBodyAsync((await iran.GetLatestAsync(request))!)));
    }

    [TestMethod]
    public async Task Export_since_skips_older_days()
    {
        var cache = NewCache("n");
        var request = new RawRequest("s", "e", "u", "p");
        await cache.StoreAsync(request, Encoding.UTF8.GetBytes("a"), 200, null, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        await cache.StoreAsync(request, Encoding.UTF8.GetBytes("b"), 200, null, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        var count = await RawCacheBundle.ExportAsync(cache, Path.Combine(roots[0], "b.zip"), new DateOnly(2026, 9, 15), CancellationToken.None);

        Assert.AreEqual(2, count);
    }
}

[TestClass]
public sealed class TradingCalendarTests
{
    [TestMethod]
    [DataRow("2026-10-17", true)]   // Saturday
    [DataRow("2026-10-15", false)]  // Thursday
    [DataRow("2026-10-16", false)]  // Friday
    [DataRow("2026-03-21", false)]  // 1 Farvardin 1405, Nowruz
    [DataRow("2026-04-01", false)]  // recorded closure
    public void Trading_days_exclude_weekend_holidays_and_closures(string date, bool expected) =>
        Assert.AreEqual(expected, DataHealthService.IsTradingDay(DateOnly.Parse(date, CultureInfo.InvariantCulture), new HashSet<DateOnly> { new(2026, 4, 1) }));
}
