namespace Boors.Market.Contracts;

/// <summary>
/// The structural "shape" of a JSON document: every path (arrays as <c>[]</c>) with the set of value kinds seen there,
/// e.g. <c>closingPriceDaily[].pClosing: number</c>. Comparing shapes detects source format changes before parsers fail.
/// </summary>
public static class JsonShape
{
    public static SortedDictionary<string, string> Describe(ReadOnlySpan<byte> json)
    {
        using var document = JsonDocument.Parse(json.ToArray());
        var kinds = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        Walk(document.RootElement, "$", kinds);
        return new SortedDictionary<string, string>(kinds.ToDictionary(k => k.Key, k => string.Join('|', k.Value)), StringComparer.Ordinal);
    }

    /// <summary>Marks a baseline path that some responses omit (e.g. TSETMC's market watch includes yVal only sometimes).</summary>
    public const string OptionalPrefix = "optional:";

    public static ContractDiff Compare(string contract, IReadOnlyDictionary<string, string> baseline, IReadOnlyDictionary<string, string> current)
    {
        var missing = baseline.Where(b => !b.Value.StartsWith(OptionalPrefix, StringComparison.Ordinal)).Select(b => b.Key)
            .Except(current.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var added = current.Keys.Except(baseline.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var changed = baseline.Keys.Intersect(current.Keys, StringComparer.Ordinal)
            .Where(k => !KindsCompatible(baseline[k], current[k]))
            .Order(StringComparer.Ordinal)
            .Select(k => $"{k}: {baseline[k]} -> {current[k]}")
            .ToList();
        return new ContractDiff(contract, missing, added, changed);
    }

    /// <summary>
    /// Merges a new sample into an existing baseline: kinds are unioned, and a path missing from either side becomes
    /// optional. Running <c>--update</c> several times therefore learns which fields a source sends only sometimes.
    /// </summary>
    public static SortedDictionary<string, string> Merge(IReadOnlyDictionary<string, string>? baseline, IReadOnlyDictionary<string, string> sample)
    {
        if (baseline is null)
            return new SortedDictionary<string, string>(sample.ToDictionary(k => k.Key, k => k.Value), StringComparer.Ordinal);

        var merged = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in baseline.Keys.Union(sample.Keys, StringComparer.Ordinal))
        {
            var inBaseline = baseline.TryGetValue(path, out var before);
            var inSample = sample.TryGetValue(path, out var now);
            var optional = !inBaseline || !inSample || before!.StartsWith(OptionalPrefix, StringComparison.Ordinal);
            var kinds = new[] { before, now }.Where(k => k is not null).SelectMany(k => Kinds(k!)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
            merged[path] = (optional ? OptionalPrefix : string.Empty) + string.Join('|', kinds);
        }

        return merged;
    }

    // A kind set is compatible when it adds nothing new except kinds already allowed; "null" appearing where it was
    // never seen counts as a change because non-nullable parser fields would fail on it.
    private static bool KindsCompatible(string baseline, string current) =>
        Kinds(current).All(kind => Kinds(baseline).Contains(kind, StringComparer.Ordinal));

    private static string[] Kinds(string value) =>
        (value.StartsWith(OptionalPrefix, StringComparison.Ordinal) ? value[OptionalPrefix.Length..] : value).Split('|');

    private static void Walk(JsonElement element, string path, Dictionary<string, SortedSet<string>> kinds)
    {
        Add(kinds, path, element.ValueKind switch
        {
            JsonValueKind.Object => "object",
            JsonValueKind.Array => "array",
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "bool",
            _ => "null",
        });

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    Walk(property.Value, $"{path}.{property.Name}", kinds);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Walk(item, path + "[]", kinds);
                break;
        }
    }

    private static void Add(Dictionary<string, SortedSet<string>> kinds, string path, string kind)
    {
        if (!kinds.TryGetValue(path, out var set))
            kinds[path] = set = new SortedSet<string>(StringComparer.Ordinal);
        set.Add(kind);
    }
}

public sealed record ContractDiff(string Contract, IReadOnlyList<string> Missing, IReadOnlyList<string> Added, IReadOnlyList<string> KindChanged)
{
    /// <summary>Missing fields or new kinds can break parsers; added fields cannot.</summary>
    public bool IsBreaking => Missing.Count > 0 || KindChanged.Count > 0;
}
