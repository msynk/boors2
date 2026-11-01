using Boors.Desktop.Infrastructure;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Boors.Desktop.Controls;

/// <summary>One column: its segments bottom to top (value and tone), the axis label under it (or null) and a tooltip.</summary>
public sealed record ChartColumn(IReadOnlyList<(double Value, Tone Tone)> Segments, string? Label, string ToolTip)
{
    public double Total => Segments.Sum(s => Math.Max(0, s.Value));
}

/// <summary>
/// Columns on one absolute scale (the tallest column fills the height), stacked when a column has several segments:
/// at most 40px wide, 2px gaps between columns and between segments, a 4px rounded end on the top segment, square at
/// the baseline, and a
/// tooltip per column whose hit target is the whole column height. Drawn in XAML (no plot canvas), so labels and
/// tooltips shape Persian text; time runs left to right in either language.
/// </summary>
public sealed partial class ColumnChart : UserControl
{
    public static readonly DependencyProperty ColumnsProperty =
        DependencyProperty.Register(nameof(Columns), typeof(IReadOnlyList<ChartColumn>), typeof(ColumnChart), new PropertyMetadata(null, (d, _) => ((ColumnChart)d).Build()));

    private const double MaxColumnWidth = 40;

    private readonly Grid bars = new() { ColumnSpacing = 2 };
    private readonly Grid labels = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly Border baseline = new() { Height = 1, VerticalAlignment = VerticalAlignment.Bottom };

    public ColumnChart()
    {
        FlowDirection = FlowDirection.LeftToRight;
        var plot = new Grid();
        plot.Children.Add(bars);
        plot.Children.Add(baseline);
        var root = new Grid { RowDefinitions = { new RowDefinition(), new RowDefinition { Height = GridLength.Auto } } };
        Grid.SetRow(labels, 1);
        root.Children.Add(plot);
        root.Children.Add(labels);
        Content = root;
    }

    public IReadOnlyList<ChartColumn>? Columns
    {
        get => (IReadOnlyList<ChartColumn>?)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    private void Build()
    {
        bars.Children.Clear();
        bars.ColumnDefinitions.Clear();
        labels.Children.Clear();
        labels.ColumnDefinitions.Clear();
        baseline.Background = new SolidColorBrush(Palette.GridLine);
        var columns = Columns;
        if (columns is not { Count: > 0 })
            return;

        var max = Math.Max(columns.Max(c => c.Total), 1e-9);
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            bars.ColumnDefinitions.Add(new ColumnDefinition());
            labels.ColumnDefinitions.Add(new ColumnDefinition());

            // The transparent background makes the whole column height the tooltip's hit target.
            var cell = new Grid { RowSpacing = 2, MaxWidth = MaxColumnWidth, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(max - column.Total, GridUnitType.Star) });
            var visible = column.Segments.Where(s => s.Value > 0).Reverse().ToList(); // top to bottom
            for (var s = 0; s < visible.Count; s++)
            {
                cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(visible[s].Value, GridUnitType.Star) });
                var segment = new Border
                {
                    Background = Palette.Brush(visible[s].Tone),
                    CornerRadius = s == 0 ? new CornerRadius(4, 4, 0, 0) : new CornerRadius(0),
                    MinHeight = 2,
                };
                Grid.SetRow(segment, cell.RowDefinitions.Count - 1);
                cell.Children.Add(segment);
            }

            ToolTipService.SetToolTip(cell, column.ToolTip);
            Grid.SetColumn(cell, i);
            bars.Children.Add(cell);
        }

        // A label spans the columns up to the next label; the last one is end-aligned over the gap before it, so it is
        // never clipped.
        var labeled = columns.Select((c, i) => (c.Label, i)).Where(x => x.Label is not null).Select(x => x.i).ToList();
        for (var k = 0; k < labeled.Count; k++)
        {
            var i = labeled[k];
            var last = k == labeled.Count - 1 && k > 0;
            var span = last ? i - labeled[k - 1] : (k + 1 < labeled.Count ? labeled[k + 1] : columns.Count) - i;
            var label = new TextBlock
            {
                Text = columns[i].Label,
                FontSize = 11,
                Foreground = Palette.TextSecondary,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalAlignment = last ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            };
            Grid.SetColumn(label, last ? i - span + 1 : i);
            Grid.SetColumnSpan(label, Math.Max(1, span));
            labels.Children.Add(label);
        }
    }
}
