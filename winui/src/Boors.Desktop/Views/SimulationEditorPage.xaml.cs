using Boors.Desktop.Controls;
using Boors.Desktop.Infrastructure;
using Boors.Desktop.ViewModels;
using Boors.Market.Services.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Boors.Desktop.Views;

/// <summary>Creates a simulation or changes its parameters, and tunes the main ones over past years.</summary>
public sealed partial class SimulationEditorPage : Page
{
    private readonly Navigator navigator = App.Get<Navigator>();
    private CancellationTokenSource? searching;
    private bool syncing;

    public SimulationEditorPage()
    {
        ViewModel = App.Get<SimulationEditorViewModel>();
        InitializeComponent();
        StartPicker.CalendarIdentifier = Loc.IsPersian ? "PersianCalendar" : "GregorianCalendar";
        StartPicker.Language = Loc.IsPersian ? "fa-IR" : "en-US";
        StartPicker.FirstDayOfWeek = Loc.IsPersian ? Windows.Globalization.DayOfWeek.Saturday : Windows.Globalization.DayOfWeek.Monday;
        StartPicker.DateFormat = "{year.full}/{month.integer(2)}/{day.integer(2)}";
        StartPicker.MaxDate = DateTimeOffset.Now;

        TuneTable.RowHeight = 30;
        TuneTable.Columns =
        [
            Col.Of<SimulationTuneRowDto>(Loc.T("Positions"), 80, r => Fmt.Number(r.Settings.Positions), sort: r => r.Settings.Positions, numeric: true, export: r => Col.Inv(r.Settings.Positions), exportHeader: "positions"),
            Col.Of<SimulationTuneRowDto>(Loc.T("Rank every"), 90, r => Fmt.Number(r.Settings.RebalanceSessions), sort: r => r.Settings.RebalanceSessions, numeric: true, export: r => Col.Inv(r.Settings.RebalanceSessions), exportHeader: "rebalance_sessions"),
            Col.Of<SimulationTuneRowDto>(Loc.T("Trailing stop"), 100, r => r.Settings.TrailingStop > 0 ? Fmt.Percent(r.Settings.TrailingStop, 0) : Loc.T("Off"), sort: r => r.Settings.TrailingStop, numeric: true, export: r => Col.Inv(r.Settings.TrailingStop), exportHeader: "trailing_stop"),
            Col.Of<SimulationTuneRowDto>(Loc.T("Regime filter"), 100, r => r.Settings.RegimeFilter ? Loc.T("On") : Loc.T("Off"), sort: r => r.Settings.RegimeFilter, export: r => r.Settings.RegimeFilter ? "on" : "off", exportHeader: "regime_filter"),
            Fraction(Loc.T("In sample, per year"), r => r.InAnnualized, "in_annualized", 130),
            Col.Of<SimulationTuneRowDto>(Loc.T("In sample, Sharpe"), 120, r => r.InSharpe is { } v ? Fmt.Num(v, "N2") : Fmt.Dash, sort: r => r.InSharpe, numeric: true, export: r => Col.Inv(r.InSharpe), exportHeader: "in_sharpe"),
            Fraction(Loc.T("In sample, deepest fall"), r => r.InMaxDrawdown, "in_max_drawdown", 150),
            Fraction(Loc.T("Out of sample, return"), r => r.OutReturn, "out_return", 150),
            Col.Of<SimulationTuneRowDto>(Loc.T("Out of sample, Sharpe"), 140, r => r.OutSharpe is { } v ? Fmt.Num(v, "N2") : Fmt.Dash, sort: r => r.OutSharpe, numeric: true, export: r => Col.Inv(r.OutSharpe), exportHeader: "out_sharpe"),
            Fraction(Loc.T("Out of sample, deepest fall"), r => r.OutMaxDrawdown, "out_max_drawdown", 170),
            Col.Of<SimulationTuneRowDto>(Loc.T("Trades"), 80, r => Fmt.Number(r.Trades), sort: r => r.Trades, numeric: true, export: r => Col.Inv(r.Trades), exportHeader: "trades"),
            Col.Of<SimulationTuneRowDto>(Loc.T("Turnover a year"), 110, r => r.Turnover is { } v ? Fmt.Num(v, "N1") : Fmt.Dash, sort: r => r.Turnover, numeric: true, export: r => Col.Inv(r.Turnover), exportHeader: "turnover"),
        ];
        TuneTable.RowInvoked += (_, row) => ViewModel.UseTuneRow((SimulationTuneRowDto)row);

        ViewModel.PropertyChanged += OnViewModelChanged;
        Unloaded += (_, _) => ViewModel.PropertyChanged -= OnViewModelChanged;
    }

    public SimulationEditorViewModel ViewModel { get; }

    /// <summary>The simulation being edited (for back navigation); null for a new one.</summary>
    public string? SimulationId => ViewModel.EditingId;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string id)
            await ViewModel.PrepareEditAsync(id);
        else
            await ViewModel.PrepareNewAsync();
        SyncForm();
        App.Get<StartupOptions>().ApplyScroll(Scroller);
    }

    private static TableColumn Fraction(string header, Func<SimulationTuneRowDto, double?> value, string exportHeader, double width) =>
        Col.Of<SimulationTuneRowDto>(header, width, r => Fmt.FractionChange(value(r)), sort: r => value(r), tone: r => Fmt.ToneOf(value(r) * 100),
            numeric: true, export: r => Col.Inv(value(r)), exportHeader: exportHeader);

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SimulationEditorViewModel.Preset) or nameof(SimulationEditorViewModel.Sizing) or nameof(SimulationEditorViewModel.Fill)
            or nameof(SimulationEditorViewModel.Mode) or nameof(SimulationEditorViewModel.StartDate) or nameof(SimulationEditorViewModel.ApplyMode))
            SyncForm();
    }

    /// <summary>Shows the view model's choices in the combo boxes and the date picker without reacting to the change.</summary>
    private void SyncForm()
    {
        syncing = true;
        var isNew = ViewModel.IsNew;
        PageTitle.Text = isNew ? Loc.T("New simulation") : Loc.F("Parameters of “{0}”", ViewModel.Name);
        SaveButton.Content = isNew ? Loc.T("Create and replay") : Loc.T("Save and replay");
        Fill(ModeBox, SimulationEditorViewModel.Modes, ViewModel.Mode);
        Fill(PresetBox, SimulationEditorViewModel.PresetChoices, ViewModel.Preset);
        Fill(SizingBox, SimulationEditorViewModel.SizingChoices, ViewModel.Sizing);
        Fill(FillBox, SimulationEditorViewModel.FillChoices, ViewModel.Fill);
        Fill(ApplyBox, SimulationEditorViewModel.ApplyChoices, ViewModel.ApplyMode);
        Fill(ObjectiveBox, SimulationEditorViewModel.Objectives, ViewModel.TuneObjective);
        StartPicker.Date = ViewModel.StartDate is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue)) : null;

        // A running simulation keeps its mode; its start and capital change only with a replay from the start.
        var restart = ViewModel.ApplyMode == SimulationEditorViewModel.Restart;
        ModeBox.IsEnabled = isNew;
        ApplyBox.Visibility = Ui.Hide(isNew);
        StartPicker.IsEnabled = CapitalBox.IsEnabled = isNew || restart;
        ManualStrategyNote.Visibility = Ui.Show(ViewModel.Mode == SimulationText.Manual);
        StartNote.Text = ViewModel.Mode == SimulationText.Manual
            ? Loc.T("A manual portfolio starts with cash on its start date; your orders can fill from the session after the latest one.")
            : Loc.T("A start in the past replays the strategy up to the latest session (using only what was known at each close), then it keeps following the market. The latest session starts it today.");
        syncing = false;
    }

    private static void Fill(ComboBox box, IReadOnlyList<Choice> choices, string selected)
    {
        // Same options (the lists are rebuilt on every call): only move the selection.
        if (box.ItemsSource is IReadOnlyList<Choice> current && current.Select(c => c.Key).SequenceEqual(choices.Select(c => c.Key)))
        {
            box.SelectedItem = current.FirstOrDefault(c => c.Key == selected);
            return;
        }

        box.ItemsSource = choices;
        box.SelectedItem = choices.FirstOrDefault(c => c.Key == selected);
    }

    private static string? Key(object sender) => (sender as ComboBox)?.SelectedItem is Choice c ? c.Key : null;

    private void OnMode(object sender, SelectionChangedEventArgs e)
    {
        if (!syncing && Key(sender) is { } key)
            ViewModel.Mode = key;
    }

    private void OnPreset(object sender, SelectionChangedEventArgs e)
    {
        if (!syncing && Key(sender) is { } key && key != ViewModel.Preset && key != "custom")
            ViewModel.ApplyPreset(key);
    }

    private void OnSizing(object sender, SelectionChangedEventArgs e)
    {
        if (!syncing && Key(sender) is { } key)
            ViewModel.Sizing = key;
    }

    private void OnFill(object sender, SelectionChangedEventArgs e)
    {
        if (!syncing && Key(sender) is { } key)
            ViewModel.Fill = key;
    }

    private void OnApply(object sender, SelectionChangedEventArgs e)
    {
        if (!syncing && Key(sender) is { } key)
            ViewModel.ApplyMode = key;
    }

    private void OnObjective(object sender, SelectionChangedEventArgs e)
    {
        if (!syncing && Key(sender) is { } key)
            ViewModel.TuneObjective = key;
    }

    private void OnStartChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (!syncing && sender.Date is { } date)
            ViewModel.StartDate = DateOnly.FromDateTime(date.Date);
    }

    private async void OnExcludeTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            return;
        searching?.Cancel();
        var cts = searching = new CancellationTokenSource();
        try
        {
            await Task.Delay(150, cts.Token);
            sender.ItemsSource = await ViewModel.SearchAsync(sender.Text, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnExcludeChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is InstrumentSearchResultDto instrument)
            Exclude(sender, instrument);
    }

    private void OnExcludeSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if ((args.ChosenSuggestion ?? (sender.ItemsSource as IEnumerable<InstrumentSearchResultDto>)?.FirstOrDefault()) is InstrumentSearchResultDto instrument)
            Exclude(sender, instrument);
    }

    private void Exclude(AutoSuggestBox box, InstrumentSearchResultDto instrument)
    {
        ViewModel.Exclude(instrument);
        ViewModel.MarkCustom();
        box.Text = string.Empty;
        box.ItemsSource = null;
    }

    private void OnRemoveExcluded(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ExcludedShare share)
        {
            ViewModel.Excluded.Remove(share);
            ViewModel.MarkCustom();
        }
    }

    private async void OnTune(object sender, RoutedEventArgs e) => await ViewModel.TuneAsync();

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (navigator.CanGoBack)
            navigator.GoBack();
        else
            navigator.Navigate(Navigator.Simulations);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        // The editor leaves the back stack: Back from the simulation goes where the editor was opened from.
        if (ViewModel.Save() is { } id)
            navigator.Navigate(Navigator.Simulation, id, replace: true);
    }
}
