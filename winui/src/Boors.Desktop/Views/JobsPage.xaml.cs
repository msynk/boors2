using System.Text;
using Boors.Desktop.Controls;
using Boors.Desktop.Infrastructure;
using Boors.Desktop.ViewModels;
using Boors.Market.Services.Contracts;
using Boors.Market.Services.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace Boors.Desktop.Views;

/// <summary>
/// The job monitor: live state, statistics, charts, the run history with a detail pane (fields and log), and the
/// network traffic. Tables are code-built (DataTable columns); the view model does every computation.
/// </summary>
public sealed partial class JobsPage : Page, IReloadable
{
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly SettingsService settings;
    private readonly ILogger<JobsPage> logger;
    private JobRow? selectedJob;
    private bool loading = true;

    public JobsPage()
    {
        ViewModel = App.Get<JobsViewModel>();
        settings = App.Get<SettingsService>();
        logger = App.Get<ILogger<JobsPage>>();
        InitializeComponent();

        PeriodSwitch.Items = JobsViewModel.Periods;
        PeriodSwitch.SelectedKey = ViewModel.Period;
        PeriodSwitch.SelectionChanged += (_, key) => ViewModel.Period = key;
        StatusSwitch.Items = JobsViewModel.StatusFilters;
        StatusSwitch.SelectedKey = ViewModel.StatusFilter;
        StatusSwitch.SelectionChanged += (_, key) => ViewModel.StatusFilter = key;
        SearchBox.Text = ViewModel.Search;
        ProblemsOnlyBox.IsChecked = ViewModel.ProblemsOnly;
        AutoDownloadSwitch.IsOn = settings.Current.AutoDownload;
        AutoDownloadSwitch.Visibility = Ui.Show(ViewModel.IsLocal);
        OpenLogsButton.Visibility = Ui.Show(ViewModel.IsLocal);
        QueueHint.Text = ViewModel.IsLocal
            ? Loc.F("Jobs run one at a time (the store has a single writer). The queue keeps this session's last {0} finished jobs with their logs.", Fmt.Number(MarketJobQueue.HistoryLimit))
            : Loc.T("Jobs run on the node; this list is the node's queue.");
        BuildLegend();
        BuildColumns();

        JobsTable.RowInvoked += (_, row) => SelectJob((JobRow)row);
        QueueTable.RowInvoked += async (_, row) => await ViewModel.ShowEntryAsync(((JobInfoDto)row).Id);
        HistoryTable.RowInvoked += async (_, row) => await ViewModel.ShowRunAsync((RunRow)row);

        ViewModel.PropertyChanged += OnViewModelChanged;
        tick.Tick += (_, _) => ViewModel.Tick();
        poll.Tick += async (_, _) => await ViewModel.PollAsync();
        SizeChanged += (_, e) => Arrange(e.NewSize.Width);
        Unloaded += (_, _) =>
        {
            ViewModel.PropertyChanged -= OnViewModelChanged;
            tick.Stop();
            poll.Stop();
        };
        Show();
        loading = false;
    }

    public JobsViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        App.Get<ShellViewModel>().ClearJobsBadge();
        if (!ViewModel.IsLocal)
            poll.Start();
        await ViewModel.LoadAsync();
        App.Get<StartupOptions>().ApplyScroll(Scroller);
    }

    public void Reload() => _ = ViewModel.LoadAsync();

    // ---------------------------------------------------------------- tables

    private void BuildColumns()
    {
        var now = () => DateTimeOffset.UtcNow;
        JobsTable.EmptyText = Loc.T("No jobs.");
        JobsTable.Columns =
        [
            Col.Of<JobRow>(Loc.T("Job"), 160, r => r.Name, sort: r => r.Name),
            Col.Of<JobRow>(Loc.T("Last run"), 120, _ => string.Empty, sort: r => r.Summary.Last?.Status,
                tag: r => r.Summary.Last is { } last ? (JobsViewModel.StatusLabel(JobRunStatistics.Outcome(last)), JobsViewModel.StatusTone(JobRunStatistics.Outcome(last))) : null),
            Col.Of<JobRow>(Loc.T("When"), 110, r => Fmt.Relative(r.Summary.Last?.StartedAt, now()), sort: r => r.Summary.Last?.StartedAt,
                export: r => r.Summary.Last?.StartedAt.ToString("O", CultureInfo.InvariantCulture)),
            Col.Of<JobRow>(Loc.T("Next run"), 140, r => r.NextRun is { } n ? $"{Fmt.Clock(n)} · {Fmt.Relative(n, now())}" : Fmt.Dash, sort: r => r.NextRun,
                export: r => r.NextRun?.ToString("O", CultureInfo.InvariantCulture)),
            Col.Of<JobRow>(Loc.T("Schedule (Iran time)"), 230, r => r.Schedule, sort: r => r.Schedule),
            Col.Of<JobRow>(Loc.T("Runs"), 64, r => Fmt.Number(r.Summary.Runs), sort: r => r.Summary.Runs, numeric: true, export: r => Col.Inv(r.Summary.Runs)),
            Col.Of<JobRow>(Loc.T("Success"), 84, r => Fmt.Percent(r.Summary.SuccessRate), sort: r => r.Summary.SuccessRate, numeric: true,
                tone: r => r.Summary.SuccessRate is < 0.95 ? Tone.Warning : Tone.Neutral, export: r => Col.Inv(r.Summary.SuccessRate)),
            Col.Of<JobRow>(Loc.T("Failed"), 64, r => Fmt.Number(r.Summary.Failed), sort: r => r.Summary.Failed, numeric: true,
                tone: r => r.Summary.Failed > 0 ? Tone.Critical : Tone.Neutral, export: r => Col.Inv(r.Summary.Failed)),
            Col.Of<JobRow>(Loc.T("Failing in a row"), 84, r => r.Summary.FailureStreak > 0 ? $"✕ {Fmt.Number(r.Summary.FailureStreak)}" : Fmt.Dash,
                sort: r => r.Summary.FailureStreak, numeric: true, tone: r => r.Summary.FailureStreak > 0 ? Tone.Critical : Tone.Neutral, export: r => Col.Inv(r.Summary.FailureStreak)),
            Col.Of<JobRow>(Loc.T("Average time"), 90, r => Fmt.Duration(r.Summary.AverageDuration), sort: r => r.Summary.AverageDuration, numeric: true,
                export: r => Col.Inv(r.Summary.AverageDuration?.TotalSeconds)),
            Col.Of<JobRow>(Loc.T("Longest"), 84, r => Fmt.Duration(r.Summary.MaxDuration), sort: r => r.Summary.MaxDuration, numeric: true,
                export: r => Col.Inv(r.Summary.MaxDuration?.TotalSeconds)),
            Col.Of<JobRow>(Loc.T("Items"), 96, r => Fmt.Number(r.Summary.Items), sort: r => r.Summary.Items, numeric: true, export: r => Col.Inv(r.Summary.Items)),
            Col.Of<JobRow>(Loc.T("Recent runs"), 190, r => r.Strip, export: r => string.Concat(r.Summary.Recent.Reverse().Select(JobsViewModel.Glyph))),
            Col.Of<JobRow>(Loc.T("Last problem"), 360, r => r.Summary.LastFailure is { } f ? $"{Fmt.DateTime(f.StartedAt)} · {f.Message}" : string.Empty, fill: true),
        ];

        QueueTable.EmptyText = Loc.T("No jobs since the app started.");
        QueueTable.Columns =
        [
            Col.Of<JobInfoDto>(Loc.T("Job"), 150, j => HealthViewModel.JobName(j.Job), sort: j => j.Job),
            Col.Of<JobInfoDto>(Loc.T("Status"), 120, _ => string.Empty, sort: j => j.State, tag: j => (JobsViewModel.StatusLabel(j.State), JobsViewModel.StatusTone(j.State))),
            Col.Of<JobInfoDto>(Loc.T("Trigger"), 90, j => JobsViewModel.TriggerName(j.Trigger), sort: j => j.Trigger),
            Col.Of<JobInfoDto>(Loc.T("Queued"), 130, j => Fmt.DateTime(j.EnqueuedAt), sort: j => j.EnqueuedAt),
            Col.Of<JobInfoDto>(Loc.T("Waited"), 80, j => Fmt.Duration((j.StartedAt ?? now()) - j.EnqueuedAt), numeric: true),
            Col.Of<JobInfoDto>(Loc.T("Duration"), 80, j => j.StartedAt is { } s ? Fmt.Duration((j.FinishedAt ?? now()) - s) : Fmt.Dash, numeric: true),
            Col.Of<JobInfoDto>(Loc.T("Items"), 80, j => Fmt.Number(j.Items), sort: j => j.Items, numeric: true),
            Col.Of<JobInfoDto>(Loc.T("Requests"), 80, j => Fmt.Number(j.Requests), sort: j => j.Requests, numeric: true),
            Col.Of<JobInfoDto>(Loc.T("Failed requests"), 80, j => Fmt.Number(j.FailedRequests), sort: j => j.FailedRequests, numeric: true,
                tone: j => j.FailedRequests > 0 ? Tone.Critical : Tone.Neutral),
            Col.Of<JobInfoDto>(Loc.T("Retries"), 70, j => Fmt.Number(j.Retries), sort: j => j.Retries, numeric: true),
            Col.Of<JobInfoDto>(Loc.T("Warnings"), 76, j => Fmt.Number(j.Warnings + j.Errors), sort: j => j.Warnings + j.Errors, numeric: true,
                tone: j => j.Errors > 0 ? Tone.Critical : j.Warnings > 0 ? Tone.Warning : Tone.Neutral),
            Col.Of<JobInfoDto>(Loc.T("Message"), 380, j => j.Message ?? j.Progress ?? string.Empty, fill: true),
        ];

        HistoryTable.EmptyText = Loc.T("No runs match.");
        HistoryTable.Columns =
        [
            Col.Of<RunRow>(Loc.T("Started"), 130, r => Fmt.DateTime(r.Run.StartedAt), sort: r => r.Run.StartedAt,
                export: r => r.Run.StartedAt.ToString("O", CultureInfo.InvariantCulture)),
            Col.Of<RunRow>(Loc.T("Job"), 150, r => r.Name, sort: r => r.Name, export: r => r.Run.Job),
            Col.Of<RunRow>(Loc.T("Status"), 120, _ => string.Empty, sort: r => r.Outcome,
                tag: r => (JobsViewModel.StatusLabel(r.Outcome), JobsViewModel.StatusTone(r.Outcome)), export: r => r.Outcome),
            Col.Of<RunRow>(Loc.T("Duration"), 84, r => r.Duration is { } d ? Fmt.Duration(d) : Loc.T("running"), sort: r => r.Duration, numeric: true,
                export: r => Col.Inv(r.Duration?.TotalSeconds)),
            Col.Of<RunRow>(Loc.T("Items"), 84, r => Fmt.Number(r.Run.Items), sort: r => r.Run.Items, numeric: true, export: r => Col.Inv(r.Run.Items)),
            Col.Of<RunRow>(Loc.T("Failures"), 72, r => Fmt.Number(r.Run.Failures), sort: r => r.Run.Failures, numeric: true,
                tone: r => r.Run.Failures > 0 ? Tone.Critical : Tone.Neutral, export: r => Col.Inv(r.Run.Failures)),
            Col.Of<RunRow>(Loc.T("Node"), 120, r => r.Run.Node, sort: r => r.Run.Node),
            Col.Of<RunRow>(Loc.T("Message"), 420, r => r.Run.Message ?? string.Empty, fill: true),
        ];
        HistoryTable.SetSort(0, descending: true);

        TrafficTable.EmptyText = Loc.T("No requests since the app started.");
        TrafficTable.Columns =
        [
            Col.Of<SourceTrafficDto>(Loc.T("Host"), 200, t => t.Host, sort: t => t.Host),
            Col.Of<SourceTrafficDto>(Loc.T("Requests"), 90, t => Fmt.Number(t.Requests), sort: t => t.Requests, numeric: true),
            Col.Of<SourceTrafficDto>(Loc.T("Failed"), 80, t => Fmt.Number(t.Failed), sort: t => t.Failed, numeric: true, tone: t => t.Failed > 0 ? Tone.Critical : Tone.Neutral),
            Col.Of<SourceTrafficDto>(Loc.T("Error rate"), 84, t => Fmt.Percent(t.Requests == 0 ? null : (double)t.Failed / t.Requests), sort: t => t.Requests == 0 ? 0 : (double)t.Failed / t.Requests, numeric: true),
            Col.Of<SourceTrafficDto>(Loc.T("Average"), 84, t => Fmt.Milliseconds(t.AverageMs), sort: t => t.AverageMs, numeric: true),
            Col.Of<SourceTrafficDto>("P95", 84, t => Fmt.Milliseconds(t.P95Ms), sort: t => t.P95Ms, numeric: true),
            Col.Of<SourceTrafficDto>(Loc.T("Slowest"), 90, t => Fmt.Milliseconds(t.MaxMs), sort: t => t.MaxMs, numeric: true),
            Col.Of<SourceTrafficDto>(Loc.T("Last status"), 80, t => t.LastStatus is { } s ? Fmt.Number(s) : Fmt.Dash, sort: t => t.LastStatus, numeric: true,
                tone: t => t.LastStatus >= 400 ? Tone.Critical : Tone.Neutral),
            Col.Of<SourceTrafficDto>(Loc.T("Last request"), 130, t => Fmt.DateTime(t.LastAt), sort: t => t.LastAt),
            Col.Of<SourceTrafficDto>(Loc.T("Last error"), 300, t => t.LastError is { } e ? $"{Fmt.DateTime(t.LastFailedAt)} · {e}" : string.Empty, fill: true),
        ];
    }

    // ---------------------------------------------------------------- view model → view

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(JobsViewModel.JobRows):
                JobsTable.Items = ViewModel.JobRows;
                if (selectedJob is not null)
                    selectedJob = ViewModel.JobRows.FirstOrDefault(r => r.Job == selectedJob.Job);
                break;
            case nameof(JobsViewModel.Entries):
                QueueTable.Items = ViewModel.Entries;
                RunMenuItems();
                break;
            case nameof(JobsViewModel.History):
                HistoryTable.Items = ViewModel.History;
                break;
            case nameof(JobsViewModel.Traffic):
                TrafficTable.Items = ViewModel.Traffic;
                break;
            case nameof(JobsViewModel.JobChoices):
                FillJobBox();
                break;
            case nameof(JobsViewModel.Running):
            case nameof(JobsViewModel.Scheduled):
                ShowState();
                break;
            case nameof(JobsViewModel.Detail):
                ShowDetail();
                break;
        }
    }

    private void Show()
    {
        JobsTable.Items = ViewModel.JobRows;
        QueueTable.Items = ViewModel.Entries;
        HistoryTable.Items = ViewModel.History;
        TrafficTable.Items = ViewModel.Traffic;
        FillJobBox();
        RunMenuItems();
        ShowState();
        ShowDetail();
    }

    /// <summary>The title badge: running (with the job), idle, or off; icon and words, never colour alone.</summary>
    private void ShowState()
    {
        var running = ViewModel.Running;
        var (text, tone) = running is not null ? ($"⟳ {Loc.F("Running: {0}", HealthViewModel.JobName(running.Job))}", Tone.Info)
            : ViewModel.Scheduled || !ViewModel.IsLocal ? ($"✓ {Loc.T("Idle")}", Tone.Good)
            : ($"⊘ {Loc.T("Background download off")}", Tone.Warning);
        Badge(StateBadge, StateText, text, tone);
        CancelRunningButton.Visibility = Ui.Show(running is not null);
        if (running is null)
            tick.Stop();
        else
            tick.Start();
    }

    private static void Badge(Border badge, TextBlock label, string text, Tone tone)
    {
        var c = Palette.ColorOf(tone);
        badge.Background = new SolidColorBrush(Color.FromArgb(0x30, c.R, c.G, c.B));
        label.Foreground = Palette.Brush(tone);
        label.Text = text;
    }

    private void ShowDetail()
    {
        var detail = ViewModel.Detail;
        DetailPane.Visibility = Ui.Show(detail is not null);
        DetailColumn.Width = detail is null ? new GridLength(0) : new GridLength(Math.Clamp(ActualWidth * 0.36, 380, 560));
        if (detail is null)
            return;
        DetailTitle.Text = detail.Title;
        Badge(DetailBadge, DetailStatus, detail.StatusText, detail.StatusTone);
        DetailFields.ItemsSource = detail.Fields;
        DetailMessage.Text = detail.Message ?? string.Empty;
        DetailMessage.Visibility = Ui.ShowText(detail.Message);
        DetailCancel.Visibility = Ui.Show(detail.CanCancel);
        DetailRunAgain.Visibility = Ui.Show(detail.CanRunAgain);
    }

    private void BuildLegend()
    {
        foreach (var (label, tone) in new[]
        {
            ($"✓ {Loc.T("OK")}", Tone.Good),
            ($"↷ {Loc.T("Skipped")}", Tone.Neutral),
            ($"⚠ {Loc.T("Partial")}", Tone.Warning),
            ($"✕ {Loc.T("Failed")}", Tone.Critical),
            ($"⊘ {Loc.T("Cancelled")}", Tone.Muted),
        })
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            item.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle { Width = 10, Height = 10, RadiusX = 2, RadiusY = 2, Fill = Palette.Brush(tone), VerticalAlignment = VerticalAlignment.Center });
            item.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Palette.TextSecondary });
            Legend.Children.Add(item);
        }
    }

    private void FillJobBox()
    {
        var choices = ViewModel.JobChoices;
        var wasLoading = loading;
        loading = true;
        JobBox.ItemsSource = choices;
        JobBox.SelectedItem = choices.FirstOrDefault(c => c.Key == ViewModel.JobFilter) ?? choices.FirstOrDefault();
        loading = wasLoading;
    }

    private void RunMenuItems()
    {
        if (RunMenu.Items.Count == ViewModel.RunnableJobs.Count)
            return;
        RunMenu.Items.Clear();
        foreach (var job in ViewModel.RunnableJobs)
        {
            var item = new MenuFlyoutItem { Text = HealthViewModel.JobName(job), Tag = job };
            item.Click += async (s, _) => await ViewModel.RunJobAsync((string)((MenuFlyoutItem)s).Tag);
            RunMenu.Items.Add(item);
        }
    }

    /// <summary>Wide: the two charts side by side; narrow: stacked.</summary>
    private void Arrange(double width)
    {
        var wide = width - DetailColumn.ActualWidth >= 1100;
        ChartsGrid.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        ChartsGrid.ColumnSpacing = wide ? 16 : 0;
        Grid.SetRow(DurationCard, wide ? 0 : 1);
        Grid.SetColumn(DurationCard, wide ? 1 : 0);
        if (ViewModel.Detail is not null)
            DetailColumn.Width = new GridLength(Math.Clamp(width * 0.36, 380, 560));
    }

    // ---------------------------------------------------------------- user actions

    private void SelectJob(JobRow row)
    {
        selectedJob = row;
        ViewModel.JobFilter = row.Job;
        FillJobBox();
        RunSelectedButton.IsEnabled = row.Runnable;
        RunSelectedText.Text = Loc.F("Run {0}", row.Name);
    }

    private void OnJobFilter(object sender, SelectionChangedEventArgs e)
    {
        if (loading || JobBox.SelectedItem is not Choice choice)
            return;
        ViewModel.JobFilter = choice.Key;
        selectedJob = ViewModel.JobRows.FirstOrDefault(r => r.Job == choice.Key);
        RunSelectedButton.IsEnabled = selectedJob?.Runnable ?? false;
        RunSelectedText.Text = selectedJob is null ? Loc.T("Run now") : Loc.F("Run {0}", selectedJob.Name);
    }

    private void OnSearch(object sender, TextChangedEventArgs e) => ViewModel.Search = SearchBox.Text;

    private async void OnRunSelected(object sender, RoutedEventArgs e)
    {
        if (selectedJob is { Runnable: true } job)
            await ViewModel.RunJobAsync(job.Job);
    }

    private async void OnCancelRunning(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Running is { } running)
            await ViewModel.CancelAsync(running.Id);
    }

    private async void OnShowRunningLog(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Running is { } running)
            await ViewModel.ShowEntryAsync(running.Id);
    }

    private async void OnCancelWaiting(object sender, RoutedEventArgs e) => await ViewModel.CancelWaitingAsync();

    private async void OnDetailCancel(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Detail?.EntryId is { } id)
            await ViewModel.CancelAsync(id);
    }

    private async void OnDetailRunAgain(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Detail is { CanRunAgain: true } detail)
            await ViewModel.RunJobAsync(detail.Job);
    }

    private void OnCloseDetail(object sender, RoutedEventArgs e) => ViewModel.CloseDetail();

    private void OnProblemsOnly(object sender, RoutedEventArgs e) => ViewModel.ProblemsOnly = ProblemsOnlyBox.IsChecked == true;

    private void OnCopyLog(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(ViewModel.LogText());
        Clipboard.SetContent(package);
        ViewModel.Notice = Loc.T("The log is on the clipboard.");
    }

    private async void OnOpenLogs(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchFolderPathAsync(RunLogReader.Folder);

    private void OnAutoDownload(object sender, RoutedEventArgs e)
    {
        if (!loading)
            settings.Update(s => s.AutoDownload = AutoDownloadSwitch.IsOn);
    }

    private void OnNoticeClosed(InfoBar sender, InfoBarClosedEventArgs args) => ViewModel.Notice = null;

    private void OnReload(object sender, RoutedEventArgs e) => Reload();

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker(XamlRoot.ContentIslandEnvironment.AppWindowId)
            {
                SuggestedFileName = $"job-runs-{DateTime.Now:yyyy-MM-dd}",
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeChoices.Add("CSV", [".csv"]);
            var file = await picker.PickSaveFileAsync();
            if (file is null)
                return;

            var rows = HistoryTable.View.Count;
            await using (var stream = File.Create(file.Path))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
                HistoryTable.ExportCsv(writer);
            ViewModel.Notice = Loc.F("Exported {0} rows to {1}", Fmt.Number(rows), Path.GetFileName(file.Path));
            logger.LogInformation("Exported {Rows} job runs to {Path}", rows, file.Path);
        }
        catch (Exception ex)
        {
            ViewModel.Notice = ex.Message;
            logger.LogWarning(ex, "CSV export failed");
        }
    }
}
