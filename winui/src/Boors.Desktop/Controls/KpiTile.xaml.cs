using Boors.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Boors.Desktop.Controls;

public sealed partial class KpiTile : UserControl
{
    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(nameof(Item), typeof(KpiItem), typeof(KpiTile), new PropertyMetadata(null));

    public KpiTile() => InitializeComponent();

    public KpiItem? Item
    {
        get => (KpiItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }
}
