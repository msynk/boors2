using Boors.Desktop.Infrastructure;

namespace Boors.Desktop.Controls;

/// <summary>
/// A column of <see cref="DataTable"/>: header, fixed width, how to show a row (text, tone, optional tag), how to sort
/// it and how to export it. Display text may hold Persian digits and isolates; <see cref="Export"/> returns invariant
/// ASCII (exports never depend on the UI language).
/// </summary>
public sealed class TableColumn
{
    public required string Header { get; init; }

    public string? ExportHeader { get; init; }

    public double Width { get; init; } = 96;

    /// <summary>Numbers are drawn left to right, end-aligned, with tabular figures.</summary>
    public bool Numeric { get; init; }

    /// <summary>Takes the spare width when the table is wider than the sum of the columns (one per table, e.g. Name).</summary>
    public bool Fill { get; init; }

    public required Func<object, string> Text { get; init; }

    public Func<object, Tone>? Tone { get; init; }

    /// <summary>A pill instead of text (e.g. queue state); null hides the pill for that row.</summary>
    public Func<object, (string Text, Tone Tone)?>? Tag { get; init; }

    /// <summary>Null means the column is not sortable.</summary>
    public Func<object, IComparable?>? SortKey { get; init; }

    public Func<object, string?>? Export { get; init; }

    /// <summary>The row type the delegates expect (set by <see cref="Col.Of{T}"/>); rows of another type show blank cells.</summary>
    public Type? RowType { get; init; }

    /// <summary>Numeric columns sort descending first, text columns ascending.</summary>
    public bool DescendingFirst => Numeric;
}

/// <summary>Typed column factory: <c>Col.Of&lt;MarketWatchEntryDto&gt;("Symbol", 110, e =&gt; e.Symbol, sort: e =&gt; e.Symbol)</c>.</summary>
public static class Col
{
    public static TableColumn Of<T>(
        string header,
        double width,
        Func<T, string> text,
        Func<T, IComparable?>? sort = null,
        Func<T, Tone>? tone = null,
        bool numeric = false,
        Func<T, string?>? export = null,
        Func<T, (string Text, Tone Tone)?>? tag = null,
        string? exportHeader = null,
        bool fill = false) => new()
    {
        Header = header,
        RowType = typeof(T),
        Fill = fill,
        ExportHeader = exportHeader,
        Width = width,
        Numeric = numeric,
        Text = o => text((T)o),
        SortKey = sort is null ? null : o => sort((T)o),
        Tone = tone is null ? null : o => tone((T)o),
        Export = export is null ? null : o => export((T)o),
        Tag = tag is null ? null : o => tag((T)o),
    };

    /// <summary>Invariant text of a number for CSV.</summary>
    public static string? Inv(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

    public static string? Inv(double? value) => value?.ToString("R", CultureInfo.InvariantCulture);

    public static string? Inv(long? value) => value?.ToString(CultureInfo.InvariantCulture);

    public static string? Inv(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
