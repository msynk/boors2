using Microsoft.Extensions.Logging;

namespace Boors.Desktop.Infrastructure;

/// <summary>
/// Loads and saves <see cref="AppSettings"/>. Command-line switches (--lang, --theme, --digits, --preset, --density,
/// --page, --data) and the BOORS_DATA / BOORS_NODE environment variables override the saved values for one session
/// without being written back, so screenshots and tests can pin an appearance.
/// </summary>
public sealed class SettingsService
{
    private readonly AppSettings saved;
    private readonly Dictionary<string, string> overrides;
    private readonly List<string> deferred = [];
    private ILogger? logger;

    /// <summary>Created before the host (the window needs its size and theme first); log lines wait for <see cref="LogDeferred"/>.</summary>
    public SettingsService(StartupOptions startup)
    {
        Directory.CreateDirectory(AppFolder);
        saved = Load();
        overrides = startup.Overrides;
        Current = Apply(saved.Clone());
    }

    public static string AppFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Boors");

    public static string SettingsPath => Path.Combine(AppFolder, "settings.json");

    public static string DefaultDataRoot => Path.Combine(AppFolder, "data");

    /// <summary>The effective settings (saved values plus this session's overrides).</summary>
    public AppSettings Current { get; private set; }

    /// <summary>The data root this process opened (changing it needs a restart: the store is opened once).</summary>
    public string EffectiveDataRoot =>
        Environment.GetEnvironmentVariable("BOORS_DATA") is { Length: > 0 } env ? Path.GetFullPath(env)
        : overrides.TryGetValue("data", out var cli) ? Path.GetFullPath(cli)
        : string.IsNullOrWhiteSpace(Current.DataRoot) ? DefaultDataRoot
        : Path.GetFullPath(Current.DataRoot);

    public bool DataRootOverridden => Environment.GetEnvironmentVariable("BOORS_DATA") is { Length: > 0 } || overrides.ContainsKey("data");

    public string EffectiveNode =>
        Environment.GetEnvironmentVariable("BOORS_NODE") is { Length: > 0 } env ? env
        : string.IsNullOrWhiteSpace(Current.NodeName) ? Environment.MachineName
        : Current.NodeName;

    /// <summary>Raised on the caller's thread after <see cref="Update"/>; the argument says what changed.</summary>
    public event EventHandler<SettingsChange>? Changed;

    public void Update(Action<AppSettings> change)
    {
        var before = Current.Clone();
        change(saved);
        change(Current);
        Save();

        var what = SettingsChange.None;
        if (before.Language != Current.Language || before.Digits != Current.Digits)
            what |= SettingsChange.Language;
        if (before.Theme != Current.Theme || before.ColorPreset != Current.ColorPreset)
            what |= SettingsChange.Appearance;
        if (before.Density != Current.Density)
            what |= SettingsChange.Density;
        if (before.Mode != Current.Mode || before.NodeUrl != Current.NodeUrl || before.OperatorToken != Current.OperatorToken || before.Offline != Current.Offline)
            what |= SettingsChange.DataSource;
        if (before.DownloadsInBackground != Current.DownloadsInBackground)
            what |= SettingsChange.Collection;
        if (before.DataRoot != Current.DataRoot || before.NodeName != Current.NodeName)
            what |= SettingsChange.RestartRequired;

        if (what != SettingsChange.None)
            Changed?.Invoke(this, what);
    }

    /// <summary>Saves the window placement without raising <see cref="Changed"/>.</summary>
    public void SaveWindow(double width, double height, bool maximized)
    {
        saved.WindowWidth = Current.WindowWidth = width;
        saved.WindowHeight = Current.WindowHeight = height;
        saved.WindowMaximized = Current.WindowMaximized = maximized;
        Save();
    }

    public void LogDeferred(ILogger log)
    {
        logger = log;
        foreach (var line in deferred)
            log.LogWarning("{Problem}", line);
        deferred.Clear();
    }

    private void Warn(string message)
    {
        if (logger is null)
            deferred.Add(message);
        else
            logger.LogWarning("{Problem}", message);
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize(File.ReadAllBytes(SettingsPath), SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Warn($"Settings at {SettingsPath} could not be read ({e.Message}); using defaults");
        }

        return new AppSettings();
    }

    private void Save()
    {
        try
        {
            var temp = SettingsPath + ".tmp";
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(saved, SettingsJsonContext.Default.AppSettings));
            File.Move(temp, SettingsPath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Warn($"Settings could not be saved to {SettingsPath}: {e.Message}");
        }
    }

    private AppSettings Apply(AppSettings settings)
    {
        string? Get(string key) => overrides.TryGetValue(key, out var value) ? value : null;
        if (Get("lang") is { } lang)
            settings.Language = lang.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "fa";
        if (Get("theme") is { } theme)
            settings.Theme = theme.ToLowerInvariant() switch { "light" => "Light", "system" => "System", _ => "Dark" };
        if (Get("digits") is { } digits)
            settings.Digits = digits.StartsWith("lat", StringComparison.OrdinalIgnoreCase) ? "Latin" : "Persian";
        if (Get("preset") is { } preset)
            settings.ColorPreset = preset.StartsWith("c", StringComparison.OrdinalIgnoreCase) ? "ColorBlind" : "Iranian";
        if (Get("density") is { } density)
            settings.Density = density.StartsWith("l", StringComparison.OrdinalIgnoreCase) ? "Lite" : "Pro";
        if (Get("mode") is { } mode)
            settings.Mode = mode.StartsWith("r", StringComparison.OrdinalIgnoreCase) ? "Remote" : "Local";
        if (Get("node-url") is { } url)
            settings.NodeUrl = url;
        return settings;
    }
}

[Flags]
public enum SettingsChange
{
    None = 0,
    Language = 1,
    Appearance = 2,
    Density = 4,
    DataSource = 8,
    RestartRequired = 16,
    Collection = 32,
}

/// <summary>Parsed command line: <c>--name value</c> pairs (e.g. --lang en --theme light --page watch).</summary>
public sealed class StartupOptions(string[] args)
{
    public Dictionary<string, string> Overrides { get; } = Parse(args);

    public string? StartPage => Overrides.TryGetValue("page", out var page) ? page : null;

    /// <summary>--exit-after-ms N: close the window N ms after the first page shows data (startup measurements).</summary>
    public int? ExitAfterMs => Overrides.TryGetValue("exit-after-ms", out var ms) && int.TryParse(ms, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>--scroll Y: scroll the page to Y after it loads (screenshots of long pages).</summary>
    public double? Scroll => Overrides.TryGetValue("scroll", out var y) && double.TryParse(y, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>--size WxH: window size in pixels for this session (screenshots).</summary>
    public (int Width, int Height)? Size => Overrides.TryGetValue("size", out var size) && size.Split('x') is [var w, var h]
        && int.TryParse(w, CultureInfo.InvariantCulture, out var width) && int.TryParse(h, CultureInfo.InvariantCulture, out var height) ? (width, height) : null;

    /// <summary>Scrolls a page's main scroller to --scroll once its data is shown.</summary>
    public void ApplyScroll(Microsoft.UI.Xaml.Controls.ScrollViewer scroller)
    {
        if (Scroll is { } y)
            scroller.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => scroller.ChangeView(null, y, null, disableAnimation: true));
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                continue;
            var name = args[i][2..];
            result[name] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
        }

        return result;
    }
}
