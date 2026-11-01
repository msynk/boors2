using Boors.Desktop.Infrastructure;
using Boors.Market.Services.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ScottPlot;
using Palette = Boors.Desktop.Infrastructure.Palette;
using ScottPlot.WinUI;

namespace Boors.Desktop.Charts;

/// <summary>
/// The symbol workspace chart: candlesticks (raw or point-in-time adjusted), value traded and individual net flow in
/// three separate panes (never dual axes) that share one X axis by session — pan or zoom any pane and the others
/// follow — with a synchronized crosshair and an OHLC readout. Each pane's Y fits the visible window.
/// </summary>
public sealed partial class SymbolChart : UserControl
{
    private readonly WinUIPlot prices = new();
    private readonly WinUIPlot value = new();
    private readonly WinUIPlot flow = new();
    private readonly TextBlock readout = new() { FontSize = 12, MinHeight = 18, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock valueCaption = new() { FontSize = 11 };
    private readonly TextBlock flowCaption = new() { FontSize = 11 };
    private readonly List<ScottPlot.Plottables.VerticalLine> cursors = [];
    private IReadOnlyList<BarDto> bars = [];
    private OHLC[] candles = [];
    private double[] values = [];
    private double[] flows = [];
    private bool linked;

    public SymbolChart()
    {
        var secondary = Palette.TextSecondary;
        readout.Foreground = secondary;
        valueCaption.Foreground = secondary;
        flowCaption.Foreground = secondary;
        valueCaption.Text = Loc.T("Value traded (billion toman)");
        flowCaption.Text = Loc.T("Individual net flow (billion toman)");

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(3, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
            },
        };
        foreach (var (element, row) in new (FrameworkElement, int)[] { (readout, 0), (prices, 1), (valueCaption, 2), (value, 3), (flowCaption, 4), (flow, 5) })
        {
            Grid.SetRow(element, row);
            root.Children.Add(element);
        }

        valueCaption.Margin = flowCaption.Margin = new Thickness(ChartTheme.LeftGutter, 4, 0, 0);
        readout.Margin = new Thickness(ChartTheme.LeftGutter, 0, 0, 2);
        readout.FlowDirection = FlowDirection.LeftToRight; // the readout belongs to the (left-to-right) chart
        Content = root;

        foreach (var pane in Panes)
        {
            pane.FlowDirection = FlowDirection.LeftToRight;
            pane.AddHandler(PointerMovedEvent, new PointerEventHandler(OnPointerMoved), handledEventsToo: true);
            pane.PointerExited += (_, _) => HideCursor();
        }
    }

    private const int MinimumVisibleSessions = 30;

    private WinUIPlot[] Panes => [prices, value, flow];

    /// <summary>Plots all bars (oldest first) and shows the last <paramref name="sessions"/> of them (0 = everything).</summary>
    public void Show(IReadOnlyList<BarDto> history, IReadOnlyList<FlowDayDto> flowDays, bool adjusted, int sessions)
    {
        bars = history;
        var flowByDate = flowDays.GroupBy(f => f.Date).ToDictionary(g => g.Key, g => g.Last().IndividualNetValue);
        candles = new OHLC[bars.Count];
        values = new double[bars.Count];
        flows = new double[bars.Count];
        for (var i = 0; i < bars.Count; i++)
        {
            var b = bars[i];
            // Point-in-time adjusted OHLC: every price of a bar scaled by its adjusted/raw close ratio.
            var scale = adjusted && b.Close != 0 ? (double)(b.AdjustedClose / b.Close) : 1;
            // TSETMC's close is a volume-weighted price and can sit outside the day's high/low (and open is 0 on some
            // rows); widen the wick so the candle is always well formed.
            var open = b.Open == 0 ? b.Close : b.Open;
            var high = Math.Max(Math.Max(b.High, open), b.Close);
            var low = Math.Min(Math.Min(b.Low > 0 ? b.Low : open, open), b.Close);
            candles[i] = new OHLC((double)open * scale, (double)high * scale, (double)low * scale, (double)b.Close * scale);
            values[i] = (double)(b.Value / Fmt.RialsPerBillionToman);
            flows[i] = flowByDate.TryGetValue(b.Date, out var net) ? (double)(net / Fmt.RialsPerBillionToman) : 0;
        }

        var dates = bars.Select(b => b.Date).ToList();
        cursors.Clear();

        var p = prices.Plot;
        p.Clear();
        ChartTheme.Apply(p, bottom: 4);
        p.Axes.Bottom.TickGenerator = new SessionTicks(dates);
        p.Axes.Bottom.TickLabelStyle.IsVisible = false;
        ChartTheme.ValueTicks(p, v => Fmt.Tick(v));
        var candlePlot = p.Add.Candlestick(candles.ToList());
        candlePlot.Sequential = true;
        candlePlot.RisingColor = Palette.Plot(Palette.Up);
        candlePlot.FallingColor = Palette.Plot(Palette.Down);
        p.Axes.ContinuouslyAutoscale = true;
        p.Axes.ContinuousAutoscaleAction = rp => FitY(rp, i => candles[i].Low, i => candles[i].High, includeZero: false);

        var v = value.Plot;
        v.Clear();
        ChartTheme.Apply(v, bottom: 4);
        v.Axes.Bottom.TickGenerator = new SessionTicks(dates);
        v.Axes.Bottom.TickLabelStyle.IsVisible = false;
        ChartTheme.ValueTicks(v);
        var valueBars = v.Add.Bars(values);
        foreach (var bar in valueBars.Bars)
        {
            bar.FillColor = Palette.Plot(Palette.Accent);
            bar.LineWidth = 0;
            bar.Size = 0.7;
        }

        v.Axes.ContinuouslyAutoscale = true;
        v.Axes.ContinuousAutoscaleAction = rp => FitY(rp, i => 0, i => values[i], includeZero: true);

        var f = flow.Plot;
        f.Clear();
        ChartTheme.Apply(f, bottom: 22);
        f.Axes.Bottom.TickGenerator = new SessionTicks(dates);
        ChartTheme.ValueTicks(f);
        var flowBars = f.Add.Bars(flows);
        for (var i = 0; i < flowBars.Bars.Count; i++)
        {
            flowBars.Bars[i].FillColor = Palette.Plot(flows[i] < 0 ? Palette.Down : Palette.Up);
            flowBars.Bars[i].LineWidth = 0;
            flowBars.Bars[i].Size = 0.7;
        }

        f.Add.HorizontalLine(0, 1, Palette.Plot(Palette.Neutral));
        f.Axes.ContinuouslyAutoscale = true;
        f.Axes.ContinuousAutoscaleAction = rp => FitY(rp, i => Math.Min(0, flows[i]), i => Math.Max(0, flows[i]), includeZero: true);

        foreach (var pane in Panes)
        {
            var cursor = pane.Plot.Add.VerticalLine(0, 1, Palette.Plot(Palette.Neutral), LinePattern.Dotted);
            cursor.IsVisible = false;
            cursors.Add(cursor);
        }

        if (!linked)
        {
            // Share the X axis: any pane's pan/zoom moves the other two.
            foreach (var a in Panes)
                foreach (var b in Panes.Where(b => b != a))
                    a.Plot.Axes.Link(b, x: true, y: false);
            linked = true;
        }

        ShowLast(sessions);
        HideCursor();
    }

    /// <summary>The range buttons: show the last N sessions (0 = all) without reloading.</summary>
    public void ShowLast(int sessions)
    {
        if (bars.Count == 0)
            return;
        var first = sessions <= 0 || sessions >= bars.Count ? 0 : bars.Count - sessions;
        first = Math.Min(first, bars.Count - MinimumVisibleSessions); // at least ~6 weeks of slots, even for a new listing
        foreach (var pane in Panes)
            pane.Plot.Axes.SetLimitsX(first - 0.6, bars.Count - 0.4);
        foreach (var pane in Panes)
            pane.Refresh();
    }

    private void FitY(RenderPack rp, Func<int, double> low, Func<int, double> high, bool includeZero)
    {
        if (bars.Count == 0)
            return;
        var range = rp.Plot.Axes.Bottom.Range;
        var lo = Math.Clamp((int)Math.Floor(range.Min), 0, bars.Count - 1);
        var hi = Math.Clamp((int)Math.Ceiling(range.Max), 0, bars.Count - 1);
        if (hi < lo)
            return;
        double min = includeZero ? 0 : double.MaxValue, max = includeZero ? 0 : double.MinValue;
        for (var i = lo; i <= hi; i++)
        {
            min = Math.Min(min, low(i));
            max = Math.Max(max, high(i));
        }

        var pad = Math.Max((max - min) * 0.06, Math.Abs(max) * 0.001 + 1e-9);
        rp.Plot.Axes.SetLimitsY(includeZero && min >= 0 ? 0 : min - pad, max + pad);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (bars.Count == 0 || sender is not WinUIPlot pane)
            return;
        var point = e.GetCurrentPoint(pane).Position;
        var scale = pane.DisplayScale;
        var x = pane.Plot.GetCoordinates(new Pixel((float)(point.X * scale), (float)(point.Y * scale))).X;
        var index = (int)Math.Round(x);
        if (index < 0 || index >= bars.Count)
        {
            HideCursor();
            return;
        }

        foreach (var cursor in cursors)
        {
            cursor.X = index;
            cursor.IsVisible = true;
        }

        var bar = bars[index];
        var c = candles[index];
        var change = bar.Return is { } r ? r * 100 : (double?)null;
        readout.Text = string.Join("   ",
            Fmt.Date(bar.Date),
            $"{Loc.T("O")} {Fmt.Num(c.Open, "N0")}",
            $"{Loc.T("H")} {Fmt.Num(c.High, "N0")}",
            $"{Loc.T("L")} {Fmt.Num(c.Low, "N0")}",
            $"{Loc.T("C")} {Fmt.Num(c.Close, "N0")}",
            Fmt.Change(change),
            $"{Loc.T("Value")} {Fmt.Num(values[index], "N1")}",
            $"{Loc.T("Individual net")} {Fmt.Signed((decimal)flows[index], x => x, "N1")}",
            Fmt.DayStatus(bar.Status));
        foreach (var p in Panes)
            p.Refresh();
    }

    private void HideCursor()
    {
        var any = false;
        foreach (var cursor in cursors.Where(c => c.IsVisible))
        {
            cursor.IsVisible = false;
            any = true;
        }

        if (any)
        {
            foreach (var p in Panes)
                p.Refresh();
        }

        readout.Text = bars.Count == 0 ? string.Empty
            : $"{Fmt.Date(bars[0].Date)} – {Fmt.Date(bars[^1].Date)}   ·   {Fmt.Number(bars.Count)} {Loc.T("sessions")}";
    }
}
