using Boors.Market.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace Boors.Desktop.Infrastructure;

/// <summary>The headline of a simulation's last replay, saved with it so the list shows at once after a restart.</summary>
public sealed class SimulationSummary
{
    public DateOnly? Start { get; set; }
    public DateOnly? AsOf { get; set; }
    public DateOnly? Latest { get; set; }
    public decimal Equity { get; set; }
    public double TotalReturn { get; set; }
    public double? DayReturn { get; set; }
    public double? AnnualizedReturn { get; set; }
    public double? TedpixReturn { get; set; }
    public double? ExcessReturn { get; set; }
    public double? UsdReturn { get; set; }
    public double MaxDrawdown { get; set; }
    public double? Sharpe { get; set; }
    public int Holdings { get; set; }
    public int Trades { get; set; }
    public int Planned { get; set; }
    public bool RiskOn { get; set; } = true;

    /// <summary>The last 60 closes of the equity curve (the list's sparkline).</summary>
    public List<double> Spark { get; set; } = [];

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Why the last replay failed (the definition stays as it was).</summary>
    public string? Error { get; set; }

    public static SimulationSummary From(SimulationResultDto r, DateTimeOffset now) => new()
    {
        Start = r.Start,
        AsOf = r.AsOf,
        Latest = r.Latest,
        Equity = r.Stats.Equity,
        TotalReturn = r.Stats.TotalReturn,
        DayReturn = r.Stats.DayReturn,
        AnnualizedReturn = r.Stats.AnnualizedReturn,
        TedpixReturn = r.Stats.TedpixReturn,
        ExcessReturn = r.Stats.ExcessReturn,
        UsdReturn = r.Stats.UsdReturn,
        MaxDrawdown = r.Stats.MaxDrawdown,
        Sharpe = r.Stats.Sharpe,
        Holdings = r.Holdings.Count,
        Trades = r.Stats.Trades,
        Planned = r.Planned.Count,
        RiskOn = r.RiskOn,
        Spark = [.. r.Equity.TakeLast(60).Select(p => (double)p.Equity)],
        UpdatedAt = now,
    };
}

/// <summary>A simulation as saved on disk: its definition, its last summary and what the user has already seen.</summary>
public sealed class SavedSimulation
{
    public SimulationSpecDto Spec { get; set; } = new();
    public SimulationSummary? Summary { get; set; }

    /// <summary>
    /// Trades the user had seen when the simulation was last open; newer ones count as new activity. Null until the
    /// first replay, which takes the trades of a start in the past as seen.
    /// </summary>
    public int? SeenTrades { get; set; }

    /// <summary>Symbols of the instruments the definition names by code (excluded shares), for display.</summary>
    public Dictionary<long, string> Symbols { get; set; } = [];

    public int NewTrades => Math.Max(0, (Summary?.Trades ?? 0) - (SeenTrades ?? Summary?.Trades ?? 0));

    public bool IsTracking => Spec.End is null;
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SavedSimulation))]
internal sealed partial class SimulationJsonContext : JsonSerializerContext;

/// <summary>
/// Simulations saved as one JSON file each (<c>{id}.json</c>, written atomically) in
/// <see cref="SettingsService.SimulationsFolder"/>. A file that cannot be read is logged and skipped, never deleted.
/// </summary>
public sealed class SimulationStore(SettingsService settings, ILogger<SimulationStore> logger)
{
    private readonly Lock gate = new();

    public string Folder => settings.SimulationsFolder;

    public List<SavedSimulation> LoadAll()
    {
        var result = new List<SavedSimulation>();
        if (!Directory.Exists(Folder))
            return result;

        foreach (var path in Directory.EnumerateFiles(Folder, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize(File.ReadAllBytes(path), SimulationJsonContext.Default.SavedSimulation) is { Spec.Id.Length: > 0 } saved)
                    result.Add(saved);
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("Simulation file {Path} could not be read: {Message}", path, e.Message);
            }
        }

        return [.. result.OrderBy(s => s.Spec.CreatedAt)];
    }

    public void Save(SavedSimulation simulation)
    {
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var path = PathOf(simulation.Spec.Id);
                var temp = path + ".tmp";
                File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(simulation, SimulationJsonContext.Default.SavedSimulation));
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("Simulation {Id} could not be saved: {Message}", simulation.Spec.Id, e.Message);
            }
        }
    }

    public void Delete(string id)
    {
        lock (gate)
        {
            try
            {
                File.Delete(PathOf(id));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("Simulation {Id} could not be deleted: {Message}", id, e.Message);
            }
        }
    }

    // Ids are generated here (hex); anything else is reduced to file-name characters.
    private string PathOf(string id) => Path.Combine(Folder, string.Concat(id.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) + ".json");
}
