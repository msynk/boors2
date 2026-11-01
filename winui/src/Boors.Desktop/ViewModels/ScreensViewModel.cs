using Boors.Desktop.Infrastructure;
using Boors.Market.Services;
using Boors.Market.Services.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Boors.Desktop.ViewModels;

/// <summary>A ranked screen row (rank is part of the row so sorting other columns keeps it).</summary>
public sealed record RankedScreenRow(int Rank, ScreenRowDto Row);

/// <summary>Single-factor equity rankings; the key is the ScreenKind name the API parses.</summary>
public sealed partial class ScreensViewModel(IMarketApi api, ILogger<ScreensViewModel> logger) : PageViewModel(api, logger)
{
    [ObservableProperty]
    public partial string Kind { get; set; } = "Momentum6To1";

    [ObservableProperty]
    public partial IReadOnlyList<RankedScreenRow> Rows { get; set; } = [];

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    public static IReadOnlyList<(string Key, string Title, string Description)> Screens =>
    [
        ("Momentum6To1", Loc.T("Momentum"), Loc.T("Return from 6 months ago to 1 month ago. The latest month is skipped because very recent winners tend to reverse.")),
        ("High52WeekProximity", Loc.T("Near 52-week high"), Loc.T("Last adjusted close divided by the 52-week closing high. 100% means the stock closed at its high.")),
        ("IndividualFlowIntensity20", Loc.T("Individual inflow"), Loc.T("Individual (retail) net buying over 20 sessions as a share of the value traded in those sessions.")),
        ("BuyerPower5", Loc.T("Buyer power"), Loc.T("Average per-capita individual buy divided by per-capita individual sell over the last 5 sessions.")),
    ];

    public Task LoadAsync(string? kind = null) => RunAsync(async ct =>
    {
        Kind = kind ?? Kind;
        Refresh();
        var rows = await Api.GetScreenAsync(Kind, 50, ct);
        Rows = [.. rows.Select((r, i) => new RankedScreenRow(i + 1, r))];
        HasData = true;
    }, $"Screen {kind}");

    public override void Refresh() => Description = Screens.FirstOrDefault(s => s.Key == Kind).Description ?? string.Empty;

    /// <summary>Momentum and flow intensity are fractions, proximity is a ratio (1 = at the high), buyer power a plain ratio.</summary>
    public string FormatScore(double score) => Kind switch
    {
        "Momentum6To1" or "IndividualFlowIntensity20" => Fmt.FractionChange(score),
        "High52WeekProximity" => Fmt.Percent(score),
        _ => Fmt.Num(score, "N2"),
    };
}
