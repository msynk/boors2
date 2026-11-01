namespace Boors.Desktop.Infrastructure;

/// <summary>
/// The schedule's cron expressions in words (Iran time): "Sat–Wed 13:35", "Every 15 min, 07:00–23:59", "Daily 09:00,
/// 13:00". Covers the forms the scheduler uses (fixed or stepped minutes, hour lists and ranges, any day or a set of
/// weekdays); anything else is shown as the cron text itself.
/// </summary>
public static class ScheduleText
{
    private static readonly string[] DayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    public static string Describe(string cron)
    {
        var f = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length != 5 || f[2] != "*" || f[3] != "*" || Expand(f[1], 0, 23) is not { } hours || Expand(f[4], 0, 7) is not { } weekdays)
            return Fmt.Ltr(cron);

        var days = Days(weekdays.Select(d => d % 7).ToHashSet());
        if (f[0].StartsWith("*/", StringComparison.Ordinal) && int.TryParse(f[0][2..], CultureInfo.InvariantCulture, out var step))
        {
            var window = hours.Count == 24 ? string.Empty : $", {Clock(hours.Min(), 0)}–{Clock(hours.Max(), 59)}";
            return $"{Loc.F("Every {0} min", Fmt.Number(step))}{window}{(days is null ? string.Empty : $", {days}")}";
        }

        if (!int.TryParse(f[0], CultureInfo.InvariantCulture, out var minute))
            return Fmt.Ltr(cron);
        var times = string.Join(Loc.IsPersian ? "، " : ", ", hours.Order().Select(h => Clock(h, minute)));
        return days is null ? $"{Loc.T("Daily")} {times}" : $"{days} {times}";
    }

    private static string Clock(int hour, int minute) => Fmt.Ltr($"{hour:00}:{minute:00}");

    /// <summary>Null for every day; "Sat–Wed" for the trading week; otherwise the day names.</summary>
    private static string? Days(HashSet<int> days)
    {
        if (days.Count == 7)
            return null;
        if (days.SetEquals([6, 0, 1, 2, 3]))
            return Loc.T("Sat–Wed");
        return string.Join(Loc.IsPersian ? "، " : ", ", new[] { 6, 0, 1, 2, 3, 4, 5 }.Where(days.Contains).Select(d => Loc.T(DayNames[d])));
    }

    private static List<int>? Expand(string field, int min, int max)
    {
        var values = new List<int>();
        foreach (var part in field.Split(','))
        {
            var (range, step) = part.Split('/') is [var r, var s] && int.TryParse(s, CultureInfo.InvariantCulture, out var n) ? (r, Math.Max(1, n)) : (part, 1);
            if (Range(range, min, max) is not (var from, var to))
                return null;
            for (var v = from; v <= to; v += step)
                values.Add(v);
        }

        return [.. values.Distinct()];
    }

    private static (int From, int To)? Range(string range, int min, int max)
    {
        if (range == "*")
            return (min, max);
        if (range.Split('-') is [var a, var b])
            return int.TryParse(a, CultureInfo.InvariantCulture, out var from) && int.TryParse(b, CultureInfo.InvariantCulture, out var to) ? (from, to) : null;
        return int.TryParse(range, CultureInfo.InvariantCulture, out var value) ? (value, value) : null;
    }
}
