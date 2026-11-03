using Boors.Desktop.ViewModels;
using Boors.Market.Services;
using Boors.Market.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace Boors.Desktop.Infrastructure;

/// <summary>
/// Keeps every simulation up to date with the market. Simulations are replays (a pure function of their definition and
/// the data), so following the market means replaying each one whenever a job brings new prices — the shell's
/// new-data signal — and once shortly after the app starts. Replays run one at a time through <see cref="IMarketApi"/>
/// (in-process, or on the node in remote mode); a request while one is running queues one more pass. Everything here
/// runs on the UI thread; the API does the work on the thread pool.
/// </summary>
public sealed class SimulationTracker(IMarketApi api, SimulationStore store, UiDispatcher dispatcher, ILogger<SimulationTracker> logger)
{
    private readonly Dictionary<string, SimulationResultDto> results = [];
    private List<SavedSimulation>? items;
    private bool running;
    private bool again;

    /// <summary>Raised on the UI thread when a simulation was added, changed, replayed or removed.</summary>
    public event EventHandler? Changed;

    public bool IsRefreshing => running;

    public IReadOnlyList<SavedSimulation> Items => items ??= store.LoadAll();

    /// <summary>Trades made since the user last opened each simulation (the menu badge).</summary>
    public int NewActivity => Items.Sum(s => s.NewTrades);

    public SavedSimulation? Find(string id) => Items.FirstOrDefault(s => s.Spec.Id == id);

    /// <summary>The last replay of a simulation in this session (its equity curve, for comparisons).</summary>
    public SimulationResultDto? Result(string id) => results.GetValueOrDefault(id);

    /// <summary>Replays after the app started (so the list and the badge are current) and after every new-data signal.</summary>
    public void Start(ShellViewModel shell)
    {
        shell.DataChanged += (_, _) => _ = RefreshAsync();
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(8));
            dispatcher.Post(() => _ = RefreshAsync());
        });
    }

    /// <summary>Replays every simulation (the ones that stopped tracking too: data repairs can still change their past).</summary>
    public async Task RefreshAsync()
    {
        if (running)
        {
            again = true;
            return;
        }

        running = true;
        Raise();
        try
        {
            do
            {
                again = false;
                foreach (var simulation in Items.ToList())
                    await ReplayAsync(simulation, detail: false, CancellationToken.None);
            }
            while (again);
        }
        finally
        {
            running = false;
            Raise();
        }
    }

    /// <summary>Replays one simulation and keeps its summary; failures are kept on the summary, not thrown.</summary>
    public async Task<SimulationResultDto?> ReplayAsync(SavedSimulation simulation, bool detail, CancellationToken cancellationToken)
    {
        try
        {
            var result = await api.RunSimulationAsync(new SimulationRunRequestDto(simulation.Spec, detail), cancellationToken);
            Record(simulation, result);
            return result;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Simulation {Id} could not be replayed: {Message}", simulation.Spec.Id, e.Message);
            simulation.Summary ??= new SimulationSummary();
            simulation.Summary.Error = PageViewModel.Describe(e);
            store.Save(simulation);
            Raise();
            if (detail)
                throw;
            return null;
        }
    }

    public SavedSimulation Add(SimulationSpecDto spec, Dictionary<long, string>? symbols = null)
    {
        var simulation = new SavedSimulation { Spec = spec, Symbols = symbols ?? [] };
        _ = Items;
        items!.Add(simulation);
        store.Save(simulation);
        Raise();
        return simulation;
    }

    /// <summary>
    /// Saves a changed definition; its last replay no longer applies. A replay from the start (<paramref name="restarted"/>)
    /// takes its trades as seen again.
    /// </summary>
    public void Update(SavedSimulation simulation, SimulationSpecDto spec, bool restarted = false)
    {
        simulation.Spec = spec;
        if (restarted)
            simulation.SeenTrades = null;
        results.Remove(spec.Id);
        store.Save(simulation);
        Raise();
    }

    public void Delete(string id)
    {
        items?.RemoveAll(s => s.Spec.Id == id);
        results.Remove(id);
        store.Delete(id);
        Raise();
    }

    /// <summary>The user has seen the simulation's trades so far.</summary>
    public void MarkSeen(SavedSimulation simulation)
    {
        var trades = simulation.Summary?.Trades ?? 0;
        if (simulation.SeenTrades == trades)
            return;
        simulation.SeenTrades = trades;
        store.Save(simulation);
        Raise();
    }

    private void Record(SavedSimulation simulation, SimulationResultDto result)
    {
        results[simulation.Spec.Id] = result;
        simulation.Summary = SimulationSummary.From(result, DateTimeOffset.Now);

        simulation.SeenTrades ??= result.Stats.Trades;
        store.Save(simulation);
        Raise();
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
