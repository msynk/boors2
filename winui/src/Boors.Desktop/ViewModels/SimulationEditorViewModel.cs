using System.Collections.ObjectModel;
using System.ComponentModel;
using Boors.Desktop.Infrastructure;
using Boors.Market.Services;
using Boors.Market.Services.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Boors.Desktop.ViewModels;

/// <summary>A factor's slider in the editor (0 = not used).</summary>
public sealed partial class FactorWeightItem(string key, string family) : ObservableObject
{
    public string Key { get; } = key;

    public string Family { get; } = family;

    public string Title => SimulationText.Factor(Key);

    public string FamilyTitle => SimulationText.Family(Family);

    [ObservableProperty]
    public partial double Weight { get; set; }
}

/// <summary>A share the strategy does not buy.</summary>
public sealed record ExcludedShare(long InsCode, string Symbol);

/// <summary>
/// Creates a simulation or changes one. Fields hold what a person types (percentages, million toman, billion toman,
/// sessions) and become a <see cref="SimulationSettingsDto"/> on save. A running simulation keeps its past: new
/// parameters apply from the latest session on as a revision, unless the user chooses to replay it from its start.
/// The tuner runs a grid of the main parameters over past years, ranked in sample and checked out of sample.
/// </summary>
public sealed partial class SimulationEditorViewModel(IMarketApi api, SimulationTracker tracker, ILogger<SimulationEditorViewModel> logger) : PageViewModel(api, logger)
{
    /// <summary>The largest amount taken from a number box: it accepts any number of digits, a decimal does not.</summary>
    public const double MaxAmount = 1e15;

    public const string FromNow = "now";
    public const string Restart = "restart";

    // Properties that are not strategy parameters: changing them keeps the preset.
    private static readonly HashSet<string> NotParameters =
    [
        nameof(Name), nameof(Mode), nameof(CapitalMillion), nameof(StartDate), nameof(Preset), nameof(PresetDescription), nameof(Notes), nameof(ApplyMode),
        nameof(Message), nameof(EditingId), nameof(IsNew), nameof(TuneYears), nameof(TuneObjective), nameof(TuneRows), nameof(HasTuneRows), nameof(TuneSummary),
        nameof(IsTuning), nameof(IsLoading), nameof(Error), nameof(HasData), nameof(LastLoadMilliseconds),
    ];

    private List<SimulationPresetDto> presets = [];
    private DateOnly? latest;
    private bool loadingFields;

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>The simulation being changed; null for a new one.</summary>
    [ObservableProperty]
    public partial string? EditingId { get; set; }

    public bool IsNew => EditingId is null;

    partial void OnEditingIdChanged(string? value) => OnPropertyChanged(nameof(IsNew));

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Mode { get; set; } = SimulationText.Automatic;

    [ObservableProperty]
    public partial double CapitalMillion { get; set; } = 1000;

    [ObservableProperty]
    public partial DateOnly? StartDate { get; set; }

    [ObservableProperty]
    public partial string Preset { get; set; } = "balanced";

    [ObservableProperty]
    public partial string PresetDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Notes { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ApplyMode { get; set; } = FromNow;

    public ObservableCollection<FactorWeightItem> Weights { get; } = [.. SimulationText.Factors.Select(f => new FactorWeightItem(f.Key, f.Family))];

    private bool trackingWeights;

    public ObservableCollection<ExcludedShare> Excluded { get; } = [];

    [ObservableProperty] public partial bool Adaptive { get; set; }
    [ObservableProperty] public partial double AdaptiveYears { get; set; } = 2;
    [ObservableProperty] public partial double AdaptiveKeepPct { get; set; } = 50;
    [ObservableProperty] public partial double Positions { get; set; } = 8;
    [ObservableProperty] public partial double MaxWeightPct { get; set; } = 20;
    [ObservableProperty] public partial double PerSector { get; set; } = 2;
    [ObservableProperty] public partial string Sizing { get; set; } = "InverseVolatility";
    [ObservableProperty] public partial double CashReservePct { get; set; } = 2;
    [ObservableProperty] public partial double RebalanceSessions { get; set; } = 5;
    [ObservableProperty] public partial double HoldPct { get; set; } = 20;
    [ObservableProperty] public partial double MinHoldSessions { get; set; } = 5;
    [ObservableProperty] public partial double BandPct { get; set; } = 25;
    [ObservableProperty] public partial double StopLossPct { get; set; } = 12;
    [ObservableProperty] public partial double TrailingPct { get; set; } = 15;
    [ObservableProperty] public partial double TakeProfitPct { get; set; }
    [ObservableProperty] public partial double MaxHoldSessions { get; set; }
    [ObservableProperty] public partial double CooldownSessions { get; set; } = 10;
    [ObservableProperty] public partial bool RegimeFilter { get; set; } = true;
    [ObservableProperty] public partial double RegimeSessions { get; set; } = 100;
    [ObservableProperty] public partial double DefensivePct { get; set; } = 50;
    [ObservableProperty] public partial double TargetVolatilityPct { get; set; }
    [ObservableProperty] public partial double MinValueBillion { get; set; } = 5;
    [ObservableProperty] public partial double MinHistorySessions { get; set; } = 120;
    [ObservableProperty] public partial string Fill { get; set; } = "Immediate";
    [ObservableProperty] public partial double SlippageBps { get; set; } = 10;
    [ObservableProperty] public partial double ParticipationPct { get; set; } = 10;

    /// <summary>Brokerage per side in percent; NaN = the list rate of the rules in force.</summary>
    [ObservableProperty] public partial double BrokeragePct { get; set; } = double.NaN;

    [ObservableProperty] public partial bool InstantProceeds { get; set; } = true;
    [ObservableProperty] public partial double CashYieldPct { get; set; } = 30;

    [ObservableProperty]
    public partial string? Message { get; set; }

    // ---------------------------------------------------------------- choices

    public static IReadOnlyList<Choice> Modes =>
    [
        new(SimulationText.Automatic, Loc.T("Automatic: the strategy decides")),
        new(SimulationText.Manual, Loc.T("Manual: I place the orders")),
    ];

    public static IReadOnlyList<Choice> PresetChoices => [.. SimulationText.Presets.Select(p => new Choice(p, SimulationText.Preset(p))), new("custom", Loc.T("Custom"))];

    public static IReadOnlyList<Choice> SizingChoices => [.. new[] { "Equal", "InverseVolatility", "Conviction" }.Select(k => new Choice(k, SizingName(k)))];

    public static IReadOnlyList<Choice> FillChoices =>
    [
        new("Immediate", Loc.T("At once, at the closing price of the session it was decided on")),
        new("NextClose", Loc.T("The next session's close (its VWAP on the main markets)")),
        new("NextOpen", Loc.T("The next session's open")),
    ];

    public static IReadOnlyList<Choice> ApplyChoices =>
    [
        new(FromNow, Loc.T("From the latest session on (the past stays as it was)")),
        new(Restart, Loc.T("Replay from the start with these parameters")),
    ];

    public static IReadOnlyList<Choice> Objectives =>
    [
        new("Sharpe", Loc.T("Return per unit of risk (Sharpe)")),
        new("Calmar", Loc.T("Return per deepest fall (Calmar)")),
        new("Return", Loc.T("Highest yearly return")),
    ];

    public static string SizingName(string key) => key switch
    {
        "Equal" => Loc.T("The same amount in each share"),
        "Conviction" => Loc.T("More in the best-ranked shares"),
        _ => Loc.T("Less in volatile shares (equal risk)"),
    };

    // ---------------------------------------------------------------- loading

    /// <summary>A new simulation from a preset, starting at the latest session (or the given date).</summary>
    public async Task PrepareNewAsync(string preset = "balanced", DateOnly? start = null)
    {
        TrackWeights();
        ClearTuning();
        EditingId = null;
        Message = null;
        Error = null;
        Notes = string.Empty;
        Mode = SimulationText.Automatic;
        CapitalMillion = 1000;
        Excluded.Clear();
        Name = Loc.F("Simulation {0}", Fmt.Number(tracker.Items.Count + 1));
        await RunAsync(async ct =>
        {
            await LoadPresetsAsync(ct);
            latest ??= tracker.Items.Select(s => s.Summary?.Latest).Max() ?? (await Api.GetOverviewAsync(null, ct))?.Session;
            StartDate = start ?? latest ?? DateOnly.FromDateTime(DateTime.Today);
            ApplyPreset(preset);
        }, "Simulation editor");
    }

    public async Task PrepareEditAsync(string id)
    {
        TrackWeights();
        ClearTuning();
        Message = null;
        Error = null;
        if (tracker.Find(id) is not { } saved || saved.Spec.Revisions.Count == 0)
        {
            Error = Loc.T("This simulation no longer exists.");
            return;
        }

        EditingId = id;
        var spec = saved.Spec;
        Name = spec.Name;
        Notes = spec.Notes ?? string.Empty;
        Mode = spec.Mode;
        CapitalMillion = (double)(spec.Capital / Fmt.RialsPerMillionToman);
        StartDate = spec.Start;
        ApplyMode = FromNow;
        Preset = spec.Preset ?? "custom";
        PresetDescription = SimulationText.PresetDescription(Preset);
        Load(spec.Revisions[^1].Settings, saved.Symbols);
        await RunAsync(LoadPresetsAsync, "Simulation editor");
    }

    /// <summary>The last tuning belongs to the definition it ran on: its rows must not be applied to another one.</summary>
    private void ClearTuning()
    {
        TuneRows = [];
        TuneSummary = string.Empty;
    }

    private async Task LoadPresetsAsync(CancellationToken ct)
    {
        if (presets.Count == 0)
            presets = await Api.GetSimulationPresetsAsync(ct);
    }

    public void ApplyPreset(string key)
    {
        Preset = key;
        PresetDescription = SimulationText.PresetDescription(key);
        if (presets.FirstOrDefault(p => p.Key == key) is { } preset)
            Load(preset.Settings, null);
    }

    private void Load(SimulationSettingsDto s, IReadOnlyDictionary<long, string>? symbols)
    {
        loadingFields = true;
        try
        {
            LoadFields(s, symbols);
        }
        finally
        {
            loadingFields = false;
        }
    }

    private void LoadFields(SimulationSettingsDto s, IReadOnlyDictionary<long, string>? symbols)
    {
        foreach (var item in Weights)
            item.Weight = s.Weights.GetValueOrDefault(item.Key);
        Adaptive = s.Adaptive;
        AdaptiveYears = Round(s.AdaptiveLookbackSessions / 240.0);
        AdaptiveKeepPct = Round(s.AdaptiveShrink * 100);
        Positions = s.Positions;
        MaxWeightPct = Round(s.MaxWeight * 100);
        PerSector = s.PerSector;
        Sizing = s.Sizing;
        CashReservePct = Round(s.CashReserve * 100);
        RebalanceSessions = s.RebalanceSessions;
        HoldPct = Round(s.HoldPercentile * 100);
        MinHoldSessions = s.MinHoldSessions;
        BandPct = Round(s.RebalanceBand * 100);
        StopLossPct = Round(s.StopLoss * 100);
        TrailingPct = Round(s.TrailingStop * 100);
        TakeProfitPct = Round(s.TakeProfit * 100);
        MaxHoldSessions = s.MaxHoldSessions;
        CooldownSessions = s.CooldownSessions;
        RegimeFilter = s.RegimeFilter;
        RegimeSessions = s.RegimeSessions;
        DefensivePct = Round(s.DefensiveExposure * 100);
        TargetVolatilityPct = Round(s.TargetVolatility * 100);
        MinValueBillion = (double)(s.MinValue / Fmt.RialsPerBillionToman);
        MinHistorySessions = s.MinHistorySessions;
        Fill = s.Fill;
        SlippageBps = s.SlippageBps;
        ParticipationPct = Round(s.ParticipationCap * 100);
        BrokeragePct = s.Brokerage is { } b ? Round((double)b * 100) : double.NaN;
        InstantProceeds = s.InstantSaleProceeds;
        CashYieldPct = Round(s.CashYield * 100);
        if (symbols is not null || s.Excluded.Count == 0)
        {
            Excluded.Clear();
            foreach (var ins in s.Excluded)
                Excluded.Add(new ExcludedShare(ins, symbols?.GetValueOrDefault(ins) ?? ins.ToString(CultureInfo.InvariantCulture)));
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!loadingFields && e.PropertyName is { } name && !NotParameters.Contains(name))
            MarkCustom();
    }

    /// <summary>A moved slider changes its item's weight (binding a slider to it does not; loading the fields is ignored).</summary>
    private void TrackWeights()
    {
        if (trackingWeights)
            return;
        trackingWeights = true;
        foreach (var item in Weights)
        {
            item.PropertyChanged += (_, _) =>
            {
                if (!loadingFields)
                    MarkCustom();
            };
        }
    }

    private static double Round(double value) => Math.Round(value, 6);

    /// <summary>Any change to a parameter makes the set custom (the preset only fills the fields).</summary>
    public void MarkCustom()
    {
        if (Preset != "custom")
        {
            Preset = "custom";
            PresetDescription = SimulationText.PresetDescription("custom");
        }
    }

    // ---------------------------------------------------------------- saving

    public SimulationSettingsDto BuildSettings()
    {
        static double N(double value, double fallback = 0) => double.IsFinite(value) ? value : fallback;
        return new SimulationSettingsDto
        {
            Weights = Weights.Where(w => w.Weight > 0).ToDictionary(w => w.Key, w => Math.Round(w.Weight, 2)),
            Adaptive = Adaptive,
            AdaptiveLookbackSessions = (int)Math.Round(N(AdaptiveYears, 2) * 240),
            AdaptiveShrink = N(AdaptiveKeepPct, 50) / 100,
            Positions = (int)N(Positions, 8),
            MaxWeight = N(MaxWeightPct, 20) / 100,
            PerSector = (int)N(PerSector, 2),
            Sizing = Sizing,
            CashReserve = N(CashReservePct) / 100,
            RebalanceSessions = Math.Max(1, (int)N(RebalanceSessions, 5)),
            HoldPercentile = N(HoldPct, 20) / 100,
            MinHoldSessions = (int)N(MinHoldSessions),
            RebalanceBand = N(BandPct, 25) / 100,
            StopLoss = N(StopLossPct) / 100,
            TrailingStop = N(TrailingPct) / 100,
            TakeProfit = N(TakeProfitPct) / 100,
            MaxHoldSessions = (int)N(MaxHoldSessions),
            CooldownSessions = (int)N(CooldownSessions),
            RegimeFilter = RegimeFilter,
            RegimeSessions = Math.Max(2, (int)N(RegimeSessions, 100)),
            DefensiveExposure = N(DefensivePct, 50) / 100,
            TargetVolatility = N(TargetVolatilityPct) / 100,
            MinValue = (decimal)Math.Min(N(MinValueBillion), MaxAmount) * Fmt.RialsPerBillionToman,
            MinHistorySessions = (int)N(MinHistorySessions, 120),
            Fill = Fill,
            SlippageBps = N(SlippageBps),
            ParticipationCap = N(ParticipationPct) / 100,
            Brokerage = double.IsFinite(BrokeragePct) && BrokeragePct >= 0 ? (decimal)BrokeragePct / 100 : null,
            InstantSaleProceeds = InstantProceeds,
            CashYield = N(CashYieldPct) / 100,
            Excluded = [.. Excluded.Select(e => e.InsCode)],
        };
    }

    /// <summary>Creates or changes the simulation; returns its id, or null with <see cref="Message"/> saying what to fix.</summary>
    public string? Save()
    {
        Message = null;
        var settings = BuildSettings();
        if (string.IsNullOrWhiteSpace(Name))
            return Fail(Loc.T("Give the simulation a name."));
        if (!double.IsFinite(CapitalMillion) || CapitalMillion <= 0)
            return Fail(Loc.T("The starting capital must be more than zero."));
        if (Mode == SimulationText.Automatic && settings.Weights.Count == 0)
            return Fail(Loc.T("Give at least one factor a weight: the strategy ranks shares by them."));
        var start = StartDate ?? latest ?? DateOnly.FromDateTime(DateTime.Today);
        var symbols = Excluded.ToDictionary(e => e.InsCode, e => e.Symbol);

        // The label says which preset the parameters are, exactly; anything else is custom.
        var json = JsonSerializer.Serialize(settings with { Excluded = [] }, MarketJsonContext.Default.SimulationSettingsDto);
        Preset = presets.FirstOrDefault(p => JsonSerializer.Serialize(p.Settings, MarketJsonContext.Default.SimulationSettingsDto) == json)?.Key ?? "custom";

        if (EditingId is null || tracker.Find(EditingId) is not { } saved)
        {
            var spec = new SimulationSpecDto
            {
                Id = NewId(),
                Name = Name.Trim(),
                Mode = Mode,
                Capital = (decimal)Math.Min(CapitalMillion, MaxAmount) * Fmt.RialsPerMillionToman,
                Start = start,
                Revisions = [new SettingsRevisionDto(start, settings)],
                Preset = Mode == SimulationText.Automatic ? Preset : null,
                Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
                CreatedAt = DateTimeOffset.Now,
            };
            tracker.Add(spec, symbols);
            return spec.Id;
        }

        var current = saved.Spec;
        var restart = ApplyMode == Restart;
        var from = saved.Summary?.Latest ?? latest ?? DateOnly.FromDateTime(DateTime.Today);
        List<SettingsRevisionDto> revisions = restart || from <= current.Start
            ? [new SettingsRevisionDto(start, settings)]
            : [.. current.Revisions.Where(r => r.From < from), new SettingsRevisionDto(from, settings)];
        saved.Symbols = symbols;
        tracker.Update(saved, current with
        {
            Name = Name.Trim(),
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            Preset = current.Mode == SimulationText.Automatic ? Preset : null,
            Start = restart ? start : current.Start,
            Capital = restart ? (decimal)CapitalMillion * Fmt.RialsPerMillionToman : current.Capital,
            Revisions = revisions,
        }, restarted: restart);
        return current.Id;

        string? Fail(string message)
        {
            Message = message;
            return null;
        }
    }

    public void Exclude(InstrumentSearchResultDto instrument)
    {
        if (Excluded.All(e => e.InsCode != instrument.InsCode))
            Excluded.Add(new ExcludedShare(instrument.InsCode, instrument.Symbol));
    }

    public async Task<List<InstrumentSearchResultDto>> SearchAsync(string text, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(text) ? [] : [.. (await Api.SearchAsync(text.Trim(), 10, cancellationToken)).Where(r => r.ParentInsCode is null && SimulationText.IsSimulated(r.InstrumentClass))];

    // ---------------------------------------------------------------- tuning

    [ObservableProperty]
    public partial double TuneYears { get; set; } = 3;

    [ObservableProperty]
    public partial string TuneObjective { get; set; } = "Sharpe";

    [ObservableProperty]
    public partial IReadOnlyList<SimulationTuneRowDto> TuneRows { get; set; } = [];

    public bool HasTuneRows => TuneRows.Count > 0;

    partial void OnTuneRowsChanged(IReadOnlyList<SimulationTuneRowDto> value) => OnPropertyChanged(nameof(HasTuneRows));

    [ObservableProperty]
    public partial string TuneSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsTuning { get; set; }

    public async Task TuneAsync()
    {
        IsTuning = true;
        TuneRows = [];
        TuneSummary = Loc.T("Running 54 parameter sets over the past years…");
        try
        {
            var start = latest ?? DateOnly.FromDateTime(DateTime.Today);
            var spec = new SimulationSpecDto
            {
                Id = "tune",
                Capital = (decimal)(double.IsFinite(CapitalMillion) && CapitalMillion > 0 ? CapitalMillion : 1000) * Fmt.RialsPerMillionToman,
                Start = start,
                Revisions = [new SettingsRevisionDto(start, BuildSettings())],
            };
            var years = Math.Clamp((int)(double.IsFinite(TuneYears) ? TuneYears : 3), 1, 10);
            var tuning = await Api.TuneSimulationAsync(new SimulationTuneRequestDto(spec, years, TuneObjective), CancellationToken.None);
            TuneRows = tuning.Rows;
            TuneSummary = tuning.Rows.Count == 0
                ? Loc.T("Not enough history for this many years yet.")
                : Loc.F("Ranked on {0} – {1} (TEDPIX {2}), checked on {3} – {4} (TEDPIX {5}). A set that leads only in the first part was probably fitted to luck: prefer one that also holds up in the second.",
                    Fmt.Date(tuning.From), Fmt.Date(tuning.Split), Fmt.FractionChange(tuning.TedpixIn), Fmt.Date(tuning.Split), Fmt.Date(tuning.To), Fmt.FractionChange(tuning.TedpixOut));
        }
        catch (Exception e)
        {
            TuneSummary = Describe(e);
            Logger.LogWarning(e, "Tuning failed: {Message}", e.Message);
        }
        finally
        {
            IsTuning = false;
        }
    }

    /// <summary>Takes a tuned row's grid parameters into the form (the rest stays as typed).</summary>
    public void UseTuneRow(SimulationTuneRowDto row)
    {
        Positions = row.Settings.Positions;
        RebalanceSessions = row.Settings.RebalanceSessions;
        TrailingPct = row.Settings.TrailingStop * 100;
        RegimeFilter = row.Settings.RegimeFilter;
        MarkCustom();
        Message = Loc.F("Took {0} positions, a ranking every {1} sessions, trailing stop {2}, regime filter {3}. Save to use them.",
            Fmt.Number(row.Settings.Positions), Fmt.Number(row.Settings.RebalanceSessions), row.Settings.TrailingStop > 0 ? Fmt.Percent(row.Settings.TrailingStop, 0) : Loc.T("off"),
            row.Settings.RegimeFilter ? Loc.T("on") : Loc.T("off"));
    }
}
