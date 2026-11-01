using Boors.Desktop.Infrastructure;
using Boors.Market.Services;
using Boors.Market.Services.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Boors.Desktop.ViewModels;

/// <summary>One instrument: header, returns and key metrics, the three-pane chart, and events, holders, disclosures, messages.</summary>
public sealed partial class SymbolViewModel(IMarketApi api, ILogger<SymbolViewModel> logger) : PageViewModel(api, logger)
{
    private CancellationTokenSource? holdersLoad;

    [ObservableProperty]
    public partial long InsCode { get; set; }

    [ObservableProperty]
    public partial InstrumentMetricsDto? Metrics { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<BarDto> Bars { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<FlowDayDto> Flows { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<CorporateEventDto> Events { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ShareholderDto> Shareholders { get; set; } = [];

    [ObservableProperty]
    public partial bool HoldersLoading { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<FeedItem> Letters { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<FeedItem> Messages { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<KpiItem> Kpis { get; set; } = [];

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NameText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? SectorText { get; set; }

    [ObservableProperty]
    public partial string BuyerPowerText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Price { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ChangeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Tone ChangeTone { get; set; }

    [ObservableProperty]
    public partial string AsOfText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ClassText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double RangeFraction { get; set; } = double.NaN;

    [ObservableProperty]
    public partial string RangeLow { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RangeHigh { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RangeNote { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double PowerFraction { get; set; } = double.NaN;

    [ObservableProperty]
    public partial string PowerNote { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Range { get; set; } = "1y";

    [ObservableProperty]
    public partial bool Adjusted { get; set; } = true;

    [ObservableProperty]
    public partial bool NotFound { get; set; }

    /// <summary>Raised when the chart data or the adjusted toggle changed.</summary>
    public event EventHandler? SeriesChanged;

    public static IReadOnlyList<Choice> Ranges =>
    [
        new("1m", Loc.T("1M")),
        new("3m", Loc.T("3M")),
        new("6m", Loc.T("6M")),
        new("1y", Loc.T("1Y")),
        new("3y", Loc.T("3Y")),
        new("5y", Loc.T("5Y")),
        new("all", Loc.T("All")),
    ];

    public Task LoadAsync(long insCode)
    {
        if (insCode == InsCode && HasData)
        {
            Refresh();
            return Task.CompletedTask;
        }

        InsCode = insCode;
        HasData = false;
        NotFound = false;
        Metrics = null;
        Title = NameText = BuyerPowerText = Price = ChangeText = AsOfText = ClassText = string.Empty;
        SectorText = null;
        Bars = [];
        Flows = [];
        Events = [];
        Shareholders = [];
        Letters = [];
        Messages = [];
        Kpis = [];
        return RunAsync(async ct =>
        {
            var metricsTask = Api.GetMetricsAsync(insCode, ct);
            var barsTask = Api.GetBarsAsync(insCode, null, ct);
            var flowsTask = Api.GetFlowsAsync(insCode, null, ct);
            var eventsTask = Api.GetEventsAsync(insCode, ct);
            var lettersTask = Api.GetLettersAsync(insCode, 50, ct);
            var messagesTask = Api.GetMessagesAsync(insCode, 50, ct);
            try
            {
                Metrics = await metricsTask;
            }
            catch (MarketApiException e) when (e.StatusCode == 404)
            {
                NotFound = true;
                return;
            }

            // Charts show sessions that traded; halted and closed days have no candle to draw.
            Bars = [.. (await barsTask).Where(b => b.Trades > 0)];
            Flows = await flowsTask;
            Events = [.. (await eventsTask).OrderByDescending(e => e.Date)];
            var letters = await lettersTask;
            var messages = await messagesTask;
            Letters = [.. letters.Select(l => new FeedItem(
                $"{(l.PublishedAt is { } at ? Fmt.DateTime(at) : Fmt.Dash)} · {l.LetterCode}",
                l.Title, null, Uri.TryCreate(l.ReportUrl, UriKind.Absolute, out var report) ? report : null,
                l.HasExcel && Uri.TryCreate(l.ExcelUrl, UriKind.Absolute, out var excel) ? excel : null))];
            Messages = [.. messages.Select(m => new FeedItem($"{Fmt.Date(m.Date)} · {MessageKind(m.Kind)}", m.Title, m.Body, null, null))];
            HasData = true;
            Refresh();
            _ = LoadHoldersAsync(insCode);
        }, $"Symbol {insCode}");
    }

    public void SetRange(string range)
    {
        Range = range;
        SeriesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetAdjusted(bool adjusted)
    {
        Adjusted = adjusted;
        SeriesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sessions visible for the selected range button, counted back from the last bar by calendar date.</summary>
    public int SessionsInRange()
    {
        if (Bars.Count == 0 || Range == "all")
            return 0;
        var end = Bars[^1].Date;
        var start = Range switch
        {
            "1m" => end.AddMonths(-1),
            "3m" => end.AddMonths(-3),
            "6m" => end.AddMonths(-6),
            "3y" => end.AddYears(-3),
            "5y" => end.AddYears(-5),
            _ => end.AddYears(-1),
        };
        return Bars.Count(b => b.Date > start);
    }

    public override void Refresh()
    {
        if (Metrics is not { } m)
            return;

        Title = m.Symbol;
        NameText = m.Name ?? string.Empty;
        SectorText = m.SectorName;
        BuyerPowerText = Fmt.Ratio(m.BuyerPower);
        ClassText = Fmt.ClassName(m.InstrumentClass);
        Price = Fmt.Price(m.Close);
        ChangeText = Fmt.Change(m.ChangePercent);
        ChangeTone = Fmt.ToneOf(m.ChangePercent);
        AsOfText = $"{Fmt.Date(m.AsOf)} · {Fmt.DayStatus(m.DayStatus)}";

        List<KpiItem> items = [];
        foreach (var (key, label) in new[] { ("1w", "1 week"), ("1m", "1 month"), ("3m", "3 months"), ("6m", "6 months"), ("1y", "1 year") })
        {
            var value = m.Returns.GetValueOrDefault(key);
            items.Add(new KpiItem(Loc.T(label), Fmt.FractionChange(value), Fmt.ToneOf(value * 100)));
        }

        items.Add(new KpiItem(Loc.T("1 year in free-market dollars"), Fmt.FractionChange(m.UsdReturn1Year), Fmt.ToneOf(m.UsdReturn1Year * 100)));
        items.Add(new KpiItem(Loc.T("Market cap (hemat)"), Fmt.Hemat(m.MarketCap)));
        items.Add(new KpiItem(Loc.T("Average value, 20 sessions (billion toman)"), Fmt.BillionToman(m.AverageValue20)));
        items.Add(new KpiItem(Loc.T("Individual net flow, 20 sessions (billion toman)"), Fmt.SignedBillionToman(m.IndividualNetFlow20), Fmt.ToneOf(m.IndividualNetFlow20),
            m.IndividualNetFlow20 is > 0 ? Loc.T("Inflow") : m.IndividualNetFlow20 is < 0 ? Loc.T("Outflow") : null, Fmt.ToneOf(m.IndividualNetFlow20)));
        Kpis = items;

        RangeLow = Fmt.Price(m.Low52Week);
        RangeHigh = Fmt.Price(m.High52Week);
        var adjustedClose = Bars.Count > 0 ? Bars[^1].AdjustedClose : m.Close;
        RangeFraction = m.Low52Week is { } low && m.High52Week is { } high && high > low && adjustedClose is { } close
            ? (double)((close - low) / (high - low)) : double.NaN;
        RangeNote = $"{Loc.T("From the high")}: {Fmt.Change(m.FromHigh52WeekPercent)}";
        PowerFraction = m.BuyerPowerPercentile ?? double.NaN;
        PowerNote = $"{Loc.T("Per-capita buy / sell (million toman)")}: {Fmt.MillionToman(m.PerCapitaBuy)} / {Fmt.MillionToman(m.PerCapitaSell)}";
        SeriesChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadHoldersAsync(long insCode)
    {
        holdersLoad?.Cancel();
        var cts = holdersLoad = new CancellationTokenSource();
        HoldersLoading = true;
        try
        {
            // May refresh from TSETMC (at most daily, bounded); never blocks the rest of the page.
            var holders = await Api.GetShareholdersAsync(insCode, cts.Token);
            if (InsCode == insCode)
                Shareholders = holders;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Logger.LogInformation("Shareholders of {InsCode} unavailable: {Message}", insCode, e.Message);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(holdersLoad, cts))
                HoldersLoading = false;
        }
    }

    private static string MessageKind(string kind) => kind switch
    {
        "Halt" => Loc.T("Halt"),
        "Reopen" => Loc.T("Reopening"),
        "Other" => Loc.T("Notice"),
        _ => kind,
    };
}
