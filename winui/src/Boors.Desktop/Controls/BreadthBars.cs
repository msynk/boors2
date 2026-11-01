using Boors.Desktop.Infrastructure;
using Boors.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Boors.Desktop.Controls;

/// <summary>
/// Breadth as 100% stacked columns, one per session (advancers, unchanged, decliners, not traded), with 2px gaps and a
/// tooltip per column. Segment heights are star rows, so layout does the arithmetic. Time runs left to right.
/// </summary>
public sealed partial class BreadthBars : UserControl
{
    public static readonly DependencyProperty DaysProperty =
        DependencyProperty.Register(nameof(Days), typeof(IReadOnlyList<BreadthColumn>), typeof(BreadthBars), new PropertyMetadata(null, (d, _) => ((BreadthBars)d).Build()));

    private readonly Grid bars = new() { ColumnSpacing = 2 };
    private readonly Grid labels = new() { Margin = new Thickness(0, 4, 0, 0) };

    public BreadthBars()
    {
        FlowDirection = FlowDirection.LeftToRight;
        var root = new Grid { RowDefinitions = { new RowDefinition(), new RowDefinition { Height = GridLength.Auto } } };
        Grid.SetRow(labels, 1);
        root.Children.Add(bars);
        root.Children.Add(labels);
        Content = root;
    }

    public IReadOnlyList<BreadthColumn>? Days
    {
        get => (IReadOnlyList<BreadthColumn>?)GetValue(DaysProperty);
        set => SetValue(DaysProperty, value);
    }

    private void Build()
    {
        bars.Children.Clear();
        bars.ColumnDefinitions.Clear();
        labels.Children.Clear();
        labels.ColumnDefinitions.Clear();
        var days = Days;
        if (days is not { Count: > 0 })
            return;

        var labelEvery = Math.Max(1, (int)Math.Ceiling(days.Count / 6.0));
        for (var i = 0; i < days.Count; i++)
        {
            var day = days[i];
            bars.ColumnDefinitions.Add(new ColumnDefinition());
            labels.ColumnDefinitions.Add(new ColumnDefinition());

            var column = new Grid { RowSpacing = 1, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            // Top to bottom: not traded, decliners, unchanged, advancers (advancers sit on the baseline).
            AddSegment(column, day.NotTraded, Tone.Muted);
            AddSegment(column, day.Decliners, Tone.Down);
            AddSegment(column, day.Unchanged, Tone.Neutral);
            AddSegment(column, day.Advancers, Tone.Up);
            ToolTipService.SetToolTip(column, day.ToolTip);
            Grid.SetColumn(column, i);
            bars.Children.Add(column);

            var last = i == days.Count - 1;
            if ((i % labelEvery == 0 && days.Count - 1 - i >= labelEvery) || last)
            {
                // Month/day only (the year is in the tooltip); the last label is right-aligned so it is never clipped.
                var label = new TextBlock
                {
                    Text = Fmt.Digits(Fmt.DateText(day.Date)[5..]),
                    FontSize = 11,
                    Foreground = Palette.TextSecondary,
                    HorizontalAlignment = last ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                    TextWrapping = TextWrapping.NoWrap,
                };
                var span = Math.Min(labelEvery, days.Count);
                Grid.SetColumn(label, last ? Math.Max(0, i - span + 1) : i);
                Grid.SetColumnSpan(label, span);
                labels.Children.Add(label);
            }
        }
    }

    private static void AddSegment(Grid column, int count, Tone tone)
    {
        column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(count, GridUnitType.Star) });
        if (count <= 0)
            return;
        var rect = new Rectangle { Fill = Palette.Brush(tone), RadiusX = 1, RadiusY = 1 };
        Grid.SetRow(rect, column.RowDefinitions.Count - 1);
        column.Children.Add(rect);
    }
}
