using Boors.Desktop.Infrastructure;
using Boors.Market.Services;
using Boors.Market.Services.Contracts;
using Boors.Market.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Boors.Desktop.ViewModels;

/// <summary>
/// دیده‌بان: the whole session (~3,100 instruments) with a TSETMC filter (run in the sandboxed Jint interpreter on the
/// store side), class groups, traded-only, a local quick search and CSV export. Filter syntax errors are shown next to
/// the box; per-instrument evaluation errors are listed, not fatal.
/// </summary>
public sealed partial class MarketWatchViewModel(IMarketApi api, ILogger<MarketWatchViewModel> logger) : PageViewModel(api, logger)
{
    /// <summary>Instrument class groups offered as toggle buttons; keys are rules.yaml class keys.</summary>
    private static readonly Dictionary<string, string[]?> ClassGroups = new()
    {
        ["shares"] = ["tse_stock", "ifb_stock", "ifb_base_yellow", "ifb_base_orange", "ifb_base_red"],
        ["funds"] = ["etf_equity", "etf_fixed_income", "etf_real_estate", "etf_mixed", "fund_leveraged", "fund_gold_silver", "fund_of_funds"],
        ["rights"] = ["rights"],
        ["all"] = null,
    };

    private CancellationTokenSource? searching;

    [ObservableProperty]
    public partial string Group { get; set; } = "shares";

    [ObservableProperty]
    public partial bool TradedOnly { get; set; } = true;

    [ObservableProperty]
    public partial string Filter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string QuickSearch { get; set; } = string.Empty;

    [ObservableProperty]
    public partial MarketWatchResultDto? Result { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<MarketWatchEntryDto> Rows { get; set; } = [];

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>The grid's sort, kept across page visits and shell rebuilds (language or theme change).</summary>
    public (int Column, bool Descending, bool Lite) Sort { get; set; } = (-1, false, false);

    [ObservableProperty]
    public partial string? FilterError { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> FilterErrors { get; set; } = [];

    public static IReadOnlyList<Choice> Groups =>
    [
        new("shares", Loc.T("Shares")),
        new("funds", Loc.T("Funds")),
        new("rights", Loc.T("Rights")),
        new("all", Loc.T("All")),
    ];

    /// <summary>Common TSETMC-style filters; the text goes into the box so it can be edited before applying.</summary>
    public static IReadOnlyList<Choice> Presets =>
    [
        new("(pc)>(py)*1.03", Loc.T("Closed more than 3% up")),
        new("(pd1)==(tmax) && (qd1)>0", Loc.T("Buy queue at the upper limit")),
        new("(po1)==(tmin) && (qo1)>0", Loc.T("Sell queue at the lower limit")),
        new("(ct).Buy_CountI>0 && (ct).Sell_CountI>0 && ((ct).Buy_I_Volume/(ct).Buy_CountI)/((ct).Sell_I_Volume/(ct).Sell_CountI)>2", Loc.T("Buyer power above 2")),
        new("(ct).Buy_I_Volume>(ct).Sell_I_Volume*1.2", Loc.T("Individuals bought more than they sold")),
        new("(bvol)>0 && (tvol)>(bvol)*3", Loc.T("Volume above 3× base volume")),
        new("(pl)>(pc) && (pl)>(py)", Loc.T("Last price above close")),
        new("(pe)>0 && (pe)<6", Loc.T("P/E between 0 and 6")),
        new("true==function(){if([ih].length<19)return false;for(var i=0;i<19;i++){if([ih][i].PClosing>=(pc))return false;}return true;}()", Loc.T("New 20-session closing high")),
    ];

    public Task LoadAsync() => RunAsync(async ct =>
    {
        FilterError = null;
        var classes = ClassGroups.TryGetValue(Group, out var keys) && keys is not null ? keys.ToList() : null;
        try
        {
            Result = await Api.GetMarketWatchAsync(new MarketWatchRequestDto(null, string.IsNullOrWhiteSpace(Filter) ? null : Filter.Trim(), classes, TradedOnly), ct);
        }
        catch (MarketApiException e) when (e.StatusCode == 400)
        {
            // A filter syntax error is a user mistake, not a failure: show it next to the box.
            FilterError = e.Message;
            Result = null;
        }

        FilterErrors = Result?.FilterErrors ?? [];
        HasData = Result is not null;
        await ApplyQuickSearchAsync(ct);
    }, "Market watch");

    public Task ApplyPresetAsync(string filter)
    {
        Filter = filter;
        return LoadAsync();
    }

    public Task ClearFilterAsync()
    {
        Filter = string.Empty;
        return LoadAsync();
    }

    /// <summary>Quick search is local (symbol or name, ی/ک-insensitive) and runs off the UI thread.</summary>
    public async Task SearchAsync(string text)
    {
        QuickSearch = text;
        searching?.Cancel();
        var cts = searching = new CancellationTokenSource();
        try
        {
            await Task.Delay(120, cts.Token);
            await ApplyQuickSearchAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override void Refresh() => UpdateStatus();

    private async Task ApplyQuickSearchAsync(CancellationToken cancellationToken)
    {
        var entries = Result?.Entries ?? [];
        var key = PersianText.MatchKey(QuickSearch ?? string.Empty);
        Rows = key.Length == 0 ? entries : await Task.Run(() => entries
            .Where(e => PersianText.MatchKey(e.Symbol).Contains(key, StringComparison.Ordinal) || PersianText.MatchKey(e.Name ?? string.Empty).Contains(key, StringComparison.Ordinal))
            .ToList(), cancellationToken);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        StatusText = Result is null ? string.Empty
            : $"{Loc.T("Session")} {Fmt.Date(Result.Session)} · {Fmt.Number(Rows.Count)} / {Fmt.Number(Result.Evaluated)}";
    }
}
