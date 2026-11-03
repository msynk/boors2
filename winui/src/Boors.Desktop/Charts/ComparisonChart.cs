using Boors.Desktop.Infrastructure;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using ScottPlot;
using ScottPlot.WinUI;
using Orientation = Microsoft.UI.Xaml.Controls.Orientation;
using Palette = Boors.Desktop.Infrastructure.Palette;
using VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment;

namespace Boors.Desktop.Charts;

/// <summary>
/// A series to compare: one value per date of the chart (NaN where it has none — a simulation that started later).
/// <paramref name="Emphasis"/> draws the 2px line (the series the chart is about), others 1.5px; <paramref name="Fill"/>
/// washes the area to the baseline (drawdown).
/// </summary>
public sealed record ChartSeries(string Name, double[] Values, Windows.UI.Color Color, bool Emphasis = false, bool Fill = false);

/// <summary>
/// Several daily series on one axis — indexed to a common base, never two scales — plotted by session, with a legend
/// (a line key, the name and the last value of each series) and a crosshair readout above the plot. Words stay in XAML;
/// the canvas draws digits only. Drag pans, the wheel zooms, Y follows the visible window.
/// </summary>
public sealed partial class ComparisonChart : UserControl
{
    private readonly WinUIPlot view = new();
    private readonly StackPanel legend = new() { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(4, 0, 4, 2) };
    private readonly TextBlock readout = new() { FontSize = 12, Margin = new Thickness(4, 0, 4, 2), MinHeight = 18, TextTrimming = TextTrimming.CharacterEllipsis };
    private IReadOnlyList<DateOnly> dates = [];
    private IReadOnlyList<ChartSeries> series = [];
    private Func<double, string> format = v => Fmt.Tick(v);
    private ScottPlot.Plottables.VerticalLine? cursor;
    private double? baseline;

    public ComparisonChart()
    {
        view.FlowDirection = FlowDirection.LeftToRight;
        readout.Foreground = Palette.TextSecondary;
        readout.FlowDirection = FlowDirection.LeftToRight;
        var root = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Auto }, new RowDefinition() } };
        Grid.SetRow(readout, 1);
        Grid.SetRow(view, 2);
        root.Children.Add(legend);
        root.Children.Add(readout);
        root.Children.Add(view);
        Content = root;
        view.AddHandler(PointerMovedEvent, new PointerEventHandler(OnPointerMoved), handledEventsToo: true);
        view.PointerExited += (_, _) => HideCursor();
    }

    /// <summary>Text shown above the plot when the pointer is not on it.</summary>
    public string? Summary { get; set; }

    /// <summary>Hides the legend (a single series named by the card's title needs none).</summary>
    public bool ShowLegend { get; set; } = true;

    public void Show(IReadOnlyList<DateOnly> chartDates, IReadOnlyList<ChartSeries> chartSeries, Func<double, string> valueFormat, double? baselineValue = null)
    {
        dates = chartDates;
        series = chartSeries;
        format = valueFormat;
        baseline = baselineValue;
        BuildLegend();

        var plot = view.Plot;
        plot.Clear();
        ChartTheme.Apply(plot);
        plot.Axes.Bottom.TickGenerator = new SessionTicks(dates);
        ChartTheme.ValueTicks(plot, v => format(v));
        cursor = null;
        if (dates.Count > 0 && series.Count > 0)
        {
            if (baseline is { } b)
                plot.Add.HorizontalLine(b, 1, Palette.Plot(Palette.Neutral), LinePattern.Dashed);

            // The emphasised series last, so it is drawn on top.
            foreach (var s in series.OrderBy(s => s.Emphasis))
            {
                var (first, last) = Range(s.Values);
                if (first < 0)
                    continue;
                var xs = Enumerable.Range(first, last - first + 1).Select(i => (double)i).ToArray();
                var ys = Enumerable.Range(first, last - first + 1).Select(i => double.IsFinite(s.Values[i]) ? s.Values[i] : s.Values[Math.Max(first, i - 1)]).ToArray();
                var line = plot.Add.Scatter(xs, ys, Palette.Plot(s.Color));
                line.LineWidth = s.Emphasis ? 2 : 1.5f;
                line.MarkerSize = 0;
                if (s.Fill)
                {
                    line.FillY = true;
                    line.FillYValue = baseline ?? 0;
                    line.FillYColor = Palette.Plot(s.Color).WithAlpha(0.1);
                }
            }

            cursor = plot.Add.VerticalLine(0, 1, Palette.Plot(Palette.Neutral), LinePattern.Dotted);
            cursor.IsVisible = false;
            plot.Axes.SetLimitsX(-1, dates.Count);
            plot.Axes.ContinuouslyAutoscale = true;
            plot.Axes.ContinuousAutoscaleAction = FitY;
        }

        HideCursor();
        view.Refresh();
    }

    private void BuildLegend()
    {
        legend.Children.Clear();
        legend.Visibility = ShowLegend && series.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var s in series)
        {
            var last = s.Values.LastOrDefault(double.IsFinite, double.NaN);
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            item.Children.Add(new Rectangle { Width = 14, Height = s.Emphasis ? 3 : 2, RadiusX = 1, RadiusY = 1, Fill = new SolidColorBrush(s.Color), VerticalAlignment = VerticalAlignment.Center });
            item.Children.Add(new TextBlock { Text = s.Name, FontSize = 12, Foreground = Palette.TextSecondary, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis });
            if (double.IsFinite(last))
                item.Children.Add(new TextBlock { Text = format(last), FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Palette.TextPrimary, VerticalAlignment = VerticalAlignment.Center });
            legend.Children.Add(item);
        }
    }

    private static (int First, int Last) Range(double[] values)
    {
        var first = Array.FindIndex(values, double.IsFinite);
        return first < 0 ? (-1, -1) : (first, Array.FindLastIndex(values, double.IsFinite));
    }

    private void FitY(RenderPack rp)
    {
        var range = rp.Plot.Axes.Bottom.Range;
        var lo = Math.Clamp((int)Math.Floor(range.Min), 0, Math.Max(0, dates.Count - 1));
        var hi = Math.Clamp((int)Math.Ceiling(range.Max), 0, Math.Max(0, dates.Count - 1));
        double min = double.MaxValue, max = double.MinValue;
        foreach (var s in series)
        {
            for (var i = lo; i <= hi && i < s.Values.Length; i++)
            {
                if (!double.IsFinite(s.Values[i]))
                    continue;
                min = Math.Min(min, s.Values[i]);
                max = Math.Max(max, s.Values[i]);
            }
        }

        if (min > max)
            return;
        if (baseline is { } b)
        {
            min = Math.Min(min, b);
            max = Math.Max(max, b);
        }

        var pad = Math.Max((max - min) * 0.08, Math.Abs(max) * 0.001 + 1e-9);
        rp.Plot.Axes.SetLimitsY(min - pad, max + pad);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (dates.Count == 0 || cursor is null)
            return;
        var p = e.GetCurrentPoint(view).Position;
        var scale = view.DisplayScale;
        var coordinates = view.Plot.GetCoordinates(new Pixel((float)(p.X * scale), (float)(p.Y * scale)));
        var index = (int)Math.Round(coordinates.X);
        if (index < 0 || index >= dates.Count)
        {
            HideCursor();
            return;
        }

        cursor.X = index;
        cursor.IsVisible = true;
        var values = series.Where(s => index < s.Values.Length && double.IsFinite(s.Values[index])).Select(s => $"{s.Name} {format(s.Values[index])}");
        readout.Text = $"{Fmt.Date(dates[index])}   {string.Join("   ", values)}";
        view.Refresh();
    }

    private void HideCursor()
    {
        if (cursor is { IsVisible: true })
        {
            cursor.IsVisible = false;
            view.Refresh();
        }

        readout.Text = Summary ?? string.Empty;
    }
}
