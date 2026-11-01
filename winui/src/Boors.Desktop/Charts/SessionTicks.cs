using Boors.Desktop.Infrastructure;
using ScottPlot;
using Palette = Boors.Desktop.Infrastructure.Palette;

namespace Boors.Desktop.Charts;

/// <summary>
/// X ticks for charts plotted by session index (no weekend or holiday gaps): labels sit on the first session of a
/// month (or quarter, or year) in the display calendar — Jalali under Persian — and fall back to every N sessions for
/// short windows. Labels are pre-formatted dates, so the axis never needs a calendar-aware date type.
/// </summary>
public sealed class SessionTicks(IReadOnlyList<DateOnly> dates) : ITickGenerator
{
    public Tick[] Ticks { get; set; } = [];

    public int MaxTickCount { get; set; } = 1000;

    public void Regenerate(CoordinateRange range, Edge edge, PixelLength size, Paint paint, LabelStyle labelStyle)
    {
        if (dates.Count == 0)
        {
            Ticks = [];
            return;
        }

        var lo = Math.Clamp((int)Math.Ceiling(range.Min), 0, dates.Count - 1);
        var hi = Math.Clamp((int)Math.Floor(range.Max), 0, dates.Count - 1);
        if (hi < lo)
        {
            Ticks = [];
            return;
        }

        var maxLabels = Math.Max(2, (int)(size.Length / 80));
        var parts = new (int Year, int Month, int Day)[hi - lo + 1];
        for (var i = lo; i <= hi; i++)
            parts[i - lo] = Fmt.CalendarParts(dates[i]);

        // Month starts within the window, then thin to quarters/years when there are too many.
        var monthStarts = new List<int>();
        for (var i = lo; i <= hi; i++)
        {
            if (i == 0 || Fmt.CalendarParts(dates[i - 1]).Month != parts[i - lo].Month)
                monthStarts.Add(i);
        }

        List<Tick> ticks = [];
        if (monthStarts.Count >= 2)
        {
            foreach (var step in new[] { 1, 3, 6, 12, 24, 60 })
            {
                var chosen = monthStarts.Where(i => (parts[i - lo].Month - 1) % Math.Min(step, 12) == 0 && (step <= 12 || parts[i - lo].Year % (step / 12) == 0)).ToList();
                if (chosen.Count <= maxLabels)
                {
                    ticks.AddRange(chosen.Select(i => new Tick(i, Label(i, yearOnly: step >= 12), isMajor: true)));
                    break;
                }
            }
        }

        if (ticks.Count < 2)
        {
            // Short window: every N sessions with day labels.
            var every = Math.Max(1, (int)Math.Ceiling((hi - lo + 1) / (double)maxLabels));
            for (var i = lo; i <= hi; i += every)
                ticks.Add(new Tick(i, DayLabel(i), isMajor: true));
        }

        // Keep labels at least ~76 px apart: a long closure (the 1404–1405 war) puts month starts next to each other.
        var pixelsPerSession = size.Length / Math.Max(1e-9, range.Max - range.Min);
        var spaced = new List<Tick>(ticks.Count);
        foreach (var tick in ticks)
        {
            if (spaced.Count == 0 || (tick.Position - spaced[^1].Position) * pixelsPerSession >= 76)
                spaced.Add(tick);
        }

        Ticks = [.. spaced];
    }

    private string Label(int index, bool yearOnly)
    {
        var (year, month, _) = Fmt.CalendarParts(dates[index]);
        var text = yearOnly ? year.ToString(CultureInfo.InvariantCulture) : $"{year:0000}/{month:00}";
        return Fmt.Digits(Fmt.JalaliDates ? text : text.Replace('/', '-'));
    }

    private string DayLabel(int index)
    {
        var (_, month, day) = Fmt.CalendarParts(dates[index]);
        return Fmt.Digits(Fmt.JalaliDates ? $"{month:00}/{day:00}" : $"{month:00}-{day:00}");
    }
}
