using System.Collections;
using Boors.Desktop.Infrastructure;
using Microsoft.UI.Composition;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Boors.Desktop.Controls;

/// <summary>
/// A virtualized, sortable grid built for ~3,000 rows: an <see cref="ItemsRepeater"/> (only visible rows exist) of
/// recycled fixed-height rows whose cells are plain TextBlocks filled directly — no per-cell bindings, no templates.
/// The header sticks to the top and the first column can be frozen; both follow the scroll offset on the composition
/// thread (expression animations on the ScrollViewer's manipulation property set), so they never lag or jitter.
/// Sorting runs on a pool thread. In a right-to-left page the whole grid mirrors (symbol column on the right) while
/// numeric cells stay left to right. <see cref="Columns"/> may change at any time (the Reports page swaps them per
/// report): every row is built for one column set, and a new set gets a new repeater, so no recycled row of the old
/// set is ever measured or bound against the new one.
/// </summary>
public sealed partial class DataTable : UserControl
{
    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(nameof(Items), typeof(object), typeof(DataTable), new PropertyMetadata(null, (d, _) => ((DataTable)d).OnItemsChanged()));

    private static readonly CompareInfo PersianCompare = CultureInfo.GetCultureInfo("fa-IR").CompareInfo;

    private readonly ScrollViewer scroller;
    private readonly StackPanel stack;
    private readonly TextBlock empty;
    private ItemsRepeater repeater;
    private RowFactory factory;
    private TableRow? header;
    private IReadOnlyList<TableColumn> columns = [];
    private List<object> source = [];
    private List<object> view = [];
    private Dictionary<object, int> rowIndex = new(ReferenceEqualityComparer.Instance);
    private ExpressionAnimation? stickyX;
    private ExpressionAnimation? stickyY;
    private int sortColumn = -1;
    private bool sortDescending;

    public DataTable()
    {
        factory = new RowFactory(this);
        repeater = NewRepeater(factory);
        stack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(repeater);
        scroller = new ScrollViewer
        {
            Content = stack,
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ZoomMode = ZoomMode.Disabled,
            IsTabStop = false,
        };
        empty = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 48, 16, 16),
            TextWrapping = TextWrapping.WrapWholeWords,
            TextAlignment = TextAlignment.Center,
            Foreground = Palette.TextSecondary,
            Visibility = Visibility.Collapsed,
        };

        var root = new Grid();
        root.Children.Add(scroller);
        root.Children.Add(empty);
        Content = root;
        Loaded += (_, _) => AttachSticky();
        SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5)
                Layout();
        };
    }

    /// <summary>Any enumerable of rows; replaced wholesale (the view is re-sorted with the current sort).</summary>
    public object? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public IReadOnlyList<TableColumn> Columns
    {
        get => columns;
        set
        {
            columns = value;
            ComputeOffsets();
            BuildHeader();

            // A fresh repeater and factory: the old repeater keeps its realized and recycled rows (built for the old
            // columns) and would measure or reuse them against the new offsets. It leaves the tree with them.
            repeater.ItemsSource = null;
            stack.Children.Remove(repeater);
            factory = new RowFactory(this);
            repeater = NewRepeater(factory);
            repeater.Width = TotalWidth;
            stack.Children.Add(repeater);
            repeater.ItemsSource = view;
        }
    }

    private static ItemsRepeater NewRepeater(RowFactory rows) => new()
    {
        Layout = new StackLayout { Orientation = Orientation.Vertical },
        ItemTemplate = rows,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    /// <summary>1 freezes the first column (the symbol) while scrolling horizontally.</summary>
    public int FrozenColumns { get; set; }

    public double RowHeight { get; set; } = 30;

    public double CellFontSize { get; set; } = 13;

    public string EmptyText
    {
        get => empty.Text;
        set => empty.Text = value;
    }

    /// <summary>The sorted column (-1 = the order the rows came in) and its direction.</summary>
    public int SortColumn => sortColumn;

    public bool SortDescending => sortDescending;

    /// <summary>Restores a sort without running it (the next <see cref="Items"/> assignment is sorted that way).</summary>
    public void SetSort(int column, bool descending)
    {
        sortColumn = column;
        sortDescending = descending;
        UpdateHeaderIndicators();
    }

    /// <summary>Elapsed time of the last sort (key extraction + ordering + view build), on a pool thread.</summary>
    public double LastSortMilliseconds { get; private set; }

    /// <summary>The rows as currently shown (filtered by the caller, sorted here).</summary>
    public IReadOnlyList<object> View => view;

    public event EventHandler<object>? RowInvoked;

    /// <summary>Raised after a sort finishes, with its duration in milliseconds.</summary>
    public event EventHandler<double>? Sorted;

    internal double[] ColumnOffsets { get; private set; } = [0];

    internal double TotalWidth => ColumnOffsets[^1];

    /// <summary>Column x offsets; the fill column (if any) absorbs the width the viewport has beyond the columns.</summary>
    private void ComputeOffsets()
    {
        var natural = columns.Sum(c => c.Width);
        var spare = Math.Max(0, ActualWidth - natural - 2);
        ColumnOffsets = new double[columns.Count + 1];
        for (var i = 0; i < columns.Count; i++)
            ColumnOffsets[i + 1] = ColumnOffsets[i] + columns[i].Width + (columns[i].Fill ? spare : 0);
    }

    private void Layout()
    {
        if (!columns.Any(c => c.Fill))
            return;
        ComputeOffsets();
        repeater.Width = TotalWidth;
        stack.Width = TotalWidth;
        header?.InvalidateMeasure();
        header?.InvalidateArrange();
        factory.Relayout();
        repeater.InvalidateMeasure();
    }

    internal int IndexOf(object item) => rowIndex.TryGetValue(item, out var i) ? i : 0;

    /// <summary>Sorts by a column (header click does the same); the second click on a column flips the direction.</summary>
    public async Task SortAsync(int column, bool? descending = null)
    {
        if (column < 0 || column >= columns.Count || columns[column].SortKey is not { } key)
            return;

        var desc = descending ?? (column == sortColumn ? !sortDescending : columns[column].DescendingFirst);
        var rows = source;
        var stopwatch = Stopwatch.StartNew();
        var sorted = await Task.Run(() => Order(rows, key, desc));
        LastSortMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        if (!ReferenceEquals(rows, source))
            return; // the rows changed while sorting

        sortColumn = column;
        sortDescending = desc;
        SetView(sorted);
        UpdateHeaderIndicators();
        scroller.ChangeView(null, 0, null, disableAnimation: true);
        Sorted?.Invoke(this, LastSortMilliseconds);
    }

    /// <summary>RFC 4180 CSV of the current view (ASCII numbers, ISO dates), UTF-8 with BOM so Excel reads Persian names.</summary>
    public void ExportCsv(TextWriter writer)
    {
        writer.WriteLine(string.Join(',', columns.Select(c => Quote(c.ExportHeader ?? Plain(c.Header)))));
        foreach (var row in view)
            writer.WriteLine(string.Join(',', columns.Select(c => Quote(c.Export is { } export ? export(row) ?? string.Empty : Plain(c.Text(row))))));
    }

    private static List<object> Order(List<object> rows, Func<object, IComparable?> key, bool descending)
    {
        var keys = new IComparable?[rows.Count];
        for (var i = 0; i < rows.Count; i++)
            keys[i] = key(rows[i]);

        var order = new int[rows.Count];
        for (var i = 0; i < order.Length; i++)
            order[i] = i;

        Array.Sort(order, (a, b) =>
        {
            var (x, y) = (keys[a], keys[b]);
            int c;
            if (x is null || y is null)
                c = x is null && y is null ? 0 : x is null ? 1 : -1; // empty values last in both directions
            else
            {
                c = x is string sx && y is string sy ? PersianCompare.Compare(sx, sy, CompareOptions.IgnoreCase) : x.CompareTo(y);
                if (descending)
                    c = -c;
            }

            return c != 0 ? c : a.CompareTo(b);
        });

        var result = new List<object>(rows.Count);
        foreach (var i in order)
            result.Add(rows[i]);
        return result;
    }

    private async void OnItemsChanged()
    {
        source = Items is IEnumerable enumerable ? enumerable.Cast<object>().ToList() : [];
        if (sortColumn >= 0 && columns.Count > sortColumn && columns[sortColumn].SortKey is not null)
        {
            var rows = source;
            var key = columns[sortColumn].SortKey!;
            var desc = sortDescending;
            var sorted = await Task.Run(() => Order(rows, key, desc));
            if (ReferenceEquals(rows, source))
                SetView(sorted);
        }
        else
        {
            SetView(source);
        }
    }

    private void SetView(List<object> rows)
    {
        view = rows;
        rowIndex = new Dictionary<object, int>(rows.Count, ReferenceEqualityComparer.Instance);
        for (var i = 0; i < rows.Count; i++)
            rowIndex.TryAdd(rows[i], i);
        repeater.ItemsSource = view;
        empty.Visibility = rows.Count == 0 && !string.IsNullOrEmpty(empty.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildHeader()
    {
        if (header is not null)
            stack.Children.Remove(header);

        header = new TableRow(this, isHeader: true);
        Canvas.SetZIndex(header, 1);
        stack.Children.Insert(0, header);
        repeater.Width = TotalWidth;
        stack.Width = TotalWidth;
        if (stickyY is not null)
            Stick(header, stickyY, "Translation.Y");
        UpdateHeaderIndicators();
    }

    private void UpdateHeaderIndicators() => header?.ShowSort(sortColumn, sortDescending);

    internal void HeaderClicked(int column) => _ = SortAsync(column);

    internal void Invoke(object row) => RowInvoked?.Invoke(this, row);

    /// <summary>Sticky header (Y) and frozen cells (X) follow the scroller on the composition thread.</summary>
    private void AttachSticky()
    {
        if (stickyY is not null)
            return;

        var scroll = ElementCompositionPreview.GetScrollViewerManipulationPropertySet(scroller);
        var compositor = scroll.Compositor;
        stickyY = compositor.CreateExpressionAnimation("-scroll.Translation.Y");
        stickyY.SetReferenceParameter("scroll", scroll);
        stickyX = compositor.CreateExpressionAnimation("-scroll.Translation.X");
        stickyX.SetReferenceParameter("scroll", scroll);
        if (header is not null)
        {
            Stick(header, stickyY, "Translation.Y");
            header.FreezeFirstCell();
        }

        factory.FreezeRealized();
    }

    internal void Freeze(UIElement cell)
    {
        if (stickyX is not null)
            Stick(cell, stickyX, "Translation.X");
    }

    private static void Stick(UIElement element, ExpressionAnimation animation, string property)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        ElementCompositionPreview.GetElementVisual(element).StartAnimation(property, animation);
    }

    private static string Quote(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;

    /// <summary>Display text → export text: no isolates, ASCII digits and separators.</summary>
    internal static string Plain(string text)
    {
        var chars = new List<char>(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '\u2066' or '\u2067' or '\u2068' or '\u2069' or '\u202A' or '\u202B' or '\u202C' or '\u202D' or '\u202E' or '\u200E' or '\u200F':
                    continue;
                case >= '۰' and <= '۹':
                    chars.Add((char)('0' + (c - '۰')));
                    break;
                case '٫':
                    chars.Add('.');
                    break;
                case '٬':
                    chars.Add(',');
                    break;
                case '٪':
                    chars.Add('%');
                    break;
                case '−':
                    chars.Add('-');
                    break;
                default:
                    chars.Add(c);
                    break;
            }
        }

        return new string([.. chars]);
    }

    /// <summary>Opaque table colours (frozen cells must hide what scrolls under them).</summary>
    internal static class Surface
    {
        public static Color Row => Palette.IsDark ? Color.FromArgb(255, 0x27, 0x27, 0x27) : Color.FromArgb(255, 0xFD, 0xFD, 0xFD);

        public static Color RowAlt => Palette.IsDark ? Color.FromArgb(255, 0x2B, 0x2B, 0x2B) : Color.FromArgb(255, 0xF5, 0xF5, 0xF5);

        public static Color Hover => Palette.IsDark ? Color.FromArgb(255, 0x36, 0x36, 0x36) : Color.FromArgb(255, 0xE8, 0xE8, 0xEC);

        public static Color Header => Palette.IsDark ? Color.FromArgb(255, 0x20, 0x20, 0x20) : Color.FromArgb(255, 0xEE, 0xEE, 0xEE);
    }

    private sealed class RowFactory(DataTable owner) : IElementFactory
    {
        // One factory per table, so every pooled row belongs to the same ItemsRepeater (which keeps recycled rows as
        // children); WinUI 3 has no public RecyclePool, and a stack is all this needs.
        private readonly Stack<TableRow> pool = new();
        private readonly List<TableRow> all = [];

        public UIElement GetElement(ElementFactoryGetArgs args)
        {
            TableRow? row = null;
            while (row is null && pool.TryPop(out var pooled))
                row = pooled.IsCurrent ? pooled : null;
            if (row is null)
            {
                row = new TableRow(owner, isHeader: false);
                all.Add(row);
            }

            row.Bind(args.Data);
            return row;
        }

        public void RecycleElement(ElementFactoryRecycleArgs args)
        {
            if (args.Element is TableRow { IsCurrent: true } row)
                pool.Push(row);
        }

        public void FreezeRealized()
        {
            foreach (var row in all)
                row.FreezeFirstCell();
        }

        public void Relayout()
        {
            foreach (var row in all)
            {
                row.InvalidateMeasure();
                row.InvalidateArrange();
            }
        }

    }

    /// <summary>One row: a panel that places its cells at the column offsets (no Grid, no measuring of text widths).</summary>
    private sealed partial class TableRow : Panel
    {
        private const double Padding = 8;
        private readonly DataTable owner;
        private readonly bool isHeader;
        private readonly IReadOnlyList<TableColumn> columns;
        private readonly TextBlock[] texts;
        private readonly Border?[] tags;
        private readonly Border? frozen;
        private object? item;
        private bool hover;
        private bool frozenAttached;

        public TableRow(DataTable owner, bool isHeader)
        {
            this.owner = owner;
            this.isHeader = isHeader;
            columns = owner.columns;
            texts = new TextBlock[columns.Count];
            tags = new Border?[columns.Count];
            Height = isHeader ? Math.Max(owner.RowHeight + 8, 2 * (owner.CellFontSize + 2) + 8) : owner.RowHeight;

            for (var i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                var text = new TextBlock
                {
                    FontSize = owner.CellFontSize,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    IsTextScaleFactorEnabled = false,
                };
                if (column.Numeric)
                {
                    text.FlowDirection = FlowDirection.LeftToRight;
                    text.TextAlignment = owner.FlowDirection == FlowDirection.RightToLeft ? TextAlignment.Left : TextAlignment.Right;
                    Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
                }

                if (isHeader)
                {
                    text.FontWeight = FontWeights.SemiBold;
                    text.Foreground = Palette.TextSecondary;
                    text.Text = column.Header;
                    text.TextWrapping = TextWrapping.WrapWholeWords;
                    text.MaxLines = 2;
                    text.FontSize = owner.CellFontSize - 1;
                    text.LineHeight = owner.CellFontSize + 2;
                    text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                    ToolTipService.SetToolTip(text, column.Header);
                }

                texts[i] = text;
                UIElement cell = text;
                if (!isHeader && column.Tag is not null)
                {
                    var pill = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Child = text };
                    tags[i] = pill;
                    cell = pill;
                }

                if (isHeader)
                {
                    // A real button: keyboard (Tab, Enter/Space), screen readers and UI Automation can sort too.
                    var index = i;
                    var button = new Button
                    {
                        Content = cell,
                        Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
                        Padding = new Thickness(0),
                        MinHeight = 0,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        IsEnabled = column.SortKey is not null,
                        IsTabStop = column.SortKey is not null,
                    };
                    AutomationProperties.SetName(button, column.Header);
                    if (column.SortKey is not null)
                    {
                        button.Click += (_, _) => owner.HeaderClicked(index);
                        button.PointerEntered += (_, _) => text.Foreground = Palette.TextPrimary;
                        button.PointerExited += (_, _) => text.Foreground = Palette.TextSecondary;
                    }

                    cell = button;
                }

                if (i == 0 && owner.FrozenColumns > 0)
                {
                    frozen = new Border { Child = cell };
                    Canvas.SetZIndex(frozen, 1);
                    cell = frozen;
                }

                Children.Add(cell);
            }

            Background = new SolidColorBrush(isHeader ? Surface.Header : Surface.Row);
            if (frozen is not null)
                frozen.Background = new SolidColorBrush(isHeader ? Surface.Header : Surface.Row);

            if (!isHeader)
            {
                PointerEntered += (_, _) => { hover = true; Paint(); };
                PointerExited += (_, _) => { hover = false; Paint(); };
                PointerCanceled += (_, _) => { hover = false; Paint(); };
                Tapped += OnTapped;
            }

            FreezeFirstCell();
        }

        /// <summary>False once the table's columns changed: the row belongs to the old set and must not be reused.</summary>
        public bool IsCurrent => ReferenceEquals(columns, owner.columns);

        public void FreezeFirstCell()
        {
            if (frozen is null || frozenAttached || owner.stickyX is null)
                return;
            owner.Freeze(frozen);
            frozenAttached = true;
        }

        public void Bind(object? data)
        {
            item = data;
            if (data is null)
                return;

            for (var i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                var text = texts[i];

                // A row object of another shape (rows and columns are swapped one after the other) shows blank cells
                // until its own columns arrive, instead of failing a cast in the middle of layout.
                if (column.RowType is { } type && !type.IsInstanceOfType(data))
                {
                    text.Text = string.Empty;
                    if (tags[i] is { } hidden)
                        hidden.Visibility = Visibility.Collapsed;
                    continue;
                }

                if (column.Tag is { } tag)
                {
                    var value = tag(data);
                    var pill = tags[i]!;
                    pill.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
                    if (value is { } v)
                    {
                        text.Text = v.Text;
                        text.Foreground = Palette.Brush(v.Tone);
                        var c = Palette.ColorOf(v.Tone);
                        pill.Background = new SolidColorBrush(Color.FromArgb(Palette.IsDark ? (byte)0x33 : (byte)0x22, c.R, c.G, c.B));
                    }

                    continue;
                }

                text.Text = column.Text(data);
                text.Foreground = column.Tone is { } tone ? Palette.ToneBrush(tone(data)) : Palette.TextPrimary;
            }

            hover = false;
            Paint();
        }

        public void ShowSort(int column, bool descending)
        {
            for (var i = 0; i < columns.Count; i++)
                texts[i].Text = i == column ? $"{columns[i].Header} {(descending ? "▼" : "▲")}" : columns[i].Header;
        }

        private void Paint()
        {
            if (isHeader)
                return;
            var color = hover ? Surface.Hover : item is not null && owner.IndexOf(item) % 2 == 1 ? Surface.RowAlt : Surface.Row;
            ((SolidColorBrush)Background).Color = color;
            if (frozen is not null)
                ((SolidColorBrush)frozen.Background).Color = color;
        }

        private void OnTapped(object sender, TappedRoutedEventArgs e)
        {
            if (item is not null)
                owner.Invoke(item);
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new RowAutomationPeer(this);

        /// <summary>A row is a data item named by its first cell; invoking it does what a tap does.</summary>
        private sealed partial class RowAutomationPeer(TableRow row) : FrameworkElementAutomationPeer(row), IInvokeProvider
        {
            protected override AutomationControlType GetAutomationControlTypeCore() =>
                row.isHeader ? AutomationControlType.Header : AutomationControlType.DataItem;

            protected override string GetNameCore() => row.texts.Length > 0 ? Plain(row.texts[0].Text) : string.Empty;

            protected override object? GetPatternCore(PatternInterface patternInterface) =>
                patternInterface == PatternInterface.Invoke && !row.isHeader ? this : base.GetPatternCore(patternInterface);

            public void Invoke()
            {
                if (row.item is not null)
                    row.owner.Invoke(row.item);
            }
        }

        protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size availableSize)
        {
            var offsets = owner.ColumnOffsets;
            if (!IsCurrent)
                return default;
            for (var i = 0; i < Math.Min(Children.Count, offsets.Length - 1); i++)
                Children[i].Measure(new Windows.Foundation.Size(Math.Max(0, offsets[i + 1] - offsets[i] - 2 * Padding), Height));
            return new Windows.Foundation.Size(owner.TotalWidth, Height);
        }

        protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size finalSize)
        {
            var offsets = owner.ColumnOffsets;
            if (!IsCurrent)
                return finalSize;
            for (var i = 0; i < Math.Min(Children.Count, offsets.Length - 1); i++)
            {
                var width = offsets[i + 1] - offsets[i];
                if (i == 0 && frozen is not null)
                {
                    // The frozen cell covers its whole column (opaque) so scrolled cells pass under it.
                    frozen.Padding = new Thickness(Padding, 0, Padding, 0);
                    Children[i].Arrange(new Windows.Foundation.Rect(offsets[i], 0, width, finalSize.Height));
                }
                else
                {
                    Children[i].Arrange(new Windows.Foundation.Rect(offsets[i] + Padding, 0, Math.Max(0, width - 2 * Padding), finalSize.Height));
                }
            }

            return finalSize;
        }
    }
}
