using System.Collections.Concurrent;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;

namespace Boors.Desktop.Infrastructure;

/// <summary>
/// UI strings: English is the key, Persian comes from <see cref="FaStrings"/>. Switching language rebuilds the shell,
/// so every <see cref="L"/> markup extension and view-model label is evaluated again.
/// </summary>
public static class Loc
{
    private static readonly ConcurrentDictionary<string, byte> missing = new(StringComparer.Ordinal);

    public static bool IsPersian { get; private set; } = true;

    public static FlowDirection Flow => IsPersian ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>English keys without a Persian entry that were asked for in this session (logged at exit).</summary>
    public static IReadOnlyCollection<string> Missing => [.. missing.Keys];

    public static void SetLanguage(string language) => IsPersian = language == "fa";

    public static string T(string english)
    {
        if (!IsPersian)
            return english;
        if (FaStrings.Map.TryGetValue(english, out var persian))
            return persian;

        missing.TryAdd(english, 0);
        return english;
    }

    /// <summary>A localized composite format; arguments are formatted by the caller (see <see cref="Fmt"/>).</summary>
    public static string F(string english, params object?[] args) => string.Format(CultureInfo.InvariantCulture, T(english), args);
}

/// <summary>XAML: <c>Text="{l:L K='Market watch'}"</c>.</summary>
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed partial class L : MarkupExtension
{
    public string K { get; set; } = string.Empty;

    protected override object ProvideValue() => Loc.T(K);
}
