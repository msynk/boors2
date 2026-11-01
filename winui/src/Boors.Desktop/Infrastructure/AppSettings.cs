namespace Boors.Desktop.Infrastructure;

/// <summary>User preferences, persisted as JSON in %LOCALAPPDATA%\Boors\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>"fa" (default, right to left, Jalali dates) or "en".</summary>
    public string Language { get; set; } = "fa";

    /// <summary>Dark (default), Light or System.</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>Persian (۱۲۳) or Latin (123) digits for display; storage and exports are always ASCII.</summary>
    public string Digits { get; set; } = "Persian";

    /// <summary>Iranian (green up, red down) or ColorBlind (blue up, orange down).</summary>
    public string ColorPreset { get; set; } = "Iranian";

    /// <summary>Pro (dense, every panel) or Lite (larger rows, dense panels hidden).</summary>
    public string Density { get; set; } = "Pro";

    /// <summary>Local (in-process DuckDB store, the offline replica) or Remote (HTTP to a Boors.Node).</summary>
    public string Mode { get; set; } = "Local";

    /// <summary>Data root for the raw cache and market.duckdb; null = %LOCALAPPDATA%\Boors\data. BOORS_DATA overrides it.</summary>
    public string? DataRoot { get; set; }

    /// <summary>Node name recorded on raw entries this desktop fetches; null = machine name. BOORS_NODE overrides it.</summary>
    public string? NodeName { get; set; }

    public string NodeUrl { get; set; } = "http://localhost:5080/";

    public string? OperatorToken { get; set; }

    /// <summary>Never call a data source from a read (shareholder refresh).</summary>
    public bool Offline { get; set; }

    /// <summary>
    /// Download market data in the background (Local mode): the Iran-time collection schedule while the app is open,
    /// plus a catch-up of what was missed while it was closed. Replaces the old opt-in "Collect" key, which is ignored.
    /// </summary>
    public bool AutoDownload { get; set; } = true;

    public double WindowWidth { get; set; } = 1440;

    public double WindowHeight { get; set; } = 900;

    public bool WindowMaximized { get; set; }

    [JsonIgnore]
    public bool IsPersian => Language == "fa";

    /// <summary>The in-process scheduler runs: background download is on and the app reads its own store.</summary>
    [JsonIgnore]
    public bool DownloadsInBackground => AutoDownload && Mode == "Local";

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
