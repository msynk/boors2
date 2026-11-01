using Boors.Desktop.Infrastructure;
using Boors.Market.Services;
using Boors.Market.Services.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Boors.Desktop.ViewModels;

/// <summary>The home screen: KPI tiles, TEDPIX (rial or dollar-indexed), 60-session breadth, sector returns, movers.</summary>
public sealed partial class OverviewViewModel(IMarketApi api, SettingsService settings, ILogger<OverviewViewModel> logger) : PageViewModel(api, logger)
{
    public const long Tedpix = 32097828799138957;
    public const long EqualWeight = 67130298613737946;
    public const int BreadthSessions = 60;

    private Dictionary<long, List<SeriesPointDto>> indexHistory = [];
    private List<SeriesPointDto> dollar = [];
    private List<SeriesPointDto> coin = [];
    private List<BreadthDayDto> breadth = [];
    private List<SectorPerformanceDto> sectors = [];

    [ObservableProperty]
    public partial MarketOverviewDto? Overview { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<KpiItem> Kpis { get; set; } = [];

    [ObservableProperty]
    public partial string SessionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Denomination { get; set; } = "rial";

    [ObservableProperty]
    public partial IReadOnlyList<DateOnly> IndexDates { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<double> IndexValues { get; set; } = [];

    [ObservableProperty]
    public partial string IndexSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<BreadthColumn> BreadthDays { get; set; } = [];

    [ObservableProperty]
    public partial string BreadthSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<DivergingBar> SectorBars { get; set; } = [];

    [ObservableProperty]
    public partial string MoverKind { get; set; } = "value";

    [ObservableProperty]
    public partial IReadOnlyList<MoverDto> Movers { get; set; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    /// <summary>The empty state: the background download is filling the store, or it is off and the user must act.</summary>
    [ObservableProperty]
    public partial string EmptyText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EmptyAction { get; set; } = string.Empty;

    /// <summary>Raised when the index series changed (the chart redraws itself; ScottPlot has no bindings).</summary>
    public event EventHandler? IndexChanged;

    public static IReadOnlyList<Choice> MoverKinds =>
    [
        new("gainers", Loc.T("Gainers")),
        new("losers", Loc.T("Losers")),
        new("value", Loc.T("Value")),
        new("inflow", Loc.T("Individual inflow")),
        new("outflow", Loc.T("Individual outflow")),
    ];

    private Task<Snapshot>? prefetch;

    /// <summary>
    /// Starts the queries on the calling (pool) thread at startup, before the page exists; <see cref="LoadAsync"/>
    /// then picks the result up. Touches no observable state, so it is safe off the UI thread.
    /// </summary>
    public void Prefetch() => Interlocked.CompareExchange(ref prefetch, FetchAsync(CancellationToken.None), null);

    public Task LoadAsync() => RunAsync(async ct =>
    {
        var data = await (Interlocked.Exchange(ref prefetch, null) ?? FetchAsync(ct));
        IsEmpty = data.Overview is null;
        if (data.Overview is null)
        {
            var downloading = settings.Current.DownloadsInBackground;
            EmptyText = downloading
                ? Loc.T("Market data is downloading in the background. The first prices appear when the first jobs finish; the full price history takes longer. Data health shows the progress.")
                : Loc.T("This computer has no market store yet. Pull the raw cache from a Boors node, or run the collection jobs (they call TSETMC, Codal and tgju directly).");
            EmptyAction = Loc.T(downloading ? "Data health" : "Sync from node");
            Overview = null;
            HasData = false;
            return;
        }

        indexHistory = data.Indices;
        dollar = data.Dollar;
        coin = data.Coin;
        breadth = data.Breadth;
        sectors = data.Sectors;
        Movers = data.Movers;
        Overview = data.Overview;
        HasData = true;
        Refresh();
    }, "Overview");

    private async Task<Snapshot> FetchAsync(CancellationToken ct)
    {
        var overview = await Api.GetOverviewAsync(null, ct).ConfigureAwait(false);
        if (overview is null)
            return new Snapshot(null, [], [], [], [], [], []);

        var from = overview.Session.AddYears(-1).AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var indexCodes = overview.Indices.Select(i => i.InsCode).Append(Tedpix).Distinct().ToList();
        var indexTasks = indexCodes.ToDictionary(c => c, c => Api.GetIndexHistoryAsync(c, from, ct));
        var dollarTask = Api.GetMacroHistoryAsync("price_dollar_rl", from, ct);
        var coinTask = Api.GetMacroHistoryAsync("sekee", from, ct);
        var breadthTask = Api.GetBreadthAsync(BreadthSessions, ct);
        var sectorsTask = Api.GetSectorsAsync(null, ct);
        var moversTask = Api.GetMoversAsync(MoverKind, 15, null, ct);
        await Task.WhenAll(new Task[] { dollarTask, coinTask, breadthTask, sectorsTask, moversTask }.Concat(indexTasks.Values)).ConfigureAwait(false);
        return new Snapshot(overview, indexTasks.ToDictionary(p => p.Key, p => p.Value.Result), dollarTask.Result, coinTask.Result,
            breadthTask.Result, sectorsTask.Result, moversTask.Result);
    }

    private sealed record Snapshot(
        MarketOverviewDto? Overview,
        Dictionary<long, List<SeriesPointDto>> Indices,
        List<SeriesPointDto> Dollar,
        List<SeriesPointDto> Coin,
        List<BreadthDayDto> Breadth,
        List<SectorPerformanceDto> Sectors,
        List<MoverDto> Movers);

    public Task LoadMoversAsync(string kind) => RunAsync(async ct =>
    {
        MoverKind = kind;
        Movers = await Api.GetMoversAsync(kind, 15, null, ct);
    }, $"Movers {kind}");

    public void SetDenomination(string denomination)
    {
        Denomination = denomination;
        BuildIndex();
    }

    public override void Refresh()
    {
        if (Overview is not { } o)
            return;

        SessionText = $"{Loc.T("Session")} {Fmt.Date(o.Session)}";
        Kpis = BuildKpis(o);
        BuildIndex();
        BuildBreadth();
        BuildSectors();
    }

    private List<KpiItem> BuildKpis(MarketOverviewDto o)
    {
        List<KpiItem> items = [];
        foreach (var index in o.Indices)
        {
            items.Add(new KpiItem(IndexName(index), Fmt.Price(index.Value), Change: Fmt.Change(index.ChangePercent), ChangeTone: Fmt.ToneOf(index.ChangePercent),
                Spark: Last(indexHistory.GetValueOrDefault(index.InsCode), BreadthSessions)));
        }

        items.Add(new KpiItem(Loc.T("TEDPIX in free-market dollars"), Fmt.Change(o.TedpixUsdChangePercent), Fmt.ToneOf(o.TedpixUsdChangePercent),
            Note: Loc.T("Today's index move net of the dollar's move")));
        items.Add(new KpiItem(Loc.T("Equity value traded (hemat)"), Fmt.Hemat(o.EquityValue),
            Note: $"{Loc.T("Funds")}: {Fmt.Hemat(o.FundValue)} · {Loc.T("Trades")}: {Fmt.Number(o.EquityTrades)}",
            Spark: [.. breadth.Select(b => (double)b.EquityValue)]));
        items.Add(new KpiItem(Loc.T("Breadth"), Fmt.Ltr($"▲{o.Advancers}  ▼{o.Decliners}"),
            Note: $"{Loc.T("Unchanged")} {Fmt.Number(o.Unchanged)} · {Loc.T("Not traded")} {Fmt.Number(o.NotTraded)} · {Loc.T("Halted")} {Fmt.Number(o.Halted)}",
            Spark: [.. breadth.Select(b => (double)(b.Advancers - b.Decliners))]));
        items.Add(new KpiItem(Loc.T("Buy queues"), Fmt.Number(o.BuyQueues), Change: QueueValue(o.BuyQueueValue), ChangeTone: Tone.Up,
            Note: o.BuyQueueValue is null ? Loc.T("Whole-day queues (no close snapshot)") : Loc.T("At the close")));
        items.Add(new KpiItem(Loc.T("Sell queues"), Fmt.Number(o.SellQueues), Change: QueueValue(o.SellQueueValue), ChangeTone: Tone.Down,
            Note: o.SellQueueValue is null ? Loc.T("Whole-day queues (no close snapshot)") : Loc.T("At the close")));
        items.Add(new KpiItem(Loc.T("Individual net flow (hemat)"), Fmt.SignedHemat(o.IndividualNetFlow), Fmt.ToneOf(o.IndividualNetFlow),
            Change: $"{Loc.T("Per-capita buy / sell (million toman)")}: {Fmt.MillionToman(o.IndividualPerCapitaBuy)} / {Fmt.MillionToman(o.IndividualPerCapitaSell)}",
            Note: o.FlowValuesEstimated ? Loc.T("Includes same-day estimates") : null));
        if (o.Usd is { } usd)
        {
            items.Add(new KpiItem(Loc.T("Free-market dollar (rial)"), Fmt.Price(usd.Value), Change: Fmt.Change(usd.ChangePercent), ChangeTone: Fmt.ToneOf(usd.ChangePercent),
                Note: Fmt.Date(usd.Date), Spark: Last(dollar, BreadthSessions)));
        }

        if (o.Coin is { } c)
        {
            items.Add(new KpiItem(Loc.T("Emami gold coin (rial)"), Fmt.Price(c.Value), Change: Fmt.Change(c.ChangePercent), ChangeTone: Fmt.ToneOf(c.ChangePercent),
                Note: Fmt.Date(c.Date), Spark: Last(coin, BreadthSessions)));
        }

        return items;
    }

    /// <summary>
    /// TEDPIX in rial, or divided by the free-market dollar of the same (or latest earlier) day and indexed to 100 at
    /// the start of the window — one axis either way, never a dual-axis chart.
    /// </summary>
    private void BuildIndex()
    {
        if (Overview is null)
            return;
        var start = Overview.Session.AddYears(-1);
        var points = indexHistory.GetValueOrDefault(Tedpix)?.Where(p => p.Date >= start).ToList() ?? [];
        List<(DateOnly Date, double Value)> series;
        if (Denomination == "usd" && dollar.Count > 0)
        {
            series = [];
            var j = 0;
            decimal? usd = null;
            foreach (var point in points)
            {
                while (j < dollar.Count && dollar[j].Date <= point.Date)
                    usd = dollar[j++].Value;
                if (usd is > 0)
                    series.Add((point.Date, (double)(point.Value / usd.Value)));
            }

            var first = series.Count > 0 ? series[0].Value : 1;
            series = [.. series.Select(p => (p.Date, p.Value / first * 100))];
        }
        else
        {
            series = [.. points.Select(p => (p.Date, (double)p.Value))];
        }

        IndexDates = [.. series.Select(p => p.Date)];
        IndexValues = [.. series.Select(p => p.Value)];
        IndexSummary = series.Count < 2 ? string.Empty
            : Loc.F("{0} to {1}: {2}", Fmt.Date(series[0].Date), Fmt.Date(series[^1].Date), Fmt.Change((series[^1].Value / series[0].Value - 1) * 100));
        IndexChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BuildBreadth()
    {
        BreadthDays = [.. breadth.Select(b => new BreadthColumn(b.Date, b.Advancers, b.Unchanged, b.Decliners, b.NotTraded,
            $"{Fmt.Date(b.Date)}\n▲ {Loc.T("Advancers")} {Fmt.Number(b.Advancers)}\n● {Loc.T("Unchanged")} {Fmt.Number(b.Unchanged)}\n▼ {Loc.T("Decliners")} {Fmt.Number(b.Decliners)}\n○ {Loc.T("Not traded")} {Fmt.Number(b.NotTraded)}\n{Loc.T("Halted")} {Fmt.Number(b.Halted)} · {Loc.T("Buy queue all day")} {Fmt.Number(b.LockedUp)} · {Loc.T("Sell queue all day")} {Fmt.Number(b.LockedDown)}"))];
        if (breadth.Count > 0)
        {
            var up = breadth.Count(b => b.Advancers > b.Decliners);
            BreadthSummary = Loc.F("{0} of the last {1} sessions had more advancers than decliners", Fmt.Number(up), Fmt.Number(breadth.Count));
        }
    }

    private void BuildSectors()
    {
        var shown = sectors.Where(s => s.WeightedReturn is not null).OrderByDescending(s => s.WeightedReturn).ToList();
        // Symmetric fixed domain: at least the ±3% daily limit, wider only if a sector moved more (reopen auctions).
        var domain = Math.Max(0.03, shown.Count == 0 ? 0 : shown.Max(s => Math.Abs(s.WeightedReturn!.Value)));
        SectorBars = [.. shown.Select(s =>
        {
            var r = s.WeightedReturn!.Value;
            var name = s.SectorName ?? s.SectorCode;
            return new DivergingBar(name, Fmt.FractionChange(r), Fmt.ToneOf(r * 100), r < 0 ? -r / domain : 0, r > 0 ? r / domain : 0,
                $"{name}\n{Loc.T("Weighted return")} {Fmt.FractionChange(r)} · {Loc.T("Median")} {Fmt.FractionChange(s.MedianReturn)}\n" +
                $"{Loc.T("Value (billion toman)")} {Fmt.BillionToman(s.Value)} · {Loc.T("Individual net (billion toman)")} {Fmt.SignedBillionToman(s.IndividualNetFlow)}\n" +
                $"▲ {Fmt.Number(s.Advancers)}  ▼ {Fmt.Number(s.Decliners)} · {Fmt.Number(s.Traded)}/{Fmt.Number(s.Instruments)} {Loc.T("traded")}",
                s.SectorCode);
        })];
    }

    private static string? QueueValue(decimal? rials) => rials is null ? null : $"{Fmt.Hemat(rials)} {Loc.T("hemat")}";

    private static List<double>? Last(List<SeriesPointDto>? points, int count) =>
        points is { Count: >= 2 } ? [.. points.TakeLast(count).Select(p => (double)p.Value)] : null;

    private static string IndexName(IndexMoveDto index) => Loc.IsPersian ? index.Name : index.InsCode switch
    {
        Tedpix => "TEDPIX",
        EqualWeight => "Equal-weight index",
        _ when index.Name.Contains("فرابورس", StringComparison.Ordinal) => "IFB index",
        _ => index.Name,
    };
}
