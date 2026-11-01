using Boors.Desktop.Infrastructure;
using Boors.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Boors.Desktop.Controls;

/// <summary>A position within a range (52-week low–high, a percentile): a track, a marker and the two end labels.</summary>
public sealed partial class RangeBar : UserControl
{
    public static readonly DependencyProperty FractionProperty =
        DependencyProperty.Register(nameof(Fraction), typeof(double), typeof(RangeBar), new PropertyMetadata(double.NaN, (d, _) => ((RangeBar)d).Build()));

    public static readonly DependencyProperty StartTextProperty =
        DependencyProperty.Register(nameof(StartText), typeof(string), typeof(RangeBar), new PropertyMetadata(null, (d, _) => ((RangeBar)d).Build()));

    public static readonly DependencyProperty EndTextProperty =
        DependencyProperty.Register(nameof(EndText), typeof(string), typeof(RangeBar), new PropertyMetadata(null, (d, _) => ((RangeBar)d).Build()));

    public RangeBar()
    {
        FlowDirection = FlowDirection.LeftToRight; // low on the left, high on the right, as on a price axis
        Build();
    }

    /// <summary>0 = at the start (low), 1 = at the end (high); NaN hides the marker.</summary>
    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public string? StartText
    {
        get => (string?)GetValue(StartTextProperty);
        set => SetValue(StartTextProperty, value);
    }

    public string? EndText
    {
        get => (string?)GetValue(EndTextProperty);
        set => SetValue(EndTextProperty, value);
    }

    private void Build()
    {
        var secondary = Palette.TextSecondary;
        var grid = new Grid { RowSpacing = 2, RowDefinitions = { new RowDefinition { Height = new GridLength(10) }, new RowDefinition { Height = GridLength.Auto } } };
        var track = new Rectangle { Height = 4, RadiusX = 2, RadiusY = 2, Fill = Palette.MutedBrush, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(track);
        if (!double.IsNaN(Fraction))
        {
            var f = Math.Clamp(Fraction, 0, 1);
            var marker = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = Ui.Star(f) }, new ColumnDefinition { Width = new GridLength(10) }, new ColumnDefinition { Width = Ui.Star(1 - f) } } };
            var dot = new Ellipse { Width = 10, Height = 10, Fill = Palette.Brush(Tone.Accent) };
            Grid.SetColumn(dot, 1);
            marker.Children.Add(dot);
            grid.Children.Add(marker);
        }

        var ends = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition() } };
        ends.Children.Add(new TextBlock { Text = StartText ?? string.Empty, FontSize = 11, Foreground = secondary });
        var end = new TextBlock { Text = EndText ?? string.Empty, FontSize = 11, Foreground = secondary, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(end, 1);
        ends.Children.Add(end);
        Grid.SetRow(ends, 1);
        grid.Children.Add(ends);
        Content = grid;
    }
}
