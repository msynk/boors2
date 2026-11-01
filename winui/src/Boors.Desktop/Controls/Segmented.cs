using Boors.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Boors.Desktop.Controls;

/// <summary>
/// A row of mutually exclusive toggle buttons (rial/dollar, class groups, chart ranges). Selection follows the buttons'
/// checked state, so a click, the keyboard and UI Automation's Toggle (screen readers) all select; one option always
/// stays selected.
/// </summary>
public sealed partial class Segmented : UserControl
{
    private readonly StackPanel panel = new() { Orientation = Orientation.Horizontal, Spacing = 0 };
    private readonly List<ToggleButton> buttons = [];
    private IReadOnlyList<Choice> items = [];
    private string? selectedKey;
    private bool updating;

    public Segmented() => Content = panel;

    public IReadOnlyList<Choice> Items
    {
        get => items;
        set
        {
            items = value;
            Build();
        }
    }

    public string? SelectedKey
    {
        get => selectedKey;
        set
        {
            selectedKey = value;
            Sync();
        }
    }

    public bool Compact { get; set; }

    public event EventHandler<string>? SelectionChanged;

    private void Build()
    {
        panel.Children.Clear();
        buttons.Clear();
        for (var i = 0; i < items.Count; i++)
        {
            var choice = items[i];
            var first = i == 0;
            var last = i == items.Count - 1;
            var button = new ToggleButton
            {
                Content = choice.Text,
                Tag = choice.Key,
                Padding = Compact ? new Thickness(10, 3, 10, 4) : new Thickness(14, 5, 14, 6),
                MinWidth = Compact ? 40 : 56,
                CornerRadius = new CornerRadius(first ? 4 : 0, last ? 4 : 0, last ? 4 : 0, first ? 4 : 0),
                Margin = new Thickness(first ? 0 : -1, 0, 0, 0),
            };
            button.Checked += (s, _) =>
            {
                var key = (string)((ToggleButton)s).Tag;
                if (updating || key == selectedKey)
                    return;
                selectedKey = key;
                Sync();
                SelectionChanged?.Invoke(this, key);
            };
            button.Unchecked += (s, _) =>
            {
                // The selected option cannot be switched off; restore it once the toggle has finished.
                if (!updating && (string)((ToggleButton)s).Tag == selectedKey)
                    DispatcherQueue.TryEnqueue(Sync);
            };
            buttons.Add(button);
            panel.Children.Add(button);
        }

        Sync();
    }

    private void Sync()
    {
        updating = true;
        foreach (var button in buttons)
            button.IsChecked = (string)button.Tag == selectedKey;
        updating = false;
    }
}
