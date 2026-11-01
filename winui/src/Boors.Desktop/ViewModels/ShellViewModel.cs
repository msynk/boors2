using Boors.Desktop.Infrastructure;
using Boors.Market.Services;
using Boors.Market.Services.Contracts;
using Boors.Market.Services.Jobs;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Boors.Desktop.ViewModels;

/// <summary>A command-palette or search entry: a symbol to open or a command to run.</summary>
public sealed record PaletteEntry(string Title, string Subtitle, string Glyph, Func<Task> Run)
{
    public override string ToString() => Title;
}

/// <summary>Title-bar search, the command palette (Ctrl+K), the job indicator and the new-data signal for pages.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    // Jobs whose new rows change what the pages show (prices, indices, series). Codal and messages change only the
    // symbol feeds, every 15–30 minutes; reloading the open page for them would only disturb the user.
    private static readonly HashSet<string> PageDataJobs = ["eod", "repair", "backfill", "indices", "instruments", "macro", "funds", "reload", "sync"];

    private readonly IMarketApi api;
    private readonly Navigator navigator;
    private readonly SettingsService settings;
    private readonly MarketJobQueue queue;
    private readonly UiDispatcher dispatcher;
    private readonly ILogger<ShellViewModel> logger;
    private CancellationTokenSource? searching;
    private int jobRefreshQueued;
    private DateTimeOffset lastFinishedSeen = DateTimeOffset.MinValue;

    public ShellViewModel(IMarketApi api, Navigator navigator, SettingsService settings, MarketJobQueue queue, UiDispatcher dispatcher, ILogger<ShellViewModel> logger)
    {
        this.api = api;
        this.navigator = navigator;
        this.settings = settings;
        this.queue = queue;
        this.dispatcher = dispatcher;
        this.logger = logger;
        queue.Changed += (_, _) =>
        {
            if (Interlocked.Exchange(ref jobRefreshQueued, 1) == 0)
                dispatcher.Post(UpdateJobIndicator);
        };
    }

    [ObservableProperty]
    public partial IReadOnlyList<PaletteEntry> Suggestions { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<PaletteEntry> PaletteEntries { get; set; } = [];

    [ObservableProperty]
    public partial string? JobText { get; set; }

    [ObservableProperty]
    public partial bool JobRunning { get; set; }

    /// <summary>Jobs that failed (or finished with failed items) since the job monitor was last open.</summary>
    [ObservableProperty]
    public partial int JobsBadge { get; set; }

    public bool HasJobsBadge => JobsBadge > 0;

    public string JobsBadgeText => Loc.F("{0} jobs with failures since you last looked", Fmt.Number(JobsBadge));

    public void ClearJobsBadge() => JobsBadge = 0;

    partial void OnJobsBadgeChanged(int value)
    {
        OnPropertyChanged(nameof(HasJobsBadge));
        OnPropertyChanged(nameof(JobsBadgeText));
    }

    /// <summary>Theme/language toggles from the palette; the shell applies them.</summary>
    public event EventHandler<Action<AppSettings>>? SettingsRequested;

    /// <summary>A job finished with new data for the pages (on the UI thread); the shell reloads the open page.</summary>
    public event EventHandler? DataChanged;

    /// <summary>Symbol suggestions for the title-bar search (debounced, symbol first, then name).</summary>
    public async Task SuggestAsync(string text)
    {
        searching?.Cancel();
        var cts = searching = new CancellationTokenSource();
        if (string.IsNullOrWhiteSpace(text))
        {
            Suggestions = [];
            return;
        }

        try
        {
            await Task.Delay(150, cts.Token);
            Suggestions = await SymbolsAsync(text, 10, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            logger.LogInformation("Search failed: {Message}", e.Message);
            Suggestions = [];
        }
    }

    /// <summary>Enter in the search box: the picked suggestion, else the best match.</summary>
    public async Task OpenBestAsync(string text, PaletteEntry? chosen)
    {
        if (chosen is not null)
        {
            await chosen.Run();
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
            return;
        var best = (await SymbolsAsync(text, 1, CancellationToken.None)).FirstOrDefault();
        if (best is not null)
            await best.Run();
    }

    /// <summary>The palette: commands matching the text, then symbols.</summary>
    public async Task UpdatePaletteAsync(string text)
    {
        searching?.Cancel();
        var cts = searching = new CancellationTokenSource();
        var commands = Commands().Where(c => Matches(c, text)).ToList();
        PaletteEntries = commands;
        if (string.IsNullOrWhiteSpace(text) || text.StartsWith('>'))
            return;

        try
        {
            await Task.Delay(120, cts.Token);
            var symbols = await SymbolsAsync(text, 8, cts.Token);
            PaletteEntries = [.. symbols, .. commands];
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            logger.LogInformation("Palette search failed: {Message}", e.Message);
        }
    }

    private async Task<List<PaletteEntry>> SymbolsAsync(string text, int take, CancellationToken cancellationToken)
    {
        var results = await api.SearchAsync(text.Trim(), take, cancellationToken);
        var seen = new HashSet<long>();
        List<PaletteEntry> entries = [];
        foreach (var r in results)
        {
            var target = r.ParentInsCode ?? r.InsCode;
            if (!seen.Add(target))
                continue;
            entries.Add(new PaletteEntry(r.Symbol, $"{r.Name} · {Fmt.ClassName(r.InstrumentClass)}", "", () =>
            {
                navigator.OpenSymbol(target);
                return Task.CompletedTask;
            }));
        }

        return entries;
    }

    private IEnumerable<PaletteEntry> Commands()
    {
        PaletteEntry Go(string title, string key, string glyph) => new(title, Loc.T("Go to"), glyph, () =>
        {
            navigator.Navigate(key);
            return Task.CompletedTask;
        });

        yield return Go(Loc.T("Market overview"), Navigator.Overview, "");
        yield return Go(Loc.T("Market watch"), Navigator.Watch, "");
        yield return Go(Loc.T("Screens"), Navigator.Screens, "");
        yield return Go(Loc.T("Reports"), Navigator.Reports, "");
        yield return Go(Loc.T("Data health"), Navigator.Health, "");
        yield return Go(Loc.T("Data collection"), Navigator.Jobs, "");
        yield return Go(Loc.T("Settings"), Navigator.Settings, "");
        yield return new(Loc.T("Toggle dark / light theme"), Loc.T("Appearance"), "", () => Apply(s => s.Theme = s.Theme == "Light" ? "Dark" : "Light"));
        yield return new(Loc.T("Switch language (فارسی / English)"), Loc.T("Appearance"), "", () => Apply(s => s.Language = s.Language == "fa" ? "en" : "fa"));
        yield return new(Loc.T("Toggle Persian / Latin digits"), Loc.T("Appearance"), "", () => Apply(s => s.Digits = s.Digits == "Persian" ? "Latin" : "Persian"));
        yield return new(Loc.T("Toggle colour-blind safe colours"), Loc.T("Appearance"), "", () => Apply(s => s.ColorPreset = s.ColorPreset == "Iranian" ? "ColorBlind" : "Iranian"));
        yield return new(Loc.T("Toggle Lite / Pro density"), Loc.T("Appearance"), "", () => Apply(s => s.Density = s.Density == "Pro" ? "Lite" : "Pro"));
        foreach (var job in new[] { "eod", "repair", "instruments", "macro", "codal", "messages", "reload" })
        {
            yield return new(Loc.F("Run job: {0}", HealthViewModel.JobName(job)), Loc.T("Operations"), "", async () =>
            {
                try
                {
                    await api.RunJobAsync(job, CancellationToken.None);
                    navigator.Navigate(Navigator.Jobs);
                }
                catch (Exception e)
                {
                    logger.LogWarning("Job {Job} not queued: {Message}", job, e.Message);
                }
            });
        }

        yield return new(Loc.T("Sync from node"), Loc.T("Operations"), "", () =>
        {
            navigator.Navigate(Navigator.Health);
            return Task.CompletedTask;
        });
    }

    private Task Apply(Action<AppSettings> change)
    {
        SettingsRequested?.Invoke(this, change);
        return Task.CompletedTask;
    }

    private static bool Matches(PaletteEntry entry, string text)
    {
        var query = text.TrimStart('>').Trim();
        return query.Length == 0
            || entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    public void UpdateJobIndicator()
    {
        Interlocked.Exchange(ref jobRefreshQueued, 0);
        if (settings.Current.Mode == "Remote")
        {
            JobRunning = false;
            JobText = null;
            return;
        }

        var running = queue.Current;
        JobRunning = running is not null;
        JobText = running is null ? null
            : string.IsNullOrWhiteSpace(running.Progress) ? HealthViewModel.JobName(running.Job) : $"{HealthViewModel.JobName(running.Job)}: {running.Progress}";

        var finished = queue.Snapshot().Where(j => j.FinishedAt > lastFinishedSeen).ToList();
        if (finished.Count == 0)
            return;
        lastFinishedSeen = finished.Max(j => j.FinishedAt!.Value);
        if (navigator.CurrentKey != Navigator.Jobs)
            JobsBadge += finished.Count(j => j.State == "failed" || j.Failures > 0);
        if (finished.Any(j => j.State == "succeeded" && j.Items > 0 && PageDataJobs.Contains(j.Job)))
            DataChanged?.Invoke(this, EventArgs.Empty);
    }
}
