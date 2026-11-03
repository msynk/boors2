using Boors.Desktop.Charts;
using Boors.Desktop.Infrastructure;
using Boors.Market.Services;
using Boors.Market.Services.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Boors.Desktop.ViewModels;

/// <summary>A simulation in the comparison chart's picker: its colour (kept while it stays selected) and its return.</summary>
public sealed partial class CompareItem(SavedSimulation simulation) : ObservableObject
{
    public SavedSimulation Simulation { get; } = simulation;

    public string Name => Simulation.Spec.Name;

    public string ReturnText => Simulation.Summary is { } s ? Fmt.FractionChange(s.TotalReturn) : Fmt.Dash;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial int? Slot { get; set; }

    public Microsoft.UI.Xaml.Media.Brush SwatchBrush => Slot is { } slot ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Palette.Series(slot)) : Palette.MutedBrush;

    partial void OnSlotChanged(int? value) => OnPropertyChanged(nameof(SwatchBrush));
}

/// <summary>
/// The Simulations page: every simulation with its latest replay (value, return against TEDPIX and the dollar, risk,
/// activity), headline tiles and a comparison of up to six equity curves indexed to 100 at their own start. The list
/// follows <see cref="SimulationTracker"/>, which replays every simulation when new market data arrives.
/// </summary>
public sealed partial class SimulationsViewModel(IMarketApi api, SimulationTracker tracker, ILogger<SimulationsViewModel> logger) : PageViewModel(api, logger)
{
    private readonly Dictionary<string, int> slots = [];
    private bool seeded;

    [ObservableProperty]
    public partial IReadOnlyList<SavedSimulation> Rows { get; set; } = [];

    [ObservableProperty]
    public partial List<KpiItem> Kpis { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<CompareItem> CompareItems { get; set; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    /// <summary>The tracker is replaying (after start-up, new data or a request from this page).</summary>
    [ObservableProperty]
    public partial bool IsReplaying { get; set; }

    public SimulationTracker Tracker => tracker;

    public IReadOnlyList<DateOnly> ChartDates { get; private set; } = [];

    public IReadOnlyList<ChartSeries> ChartSeries { get; private set; } = [];

    /// <summary>Raised when the comparison chart has new data.</summary>
    public event EventHandler? ChartChanged;

    /// <summary>Shows what is known at once, then replays every simulation (always with <paramref name="force"/>, else when none was replayed yet).</summary>
    public async Task LoadAsync(bool force = false)
    {
        Build();
        if (tracker.Items.Count == 0 || (!force && tracker.Items.All(s => tracker.Result(s.Spec.Id) is not null)))
            return;
        await RunAsync(_ => tracker.RefreshAsync(), "Simulations");
    }

    public override void Refresh() => Build();

    /// <summary>Rebuilds rows, tiles and the comparison from the tracker (after a replay, an edit or a language change).</summary>
    public void Build()
    {
        var items = tracker.Items;
        IsReplaying = tracker.IsRefreshing;
        Rows = [.. items];
        IsEmpty = items.Count == 0;
        HasData = items.Count > 0;
        BuildKpis(items);
        BuildCompare(items);

        var tracking = items.Count(s => s.IsTracking);
        var latest = items.Select(s => s.Summary?.Latest).Max();
        Summary = items.Count == 0 ? string.Empty
            : latest is { } date ? Loc.F("{0} simulations, {1} following the market · data up to {2}", Fmt.Number(items.Count), Fmt.Number(tracking), Fmt.Date(date))
            : Loc.F("{0} simulations, {1} following the market", Fmt.Number(items.Count), Fmt.Number(tracking));
    }

    private void BuildKpis(IReadOnlyList<SavedSimulation> items)
    {
        var known = items.Where(s => s.Summary is { Error: null }).ToList();
        if (known.Count == 0)
        {
            Kpis = [];
            return;
        }

        var equity = known.Sum(s => s.Summary!.Equity);
        var capital = known.Sum(s => s.Spec.Capital);
        var yesterday = known.Sum(s => s.Summary!.DayReturn is { } d ? s.Summary.Equity / (decimal)(1 + d) : s.Summary.Equity);
        var best = known.MaxBy(s => s.Summary!.TotalReturn)!;
        var beating = known.Count(s => s.Summary!.ExcessReturn > 0);
        double? day = yesterday > 0 ? (double)(equity / yesterday) - 1 : null;
        Kpis =
        [
            new(Loc.T("Simulations"), Fmt.Number(items.Count), Note: Loc.F("{0} following the market", Fmt.Number(items.Count(s => s.IsTracking)))),
            new(Loc.T("Value of all (million toman)"), Fmt.MillionToman(equity), Change: Fmt.FractionChange(capital > 0 ? (double)(equity / capital) - 1 : null),
                ChangeTone: Fmt.ToneOf(capital > 0 ? ((double)(equity / capital) - 1) * 100 : null), Note: Loc.F("Started with {0}", Fmt.MillionToman(capital))),
            new(Loc.T("Last session"), Fmt.FractionChange(day), Fmt.ToneOf(day * 100), Note: Loc.T("All simulations together")),
            new(Loc.T("Best so far"), best.Spec.Name, Change: Fmt.FractionChange(best.Summary!.TotalReturn), ChangeTone: Fmt.ToneOf(best.Summary.TotalReturn * 100)),
            new(Loc.T("Beating TEDPIX"), Loc.F("{0} of {1}", Fmt.Number(beating), Fmt.Number(known.Count)), Note: Loc.T("Return against TEDPIX over each simulation's own period")),
            new(Loc.T("New trades"), Fmt.Number(tracker.NewActivity), tracker.NewActivity > 0 ? Tone.Accent : Tone.Neutral, Note: Loc.T("Since you last opened each simulation")),
        ];
    }

    // ---------------------------------------------------------------- comparison

    private void BuildCompare(IReadOnlyList<SavedSimulation> items)
    {
        // At first, the oldest six are compared; afterwards the user's picks stand, and new simulations join while there is room.
        foreach (var gone in slots.Keys.Where(id => items.All(s => s.Spec.Id != id)).ToList())
            slots.Remove(gone);
        foreach (var simulation in items)
        {
            if (!slots.ContainsKey(simulation.Spec.Id) && (!seeded || simulation.Summary is null) && slots.Count < Palette.SeriesSlots)
                slots[simulation.Spec.Id] = FreeSlot();
        }

        seeded = items.Count > 0;
        CompareItems = [.. items.Select(s => new CompareItem(s)
        {
            IsSelected = slots.ContainsKey(s.Spec.Id),
            Slot = slots.TryGetValue(s.Spec.Id, out var slot) ? slot : null,
        })];
        BuildChart();
    }

    /// <summary>Adds or removes a simulation from the comparison; a seventh is refused (there are six colours).</summary>
    public bool Toggle(CompareItem item, bool selected)
    {
        var id = item.Simulation.Spec.Id;
        if (selected && !slots.ContainsKey(id))
        {
            if (slots.Count >= Palette.SeriesSlots)
                return false;
            slots[id] = FreeSlot();
        }
        else if (!selected)
        {
            slots.Remove(id);
        }

        item.IsSelected = slots.ContainsKey(id);
        item.Slot = slots.TryGetValue(id, out var slot) ? slot : null;
        BuildChart();
        return true;
    }

    private int FreeSlot() => Enumerable.Range(0, Palette.SeriesSlots).First(k => !slots.ContainsValue(k));

    private void BuildChart()
    {
        var curves = slots
            .Select(s => (Id: s.Key, Slot: s.Value, Result: tracker.Result(s.Key), Simulation: tracker.Find(s.Key)))
            .Where(c => c.Result is { Equity.Count: > 1 } && c.Simulation is not null)
            .OrderBy(c => c.Slot)
            .ToList();
        var dates = curves.SelectMany(c => c.Result!.Equity.Select(p => p.Date)).Distinct().Order().ToList();
        var index = dates.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);
        var series = new List<ChartSeries>();
        foreach (var curve in curves)
        {
            var values = Enumerable.Repeat(double.NaN, dates.Count).ToArray();
            var first = (double)curve.Result!.Equity[0].Equity;
            foreach (var point in curve.Result.Equity)
                values[index[point.Date]] = first > 0 ? (double)point.Equity / first * 100 : double.NaN;
            series.Add(new ChartSeries(curve.Simulation!.Spec.Name, values, Palette.Series(curve.Slot), Emphasis: true));
        }

        // TEDPIX from the earliest start among them, on the same base (MinBy throws on no curves: they are value tuples).
        if (curves.Count > 0 && curves.MinBy(c => c.Result!.Equity[0].Date) is { Result: { } earliest })
        {
            var values = Enumerable.Repeat(double.NaN, dates.Count).ToArray();
            var first = earliest.Equity.Select(p => p.Tedpix).FirstOrDefault(v => v is > 0);
            foreach (var date in dates)
            {
                if (first is { } f && earliest.Equity.FirstOrDefault(p => p.Date == date)?.Tedpix is { } v)
                    values[index[date]] = v / f * 100;
            }

            if (values.Any(double.IsFinite))
                series.Add(new ChartSeries(Loc.T("TEDPIX"), values, Palette.Neutral));
        }

        ChartDates = dates;
        ChartSeries = series;
        ChartChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- actions

    /// <summary>A copy of a simulation's definition under a new name, replayed from its start.</summary>
    public SavedSimulation Duplicate(SavedSimulation source) => tracker.Add(source.Spec with
    {
        Id = SimulationEditorViewModel.NewId(),
        Name = Loc.F("{0} (copy)", source.Spec.Name),
        CreatedAt = DateTimeOffset.Now,
        Revisions = [.. source.Spec.Revisions],
        Orders = [.. source.Spec.Orders],
    }, new(source.Symbols));

    public void Delete(SavedSimulation simulation)
    {
        tracker.Delete(simulation.Spec.Id);
        Build();
    }

    /// <summary>Last value, return and status words for the table.</summary>
    public static string Status(SavedSimulation s) => s.Summary switch
    {
        { Error: { } } => Loc.T("Could not replay"),
        null => Loc.T("Not replayed yet"),
        { AsOf: null } => Loc.T("Waiting for its first session"),
        _ when !s.IsTracking => Loc.T("Stopped"),
        { RiskOn: false } => Loc.T("Following · defensive"),
        _ => Loc.T("Following the market"),
    };

    /// <summary>Icon and word (never colour alone); a replay that failed is the one problem state.</summary>
    public static (string Text, Tone Tone)? StatusTag(SavedSimulation s) => s.Summary switch
    {
        { Error: { } } => ($"✕ {Status(s)}", Tone.Critical),
        null or { AsOf: null } => ($"… {Status(s)}", Tone.Muted),
        _ when !s.IsTracking => ($"■ {Status(s)}", Tone.Muted),
        { RiskOn: false } => ($"◐ {Status(s)}", Tone.Accent),
        _ => ($"● {Status(s)}", Tone.Accent),
    };
}
