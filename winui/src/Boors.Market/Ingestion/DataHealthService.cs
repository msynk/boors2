using Boors.Market.Rules;
using Boors.Market.Storage;
using Boors.Market.Text;
using DNTPersianUtils.Core;
using Microsoft.Extensions.Options;

namespace Boors.Market.Ingestion;

public enum DatasetStatus
{
    Ok,
    Stale,
    Empty,
    Info,
}

public sealed record DatasetHealth(string Dataset, long Rows, DateOnly? LatestDate, DateOnly? ExpectedDate, DatasetStatus Status);

public sealed record SourceReachability(string Name, string Verdict, int? Status, string? Error);

public sealed record DataHealthReport(
    DateTimeOffset GeneratedAt,
    string Node,
    DateOnly ExpectedSession,
    IReadOnlyList<DatasetHealth> Datasets,
    IReadOnlyList<CollectionRun> LatestRuns,
    DateTimeOffset? LastProbeAt,
    IReadOnlyList<SourceReachability> Sources)
{
    public bool Healthy => Datasets.All(d => d.Status is DatasetStatus.Ok or DatasetStatus.Info)
        && LatestRuns.All(r => r.Status is "ok" or "skipped" or "running")
        && Sources.All(s => s.Verdict is "ok" or "reachable");
}

/// <summary>
/// Freshness of every dataset against the session it should already contain, latest job runs and the last
/// reachability probe. Feeds the CLI (<c>boors health</c>), the server endpoint and, later, the data-health page.
/// </summary>
public sealed class DataHealthService(MarketStore store, MarketRules rules, TimeProvider timeProvider, IOptions<MarketDataOptions> options)
{
    // Calendar days a dataset may lag behind the expected session before it is stale.
    private static readonly IReadOnlyDictionary<string, int?> AllowedLagDays = new Dictionary<string, int?>
    {
        ["daily_bar"] = 0,
        ["client_type_daily"] = 0,
        ["index_daily"] = 0,
        ["instrument"] = 3,
        ["supervisor_message"] = 3,
        ["codal_letter"] = 3,
        ["macro_daily"] = 4,
        ["fund_daily"] = 6,
    };

    public DataHealthReport GetReport()
    {
        var now = IranTime.Now(timeProvider);
        var expected = ExpectedSession(now);

        var datasets = store.GetFreshness()
            .Select(f =>
            {
                var lag = AllowedLagDays.GetValueOrDefault(f.Dataset);
                var expectedDate = lag is { } days ? expected.AddDays(-days) : (DateOnly?)null;
                var status = f.Rows == 0 ? (lag is null ? DatasetStatus.Info : DatasetStatus.Empty)
                    : expectedDate is null ? DatasetStatus.Info
                    : f.LatestDate >= expectedDate ? DatasetStatus.Ok
                    : DatasetStatus.Stale;
                return new DatasetHealth(f.Dataset, f.Rows, f.LatestDate, expectedDate, status);
            })
            .ToList();

        var (probedAt, sources) = ReadLatestProbe();
        return new DataHealthReport(timeProvider.GetUtcNow(), options.Value.Node, expected, datasets, store.GetLatestRuns(), probedAt, sources);
    }

    /// <summary>
    /// The latest session whose end-of-day data should be in the store: today after the close (+30 min), otherwise the
    /// previous trading day. Trading days are Saturday–Wednesday, excluding official holidays and recorded closures.
    /// </summary>
    public DateOnly ExpectedSession(DateTimeOffset iranNow)
    {
        var closures = store.GetClosureDays().ToHashSet();
        var today = new DateOnly(iranNow.Year, iranNow.Month, iranNow.Day);
        var close = rules.GetSession(Domain.InstrumentClass.TseStock, today).Value.Close.AddMinutes(30);
        var day = TimeOnly.FromTimeSpan(iranNow.TimeOfDay) >= close ? today : today.AddDays(-1);

        for (var i = 0; i < 60; i++, day = day.AddDays(-1))
        {
            if (IsTradingDay(day, closures))
                return day;
        }

        return day;
    }

    public static bool IsTradingDay(DateOnly day, IReadOnlySet<DateOnly> closures)
    {
        if (day.DayOfWeek is DayOfWeek.Thursday or DayOfWeek.Friday || closures.Contains(day))
            return false;

#pragma warning disable DateTimeOffsetInsteadOfDateTimeAnalyzer // DNTPersianUtils' holiday table takes a DateTime date.
        return !day.ToDateTime(TimeOnly.MinValue).IsHoliday(convertToIranTimeZone: false);
#pragma warning restore DateTimeOffsetInsteadOfDateTimeAnalyzer
    }

    private (DateTimeOffset? ProbedAt, IReadOnlyList<SourceReachability> Sources) ReadLatestProbe()
    {
        var directory = Path.Combine(options.Value.DataRoot, "reports");
        var latest = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, $"probe-{options.Value.Node}-*.json").Order(StringComparer.Ordinal).LastOrDefault()
            : null;
        if (latest is null)
            return (null, []);

        using var document = JsonDocument.Parse(File.ReadAllBytes(latest));
        var root = document.RootElement;
        var sources = root.GetProperty("results").EnumerateArray()
            .Select(r => new SourceReachability(
                r.GetProperty("Name").GetString() ?? string.Empty,
                r.GetProperty("Verdict").GetString() ?? string.Empty,
                r.GetProperty("Status").ValueKind == JsonValueKind.Number ? r.GetProperty("Status").GetInt32() : null,
                r.GetProperty("Error").ValueKind == JsonValueKind.String ? r.GetProperty("Error").GetString() : null))
            .ToList();
        return (root.GetProperty("probedAtUtc").GetDateTimeOffset(), sources);
    }
}
