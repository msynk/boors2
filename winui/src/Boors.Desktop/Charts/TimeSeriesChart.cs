using Boors.Desktop.Infrastructure;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ScottPlot;
using Palette = Boors.Desktop.Infrastructure.Palette;
using ScottPlot.WinUI;

namespace Boors.Desktop.Charts;

/// <summary>
/// One daily series on one axis (TEDPIX in rial, or indexed to 100 in dollars — never a dual axis), plotted by session
/// with a crosshair and a readout line (date · value) in XAML above the plot. Drag pans, the wheel zooms, Y follows
/// the visible window.
/// </summary>
public sealed partial class TimeSeriesChart : UserControl
{
    private readonly WinUIPlot view = new();
    private readonly TextBlock readout = new() { FontSize = 12, Margin = new Thickness(4, 0, 4, 2), MinHeight = 18 };
    private IReadOnlyList<DateOnly> dates = [];
    private double[] values = [];
    private Func<double, string> format = v => Fmt.Tick(v);
    private ScottPlot.Plottables.VerticalLine? cursor;
    private double? baseline;

    public TimeSeriesChart()
    {
        view.FlowDirection = FlowDirection.LeftToRight;
        readout.Foreground = Palette.TextSecondary;
        readout.FlowDirection = FlowDirection.LeftToRight; // the readout belongs to the (left-to-right) chart
        var root = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition() } };
        Grid.SetRow(view, 1);
        root.Children.Add(readout);
        root.Children.Add(view);
        Content = root;
        view.AddHandler(PointerMovedEvent, new PointerEventHandler(OnPointerMoved), handledEventsToo: true);
        view.PointerExited += (_, _) => HideCursor();
    }

    /// <summary>Text shown when the pointer is not on the plot (e.g. the last value).</summary>
    public string? Summary { get; set; }

    public void Show(IReadOnlyList<DateOnly> seriesDates, IReadOnlyList<double> seriesValues, Func<double, string> valueFormat, double? baselineValue = null)
    {
        dates = seriesDates;
        values = [.. seriesValues];
        format = valueFormat;
        baseline = baselineValue;

        var plot = view.Plot;
        plot.Clear();
        ChartTheme.Apply(plot);
        plot.Axes.Bottom.TickGenerator = new SessionTicks(dates);
        ChartTheme.ValueTicks(plot, v => format(v));
        if (values.Length > 0)
        {
            if (baseline is { } b)
                plot.Add.HorizontalLine(b, 1, Palette.Plot(Palette.Neutral), LinePattern.Dashed);
            var signal = plot.Add.Signal(values, 1, Palette.Plot(Palette.Accent));
            signal.LineWidth = 2;
            signal.MarkerSize = 0;
            cursor = plot.Add.VerticalLine(0, 1, Palette.Plot(Palette.Neutral), LinePattern.Dotted);
            cursor.IsVisible = false;
            plot.Axes.SetLimitsX(-1, values.Length);
            plot.Axes.ContinuouslyAutoscale = true;
            plot.Axes.ContinuousAutoscaleAction = FitY;
        }

        HideCursor();
        view.Refresh();
    }

    private void FitY(RenderPack rp)
    {
        var range = rp.Plot.Axes.Bottom.Range;
        var lo = Math.Clamp((int)Math.Floor(range.Min), 0, Math.Max(0, values.Length - 1));
        var hi = Math.Clamp((int)Math.Ceiling(range.Max), 0, Math.Max(0, values.Length - 1));
        if (values.Length == 0 || hi < lo)
            return;
        double min = double.MaxValue, max = double.MinValue;
        for (var i = lo; i <= hi; i++)
        {
            min = Math.Min(min, values[i]);
            max = Math.Max(max, values[i]);
        }

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
        if (values.Length == 0 || cursor is null)
            return;
        var p = e.GetCurrentPoint(view).Position;
        var scale = view.DisplayScale;
        var coordinates = view.Plot.GetCoordinates(new Pixel((float)(p.X * scale), (float)(p.Y * scale)));
        var index = (int)Math.Round(coordinates.X);
        if (index < 0 || index >= values.Length)
        {
            HideCursor();
            return;
        }

        cursor.X = index;
        cursor.IsVisible = true;
        var change = index > 0 && values[index - 1] != 0 ? (values[index] / values[index - 1] - 1) * 100 : (double?)null;
        readout.Text = $"{Fmt.Date(dates[index])}   {format(values[index])}   {Fmt.Change(change)}";
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
