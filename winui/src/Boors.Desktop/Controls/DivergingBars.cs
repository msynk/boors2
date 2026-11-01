using Boors.Desktop.Infrastructure;
using Boors.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Boors.Desktop.Controls;

/// <summary>
/// Sorted horizontal bars diverging from a zero line (sector returns): the label is text in the page's direction
/// (Persian shapes correctly, unlike canvas text), the bar area always runs left (negative) to right (positive) on a
/// symmetric fixed domain, and every bar carries its signed value as a direct label.
/// </summary>
public sealed partial class DivergingBars : UserControl
{
    public static readonly DependencyProperty BarsProperty =
        DependencyProperty.Register(nameof(Bars), typeof(IReadOnlyList<DivergingBar>), typeof(DivergingBars), new PropertyMetadata(null, (d, _) => ((DivergingBars)d).Build()));

    private readonly StackPanel rows = new() { Spacing = 2 };

    public DivergingBars() => Content = rows;

    public IReadOnlyList<DivergingBar>? Bars
    {
        get => (IReadOnlyList<DivergingBar>?)GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    public double LabelWidth { get; set; } = 150;

    public event EventHandler<DivergingBar>? BarInvoked;

    private void Build()
    {
        rows.Children.Clear();
        if (Bars is not { } bars)
            return;

        var secondary = Palette.TextSecondary;
        var zero = Palette.NeutralBrush;
        foreach (var bar in bars)
        {
            var row = new Grid
            {
                ColumnSpacing = 8,
                Height = 20,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(LabelWidth) },
                    new ColumnDefinition(),
                    new ColumnDefinition { Width = new GridLength(70) },
                },
            };
            var label = new TextBlock { Text = bar.Label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = secondary };
            row.Children.Add(label);

            // The bar area: two halves around a zero line, left to right in every language.
            var area = new Grid { FlowDirection = FlowDirection.LeftToRight, ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = new GridLength(1) }, new ColumnDefinition() } };
            var negative = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = Ui.Star(1 - bar.Negative) }, new ColumnDefinition { Width = Ui.Star(bar.Negative) } } };
            var positive = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = Ui.Star(bar.Positive) }, new ColumnDefinition { Width = Ui.Star(1 - bar.Positive) } } };
            var down = new Rectangle { Fill = Palette.Brush(Tone.Down), RadiusX = 2, RadiusY = 2, Margin = new Thickness(0, 3, 0, 3) };
            Grid.SetColumn(down, 1);
            negative.Children.Add(down);
            var up = new Rectangle { Fill = Palette.Brush(Tone.Up), RadiusX = 2, RadiusY = 2, Margin = new Thickness(0, 3, 0, 3) };
            positive.Children.Add(up);
            var axis = new Rectangle { Fill = zero };
            Grid.SetColumn(axis, 1);
            Grid.SetColumn(positive, 2);
            area.Children.Add(negative);
            area.Children.Add(axis);
            area.Children.Add(positive);
            Grid.SetColumn(area, 1);
            row.Children.Add(area);

            var value = new TextBlock
            {
                Text = bar.ValueText,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Palette.ToneBrush(bar.Tone),
                FlowDirection = FlowDirection.LeftToRight,
                TextAlignment = TextAlignment.Right,
            };
            Grid.SetColumn(value, 2);
            row.Children.Add(value);
            ToolTipService.SetToolTip(row, bar.ToolTip);
            row.Tapped += (_, _) => BarInvoked?.Invoke(this, bar);
            rows.Children.Add(row);
        }
    }
}
