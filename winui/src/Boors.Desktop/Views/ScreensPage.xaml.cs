using System.Text;
using Boors.Desktop.Controls;
using Boors.Desktop.Infrastructure;
using Boors.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.Storage.Pickers;

namespace Boors.Desktop.Views;

public sealed partial class ScreensPage : Page, IReloadable
{
    private readonly Navigator navigator = App.Get<Navigator>();

    public ScreensPage()
    {
        ViewModel = App.Get<ScreensViewModel>();
        InitializeComponent();
        var lite = App.Get<SettingsService>().Current.Density == "Lite";
        foreach (var screen in ScreensViewModel.Screens)
            Kinds.Items.Add(new SelectorBarItem { Text = screen.Title, Tag = screen.Key, IsSelected = screen.Key == ViewModel.Kind });

        Table.RowHeight = lite ? 38 : 30;
        Table.CellFontSize = lite ? 14 : 13;
        Table.EmptyText = Loc.T("Not enough history for this screen yet.");
        Table.Columns =
        [
            Col.Of<RankedScreenRow>("#", 56, r => Fmt.Number(r.Rank), sort: r => r.Rank, numeric: true, export: r => Col.Inv(r.Rank)),
            Col.Of<RankedScreenRow>(Loc.T("Symbol"), 110, r => r.Row.Symbol, sort: r => r.Row.Symbol, export: r => r.Row.Symbol),
            Col.Of<RankedScreenRow>(Loc.T("Name"), 280, r => r.Row.Name ?? string.Empty, sort: r => r.Row.Name, export: r => r.Row.Name, fill: true),
            Col.Of<RankedScreenRow>(Loc.T("Class"), 130, r => Fmt.ClassName(r.Row.InstrumentClass), sort: r => r.Row.InstrumentClass, export: r => r.Row.InstrumentClass),
            Col.Of<RankedScreenRow>(Loc.T("Score"), 120, r => ViewModel.FormatScore(r.Row.Score), sort: r => r.Row.Score, numeric: true, export: r => Col.Inv(r.Row.Score)),
            Col.Of<RankedScreenRow>(Loc.T("Sessions used"), 110, r => Fmt.Number(r.Row.Observations), sort: r => r.Row.Observations, numeric: true, export: r => Col.Inv(r.Row.Observations)),
        ];
        Table.RowInvoked += (_, row) => navigator.OpenSymbol(((RankedScreenRow)row).Row.InsCode);
    }

    public ScreensViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Refresh();
        if (!ViewModel.HasData)
            await ViewModel.LoadAsync();
    }

    public void Reload() => _ = ViewModel.LoadAsync();

    private void OnKindChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is string kind && kind != ViewModel.Kind)
            _ = ViewModel.LoadAsync(kind);
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker(XamlRoot.ContentIslandEnvironment.AppWindowId) { SuggestedFileName = $"screen-{ViewModel.Kind}" };
        picker.FileTypeChoices.Add("CSV", [".csv"]);
        if (await picker.PickSaveFileAsync() is { } file)
        {
            await using var stream = File.Create(file.Path);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(true));
            Table.ExportCsv(writer);
        }
    }
}
