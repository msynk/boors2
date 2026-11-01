using Boors.Desktop.Infrastructure;
using Microsoft.UI.Xaml;

namespace Boors.Desktop.ViewModels;

/// <summary>A stat tile: caption, headline value, a signed change (tone + arrow), a note and an optional sparkline.</summary>
public sealed record KpiItem(string Caption, string Value, Tone ValueTone = Tone.Neutral, string? Change = null, Tone ChangeTone = Tone.Neutral, string? Note = null, IReadOnlyList<double>? Spark = null)
{
    public Visibility ChangeVisibility => Change is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility NoteVisibility => Note is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility SparkVisibility => Spark is { Count: >= 2 } ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>One diverging bar (sector return): the label, the signed value text and the bar's share of each half.</summary>
public sealed record DivergingBar(string Label, string ValueText, Tone Tone, double Negative, double Positive, string ToolTip, string Key);

/// <summary>A day of breadth for the 100% stacked bars.</summary>
public sealed record BreadthColumn(DateOnly Date, int Advancers, int Unchanged, int Decliners, int NotTraded, string ToolTip);

/// <summary>A line of a feed (Codal letters, supervisor messages).</summary>
public sealed record FeedItem(string Meta, string Title, string? Body, Uri? Link, Uri? ExcelLink)
{
    public Visibility BodyVisibility => string.IsNullOrWhiteSpace(Body) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility LinkVisibility => Link is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility TextTitleVisibility => Link is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ExcelVisibility => ExcelLink is null ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>A selectable option (segmented buttons, combo boxes).</summary>
public sealed record Choice(string Key, string Text)
{
    public override string ToString() => Text;
}

public static class Ui
{
    public static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Hide(bool hidden) => hidden ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility ShowText(string? text) => string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility ShowValue(object? value) => value is null ? Visibility.Collapsed : Visibility.Visible;

    public static GridLength Star(double value) => new(Math.Max(0, value), GridUnitType.Star);

    public static bool Not(bool value) => !value;
}
