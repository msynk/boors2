using System.Text.RegularExpressions;
using Boors.Market.Domain;
using Boors.Market.Text;

namespace Boors.Market.Sources.Tsetmc;

// Bulk and auxiliary endpoints: same-day flows for all instruments, index snapshots, supervisor messages,
// shareholders and per-date instrument state.
public static partial class TsetmcParser
{
    public static IReadOnlyList<ClientTypeSnapshot> ParseClientTypeAll(ReadOnlySpan<byte> json)
    {
        var envelope = Deserialize(json, TsetmcJsonContext.Default.ClientTypeAllEnvelope);
        return (envelope?.ClientTypeAllDto ?? [])
            .Where(d => d.InsCode is > 0)
            .Select(d => new ClientTypeSnapshot
            {
                InsCode = d.InsCode!.Value,
                BuyIndividualVolume = (long)d.BuyIVolume,
                BuyInstitutionalVolume = (long)d.BuyNVolume,
                BuyIndividualCount = (int)d.BuyCountI,
                BuyInstitutionalCount = (int)d.BuyCountN,
                SellIndividualVolume = (long)d.SellIVolume,
                SellInstitutionalVolume = (long)d.SellNVolume,
                SellIndividualCount = (int)d.SellCountI,
                SellInstitutionalCount = (int)d.SellCountN,
            })
            .ToList();
    }

    public static IReadOnlyList<IndexSnapshot> ParseIndexSnapshots(ReadOnlySpan<byte> json)
    {
        var envelope = Deserialize(json, TsetmcJsonContext.Default.IndexB1Envelope);
        return (envelope?.IndexB1 ?? [])
            .Where(d => d.InsCode is > 0)
            .Select(d => new IndexSnapshot
            {
                InsCode = d.InsCode!.Value,
                Name = PersianText.Normalize(d.LVal30) ?? d.InsCode.Value.ToString(CultureInfo.InvariantCulture),
                Time = DEven.ToTime(d.HEven),
                Value = d.XDrNivJIdx004,
                High = d.XPhNivJIdx004,
                Low = d.XPbNivJIdx004,
                Change = d.IndexChange,
                ChangePercent = d.XVarIdxJRfV,
            })
            .ToList();
    }

    public static IReadOnlyList<SupervisorMessage> ParseMessages(ReadOnlySpan<byte> json)
    {
        var envelope = Deserialize(json, TsetmcJsonContext.Default.MessageEnvelope);
        return (envelope?.Msg ?? [])
            .Where(d => d.TseMsgIdn > 0 && d.DEven > 0)
            .Select(d => ClassifyMessage(d.TseMsgIdn, DEven.ToDate(d.DEven), DEven.ToTime(d.HEven), d.TseTitle, d.TseDesc, d.Flow))
            .OrderBy(m => m.Date)
            .ThenBy(m => m.Time)
            .ToList();
    }

    /// <summary>
    /// Classifies a supervisor message from its wording. Keyword rules (on normalized text):
    /// title "بازگشایی"/"رفع تعلیق"/"حراج" = reopen, otherwise "توقف"/"تعلیق" = halt; "بدون محدودیت" = reopens with no price limit;
    /// reasons: "افشای اطلاعات" = disclosure, "مجمع" = general meeting, "تعلیق"/"شفاف"/"بررسی"/"دستکاری" = supervisory,
    /// "نماد اصلی"/"قرارداد آتی"/"اختیار" = halted because of the underlying.
    /// </summary>
    public static SupervisorMessage ClassifyMessage(long id, DateOnly date, TimeOnly? time, string? title, string? body, int flow)
    {
        var normalizedTitle = PersianText.Normalize(title) ?? string.Empty;
        var normalizedBody = PersianText.Normalize(body);
        var text = normalizedTitle + " " + normalizedBody;

        var kind = ContainsAny(normalizedTitle, "بازگشایی", "رفع تعلیق", "حراج") ? MessageKind.Reopen
            : ContainsAny(normalizedTitle, "توقف", "تعلیق") ? MessageKind.Halt
            : MessageKind.Other;

        var reason = kind == MessageKind.Other ? HaltReason.Unknown
            : ContainsAny(text, "نماد اصلی", "قرارداد آتی", "اختیار معامله", "اوراق تبعی") ? HaltReason.Underlying
            : ContainsAny(text, "تعلیق", "شفاف", "بررسی", "دستکاری", "ابهام", "رسیدگی") ? HaltReason.Supervisory
            : ContainsAny(text, "مجمع") ? HaltReason.GeneralMeeting
            : ContainsAny(text, "افشای اطلاعات", "اطلاعات با اهمیت", "اطلاعات بااهمیت") ? HaltReason.Disclosure
            : HaltReason.Unknown;

        return new SupervisorMessage
        {
            Id = id,
            Date = date,
            Time = time,
            Title = normalizedTitle,
            Body = normalizedBody,
            Flow = flow,
            Kind = kind,
            Reason = reason,
            ReopensWithoutLimit = ContainsAny(text, "بدون محدودیت"),
            MentionedSymbols = SymbolInParenthesesRegex().Matches(text)
                .Select(m => m.Groups[1].Value.Trim())
                // Symbols may contain a space ("پی پاد"); other parentheticals are harmless because unmatched symbols are not linked.
                .Where(s => s.Length is > 0 and <= 20 && s.Count(c => c == ' ') <= 1)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
        };
    }

    public static IReadOnlyList<ShareholderPosition> ParseShareholders(ReadOnlySpan<byte> json, long insCode, DateOnly snapshotDate)
    {
        var envelope = Deserialize(json, TsetmcJsonContext.Default.ShareholderEnvelope);
        return (envelope?.ShareHolder ?? [])
            .Select(d => new ShareholderPosition
            {
                InsCode = insCode,
                SnapshotDate = DEven.ToDateOrNull(d.DEven) ?? snapshotDate,
                HolderShareId = d.ShareHolderShareID,
                HolderName = PersianText.Normalize(d.ShareHolderName) ?? string.Empty,
                Shares = d.NumberOfShares,
                Percent = d.PerOfShares,
                Change = d.Change,
                ChangeAmount = d.ChangeAmount,
            })
            .ToList();
    }

    /// <summary>
    /// GetInstrumentState returns an object for dates with a state record and an empty array (<c>[]</c>) otherwise;
    /// the latter yields a null state (unknown), which callers must not read as "not halted".
    /// </summary>
    public static InstrumentStateOnDate ParseInstrumentState(ReadOnlySpan<byte> json, long insCode, DateOnly date)
    {
        using var document = JsonDocument.Parse(json.ToArray());
        if (!document.RootElement.TryGetProperty("instrumentState", out var element))
            return new InstrumentStateOnDate(insCode, date, null, null);

        if (element.ValueKind == JsonValueKind.Array)
            element = element.GetArrayLength() > 0 ? element[0] : default;

        if (element.ValueKind != JsonValueKind.Object)
            return new InstrumentStateOnDate(insCode, date, null, null);

        var state = element.TryGetProperty("cEtaval", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()?.Trim() : null;
        var time = element.TryGetProperty("realHeven", out var h) && h.TryGetInt32(out var hEven) ? DEven.ToTime(hEven) : null;
        return new InstrumentStateOnDate(insCode, date, state, time);
    }

    public static IReadOnlyList<InstrumentStatistic> ParseInstrumentStatistics(ReadOnlySpan<byte> json)
    {
        using var document = JsonDocument.Parse(json.ToArray());
        if (!document.RootElement.TryGetProperty("instValueAllInstAllParam", out var items) || items.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<InstrumentStatistic>(items.GetArrayLength());
        foreach (var item in items.EnumerateArray())
        {
            var insCode = item.GetProperty("insCode") is var c && c.ValueKind == JsonValueKind.String && long.TryParse(c.GetString(), CultureInfo.InvariantCulture, out var code) ? code
                : c.ValueKind == JsonValueKind.Number ? c.GetInt64() : 0;
            var value = item.GetProperty("dataValue") is var v && v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed
                : v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN;
            if (insCode <= 0 || double.IsNaN(value) || !item.TryGetProperty("dataType", out var type) || !type.TryGetInt32(out var dataType))
                continue;

            var dEven = item.TryGetProperty("dEven", out var d) && d.TryGetInt32(out var de) ? de : 0;
            result.Add(new InstrumentStatistic(insCode, dataType, DEven.ToDateOrNull(dEven), value));
        }

        return result;
    }

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(n => text.Contains(n, StringComparison.Ordinal));

    [GeneratedRegex(@"\(\s*([^()\r\n]{1,24}?)\s*\)", RegexOptions.CultureInvariant)]
    private static partial Regex SymbolInParenthesesRegex();
}
