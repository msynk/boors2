using Boors.Desktop.Infrastructure;
using Boors.Market.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace Boors.Desktop.ViewModels;

/// <summary>
/// Base of the page view models: one load at a time (a new load cancels the previous one), loading/error/empty
/// state for the page, and errors turned into a sentence instead of a crash. Data calls go through
/// <see cref="IMarketApi"/>, which runs store work on the thread pool; continuations come back to the UI thread.
/// View models are singletons, so a page shows its last state instantly when the user comes back to it.
/// </summary>
public abstract partial class PageViewModel(IMarketApi api, ILogger logger) : ObservableObject
{
    private CancellationTokenSource? loading;

    protected IMarketApi Api { get; } = api;

    protected ILogger Logger { get; } = logger;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>True once data was loaded at least once (the page then shows it while reloading).</summary>
    [ObservableProperty]
    public partial bool HasData { get; set; }

    /// <summary>Time of the last completed load, for the page footer.</summary>
    [ObservableProperty]
    public partial double LastLoadMilliseconds { get; set; }

    /// <summary>Re-computes every display string from the loaded data (language, digits or colours changed).</summary>
    public virtual void Refresh()
    {
    }

    protected async Task RunAsync(Func<CancellationToken, Task> work, string what)
    {
        loading?.Cancel();
        var cts = new CancellationTokenSource();
        loading = cts;
        IsLoading = true;
        Error = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await work(cts.Token);
            LastLoadMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            Logger.LogInformation("{What} loaded in {Elapsed:0} ms", what, LastLoadMilliseconds);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            Error = Describe(e);
            Logger.LogWarning(e, "{What} failed: {Message}", what, e.Message);
        }
        finally
        {
            if (ReferenceEquals(loading, cts))
            {
                IsLoading = false;
                loading = null;
            }
        }
    }

    public static string Describe(Exception e) => e switch
    {
        MarketApiException api => api.Message,
        HttpRequestException http => Loc.F("The node could not be reached: {0}", http.Message),
        TaskCanceledException => Loc.T("The request timed out."),
        IOException or UnauthorizedAccessException => Loc.F("The data store could not be opened: {0}", e.Message),
        _ when e.GetType().Name.Contains("DuckDB", StringComparison.Ordinal) => Loc.F("The data store could not be opened: {0}", e.Message),
        _ => e.Message,
    };
}

/// <summary>The UI thread's dispatcher, for events raised on pool threads (job queue changes).</summary>
public sealed class UiDispatcher
{
    public DispatcherQueue? Queue { get; set; }

    public void Post(Action action)
    {
        if (Queue is null)
            return;
        if (Queue.HasThreadAccess)
            action();
        else
            Queue.TryEnqueue(() => action());
    }
}
