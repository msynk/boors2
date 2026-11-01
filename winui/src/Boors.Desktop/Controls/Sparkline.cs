using Boors.Desktop.Infrastructure;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Boors.Desktop.Controls;

/// <summary>A tiny line of recent values (KPI tiles): one 1.5px stroke, coloured by the direction first → last.</summary>
public sealed partial class Sparkline : UserControl
{
    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(nameof(Values), typeof(IReadOnlyList<double>), typeof(Sparkline), new PropertyMetadata(null, (d, _) => ((Sparkline)d).Redraw()));

    private readonly Polyline line = new() { StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round };

    public Sparkline()
    {
        FlowDirection = FlowDirection.LeftToRight; // time runs left to right in every language
        Content = new Grid { Children = { line } };
        SizeChanged += (_, _) => Redraw();
        IsHitTestVisible = false;
    }

    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    private void Redraw()
    {
        line.Points.Clear();
        var values = Values;
        if (values is not { Count: >= 2 } || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        double min = values.Min(), max = values.Max();
        var range = max - min is > 0 and var r ? r : 1;
        var (w, h) = (ActualWidth - 2, ActualHeight - 2);
        for (var i = 0; i < values.Count; i++)
            line.Points.Add(new Windows.Foundation.Point(1 + i * w / (values.Count - 1), 1 + h - (values[i] - min) / range * h));
        line.Stroke = Palette.Brush(values[^1] > values[0] ? Tone.Up : values[^1] < values[0] ? Tone.Down : Tone.Neutral);
    }
}
