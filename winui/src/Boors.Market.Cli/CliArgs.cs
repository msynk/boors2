namespace Boors.Market.Cli;

/// <summary>Minimal argument parser: positional words, <c>--name value</c> options and <c>--flag</c> switches.</summary>
public sealed class CliArgs
{
    private static readonly HashSet<string> Switches = ["details", "verbose", "offline", "cache", "help", "force", "history", "update", "with-messages", "traded"];

    private readonly Dictionary<string, string?> options = new(StringComparer.OrdinalIgnoreCase);

    public CliArgs(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var name = arg[2..];
                var takesValue = !Switches.Contains(name) && i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
                options[name] = takesValue ? args[++i] : null;
            }
            else
            {
                Positional.Add(arg);
            }
        }
    }

    public List<string> Positional { get; } = [];

    public bool Has(string name) => options.ContainsKey(name);

    public string? Get(string name) => options.TryGetValue(name, out var value) ? value : null;

    public int GetInt(string name, int fallback) =>
        int.TryParse(Get(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    public string? At(int index) => index < Positional.Count ? Positional[index] : null;
}
