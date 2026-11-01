using Boors.Desktop.Infrastructure;
using ScottPlot;
using Palette = Boors.Desktop.Infrastructure.Palette;

namespace Boors.Desktop.Charts;

/// <summary>
/// Shared look of every ScottPlot chart: transparent figure (the card shows through), recessive grid and axes in
/// secondary text ink, Vazirmatn for tick labels (digits only — words go in XAML text, which shapes Persian correctly),
/// numbers through <see cref="Fmt"/> so Persian digits apply, and a fixed left gutter so stacked panes line up.
/// </summary>
public static class ChartTheme
{
    public const string FontName = "Vazirmatn";

    public const float LeftGutter = 64;

    private static bool fontRegistered;

    public static void RegisterFont()
    {
        if (fontRegistered)
            return;
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Vazirmatn-Variable.ttf");
        if (File.Exists(path))
            Fonts.AddFontFile(FontName, path);
        fontRegistered = true;
    }

    public static void Apply(Plot plot, float bottom = 22, float top = 6)
    {
        RegisterFont();
        plot.FigureBackground.Color = Colors.Transparent;
        plot.DataBackground.Color = Colors.Transparent;
        plot.Axes.Color(Palette.Plot(Palette.AxisText));
        plot.Axes.FrameColor(Palette.Plot(Palette.GridLine));
        plot.Grid.MajorLineColor = Palette.Plot(Palette.GridLine);
        plot.Font.Set(FontName);
        foreach (var axis in plot.Axes.GetAxes())
        {
            axis.TickLabelStyle.FontSize = 11;
            axis.TickLabelStyle.ForeColor = Palette.Plot(Palette.AxisText);
            axis.MajorTickStyle.Color = Palette.Plot(Palette.GridLine);
            axis.MinorTickStyle.Length = 0;
        }

        plot.HideLegend();
        plot.Layout.Fixed(new PixelPadding(LeftGutter, 10, bottom, top));
    }

    /// <summary>Y tick labels through <see cref="Fmt"/> (grouping, Persian digits), compact for large values.</summary>
    public static void ValueTicks(Plot plot, Func<double, string>? format = null)
    {
        plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic
        {
            LabelFormatter = format ?? Compact,
            TargetTickCount = 4,
        };
    }

    public static string Compact(double value) => Math.Abs(value) switch
    {
        >= 1e9 => Fmt.Tick(value / 1e9, "0.#") + "B",
        >= 1e6 => Fmt.Tick(value / 1e6, "0.#") + "M",
        >= 1e4 => Fmt.Tick(value / 1e3, "0") + "K",
        _ => Fmt.Tick(value, Math.Abs(value) < 10 ? "0.##" : "N0"),
    };
}
