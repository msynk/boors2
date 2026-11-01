using Boors.Market.Rules;

namespace Boors.Market.Domain;

/// <summary>
/// Knows which dates the whole market was closed. TSETMC fills closures with phantom rows (flat prices, zero trades),
/// so closures come from two places: the seeded list in rules.yaml and days on which the index did not move at all.
/// </summary>
public sealed class MarketCalendar
{
    private readonly IReadOnlyList<ClosureRange> knownClosures;
    private readonly HashSet<DateOnly> detectedClosedDays;

    public MarketCalendar(IEnumerable<ClosureRange> knownClosures, IEnumerable<DateOnly>? detectedClosedDays = null)
    {
        this.knownClosures = [.. knownClosures];
        this.detectedClosedDays = [.. detectedClosedDays ?? []];
    }

    public static MarketCalendar FromRules(MarketRules rules, IEnumerable<IndexBar>? referenceIndex = null) =>
        new(rules.KnownClosures, referenceIndex is null ? null : DetectFlatIndexDays(referenceIndex));

    public bool IsMarketClosed(DateOnly date) =>
        detectedClosedDays.Contains(date) || knownClosures.Any(c => c.Contains(date));

    public IReadOnlyCollection<DateOnly> DetectedClosedDays => detectedClosedDays;

    /// <summary>
    /// Days on which the reference index (normally TEDPIX) closed exactly where it closed on the previous row.
    /// A market-cap index of hundreds of stocks does not repeat to the decimal on a real trading day, so a repeat
    /// means TSETMC copied the previous value into a closure row.
    /// </summary>
    public static IReadOnlyList<DateOnly> DetectFlatIndexDays(IEnumerable<IndexBar> bars)
    {
        var ordered = bars.OrderBy(b => b.Date).ToList();
        var flat = new List<DateOnly>();

        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Close == ordered[i - 1].Close)
                flat.Add(ordered[i].Date);
        }

        return flat;
    }
}
