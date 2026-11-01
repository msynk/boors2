namespace Boors.Market.Domain;

/// <summary>
/// TSETMC encodes dates as <c>dEven</c> integers (YYYYMMDD, Gregorian) and times as <c>hEven</c> integers
/// (HHMMSS without leading zeros, e.g. 91530 = 09:15:30). Storage keeps these; conversions happen at the edges.
/// </summary>
public static class DEven
{
    public static DateOnly ToDate(int dEven)
    {
        if (dEven < 1000_01_01 || dEven > 9999_12_31)
            throw new ArgumentOutOfRangeException(nameof(dEven), dEven, "Expected a YYYYMMDD integer.");

        return new DateOnly(dEven / 10000, dEven / 100 % 100, dEven % 100);
    }

    public static DateOnly? ToDateOrNull(int dEven) => dEven <= 0 ? null : ToDate(dEven);

    public static int FromDate(DateOnly date) => date.Year * 10000 + date.Month * 100 + date.Day;

    /// <summary>Converts an <c>hEven</c> value to a time of day; returns null for 0 (no trade / not set).</summary>
    public static TimeOnly? ToTime(int hEven)
    {
        if (hEven <= 0)
            return null;

        int hours = hEven / 10000, minutes = hEven / 100 % 100, seconds = hEven % 100;
        if (hours > 23 || minutes > 59 || seconds > 59)
            return null;

        return new TimeOnly(hours, minutes, seconds);
    }
}
