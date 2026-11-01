using DNTPersianUtils.Core;

namespace Boors.Market.Text;

/// <summary>Text normalization applied to every Persian string before it is stored or compared.</summary>
public static class PersianText
{
    private static readonly PersianCalendar Calendar = new();

    /// <summary>
    /// Arabic ي/ك become Persian ی/ک, digits become ASCII, and surrounding whitespace (TSETMC pads fixed-width fields)
    /// is trimmed. Returns null for null or blank input.
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.ApplyCorrectYeKe().ToEnglishNumbers().Trim();
    }

    /// <summary>The spelling TSETMC stores internally: Persian ی/ک replaced by Arabic ي/ك.</summary>
    public static string ToArabicLetters(string value) =>
        value.Replace('ی', 'ي').Replace('ک', 'ك');

    /// <summary>A comparison key for symbol and name matching: normalized, with ZWNJ and spaces removed.</summary>
    public static string MatchKey(string value) =>
        (Normalize(value) ?? string.Empty).Replace("‌", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Parses a Jalali timestamp such as "۱۴۰۵/۰۷/۰۷ ۱۸:۵۵:۳۴" or "1405/07/07" (Persian or ASCII digits) into an
    /// Iran-local <see cref="DateTimeOffset"/>.
    /// </summary>
    public static DateTimeOffset? ParseJalali(string? value)
    {
        var text = Normalize(value);
        if (text is null)
            return null;

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var date = parts[0].Split('/', '-');
        if (date.Length != 3
            || !int.TryParse(date[0], CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(date[1], CultureInfo.InvariantCulture, out var month)
            || !int.TryParse(date[2], CultureInfo.InvariantCulture, out var day))
            return null;

        int hour = 0, minute = 0, second = 0;
        if (parts.Length > 1)
        {
            var time = parts[1].Split(':');
            if (time.Length < 2
                || !int.TryParse(time[0], CultureInfo.InvariantCulture, out hour)
                || !int.TryParse(time[1], CultureInfo.InvariantCulture, out minute)
                || (time.Length > 2 && !int.TryParse(time[2], CultureInfo.InvariantCulture, out second)))
                return null;
        }

        try
        {
#pragma warning disable DateTimeOffsetInsteadOfDateTimeAnalyzer // PersianCalendar only produces DateTime; it is converted to an offset immediately.
            var local = Calendar.ToDateTime(year, month, day, hour, minute, second, 0);
#pragma warning restore DateTimeOffsetInsteadOfDateTimeAnalyzer
            return IranTime.ToOffset(local);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}

/// <summary>Iran local time. Iran abolished daylight saving in 2022, but historical timestamps still need the zone rules.</summary>
public static class IranTime
{
    public static TimeZoneInfo Zone { get; } = FindZone();

#pragma warning disable DateTimeOffsetInsteadOfDateTimeAnalyzer // Boundary helper: turns a wall-clock Iran time into an offset.
    public static DateTimeOffset ToOffset(DateTime local)
    {
        var wallClock = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new(wallClock, Zone.GetUtcOffset(wallClock));
    }
#pragma warning restore DateTimeOffsetInsteadOfDateTimeAnalyzer

    public static DateTimeOffset Now(TimeProvider timeProvider) => TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), Zone);

    private static TimeZoneInfo FindZone()
    {
        foreach (var id in new[] { "Asia/Tehran", "Iran Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("Iran", TimeSpan.FromHours(3.5), "Iran", "Iran");
    }
}
