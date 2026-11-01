namespace Boors.Desktop.Infrastructure;

/// <summary>
/// Number, date and sign conventions of the workstation (the boilerplate's MarketFormat, plus Persian digits and
/// Jalali dates). Every numeric run is wrapped in a left-to-right embedding (LRE U+202A … PDF U+202C) so a
/// right-to-left line never reorders "▲ +2.50%" into "2.50%+ ▲". (The newer isolates U+2066…U+2069 are not honoured by
/// the WinUI text stack.) Exports never go through this class: they stay ASCII and invariant.
/// </summary>
public static class Fmt
{
    /// <summary>همت = 1,000 billion toman = 10^13 rial, the unit Iranian market reports use for value traded.</summary>
    public const decimal RialsPerHemat = 10_000_000_000_000m;

    /// <summary>1 billion toman = 10^10 rial.</summary>
    public const decimal RialsPerBillionToman = 10_000_000_000m;

    public const decimal RialsPerMillionToman = 10_000_000m;

    public const string Dash = "–";

    private const char Lre = '\u202A';
    private const char Pdf = '\u202C';

    private static readonly PersianCalendar Jalali = new();
    private static NumberFormatInfo numbers = NumberFormatInfo.InvariantInfo;

    public static bool PersianDigits { get; private set; } = true;

    public static bool JalaliDates { get; private set; } = true;

    /// <summary>Called when the language or digit setting changes.</summary>
    public static void Configure(bool persianLanguage, bool persianDigits)
    {
        JalaliDates = persianLanguage;
        PersianDigits = persianLanguage && persianDigits;
        var format = (NumberFormatInfo)NumberFormatInfo.InvariantInfo.Clone();
        if (PersianDigits)
        {
            // Persian typography: ٬ groups thousands, ٫ is the decimal point, ٪ the percent sign.
            format.NumberGroupSeparator = "٬";
            format.NumberDecimalSeparator = "٫";
            format.PercentSymbol = "٪";
        }

        numbers = format;
    }

    /// <summary>ASCII digits → Persian digits when that setting is on.</summary>
    public static string Digits(string text)
    {
        if (!PersianDigits || string.IsNullOrEmpty(text))
            return text;

        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                span[i] = source[i] is >= '0' and <= '9' ? (char)('۰' + (source[i] - '0')) : source[i];
        });
    }

    /// <summary>A left-to-right isolated run with display digits.</summary>
    public static string Ltr(string text) => $"{Lre}{Digits(text)}{Pdf}";

    public static string Num(decimal value, string format) => Ltr(value.ToString(format, numbers));

    public static string Num(double value, string format) => Ltr(value.ToString(format, numbers));

    public static string Hemat(decimal? rials) => rials is { } r ? Num(r / RialsPerHemat, "N2") : Dash;

    public static string BillionToman(decimal? rials) => rials is { } r ? Num(r / RialsPerBillionToman, "N1") : Dash;

    public static string MillionToman(decimal? rials) => rials is { } r ? Num(r / RialsPerMillionToman, "N0") : Dash;

    public static string Price(decimal? value) => value is { } v ? Num(v, "N0") : Dash;

    public static string Number(long? value) => value is { } v ? Ltr(v.ToString("N0", numbers)) : Dash;

    public static string Number(int value) => Ltr(value.ToString("N0", numbers));

    public static string Ratio(decimal? value) => value is { } v ? Num(v, "N2") : Dash;

    public static string Decimal(decimal? value, int places) => value is { } v ? Num(v, "N" + places.ToString(CultureInfo.InvariantCulture)) : Dash;

    /// <summary>A plain number for chart ticks (digits converted, no isolates: charts draw left to right anyway).</summary>
    public static string Tick(double value, string format = "N0") => Digits(value.ToString(format, numbers));

    /// <summary>A percentage (2.5 = 2.5%) with sign and arrow: "▲ +2.50%" (never colour alone).</summary>
    public static string Change(double? percent) => percent switch
    {
        null => Dash,
        > 0.005 => Ltr($"▲ +{percent.Value.ToString("N2", numbers)}{numbers.PercentSymbol}"),
        < -0.005 => Ltr($"▼ −{Math.Abs(percent.Value).ToString("N2", numbers)}{numbers.PercentSymbol}"),
        _ => Ltr($"● {0.0.ToString("N2", numbers)}{numbers.PercentSymbol}"),
    };

    /// <summary>A fraction (0.25 = 25%) as a signed percentage.</summary>
    public static string FractionChange(double? fraction) => Change(fraction * 100);

    /// <summary>An unsigned percentage of a fraction: 0.873 → "87.3%".</summary>
    public static string Percent(double? fraction, int places = 1) =>
        fraction is { } f ? Ltr((f * 100).ToString("N" + places.ToString(CultureInfo.InvariantCulture), numbers) + numbers.PercentSymbol) : Dash;

    /// <summary>A signed amount (flows): "+1,234.5" / "−1,234.5".</summary>
    public static string Signed(decimal? value, Func<decimal, decimal> scale, string format) => value switch
    {
        null => Dash,
        > 0 => Ltr("+" + scale(value.Value).ToString(format, numbers)),
        < 0 => Ltr("−" + Math.Abs(scale(value.Value)).ToString(format, numbers)),
        _ => Ltr(0m.ToString(format, numbers)),
    };

    public static string SignedBillionToman(decimal? rials) => Signed(rials, r => r / RialsPerBillionToman, "N1");

    public static string SignedHemat(decimal? rials) => Signed(rials, r => r / RialsPerHemat, "N2");

    /// <summary>Jalali yyyy/MM/dd in Persian, ISO yyyy-MM-dd in English.</summary>
    public static string Date(DateOnly? date) => date is { } d ? Ltr(DateText(d)) : Dash;

    public static string DateText(DateOnly d)
    {
        if (!JalaliDates)
            return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var dt = d.ToDateTime(TimeOnly.MinValue);
        return string.Create(CultureInfo.InvariantCulture, $"{Jalali.GetYear(dt):0000}/{Jalali.GetMonth(dt):00}/{Jalali.GetDayOfMonth(dt):00}");
    }

    /// <summary>Year and month of a date in the display calendar (Jalali under Persian).</summary>
    public static (int Year, int Month, int Day) CalendarParts(DateOnly d)
    {
        if (!JalaliDates)
            return (d.Year, d.Month, d.Day);
        var dt = d.ToDateTime(TimeOnly.MinValue);
        return (Jalali.GetYear(dt), Jalali.GetMonth(dt), Jalali.GetDayOfMonth(dt));
    }

    public static string DateTime(DateTimeOffset? value)
    {
        if (value is not { } v)
            return Dash;
        var local = v.ToLocalTime();
        return Ltr($"{DateText(DateOnly.FromDateTime(local.DateTime))} {local:HH:mm}");
    }

    public static string Duration(TimeSpan span) => span.TotalSeconds < 60
        ? Ltr(span.TotalSeconds.ToString("N1", numbers)) + " " + Loc.T("s")
        : Ltr(span.TotalMinutes.ToString("N1", numbers)) + " " + Loc.T("min");

    public static string Duration(TimeSpan? span) => span is { } s ? Duration(s) : Dash;

    /// <summary>Milliseconds, rounded: "340 ms".</summary>
    public static string Milliseconds(double? ms) => ms is { } m ? Ltr(m.ToString("N0", numbers)) + " " + Loc.T("ms") : Dash;

    /// <summary>A time relative to now in whole units: "12 min ago", "in 3 h", "2 days ago".</summary>
    public static string Relative(DateTimeOffset? at, DateTimeOffset now)
    {
        if (at is not { } t)
            return Dash;
        var delta = t - now;
        var span = delta.Duration();
        var amount = span.TotalMinutes < 1 ? Loc.F("{0} s", Number((int)span.TotalSeconds))
            : span.TotalHours < 1 ? Loc.F("{0} min", Number((int)span.TotalMinutes))
            : span.TotalDays < 1 ? Loc.F("{0} h", Number((int)span.TotalHours))
            : Loc.F("{0} days", Number((int)span.TotalDays));
        return delta.Ticks > 0 ? Loc.F("in {0}", amount) : Loc.F("{0} ago", amount);
    }

    /// <summary>Clock time of a moment in local time: "13:35".</summary>
    public static string Clock(DateTimeOffset? value) => value is { } v ? Ltr(v.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)) : Dash;

    public static Tone ToneOf(double? value) => value switch
    {
        > 0.005 => Tone.Up,
        < -0.005 => Tone.Down,
        _ => Tone.Neutral,
    };

    public static Tone ToneOf(decimal? value) => value switch
    {
        > 0 => Tone.Up,
        < 0 => Tone.Down,
        _ => Tone.Neutral,
    };

    public static string ClassName(string instrumentClass) => instrumentClass switch
    {
        "tse_stock" => Loc.T("TSE share"),
        "ifb_stock" => Loc.T("IFB share"),
        "ifb_base_yellow" or "ifb_base_orange" or "ifb_base_red" => Loc.T("Base market"),
        "rights" => Loc.T("Rights"),
        "etf_equity" => Loc.T("Equity fund"),
        "etf_fixed_income" => Loc.T("Fixed-income fund"),
        "etf_real_estate" => Loc.T("Real-estate fund"),
        "etf_mixed" => Loc.T("Mixed fund"),
        "fund_leveraged" => Loc.T("Leveraged fund"),
        "fund_gold_silver" => Loc.T("Gold fund"),
        "fund_of_funds" => Loc.T("Fund of funds"),
        "option" => Loc.T("Option"),
        "debt" => Loc.T("Bond / sukuk"),
        _ => Loc.T("Other"),
    };

    public static string DayStatus(string? status) => status switch
    {
        "Traded" => Loc.T("Traded"),
        "NoTrade" => Loc.T("No trade"),
        "SymbolHalted" => Loc.T("Halted"),
        "MarketClosed" => Loc.T("Market closed"),
        "ReopenAuction" => Loc.T("Reopening auction"),
        "QueueLockedUp" => Loc.T("Buy queue all day"),
        "QueueLockedDown" => Loc.T("Sell queue all day"),
        null => Dash,
        _ => status,
    };
}

/// <summary>Direction of a value for colouring: always paired with a sign or arrow in the text.</summary>
public enum Tone
{
    Neutral,
    Up,
    Down,
    Accent,
    Muted,

    /// <summary>Status colours are reserved for data health and always come with an icon and a label.</summary>
    Good,
    Warning,
    Critical,
    Info,
}
