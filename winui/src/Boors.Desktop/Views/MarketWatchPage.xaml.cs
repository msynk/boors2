using System.Text;
using Boors.Desktop.Controls;
using Boors.Desktop.Infrastructure;
using Boors.Desktop.ViewModels;
using Boors.Market.Services.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.Storage.Pickers;

namespace Boors.Desktop.Views;

public sealed partial class MarketWatchPage : Page, IReloadable
{
    private readonly Navigator navigator = App.Get<Navigator>();
    private readonly ILogger<MarketWatchPage> logger = App.Get<ILogger<MarketWatchPage>>();

    public MarketWatchPage()
    {
        ViewModel = App.Get<MarketWatchViewModel>();
        InitializeComponent();
        var lite = App.Get<SettingsService>().Current.Density == "Lite";

        GroupSwitch.Items = MarketWatchViewModel.Groups;
        GroupSwitch.SelectedKey = ViewModel.Group;
        GroupSwitch.SelectionChanged += (_, key) =>
        {
            ViewModel.Group = key;
            _ = ViewModel.LoadAsync();
        };

        var presets = new MenuFlyout();
        foreach (var preset in MarketWatchViewModel.Presets)
        {
            var item = new MenuFlyoutItem { Text = preset.Text };
            ToolTipService.SetToolTip(item, preset.Key);
            item.Click += (_, _) => _ = ViewModel.ApplyPresetAsync(preset.Key);
            presets.Items.Add(item);
        }

        PresetsButton.Flyout = presets;
        QuickSearchBox.Text = ViewModel.QuickSearch;

        Table.RowHeight = lite ? 38 : 30;
        Table.CellFontSize = lite ? 14 : 13;
        Table.EmptyText = Loc.T("No instruments match.");
        Table.Columns = lite ? LiteColumns() : ProColumns();
        Table.RowInvoked += (_, row) => navigator.OpenSymbol(((MarketWatchEntryDto)row).InsCode);
        if (ViewModel.Sort.Column >= 0 && ViewModel.Sort.Lite == lite && ViewModel.Sort.Column < Table.Columns.Count)
            Table.SetSort(ViewModel.Sort.Column, ViewModel.Sort.Descending);
        Table.Sorted += (_, ms) =>
        {
            ViewModel.Sort = (Table.SortColumn, Table.SortDescending, lite);
            TimingText.Text = Loc.F("Sorted {0} rows in {1} ms", Fmt.Number(Table.View.Count), Fmt.Num(ms, "N1"));
            logger.LogInformation("Market watch sorted {Rows} rows in {Elapsed:0.0} ms", Table.View.Count, ms);
        };
    }

    public MarketWatchViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (!ViewModel.HasData && !ViewModel.IsLoading)
            await ViewModel.LoadAsync();
        if (ViewModel.LastLoadMilliseconds > 0)
            TimingText.Text = Loc.F("Loaded in {0} ms", Fmt.Num(ViewModel.LastLoadMilliseconds, "N0"));
    }

    public void Reload() => _ = ApplyAsync();

    private static List<TableColumn> ProColumns() =>
    [
        Col.Of<MarketWatchEntryDto>(Loc.T("Symbol"), 104, e => e.Symbol, sort: e => e.Symbol, export: e => e.Symbol),
        Col.Of<MarketWatchEntryDto>(Loc.T("Name"), 200, e => e.Name ?? string.Empty, sort: e => e.Name, export: e => e.Name, fill: true),
        Col.Of<MarketWatchEntryDto>(Loc.T("Last"), 84, e => Fmt.Price(e.Last), sort: e => e.Last, numeric: true, export: e => Col.Inv(e.Last)),
        Col.Of<MarketWatchEntryDto>(Loc.T("Last %"), 96, e => Fmt.Change(e.LastChangePercent), sort: e => e.LastChangePercent, tone: e => Fmt.ToneOf(e.LastChangePercent), numeric: true, export: e => Col.Inv(e.LastChangePercent)),
        Col.Of<MarketWatchEntryDto>(Loc.T("Closing price"), 88, e => Fmt.Price(e.Close), sort: e => e.Close, numeric: true, export: e => Col.Inv(e.Close)),
        Col.Of<MarketWatchEntryDto>(Loc.T("Close %"), 96, e => Fmt.Change(e.ChangePercent), sort: e => e.ChangePercent, tone: e => Fmt.ToneOf(e.ChangePercent), numeric: true, export: e => Col.Inv(e.ChangePercent)),
        Col.Of<MarketWatchEntryDto>(Loc.T("Value (billion toman)"), 104, e => Fmt.BillionToman(e.Value), sort: e => e.Value, numeric: true, export: e => Col.Inv(e.Value), exportHeader: "value_rial"),
        Col.Of<MarketWatchEntryDto>(Loc.T("Volume"), 112, e => Fmt.Number(e.Volume), sort: e => e.Volume, numeric: true, export: e => Col.Inv(e.Volume)),
        Col.Of<MarketWatchEntryDto>(Loc.T("Trades"), 76, e => Fmt.Number(e.Trades), sort: e => e.Trades, numeric: true, export: e => Col.Inv(e.Trades)),
        Col.Of<MarketWatchEntryDto>(Loc.T("Queue"), 108, e => string.Empty, sort: e => e.Queue is null ? null : (e.Queue == "buy" ? 1 : -1) * (e.QueueValue ?? 0),
            tag: e => e.Queue switch
            {
                "buy" => ($"{Loc.T("Buy")} {Fmt.BillionToman(e.QueueValue)}", Tone.Up),
                "sell" => ($"{Loc.T("Sell")} {Fmt.BillionToman(e.QueueValue)}", Tone.Down),
                _ => null,
            },
            export: e => e.Queue is null ? null : $"{e.Queue} {Col.Inv(e.QueueValue)}"),
        Col.Of<MarketWatchEntryDto>(Loc.T("Individual net (billion toman)"), 112, e => Fmt.SignedBillionToman(e.IndividualNetValue), sort: e => e.IndividualNetValue, tone: e => Fmt.ToneOf(e.IndividualNetValue), numeric: true, export: e => Col.Inv(e.IndividualNetValue), exportHeader: "individual_net_rial"),
        Col.Of<MarketWatchEntryDto>(Loc.T("Buyer power"), 84, e => Fmt.Ratio(e.BuyerPower), sort: e => e.BuyerPower, numeric: true, export: e => Col.Inv(e.BuyerPower)),
        Col.Of<MarketWatchEntryDto>(Loc.T("Individual buy share"), 92, e => Fmt.Percent(e.IndividualBuyShare, 0), sort: e => e.IndividualBuyShare, numeric: true, export: e => Col.Inv(e.IndividualBuyShare)),
        Col.Of<MarketWatchEntryDto>(Loc.T("Per-capita buy (million toman)"), 104, e => Fmt.MillionToman(e.PerCapitaBuy), sort: e => e.PerCapitaBuy, numeric: true, export: e => Col.Inv(e.PerCapitaBuy), exportHeader: "per_capita_buy_rial"),
        Col.Of<MarketWatchEntryDto>(Loc.T("Per-capita sell (million toman)"), 104, e => Fmt.MillionToman(e.PerCapitaSell), sort: e => e.PerCapitaSell, numeric: true, export: e => Col.Inv(e.PerCapitaSell), exportHeader: "per_capita_sell_rial"),
        Col.Of<MarketWatchEntryDto>(Loc.T("P/E"), 64, e => Fmt.Decimal(e.PriceEarnings, 1), sort: e => e.PriceEarnings, numeric: true, export: e => Col.Inv(e.PriceEarnings)),
        Col.Of<MarketWatchEntryDto>(Loc.T("Market cap (hemat)"), 96, e => Fmt.Hemat(e.MarketCap), sort: e => e.MarketCap, numeric: true, export: e => Col.Inv(e.MarketCap), exportHeader: "market_cap_rial"),
        Col.Of<MarketWatchEntryDto>(Loc.T("Allowed range"), 132, e => e.AllowedLow is null ? Fmt.Dash : $"{Fmt.Price(e.AllowedLow)} – {Fmt.Price(e.AllowedHigh)}", sort: e => e.AllowedHigh, numeric: true, export: e => $"{Col.Inv(e.AllowedLow)}-{Col.Inv(e.AllowedHigh)}"),
        Col.Of<MarketWatchEntryDto>(Loc.T("Class"), 110, e => Fmt.ClassName(e.InstrumentClass), sort: e => Fmt.ClassName(e.InstrumentClass), export: e => e.InstrumentClass),
        Col.Of<MarketWatchEntryDto>(Loc.T("Status"), 128, e => Fmt.DayStatus(e.DayStatus), sort: e => e.DayStatus, export: e => e.DayStatus),
    ];

    /// <summary>Lite: the columns needed for a quick decision.</summary>
    private static List<TableColumn> LiteColumns() => [.. ProColumns().Where((_, i) => i is 0 or 1 or 4 or 5 or 6 or 9 or 10 or 18)];

    private Task ApplyAsync()
    {
        ViewModel.Filter = FilterBox.Text;
        return ViewModel.LoadAsync();
    }

    private void OnApply(object sender, RoutedEventArgs e) => _ = ApplyAsync();

    private void OnClear(object sender, RoutedEventArgs e) => _ = ViewModel.ClearFilterAsync();

    private void OnFilterKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            _ = ApplyAsync();
        }
    }

    private void OnTradedOnly(object sender, RoutedEventArgs e)
    {
        ViewModel.TradedOnly = TradedOnlyBox.IsChecked == true;
        _ = ViewModel.LoadAsync();
    }

    private void OnQuickSearch(object sender, TextChangedEventArgs e) => _ = ViewModel.SearchAsync(QuickSearchBox.Text);

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker(XamlRoot.ContentIslandEnvironment.AppWindowId)
            {
                SuggestedFileName = $"market-watch-{ViewModel.Result?.Session:yyyy-MM-dd}",
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeChoices.Add("CSV", [".csv"]);
            var file = await picker.PickSaveFileAsync();
            if (file is null)
                return;

            var rows = Table.View.Count;
            await using (var stream = File.Create(file.Path))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
                Table.ExportCsv(writer);
            TimingText.Text = Loc.F("Exported {0} rows to {1}", Fmt.Number(rows), Path.GetFileName(file.Path));
            logger.LogInformation("Exported {Rows} market-watch rows to {Path}", rows, file.Path);
        }
        catch (Exception ex)
        {
            TimingText.Text = ex.Message;
            logger.LogWarning(ex, "CSV export failed");
        }
    }
}
