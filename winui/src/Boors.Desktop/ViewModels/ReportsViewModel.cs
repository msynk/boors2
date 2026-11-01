using Boors.Desktop.Infrastructure;
using Boors.Market.Services;
using Boors.Market.Services.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Boors.Desktop.ViewModels;

/// <summary>A ranked equity of a performance report (the rank stays with the row when another column is sorted).</summary>
public sealed record RankedPerformanceRow(int Rank, PerformanceRowDto Row);

/// <summary>A share on a shortlist, ranked within its horizon.</summary>
public sealed record RankedShortlistPick(int Rank, ShortlistPickDto Pick);

/// <summary>A current pick of a ranking rule, ranked.</summary>
public sealed record RankedPick(int Rank, SignalPickDto Pick);

/// <summary>
/// A line of the seasonality table: a Jalali year, or a summary across years (average, median, share of up years).
/// <see cref="Months"/> has 12 entries, Farvardin first.
/// </summary>
public sealed record SeasonalityRow(string Label, IReadOnlyList<double?> Months, double? Total, bool IsShare, int? PartialMonth);

/// <summary>
/// A report of the Reports page: its key, title, explanation, the periods it offers, whether it uses the liquidity
/// filter and whether its window can be stepped back and forth by one period.
/// </summary>
public sealed record ReportInfo(string Key, string Title, string Description, IReadOnlyList<string> Periods, string DefaultPeriod, bool UsesLiquidity, bool CanStep);

/// <summary>
/// Multi-period reports for choosing stocks: top performers, steady compounders, quality on a dip, sector returns,
/// the walk-forward track record of ranking rules and Jalali-month seasonality. Every report's window ends on a base
/// date (the latest session unless one is chosen) and is either a preset period or a custom range; a window that ends
/// in the past also shows what followed it. Loaded reports are kept per parameters, so switching back and forth is
/// instant; F5 or new data clears them.
/// </summary>
public sealed partial class ReportsViewModel(IMarketApi api, ILogger<ReportsViewModel> logger) : PageViewModel(api, logger)
{
    public const string Shortlist = "shortlist";
    public const string Top = "top";
    public const string Steady = "steady";
    public const string Dip = "dip";
    public const string Sectors = "sectors";
    public const string Signals = "signals";
    public const string Seasonality = "seasonality";

    /// <summary>The period key of a window given by its own start date.</summary>
    public const string Custom = "custom";

    private static readonly string[] AllPeriods = ["1m", "3m", "6m", "ytd", "1y", "3y", "5y", Custom];

    private readonly Dictionary<string, object> cache = [];
    private readonly Dictionary<string, string> periods = [];
    private object? data;

    [ObservableProperty]
    public partial string Report { get; set; } = Shortlist;

    [ObservableProperty]
    public partial string Period { get; set; } = "long";

    /// <summary>Minimum average value traded per session, in billion toman.</summary>
    [ObservableProperty]
    public partial string Liquidity { get; set; } = "5";

    /// <summary>The base date every window ends on; null means the latest session.</summary>
    [ObservableProperty]
    public partial DateOnly? EndDate { get; set; }

    /// <summary>The start of a custom window (period <see cref="Custom"/>).</summary>
    [ObservableProperty]
    public partial DateOnly? StartDate { get; set; }

    [ObservableProperty]
    public partial string Measure { get; set; } = "tedpix";

    /// <summary>Sector code that narrows the share reports (set by clicking a sector), with its name for the chip.</summary>
    [ObservableProperty]
    public partial string? SectorFilter { get; set; }

    [ObservableProperty]
    public partial string SectorFilterText { get; set; } = string.Empty;

    /// <summary>The report whose rows are in <see cref="Rows"/> (the page picks its columns from it).</summary>
    public string ShownReport { get; private set; } = Top;

    /// <summary>True when the shown report's window ends before the latest session, so it carries "since then" figures.</summary>
    public bool ShownPast { get; private set; }

    /// <summary>The shortlist horizon shown (empty for the other reports): its columns differ per horizon.</summary>
    public string ShownVariant { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<object> Rows { get; set; } = [];

    [ObservableProperty]
    public partial List<KpiItem> Kpis { get; set; } = [];

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSignals { get; set; }

    [ObservableProperty]
    public partial string? SelectedSignal { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<RankedPick> Picks { get; set; } = [];

    [ObservableProperty]
    public partial string PicksTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ChartTitle { get; set; } = string.Empty;

    public IReadOnlyList<DateOnly> ChartDates { get; private set; } = [];

    public IReadOnlyList<double> ChartValues { get; private set; } = [];

    /// <summary>Raised when the signal chart has new data.</summary>
    public event EventHandler? ChartChanged;

    public static IReadOnlyList<ReportInfo> Reports =>
    [
        new(Shortlist, Loc.T("Shortlist"), string.Empty, ["short", "mid", "long"], "long", false, false),
        new(Top, Loc.T("Top performers"),
            Loc.T("Shares ranked by total return over the period, with what the return cost: the deepest fall on the way, the volatility, how many days the share was locked in a buy queue (that part of the return could not be bought) and how often it traded. Only shares listed for the whole period and above the liquidity filter are ranked."),
            AllPeriods, "1y", true, true),
        new(Steady, Loc.T("Steady compounders"),
            Loc.T("Shares ranked by a reliability score: the average percentile of their yearly return, return per unit of risk, share of up months, shallowness of the deepest fall and, over 3 and 5 years, how often a one-year hold started at any month-end beat the free-market dollar. Shares that were halted for more than a quarter of the period are left out."),
            ["1y", "3y", "5y", Custom], "3y", true, true),
        new(Dip, Loc.T("Quality on a dip"),
            Loc.T("Steady compounders (score 60 or more) that beat TEDPIX over the period but now trade at least 15% below their 52-week high. A fall can be an opportunity or the start of a real problem — read the company's latest Codal letters before acting."),
            ["3y", "5y", Custom], "3y", true, true),
        new(Sectors, Loc.T("Sector returns"),
            Loc.T("Industries compared by the median and average return of their listed shares over the period, the share of members that rose and that beat TEDPIX, and their best member. Click a sector to see its shares."),
            AllPeriods, "1y", true, true),
        new(Signals, Loc.T("Signal track record"),
            Loc.T("Did picking shares by a rule actually work in Tehran? Each month of the test, shares that could be bought that day (priced, not queued to buy, not among the least liquid 30%) are ranked by the rule using only data known then; the top fifth is held for a month and compared with the average share. A rule earns ✓ only if its edge is unlikely to be luck (t ≥ 2) and survives the 1.43% round-trip cost. Click a rule to see its record and today's picks."),
            ["3y", "5y", "10y", Custom], "5y", false, false),
        new(Seasonality, Loc.T("Seasonality"),
            Loc.T("Monthly returns by Jalali month: TEDPIX, TEDPIX measured in free-market dollars, the average share (equal weight) and the dollar itself. The top rows summarise each month across years. A month that was often up is a tendency, not a promise."),
            ["all", "5y", "10y", Custom], "all", false, false),
    ];

    public static IReadOnlyList<Choice> PeriodChoices(IEnumerable<string> keys) => [.. keys.Select(k => new Choice(k, PeriodLabel(k)))];

    public static string PeriodLabel(string period) => period switch
    {
        "short" => Loc.T("Short-term (1–3 months)"),
        "mid" => Loc.T("Mid-term (6–12 months)"),
        "long" => Loc.T("Long-term (3–5 years)"),
        Custom => Loc.T("Custom"),
        "all" => Loc.T("All"),
        _ => Loc.T(period.ToUpperInvariant()),
    };

    public static IReadOnlyList<Choice> LiquidityChoices =>
    [
        new("0", Loc.T("All")),
        new("1", Loc.F("≥ {0} billion toman/day", Fmt.Number(1))),
        new("5", Loc.F("≥ {0} billion toman/day", Fmt.Number(5))),
        new("20", Loc.F("≥ {0} billion toman/day", Fmt.Number(20))),
        new("50", Loc.F("≥ {0} billion toman/day", Fmt.Number(50))),
    ];

    public static IReadOnlyList<Choice> MeasureChoices =>
    [
        new("tedpix", Loc.T("TEDPIX")),
        new("tedpixUsd", Loc.T("TEDPIX in dollars")),
        new("equal", Loc.T("Average share")),
        new("usd", Loc.T("Free-market dollar")),
    ];

    public static string[] MonthNames =>
    [
        Loc.T("Farvardin"), Loc.T("Ordibehesht"), Loc.T("Khordad"), Loc.T("Tir"), Loc.T("Mordad"), Loc.T("Shahrivar"),
        Loc.T("Mehr"), Loc.T("Aban"), Loc.T("Azar"), Loc.T("Dey"), Loc.T("Bahman"), Loc.T("Esfand"),
    ];

    public ReportInfo Info => Reports.First(r => r.Key == Report);

    private decimal MinValue => decimal.Parse(Liquidity, CultureInfo.InvariantCulture) * Fmt.RialsPerBillionToman;

    /// <summary>Switches report, restoring the period last used with it.</summary>
    public Task ShowReportAsync(string report)
    {
        periods[Report] = Period;
        Report = report;
        var info = Info;
        Period = periods.TryGetValue(report, out var period) && info.Periods.Contains(period) ? period : info.DefaultPeriod;
        return LoadAsync();
    }

    /// <summary>
    /// Chooses what the page shows when it opens: --page reports:&lt;key&gt;[:&lt;base date&gt;[:&lt;start of a custom range&gt;]],
    /// dates as yyyy-MM-dd (e.g. reports:top:2025-10-17 or reports:sectors:2025-10-17:2025-03-21). Unknown parts are ignored.
    /// </summary>
    public void Preselect(string spec)
    {
        var parts = spec.Split(':');
        if (Reports.FirstOrDefault(r => r.Key == parts[0]) is not { } info)
            return;

        static DateOnly? Date(string[] parts, int i) =>
            i < parts.Length && DateOnly.TryParseExact(parts[i], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        Report = info.Key;
        EndDate = Date(parts, 1);
        StartDate = Date(parts, 2);
        Period = StartDate is not null && info.Periods.Contains(Custom) ? Custom : info.DefaultPeriod;
    }

    /// <summary>Changes the period; a first switch to a custom range starts it where the shown window starts.</summary>
    public Task SetPeriodAsync(string period)
    {
        if (period == Custom && StartDate is null)
            StartDate = ShownWindow().From ?? (EndDate ?? DateOnly.FromDateTime(DateTime.Today)).AddYears(-1);
        Period = period;
        return LoadAsync();
    }

    /// <summary>Sets the base date (null = the latest session) and the start of a custom range.</summary>
    public Task SetDatesAsync(DateOnly? end, DateOnly? start)
    {
        EndDate = end;
        StartDate = start;
        return LoadAsync();
    }

    /// <summary>
    /// Moves the window back (−1) or forward (+1) by its own length: the previous window ends where the shown one
    /// starts, so consecutive periods can be compared. Reaching the latest session clears the base date.
    /// </summary>
    public Task StepAsync(int direction)
    {
        var (from, asOf, latest) = ShownWindow();
        if (from is not { } start || asOf is not { } end)
            return Task.CompletedTask;

        DateOnly newEnd;
        if (Period == Custom)
        {
            var length = end.DayNumber - start.DayNumber;
            newEnd = direction < 0 ? start : end.AddDays(length);
            StartDate = direction < 0 ? start.AddDays(-length) : end;
        }
        else
        {
            newEnd = direction < 0 ? start : Advance(end, Period);
        }

        EndDate = latest is { } last && newEnd >= last ? null : newEnd;
        return LoadAsync();
    }

    public Task ShowLatestAsync() => SetDatesAsync(null, StartDate);

    /// <summary>Opens the top performers of one sector (from a click on the sector table).</summary>
    public Task ShowSectorAsync(string sectorCode, string? sectorName)
    {
        SectorFilter = sectorCode;
        SectorFilterText = sectorName ?? sectorCode;
        var period = Period;
        periods[Top] = period;
        return ShowReportAsync(Top);
    }

    public void ClearSector()
    {
        SectorFilter = null;
        Build();
    }

    public void SelectSignal(string signal)
    {
        SelectedSignal = signal;
        BuildSignalDetail();
    }

    public void SetMeasure(string measure)
    {
        Measure = measure;
        Build();
    }

    /// <param name="force">Drop the loaded reports (F5, new data) instead of showing a kept one.</param>
    public Task LoadAsync(bool force = false) => RunAsync(async ct =>
    {
        if (force)
            cache.Clear();
        Description = Info.Description;
        var custom = Period == Custom;
        if (custom && StartDate is { } first && EndDate is { } last && first >= last)
        {
            Error = Loc.T("The start date must be before the end date.");
            return;
        }

        var end = Iso(EndDate);
        var from = custom ? Iso(StartDate) : null;
        var preset = custom ? null : Period;
        var key = Report switch
        {
            Shortlist => $"{Shortlist}|{end}",
            Seasonality => $"{Seasonality}|{end}|{from}",
            Signals => $"{Signals}|{end}|{from ?? preset}",
            _ => $"{Report}|{end}|{from ?? preset}|{Liquidity}",
        };
        if (!cache.TryGetValue(key, out var loaded))
        {
            loaded = Report switch
            {
                Shortlist => await Api.GetShortlistAsync(end, ct),
                Signals => await Api.GetSignalTrackRecordAsync(custom ? 0 : YearsOf(Period), end, from, ct),
                Seasonality => await Api.GetSeasonalityAsync(end, from, ct),
                Sectors => await Api.GetSectorReturnsAsync(preset, MinValue, end, from, ct),
                _ => (object)await Api.GetPerformanceReportAsync(KindOf(Report), preset, 500, MinValue, end, from, ct),
            };
            cache[key] = loaded;
        }

        data = loaded;
        HasData = true;
        Build();
    }, $"Report {Report}");

    public override void Refresh()
    {
        Description = Info.Description;
        Build();
    }

    private static string? Iso(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static int YearsOf(string period) => int.TryParse(period.TrimEnd('y'), CultureInfo.InvariantCulture, out var years) ? years : 5;

    private static DateOnly Advance(DateOnly date, string period) => period switch
    {
        "1m" => date.AddMonths(1),
        "3m" => date.AddMonths(3),
        "6m" => date.AddMonths(6),
        "3y" => date.AddYears(3),
        "5y" => date.AddYears(5),
        "10y" => date.AddYears(10),
        _ => date.AddYears(1),
    };

    /// <summary>Start, end and latest session of the shown report (start and end are null for seasonality).</summary>
    private (DateOnly? From, DateOnly? AsOf, DateOnly? Latest) ShownWindow() => data switch
    {
        PerformanceReportDto p => (p.From, p.AsOf, p.Latest),
        SectorReturnsReportDto s => (s.From, s.AsOf, s.Latest),
        SignalTrackRecordDto t => (t.From, t.AsOf, null),
        ShortlistDto l => (null, l.AsOf, l.Latest),
        _ => (null, null, null),
    };

    private static string KindOf(string report) => report switch
    {
        Steady => "SteadyCompounders",
        Dip => "QualityOnDip",
        _ => "TopPerformers",
    };

    private void Build()
    {
        IsSignals = data is SignalTrackRecordDto;
        switch (data)
        {
            case PerformanceReportDto p:
                BuildPerformance(p);
                break;
            case SectorReturnsReportDto s:
                BuildSectors(s);
                break;
            case SignalTrackRecordDto t:
                BuildSignals(t);
                break;
            case SeasonalityReportDto y:
                BuildSeasonality(y);
                break;
            case ShortlistDto l:
                BuildShortlist(l);
                break;
        }
    }

    /// <summary>One horizon of the shortlist, its rule in the description, and its benchmarks (or, for a past base date, the check of what followed).</summary>
    private void BuildShortlist(ShortlistDto l)
    {
        var (picks, label, tedpix, usd, rule) = Period switch
        {
            "short" => (l.ShortTerm, Loc.T("3M"), l.Tedpix3M, l.Usd3M,
                Loc.T("Strong 12-month trend, steady and calm rise, close to the 52-week high. Last month's hottest tenth and shares locked in buy queues on more than 30% of the last three months' sessions are left out; at least 10 billion toman traded a day. The weakest evidence of the three: no short-term rule beat the average share reliably in the signal track record.")),
            "mid" => (l.MidTerm, Loc.T("1Y"), l.Tedpix1Y, l.Usd1Y,
                Loc.T("The 1-year reliability score, the 12-month trend, the 1-year return in dollars, the 3-year yearly return and a shallow deepest fall, together. Every share beat the dollar over the year, rose in at least half of the months and traded at least 10 billion toman a day.")),
            _ => (l.LongTerm, Loc.T("5Y"), l.Tedpix5Y, l.Usd5Y,
                Loc.T("Ranked by the 5- and 3-year reliability scores. Every share beat TEDPIX, won at least half of its one-year holds against the dollar, never fell more than 50% and traded on at least 85% of the sessions (at least 5 billion toman a day).")),
        };
        Description = $"{rule} {Loc.T("At most two shares per sector. A screen, not a forecast: read the company's latest Codal letters before acting.")}";

        var past = l.AsOf < l.Latest;
        var overlap = picks.Count(p => p.AlsoIn.Count > 0);
        List<KpiItem> kpis =
        [
            new($"{Loc.T("TEDPIX")} · {label}", Fmt.FractionChange(tedpix), Fmt.ToneOf(tedpix * 100), Note: Loc.T("The market's cap-weighted index")),
            new($"{Loc.T("Free-market dollar")} · {label}", Fmt.FractionChange(usd), Fmt.ToneOf(usd * 100), Note: Loc.T("A share that did not beat this lost value in dollars")),
            new(Loc.T("Also on another horizon"), Fmt.Ltr($"{Fmt.Number(overlap)} / {Fmt.Number(picks.Count)}"), Note: Loc.T("The sturdiest candidates show up on more than one list")),
        ];
        if (past)
        {
            var since = picks.Where(p => p.SinceThen is not null).Select(p => p.SinceThen!.Value).ToList();
            double? average = since.Count > 0 ? since.Average() : null;
            var after = Loc.F("{0} to {1}", Fmt.Date(l.AsOf), Fmt.Date(l.Latest));
            kpis.Add(new(Loc.T("These picks since then"), Fmt.FractionChange(average), Fmt.ToneOf(average * 100), Note: $"{after} · {Loc.T("average of the list")}"));
            kpis.Add(new(Loc.T("TEDPIX since then"), Fmt.FractionChange(l.TedpixSinceThen), Fmt.ToneOf(l.TedpixSinceThen * 100),
                Note: $"{Loc.T("Free-market dollar")}: {Fmt.FractionChange(l.UsdSinceThen)}"));
        }
        else
        {
            kpis.Add(new(Loc.T("Shares without enough history"), Fmt.Number(l.MissingHistory),
                Note: Loc.T("Not on the long list until the background download completes their history")));
        }

        Kpis = kpis;
        Summary = l.AsOf is null ? string.Empty
            : past ? Loc.F("Picked with data up to {0}; the Since then column shows what followed", Fmt.Date(l.AsOf))
            : Loc.F("As of {0}; rebuilt whenever new market data arrives", Fmt.Date(l.AsOf));
        Show([.. picks.Select((p, i) => new RankedShortlistPick(i + 1, p))]);
    }

    public static string HorizonName(string horizon) => horizon switch
    {
        "short" => Loc.T("Short"),
        "mid" => Loc.T("Mid"),
        _ => Loc.T("Long"),
    };

    private void BuildPerformance(PerformanceReportDto p)
    {
        var rows = p.Rows.Where(r => SectorFilter is null || r.SectorCode == SectorFilter).ToList();
        var median = new KpiItem(Loc.T("Median share"), Fmt.FractionChange(p.MedianReturn), Fmt.ToneOf(p.MedianReturn * 100), Note: Loc.T("Half of the ranked shares did better"));
        var counts = Loc.F("{0} too new for this period · {1} below the liquidity filter", Fmt.Number(p.ShortHistory), Fmt.Number(p.Illiquid));
        Summary = Window(p.AsOf, p.From, p.Sessions) + (SectorFilter is null ? string.Empty : $" · {Loc.T("Sector")}: {SectorFilterText} ({Fmt.Number(rows.Count)})");
        if (p.AsOf < p.Latest)
        {
            // What followed: did the leaders of this window keep leading?
            var leaders = rows.Take(20).Where(r => r.SinceThen is not null).Select(r => r.SinceThen!.Value).ToList();
            double? leadersAverage = leaders.Count > 0 ? leaders.Average() : null;
            var everyone = Median(rows.Where(r => r.SinceThen is not null).Select(r => r.SinceThen!.Value).ToList());
            var after = Loc.F("{0} to {1}", Fmt.Date(p.AsOf), Fmt.Date(p.Latest));
            Kpis = [.. Benchmarks(p.Period, p.TedpixReturn, p.UsdReturn, null), median,
                new KpiItem(Loc.F("First {0} since then", Fmt.Number(Math.Min(20, rows.Count))), Fmt.FractionChange(leadersAverage), Fmt.ToneOf(leadersAverage * 100),
                    Note: $"{after} · {Loc.T("average of the top of this ranking")}"),
                new KpiItem(Loc.T("Median share since then"), Fmt.FractionChange(everyone), Fmt.ToneOf(everyone * 100), Note: after),
                new KpiItem(Loc.T("TEDPIX since then"), Fmt.FractionChange(p.TedpixSinceThen), Fmt.ToneOf(p.TedpixSinceThen * 100),
                    Note: $"{Loc.T("Free-market dollar")}: {Fmt.FractionChange(p.UsdSinceThen)}")];
            Summary += $" · {Loc.F("{0} shares ranked", Fmt.Number(p.Universe))} · {counts}";
        }
        else
        {
            Kpis = [.. Benchmarks(p.Period, p.TedpixReturn, p.UsdReturn, p.CoinReturn), median,
                new KpiItem(Loc.T("Shares ranked"), Fmt.Number(p.Universe), Note: counts)];
        }

        Show(p.Rows.Count == 0 ? [] : [.. rows.Select((r, i) => new RankedPerformanceRow(i + 1, r))]);
    }

    private void BuildSectors(SectorReturnsReportDto s)
    {
        Kpis = [.. Benchmarks(s.Period, s.TedpixReturn, s.UsdReturn, null),
            new KpiItem(Loc.T("Sectors that beat TEDPIX"),
                Fmt.Ltr($"{Fmt.Number(s.Sectors.Count(x => s.TedpixReturn is { } t && x.MedianReturn > t))} / {Fmt.Number(s.Sectors.Count)}"),
                Note: Loc.T("By the median member")),
            .. s.AsOf < s.Latest
                ? new[] { new KpiItem(Loc.T("TEDPIX since then"), Fmt.FractionChange(s.TedpixSinceThen), Fmt.ToneOf(s.TedpixSinceThen * 100), Note: Loc.F("{0} to {1}", Fmt.Date(s.AsOf), Fmt.Date(s.Latest))) }
                : []];
        Summary = Window(s.AsOf, s.From, null);
        Show([.. s.Sectors]);
    }

    private void BuildSignals(SignalTrackRecordDto t)
    {
        var worked = t.Signals.Count(s => s.Verdict == "works");
        var months = t.Signals.Max(s => (int?)s.Months) ?? 0;
        var average = t.Signals.FirstOrDefault(s => s.Signal == "Return12M")?.UniverseReturn;
        Kpis =
        [
            new KpiItem(Loc.T("Rules that worked"), Fmt.Ltr($"{Fmt.Number(worked)} / {Fmt.Number(t.Signals.Count)}"), worked > 0 ? Tone.Up : Tone.Neutral,
                Note: Loc.T("Edge unlikely to be luck and left after costs")),
            new KpiItem(Loc.T("Months tested"), Fmt.Number(months), Note: t.From is null ? null : $"{Fmt.Date(t.From)} – {Fmt.Date(t.AsOf)}"),
            new KpiItem(Loc.T("Average share per month"), Fmt.FractionChange(average), Fmt.ToneOf(average * 100), Note: Loc.T("The bar every rule must beat")),
            new KpiItem(Loc.T("Round-trip cost"), Fmt.Percent(t.RoundTripCost, 2), Note: Loc.T("Fees and tax of buying and selling, at list brokerage")),
        ];
        Summary = t.AsOf is null ? string.Empty
            : Loc.F("Monthly rebalancing on the {0} of each month · today's universe (delisted companies are missing)", Fmt.Number(t.AsOf.Value.Day));
        if (SelectedSignal is null || t.Signals.All(s => s.Signal != SelectedSignal))
            SelectedSignal = t.Signals.Where(s => s.Verdict == "works").OrderByDescending(s => s.NetExcess).FirstOrDefault()?.Signal
                ?? t.Signals.OrderByDescending(s => s.NetExcess).FirstOrDefault()?.Signal;
        Show([.. t.Signals]);
        BuildSignalDetail();
    }

    private void BuildSignalDetail()
    {
        if (data is not SignalTrackRecordDto t || t.Signals.FirstOrDefault(s => s.Signal == SelectedSignal) is not { } signal)
        {
            Picks = [];
            ChartDates = [];
            ChartValues = [];
            ChartChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Growth of the top fifth relative to the average share, 100 = level: rising means the rule was adding value.
        var level = 100.0;
        var points = new List<(DateOnly Date, double Value)>();
        foreach (var month in signal.History)
        {
            level *= (1 + month.Top) / (1 + month.Universe);
            points.Add((month.Date.AddMonths(1), level));
        }

        if (points.Count > 0)
            points.Insert(0, (signal.History[0].Date, 100));
        ChartDates = [.. points.Select(p => p.Date)];
        ChartValues = [.. points.Select(p => p.Value)];
        ChartTitle = Loc.F("{0}: top fifth against the average share (100 = even, before costs)", SignalName(signal.Signal));
        PicksTitle = (EndDate is null
                ? Loc.F("Today's top {0} by {1}", Fmt.Number(signal.Picks.Count), SignalName(signal.Signal))
                : Loc.F("Top {0} by {1} on {2}", Fmt.Number(signal.Picks.Count), SignalName(signal.Signal), Fmt.Date(t.AsOf)))
            + (signal.Verdict == "works" ? string.Empty : $" · {Loc.T("this rule did not prove itself in the test")}");
        Picks = [.. signal.Picks.Select((p, i) => new RankedPick(i + 1, p))];
        ChartChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BuildSeasonality(SeasonalityReportDto s)
    {
        List<double?> Pick(SeasonalityYearDto y) => Measure switch
        {
            "tedpixUsd" => y.TedpixUsd,
            "equal" => y.EqualWeight,
            "usd" => y.Usd,
            _ => y.Tedpix,
        };

        // The current month is month-to-date: shown in its year, left out of the summaries.
        bool Partial(int year, int month) => year == s.CurrentYear && month + 1 == s.CurrentMonth;
        var years = s.Years.Select(y => (y.Year, Months: Pick(y))).Where(y => y.Months.Any(m => m is not null)).ToList();
        if (Period is "5y" or "10y")
            years = [.. years.Take(YearsOf(Period))];
        var full = years.Where(y => y.Months.All(m => m is not null) && !(y.Year == s.CurrentYear)).Select(y => Compound(y.Months)!.Value).ToList();
        var byMonth = Enumerable.Range(0, 12)
            .Select(m => years.Where(y => !Partial(y.Year, m) && y.Months[m] is not null).Select(y => y.Months[m]!.Value).ToList())
            .ToList();

        var rows = new List<object>
        {
            new SeasonalityRow(Loc.T("Average"), [.. byMonth.Select(v => v.Count > 0 ? v.Average() : (double?)null)], full.Count > 0 ? full.Average() : null, false, null),
            new SeasonalityRow(Loc.T("Median"), [.. byMonth.Select(Median)], Median(full), false, null),
            new SeasonalityRow(Loc.T("Up years"), [.. byMonth.Select(v => v.Count > 0 ? v.Count(x => x > 0) / (double)v.Count : (double?)null)],
                full.Count > 0 ? full.Count(x => x > 0) / (double)full.Count : null, true, null),
        };
        rows.AddRange(years.Select(y => new SeasonalityRow(Fmt.Ltr(Fmt.Digits(y.Year.ToString(CultureInfo.InvariantCulture))), y.Months, Compound(y.Months), false,
            y.Year == s.CurrentYear ? s.CurrentMonth : null)));

        var averages = byMonth.Select((v, m) => (Month: m, Average: v.Count >= 3 ? v.Average() : (double?)null)).Where(x => x.Average is not null).ToList();
        var best = averages.MaxBy(x => x.Average);
        var worst = averages.MinBy(x => x.Average);
        var names = MonthNames;
        Kpis =
        [
            new KpiItem(Loc.T("Best month on average"), best.Average is null ? Fmt.Dash : names[best.Month], Change: Fmt.FractionChange(best.Average), ChangeTone: Fmt.ToneOf(best.Average * 100),
                Note: best.Average is null ? null : Loc.F("Up in {0} of {1} years", Fmt.Number(byMonth[best.Month].Count(x => x > 0)), Fmt.Number(byMonth[best.Month].Count))),
            new KpiItem(Loc.T("Worst month on average"), worst.Average is null ? Fmt.Dash : names[worst.Month], Change: Fmt.FractionChange(worst.Average), ChangeTone: Fmt.ToneOf(worst.Average * 100),
                Note: worst.Average is null ? null : Loc.F("Up in {0} of {1} years", Fmt.Number(byMonth[worst.Month].Count(x => x > 0)), Fmt.Number(byMonth[worst.Month].Count))),
            new KpiItem(Loc.T("Years covered"), Fmt.Number(years.Count), Note: years.Count == 0 ? null : $"{Fmt.Digits(years[^1].Year.ToString(CultureInfo.InvariantCulture))} – {Fmt.Digits(years[0].Year.ToString(CultureInfo.InvariantCulture))}"),
        ];
        Summary = s.CurrentMonth is { } current && s.CurrentYear is { } year
            ? Loc.F("{0} {1} is month-to-date and left out of the summary rows", names[current - 1], Fmt.Digits(year.ToString(CultureInfo.InvariantCulture)))
            : string.Empty;
        Show(rows);
    }

    private void Show(IReadOnlyList<object> rows)
    {
        ShownReport = Report;
        ShownPast = data switch
        {
            PerformanceReportDto p => p.AsOf < p.Latest,
            SectorReturnsReportDto s => s.AsOf < s.Latest,
            ShortlistDto l => l.AsOf < l.Latest,
            _ => false,
        };
        ShownVariant = data is ShortlistDto ? Period : string.Empty;
        Rows = rows;
    }

    private static IEnumerable<KpiItem> Benchmarks(string period, double? tedpix, double? usd, double? coin)
    {
        var label = PeriodLabel(period);
        yield return new KpiItem($"{Loc.T("TEDPIX")} · {label}", Fmt.FractionChange(tedpix), Fmt.ToneOf(tedpix * 100), Note: Loc.T("The market's cap-weighted index"));
        yield return new KpiItem($"{Loc.T("Free-market dollar")} · {label}", Fmt.FractionChange(usd), Fmt.ToneOf(usd * 100), Note: Loc.T("A share that did not beat this lost value in dollars"));
        if (coin is not null)
            yield return new KpiItem($"{Loc.T("Emami gold coin")} · {label}", Fmt.FractionChange(coin), Fmt.ToneOf(coin * 100));
    }

    private static string Window(DateOnly? asOf, DateOnly? from, int? sessions) =>
        asOf is null ? string.Empty
        : Loc.F("{0} to {1}", Fmt.Date(from), Fmt.Date(asOf)) + (sessions is { } n ? $" · {Loc.F("{0} sessions", Fmt.Number(n))}" : string.Empty);

    public static string SignalName(string signal) => signal switch
    {
        "Return1M" => Loc.T("1-month return"),
        "Return3M" => Loc.T("3-month return"),
        "Return6M" => Loc.T("6-month return"),
        "Return12M" => Loc.T("12-month return"),
        "Momentum12To1" => Loc.T("Momentum 12-1"),
        "Momentum6To1" => Loc.T("Momentum 6-1"),
        "High52WeekProximity" => Loc.T("Near 52-week high"),
        "LowVolatility" => Loc.T("Low volatility"),
        "RiskAdjusted12M" => Loc.T("12-month return per unit of risk"),
        "Consistency12M" => Loc.T("Up months in the last 12"),
        "IndividualFlow20" => Loc.T("Individual inflow"),
        _ => signal,
    };

    public static (string Text, Tone Tone) VerdictTag(string verdict) => verdict switch
    {
        "works" => ($"✓ {Loc.T("Worked")}", Tone.Up),
        "eaten-by-costs" => ($"≈ {Loc.T("Edge lost to costs")}", Tone.Neutral),
        "reverses" => ($"⇅ {Loc.T("Worked in reverse")}", Tone.Down),
        "no-edge" => ($"○ {Loc.T("No reliable edge")}", Tone.Neutral),
        _ => ($"… {Loc.T("Too little history")}", Tone.Neutral),
    };

    /// <summary>A pick's value in the rule's own unit.</summary>
    public string FormatPick(double value) => SelectedSignal switch
    {
        "LowVolatility" => Fmt.Percent(value),
        "RiskAdjusted12M" => Fmt.Num(value, "N2"),
        "Consistency12M" => Loc.F("{0} of 12", Fmt.Number((int)Math.Round(value * 12))),
        _ => Fmt.FractionChange(value),
    };

    private static double? Compound(IReadOnlyList<double?> months) =>
        months.Any(m => m is not null) ? months.Where(m => m is not null).Aggregate(1.0, (level, m) => level * (1 + m!.Value)) - 1 : null;

    private static double? Median(List<double> values)
    {
        if (values.Count == 0)
            return null;
        var sorted = values.Order().ToList();
        return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    }
}
