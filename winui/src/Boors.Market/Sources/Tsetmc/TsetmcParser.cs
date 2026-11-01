using System.Text.Json.Serialization.Metadata;
using Boors.Market.Domain;
using Boors.Market.Text;

namespace Boors.Market.Sources.Tsetmc;

public sealed record InstrumentSearchHit
{
    public required long InsCode { get; init; }
    public required string Symbol { get; init; }
    public string? Name { get; init; }
    public int Flow { get; init; }
    public string? FlowTitle { get; init; }
    public string? Board { get; init; }

    /// <summary>TSETMC marks the currently active instrument with lastDate = 1; older listings of the same name have 0.</summary>
    public bool IsActive { get; init; }

    public IReadOnlyList<long> AlternateInsCodes { get; init; } = [];
}

public sealed record MarketWatchSnapshot(IReadOnlyList<MarketWatchRow> Rows)
{
    /// <summary>Largest hEven in the snapshot; pass back as <c>hEven</c> to poll incrementally.</summary>
    public int MaxHEven => Rows.Count == 0 ? 0 : Rows.Max(r => r.HEven);
}

/// <summary>Pure functions from TSETMC JSON bodies to domain records. No I/O; safe to replay over the raw cache.</summary>
public static partial class TsetmcParser
{
    public static IReadOnlyList<DailyBar> ParseDailyHistory(ReadOnlySpan<byte> json, long insCode)
    {
        var envelope = Deserialize(json, TsetmcJsonContext.Default.ClosingPriceDailyEnvelope);
        return (envelope?.ClosingPriceDaily ?? [])
            .Where(d => d.DEven > 0)
            .Select(d => ToDailyBar(d, d.InsCode is > 0 ? d.InsCode.Value : insCode))
            .OrderBy(b => b.Date)
            .ToList();
    }

    /// <summary>Bars of every instrument that traded on <paramref name="date"/> (GetInstrmentsHistoryInDay).</summary>
    public static IReadOnlyList<DailyBar> ParseInstrumentsHistoryInDay(ReadOnlySpan<byte> json, DateOnly date)
    {
        var envelope = Deserialize(json, TsetmcJsonContext.Default.InstrumentsHistoryInDayEnvelope);
        return (envelope?.ClosingPriceDailyHistoryWithInstDetails ?? [])
            .Where(d => d.InsCode is > 0)
            .Select(d => ToDailyBar(d, d.InsCode!.Value, date))
            .ToList();
    }

    public static ClosingPriceSnapshot ParseClosingPriceInfo(ReadOnlySpan<byte> json, long insCode)
    {
        var dto = Deserialize(json, TsetmcJsonContext.Default.ClosingPriceInfoEnvelope)?.ClosingPriceInfo
            ?? throw new FormatException("closingPriceInfo is missing.");

        return new ClosingPriceSnapshot
        {
            InsCode = insCode,
            Date = DEven.ToDateOrNull(dto.DEven),
            Time = DEven.ToTime(dto.HEven),
            Last = dto.PDrCotVal,
            Close = dto.PClosing,
            Open = dto.PriceFirst,
            Low = dto.PriceMin,
            High = dto.PriceMax,
            Yesterday = dto.PriceYesterday,
            TradeCount = (long)dto.ZTotTran,
            Volume = (long)dto.QTotTran5J,
            Value = dto.QTotCap,
            State = dto.InstrumentState?.CEtaval?.Trim(),
            StateTitle = PersianText.Normalize(dto.InstrumentState?.CEtavalTitle),
        };
    }

    public static IReadOnlyList<ClientTypeDay> ParseClientTypeHistory(ReadOnlySpan<byte> json, long insCode)
    {
        var envelope = Deserialize(json, TsetmcJsonContext.Default.ClientTypeHistoryEnvelope);
        return (envelope?.ClientType ?? [])
            .Where(d => d.RecDate > 0)
            .Select(d => new ClientTypeDay
            {
                InsCode = d.InsCode is > 0 ? d.InsCode.Value : insCode,
                Date = DEven.ToDate(d.RecDate),
                BuyIndividualVolume = (long)d.BuyIVolume,
                BuyInstitutionalVolume = (long)d.BuyNVolume,
                BuyIndividualValue = d.BuyIValue,
                BuyInstitutionalValue = d.BuyNValue,
                BuyIndividualCount = (int)d.BuyICount,
                BuyInstitutionalCount = (int)d.BuyNCount,
                SellIndividualVolume = (long)d.SellIVolume,
                SellInstitutionalVolume = (long)d.SellNVolume,
                SellIndividualValue = d.SellIValue,
                SellInstitutionalValue = d.SellNValue,
                SellIndividualCount = (int)d.SellICount,
                SellInstitutionalCount = (int)d.SellNCount,
            })
            .OrderBy(d => d.Date)
            .ToList();
    }

    public static IReadOnlyList<PriceAdjustment> ParsePriceAdjustments(ReadOnlySpan<byte> json, long insCode)
    {
        var envelope = Deserialize(json, TsetmcJsonContext.Default.PriceAdjustEnvelope);
        return (envelope?.PriceAdjust ?? [])
            .Where(d => d.DEven > 0)
            .Select(d => new PriceAdjustment
            {
                InsCode = d.InsCode is > 0 ? d.InsCode.Value : insCode,
                Date = DEven.ToDate(d.DEven),
                AdjustedClose = d.PClosing,
                UnadjustedClose = d.PClosingNotAdjusted,
            })
            .OrderBy(d => d.Date)
            .ToList();
    }

    public static IReadOnlyList<ShareChange> ParseShareChanges(ReadOnlySpan<byte> json, long insCode)
    {
        var envelope = Deserialize(json, TsetmcJsonContext.Default.ShareChangeEnvelope);
        return (envelope?.InstrumentShareChange ?? [])
            .Where(d => d.DEven > 0)
            .Select(d => new ShareChange
            {
                InsCode = d.InsCode is > 0 ? d.InsCode.Value : insCode,
                Date = DEven.ToDate(d.DEven),
                SharesBefore = d.NumberOfShareOld,
                SharesAfter = d.NumberOfShareNew,
            })
            .OrderBy(d => d.Date)
            .ToList();
    }

    public static IReadOnlyList<IndexBar> ParseIndexHistory(ReadOnlySpan<byte> json, long insCode)
    {
        var envelope = Deserialize(json, TsetmcJsonContext.Default.IndexB2Envelope);
        return (envelope?.IndexB2 ?? [])
            .Where(d => d.DEven > 0)
            .Select(d => new IndexBar
            {
                InsCode = d.InsCode is > 0 ? d.InsCode.Value : insCode,
                Date = DEven.ToDate(d.DEven),
                Close = d.XNivInuClMresIbs,
                Low = d.XNivInuPbMresIbs,
                High = d.XNivInuPhMresIbs,
            })
            .OrderBy(d => d.Date)
            .ToList();
    }

    public static IReadOnlyList<BestLimitLevel> ParseBestLimits(ReadOnlySpan<byte> json)
    {
        var envelope = Deserialize(json, TsetmcJsonContext.Default.BestLimitsEnvelope);
        return (envelope?.BestLimits ?? [])
            .Select(d => new BestLimitLevel
            {
                Level = d.Number,
                BidPrice = d.PMeDem,
                BidVolume = (long)d.QTitMeDem,
                BidOrders = (int)d.ZOrdMeDem,
                AskPrice = d.PMeOf,
                AskVolume = (long)d.QTitMeOf,
                AskOrders = (int)d.ZOrdMeOf,
            })
            .OrderBy(l => l.Level)
            .ToList();
    }

    /// <summary>Parses GetInstrumentInfo (or GetInstrumentIdentity) into an <see cref="Instrument"/>.</summary>
    public static Instrument ParseInstrumentInfo(ReadOnlySpan<byte> json, long insCode)
    {
        var info = Deserialize(json, TsetmcJsonContext.Default.InstrumentInfoEnvelope)?.InstrumentInfo
            ?? Deserialize(json, TsetmcJsonContext.Default.InstrumentIdentityEnvelope)?.InstrumentIdentity
            ?? throw new FormatException("instrumentInfo / instrumentIdentity is missing.");

        int? yVal = int.TryParse(info.YVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) ? y : null;
        int? flow = info.Flow > 0 ? info.Flow : null;
        var isin = info.InstrumentID?.Trim();

        return new Instrument
        {
            InsCode = info.InsCode is > 0 ? info.InsCode.Value : insCode,
            Symbol = PersianText.Normalize(info.LVal18AFC) ?? insCode.ToString(CultureInfo.InvariantCulture),
            Name = PersianText.Normalize(info.LVal30),
            LatinName = info.LVal18?.Trim(),
            Isin = isin,
            CompanyIsin = info.CIsin?.Trim(),
            YVal = yVal,
            Flow = flow,
            Board = info.CgrValCot?.Trim(),
            BoardTitle = PersianText.Normalize(info.CgrValCotTitle),
            SectorCode = info.Sector?.CSecVal?.Trim(),
            SectorName = PersianText.Normalize(info.Sector?.LSecVal),
            SubSectorCode = info.SubSector?.CSoSecVal,
            SubSectorName = PersianText.Normalize(info.SubSector?.LSoSecVal),
            BaseVolume = info.BaseVol > 0 ? (long)info.BaseVol : null,
            TotalShares = info.ZTitad > 0 ? info.ZTitad : null,
            FreeFloatPercent = info.KAjCapValCpsIdx,
            Class = InstrumentClassifier.Classify(yVal, flow, isin),
        };
    }

    /// <summary>Allowed price band for the current day from GetInstrumentInfo's staticThreshold, if present.</summary>
    public static (decimal Low, decimal High)? ParseStaticThreshold(ReadOnlySpan<byte> json)
    {
        var threshold = Deserialize(json, TsetmcJsonContext.Default.InstrumentInfoEnvelope)?.InstrumentInfo?.StaticThreshold;
        return threshold is { PsGelStaMax: > 0 } t ? (t.PsGelStaMin, t.PsGelStaMax) : null;
    }

    public static IReadOnlyList<InstrumentSearchHit> ParseInstrumentSearch(ReadOnlySpan<byte> json)
    {
        var envelope = Deserialize(json, TsetmcJsonContext.Default.InstrumentSearchEnvelope);
        return (envelope?.InstrumentSearch ?? [])
            .Where(d => d.InsCode is > 0)
            .Select(d => new InstrumentSearchHit
            {
                InsCode = d.InsCode!.Value,
                Symbol = PersianText.Normalize(d.LVal18AFC) ?? string.Empty,
                Name = PersianText.Normalize(d.LVal30),
                Flow = d.Flow,
                FlowTitle = PersianText.Normalize(d.FlowTitle),
                Board = d.CgrValCot?.Trim(),
                IsActive = d.LastDate == 1,
                AlternateInsCodes = new[] { d.InsCode2, d.InsCode3, d.InsCode4 }
                    .Where(c => c is > 0 && c != d.InsCode)
                    .Select(c => c!.Value)
                    .Distinct()
                    .ToList(),
            })
            .ToList();
    }

    public static IReadOnlyList<StaticDataItem> ParseStaticData(ReadOnlySpan<byte> json)
    {
        var envelope = Deserialize(json, TsetmcJsonContext.Default.StaticDataEnvelope);
        return (envelope?.StaticData ?? [])
            .Select(d => new StaticDataItem(d.Id, d.Code, d.Type?.Trim() ?? string.Empty, PersianText.Normalize(d.Name) ?? string.Empty, PersianText.Normalize(d.Description)))
            .ToList();
    }

    public static MarketWatchSnapshot ParseMarketWatch(ReadOnlySpan<byte> json)
    {
        var envelope = Deserialize(json, TsetmcJsonContext.Default.MarketWatchEnvelope);
        var rows = (envelope?.Marketwatch ?? [])
            .Where(d => d.InsCode is > 0)
            .Select(d => new MarketWatchRow
            {
                InsCode = d.InsCode!.Value,
                Symbol = PersianText.Normalize(d.Lva) ?? string.Empty,
                Name = PersianText.Normalize(d.Lvc),
                Isin = d.InsId?.Trim(),
                SectorCode = d.Csv?.Trim(),
                Last = d.Pdv,
                Close = d.Pcl,
                Open = d.Pf,
                Low = d.Pmn,
                High = d.Pmx,
                Yesterday = d.Py,
                AllowedHigh = d.PMax,
                AllowedLow = d.PMin,
                TradeCount = (long)d.Ztt,
                Volume = (long)d.Qtj,
                Value = d.Qtc,
                BaseVolume = (long)d.Bv,
                TotalShares = d.Ztd,
                Eps = d.Eps,
                PriceEarnings = d.Pe,
                VisitCount = d.Vc,
                HEven = d.HEven,
                YVal = d.YVal,
                Flow = d.Flow,
                Board = d.CGrValCot?.Trim(),
                LastTradeTime = DEven.ToTime(d.HEven),
                BestLimits = (d.BlDs ?? [])
                    .Select(l => new BestLimitLevel
                    {
                        Level = l.N,
                        BidPrice = l.Pmd,
                        BidVolume = (long)l.Qmd,
                        BidOrders = (int)l.Zmd,
                        AskPrice = l.Pmo,
                        AskVolume = (long)l.Qmo,
                        AskOrders = (int)l.Zmo,
                    })
                    .OrderBy(l => l.Level)
                    .ToList(),
            })
            .ToList();

        return new MarketWatchSnapshot(rows);
    }

    private static DailyBar ToDailyBar(ClosingPriceDto d, long insCode, DateOnly? date = null) => new()
    {
        InsCode = insCode,
        Date = date ?? DEven.ToDate(d.DEven),
        Open = d.PriceFirst,
        High = d.PriceMax,
        Low = d.PriceMin,
        Close = d.PClosing,
        Last = d.PDrCotVal,
        Yesterday = d.PriceYesterday,
        TradeCount = (long)d.ZTotTran,
        Volume = (long)d.QTotTran5J,
        Value = d.QTotCap,
        LastTradeTime = DEven.ToTime(d.HEven),
    };

    private static T? Deserialize<T>(ReadOnlySpan<byte> json, JsonTypeInfo<T> typeInfo) where T : class =>
        json.IsEmpty ? null : JsonSerializer.Deserialize(json, typeInfo);
}
