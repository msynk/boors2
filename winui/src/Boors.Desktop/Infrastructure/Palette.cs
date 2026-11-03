using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Boors.Desktop.Infrastructure;

/// <summary>
/// Data colours, selected per theme (dark steps are their own values, not an inversion) and per preset. Validated with
/// the dataviz palette validator against #202020 (dark) and #fbfbfb (light):
/// Iranian green/red passes lightness, chroma, normal-vision and contrast but not CVD separation (ΔE 6.2 dark / 5.5
/// light), so every up/down value also carries ▲/▼ and a sign; the colour-blind preset (blue/orange) passes everything.
/// Text uses theme text brushes; these colours go on marks, and on numbers only next to their arrow.
/// </summary>
public static class Palette
{
    private static readonly Dictionary<Tone, SolidColorBrush> brushes = [];

    public static bool IsDark { get; private set; } = true;

    public static bool ColorBlind { get; private set; }

    public static Color Up { get; private set; }

    public static Color Down { get; private set; }

    public static Color Neutral { get; private set; }

    /// <summary>Series colour for non-directional marks (index line, value bars): violet, distinct from both up/down presets.</summary>
    public static Color Accent { get; private set; }

    public static Color Muted { get; private set; }

    /// <summary>Chart ink: axis labels and tick text (secondary text), grid lines (recessive).</summary>
    public static Color AxisText { get; private set; }

    public static Color GridLine { get; private set; }

    public static Color Surface { get; private set; }

    public static void Configure(bool dark, bool colorBlind)
    {
        IsDark = dark;
        ColorBlind = colorBlind;
        (Up, Down) = (dark, colorBlind) switch
        {
            (true, false) => (Hex(0x2EA043), Hex(0xF85149)),
            (false, false) => (Hex(0x1A7F37), Hex(0xCF222E)),
            (true, true) => (Hex(0x4493F8), Hex(0xDB6D28)),
            (false, true) => (Hex(0x0969DA), Hex(0xBC4C00)),
        };
        Neutral = dark ? Hex(0x8B949E) : Hex(0x6E7781);
        Muted = dark ? Hex(0x484F58) : Hex(0xC6CDD5);
        Accent = dark ? Hex(0x9085E9) : Hex(0x6250D6);
        AxisText = dark ? Hex(0xC5C5C5) : Hex(0x5C5C5C);
        GridLine = dark ? Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x18, 0x00, 0x00, 0x00);
        Surface = dark ? Hex(0x202020) : Hex(0xFBFBFB);
        brushes.Clear();
        inks.Clear();
    }

    public static Color ColorOf(Tone tone) => tone switch
    {
        Tone.Up => Up,
        Tone.Down => Down,
        Tone.Accent => Accent,
        Tone.Muted => Muted,
        // Windows status fills (SystemFillColorSuccess/Caution/Critical/Attention), per theme.
        Tone.Good => IsDark ? Hex(0x6CCB5F) : Hex(0x0F7B0F),
        Tone.Warning => IsDark ? Hex(0xFCE100) : Hex(0x9D5D00),
        Tone.Critical => IsDark ? Hex(0xFF99A4) : Hex(0xC42B1C),
        Tone.Info => IsDark ? Hex(0x99EBFF) : Hex(0x005FB7),
        _ => Neutral,
    };

    /// <summary>Series colours for comparing entities (simulations, benchmarks); never cycled past the last slot.</summary>
    public const int SeriesSlots = 6;

    /// <summary>
    /// Categorical colour of a slot, in a fixed order: violet (the accent), aqua, orange, blue, magenta, yellow — no
    /// green or red, which mean up and down here. Validated against #202020 and #fbfbfb: every adjacent pair passes the
    /// colour-vision-deficiency (ΔE ≥ 9.2) and normal-vision (≥ 19.3) separation, and the first three pass all pairs; in
    /// light mode aqua, magenta and yellow are under 3:1 contrast, so charts using them carry a legend with values.
    /// </summary>
    public static Color Series(int slot) => (IsDark, Math.Clamp(slot, 0, SeriesSlots - 1)) switch
    {
        (true, 0) => Hex(0x9085E9),
        (true, 1) => Hex(0x199E70),
        (true, 2) => Hex(0xD95926),
        (true, 3) => Hex(0x3987E5),
        (true, 4) => Hex(0xD55181),
        (true, _) => Hex(0xC98500),
        (false, 0) => Hex(0x6250D6),
        (false, 1) => Hex(0x1BAF7A),
        (false, 2) => Hex(0xEB6834),
        (false, 3) => Hex(0x2A78D6),
        (false, 4) => Hex(0xE87BA4),
        (false, _) => Hex(0xEDA100),
    };

    /// <summary>A cached brush per tone; must be called on the UI thread.</summary>
    public static SolidColorBrush Brush(Tone tone)
    {
        if (!brushes.TryGetValue(tone, out var brush))
            brushes[tone] = brush = new SolidColorBrush(ColorOf(tone));
        return brush;
    }

    public static SolidColorBrush UpBrush => Brush(Tone.Up);

    public static SolidColorBrush DownBrush => Brush(Tone.Down);

    public static SolidColorBrush NeutralBrush => Brush(Tone.Neutral);

    public static SolidColorBrush MutedBrush => Brush(Tone.Muted);

    public static SolidColorBrush AccentBrush => Brush(Tone.Accent);

    /// <summary>For x:Bind: the brush of a value's tone (text of a change, next to its arrow).</summary>
    public static Brush ToneBrush(Tone tone) => tone == Tone.Neutral ? TextSecondary : Brush(tone);

    /// <summary>For x:Bind: a headline value — primary text unless the value itself is a change.</summary>
    public static Brush ValueBrush(Tone tone) => tone == Tone.Neutral ? TextPrimary : Brush(tone);

    // Text inks of the *selected* theme. Application.Current.Resources would answer with the app's theme, which differs
    // from the shell's RequestedTheme when the user picks one, so code-built elements use these (Fluent values).
    public static SolidColorBrush TextPrimary => Text("primary", IsDark ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xE4, 0, 0, 0));

    public static SolidColorBrush TextSecondary => Text("secondary", IsDark ? Color.FromArgb(0xC5, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x9E, 0, 0, 0));

    public static SolidColorBrush TextTertiary => Text("tertiary", IsDark ? Color.FromArgb(0x87, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x72, 0, 0, 0));

    /// <summary>Solid window background when there is no Mica (Windows 10).</summary>
    public static SolidColorBrush WindowBackground => Text("window", IsDark ? Hex(0x202020) : Hex(0xF3F3F3));

    private static readonly Dictionary<string, SolidColorBrush> inks = [];

    private static SolidColorBrush Text(string key, Color color)
    {
        if (!inks.TryGetValue(key, out var brush))
            inks[key] = brush = new SolidColorBrush(color);
        return brush;
    }

    public static ScottPlot.Color Plot(Color c) => new(c.R, c.G, c.B, c.A);

    private static Color Hex(int rgb) => Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
