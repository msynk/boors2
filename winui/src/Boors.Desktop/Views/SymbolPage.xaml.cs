using Boors.Desktop.Controls;
using Boors.Desktop.Infrastructure;
using Boors.Desktop.ViewModels;
using Boors.Market.Services.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Boors.Desktop.Views;

public sealed partial class SymbolPage : Page, IReloadable
{
    private string tab = "events";

    public SymbolPage()
    {
        ViewModel = App.Get<SymbolViewModel>();
        InitializeComponent();
        var lite = App.Get<SettingsService>().Current.Density == "Lite";

        RangeSwitch.Items = SymbolViewModel.Ranges;
        RangeSwitch.SelectedKey = ViewModel.Range;
        RangeSwitch.SelectionChanged += (_, key) => ViewModel.SetRange(key);
        AdjustedSwitch.IsOn = ViewModel.Adjusted;
        PowerBar.StartText = Fmt.Number(0);
        PowerBar.EndText = Fmt.Number(100);

        EventsTable.RowHeight = HoldersTable.RowHeight = lite ? 36 : 30;
        EventsTable.EmptyText = Loc.T("No capital events recorded.");
        EventsTable.Columns =
        [
            Col.Of<CorporateEventDto>(Loc.T("Date"), 110, e => Fmt.Date(e.Date), sort: e => e.Date, export: e => Col.Inv(e.Date)),
            Col.Of<CorporateEventDto>(Loc.T("Event"), 170, e => e.Kind == "share_change" ? Loc.T("Share count change") : Loc.T("Price adjustment"), sort: e => e.Kind),
            Col.Of<CorporateEventDto>(Loc.T("Factor"), 90, e => Fmt.Decimal(e.Factor, 4), sort: e => e.Factor, numeric: true),
            Col.Of<CorporateEventDto>(Loc.T("Shares before"), 150, e => Fmt.Decimal(e.SharesBefore, 0), sort: e => e.SharesBefore, numeric: true),
            Col.Of<CorporateEventDto>(Loc.T("Shares after"), 150, e => Fmt.Decimal(e.SharesAfter, 0), sort: e => e.SharesAfter, numeric: true),
            Col.Of<CorporateEventDto>(Loc.T("Source"), 120, e => e.Source switch { "implied" => Loc.T("Implied by prices"), "published" => Loc.T("Published by TSETMC"), null => Fmt.Dash, var s => s }, sort: e => e.Source, fill: true),
        ];
        HoldersTable.EmptyText = Loc.T("No shareholder snapshot yet.");
        HoldersTable.Columns =
        [
            Col.Of<ShareholderDto>(Loc.T("Holder"), 300, s => s.HolderName, sort: s => s.HolderName, fill: true),
            Col.Of<ShareholderDto>(Loc.T("Percent"), 90, s => Fmt.Decimal(s.Percent, 2), sort: s => s.Percent, numeric: true),
            Col.Of<ShareholderDto>(Loc.T("Shares"), 160, s => Fmt.Decimal(s.Shares, 0), sort: s => s.Shares, numeric: true),
            Col.Of<ShareholderDto>(Loc.T("Change"), 140, s => s.ChangeAmount == 0 ? Fmt.Dash : Fmt.Signed(s.ChangeAmount, x => x, "N0"), sort: s => s.ChangeAmount, tone: s => Fmt.ToneOf(s.ChangeAmount), numeric: true),
            Col.Of<ShareholderDto>(Loc.T("As of"), 110, s => Fmt.Date(s.SnapshotDate), sort: s => s.SnapshotDate),
        ];

        ViewModel.SeriesChanged += OnSeriesChanged;
        ViewModel.PropertyChanged += OnViewModelChanged;
        Unloaded += (_, _) =>
        {
            ViewModel.SeriesChanged -= OnSeriesChanged;
            ViewModel.PropertyChanged -= OnViewModelChanged;
        };
        UpdateTabHeaders();
    }

    public SymbolViewModel ViewModel { get; }

    public long InsCode { get; private set; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        InsCode = e.Parameter is long code ? code : ViewModel.InsCode;
        await ViewModel.LoadAsync(InsCode);
        UpdateTabHeaders();
        ShowTab(tab);
        App.Get<StartupOptions>().ApplyScroll(Scroller);
    }

    public void Reload()
    {
        ViewModel.HasData = false;
        _ = ViewModel.LoadAsync(InsCode);
    }

    private void OnSeriesChanged(object? sender, EventArgs e) =>
        Chart.Show(ViewModel.Bars, ViewModel.Flows, ViewModel.Adjusted, ViewModel.SessionsInRange());

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SymbolViewModel.Events) or nameof(SymbolViewModel.Shareholders) or nameof(SymbolViewModel.Letters) or nameof(SymbolViewModel.Messages))
        {
            UpdateTabHeaders();
            ShowTab(tab);
        }
    }

    private void UpdateTabHeaders()
    {
        EventsTab.Text = $"{Loc.T("Capital events")} ({Fmt.Number(ViewModel.Events.Count)})";
        HoldersTab.Text = $"{Loc.T("Shareholders")} ({Fmt.Number(ViewModel.Shareholders.Count)})";
        LettersTab.Text = $"{Loc.T("Codal disclosures")} ({Fmt.Number(ViewModel.Letters.Count)})";
        MessagesTab.Text = $"{Loc.T("Supervisor messages")} ({Fmt.Number(ViewModel.Messages.Count)})";
    }

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is string key)
            ShowTab(key);
    }

    private void ShowTab(string key)
    {
        tab = key;
        EventsTable.Visibility = Ui.Show(key == "events");
        HoldersPanel.Visibility = Ui.Show(key == "holders");
        LettersList.Visibility = Ui.Show(key == "letters");
        MessagesList.Visibility = Ui.Show(key == "messages");
        var emptyText = key switch
        {
            "letters" when ViewModel.Letters.Count == 0 => Loc.T("No disclosures collected yet."),
            "messages" when ViewModel.Messages.Count == 0 => Loc.T("No supervisor messages for this symbol."),
            _ => null,
        };
        FeedEmpty.Text = emptyText ?? string.Empty;
        FeedEmpty.Visibility = Ui.ShowText(emptyText);
    }

    private void OnAdjustedToggled(object sender, RoutedEventArgs e)
    {
        if (AdjustedSwitch.IsOn != ViewModel.Adjusted)
            ViewModel.SetAdjusted(AdjustedSwitch.IsOn);
    }
}
