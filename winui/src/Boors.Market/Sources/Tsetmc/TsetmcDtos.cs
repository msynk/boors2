using Boors.Market.Sources.Json;

namespace Boors.Market.Sources.Tsetmc;

// Wire-format mirrors of cdn.tsetmc.com/api responses. Property names follow the JSON (camelCase policy) so the
// mapping stays mechanical; meaning is assigned in TsetmcParser. Numbers are decimals because TSETMC sends integral
// quantities as 123.0, and insCode as either a string or a number (sometimes beyond 2^53), hence AllowReadingFromString.

internal sealed record ClosingPriceDailyEnvelope(List<ClosingPriceDto>? ClosingPriceDaily);

internal sealed record ClosingPriceInfoEnvelope(ClosingPriceDto? ClosingPriceInfo);

// Rows also carry instrumentID, lVal18AFC and lVal30 (ISIN, symbol, name); the instrument master comes from elsewhere.
internal sealed record InstrumentsHistoryInDayEnvelope(List<ClosingPriceDto>? ClosingPriceDailyHistoryWithInstDetails);

internal sealed record ClosingPriceDto
{
    public decimal PriceChange { get; init; }
    public decimal PriceMin { get; init; }
    public decimal PriceMax { get; init; }
    public decimal PriceYesterday { get; init; }
    public decimal PriceFirst { get; init; }
    public bool Last { get; init; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? InsCode { get; init; }

    public int DEven { get; init; }
    public int HEven { get; init; }
    public decimal PClosing { get; init; }
    public bool IClose { get; init; }
    public bool YClose { get; init; }
    public decimal PDrCotVal { get; init; }
    public decimal ZTotTran { get; init; }
    public decimal QTotTran5J { get; init; }
    public decimal QTotCap { get; init; }
    public InstrumentStateDto? InstrumentState { get; init; }
}

internal sealed record InstrumentStateDto
{
    public int HEven { get; init; }
    public int RealHeven { get; init; }
    public string? CEtaval { get; init; }
    public string? CEtavalTitle { get; init; }
    public int UnderSupervision { get; init; }
}

internal sealed record ClientTypeHistoryEnvelope(List<ClientTypeDto>? ClientType);

internal sealed record ClientTypeDto
{
    public int RecDate { get; init; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? InsCode { get; init; }

    [JsonPropertyName("buy_I_Volume")] public decimal BuyIVolume { get; init; }
    [JsonPropertyName("buy_N_Volume")] public decimal BuyNVolume { get; init; }
    [JsonPropertyName("buy_I_Value")] public decimal BuyIValue { get; init; }
    [JsonPropertyName("buy_N_Value")] public decimal BuyNValue { get; init; }
    [JsonPropertyName("buy_I_Count")] public decimal BuyICount { get; init; }
    [JsonPropertyName("buy_N_Count")] public decimal BuyNCount { get; init; }
    [JsonPropertyName("sell_I_Volume")] public decimal SellIVolume { get; init; }
    [JsonPropertyName("sell_N_Volume")] public decimal SellNVolume { get; init; }
    [JsonPropertyName("sell_I_Value")] public decimal SellIValue { get; init; }
    [JsonPropertyName("sell_N_Value")] public decimal SellNValue { get; init; }
    [JsonPropertyName("sell_I_Count")] public decimal SellICount { get; init; }
    [JsonPropertyName("sell_N_Count")] public decimal SellNCount { get; init; }
}

internal sealed record PriceAdjustEnvelope(List<PriceAdjustDto>? PriceAdjust);

internal sealed record PriceAdjustDto
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? InsCode { get; init; }

    public int DEven { get; init; }
    public decimal PClosing { get; init; }
    public decimal PClosingNotAdjusted { get; init; }
}

internal sealed record ShareChangeEnvelope(List<ShareChangeDto>? InstrumentShareChange);

internal sealed record ShareChangeDto
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? InsCode { get; init; }

    public int DEven { get; init; }
    public decimal NumberOfShareNew { get; init; }
    public decimal NumberOfShareOld { get; init; }
}

internal sealed record IndexB2Envelope(List<IndexB2Dto>? IndexB2);

internal sealed record IndexB2Dto
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? InsCode { get; init; }

    public int DEven { get; init; }

    /// <summary>Close.</summary>
    public decimal XNivInuClMresIbs { get; init; }

    /// <summary>Low.</summary>
    public decimal XNivInuPbMresIbs { get; init; }

    /// <summary>High.</summary>
    public decimal XNivInuPhMresIbs { get; init; }
}

internal sealed record BestLimitsEnvelope(List<BestLimitDto>? BestLimits);

internal sealed record BestLimitDto
{
    public int Number { get; init; }
    public decimal QTitMeDem { get; init; }
    public decimal ZOrdMeDem { get; init; }
    public decimal PMeDem { get; init; }
    public decimal PMeOf { get; init; }
    public decimal ZOrdMeOf { get; init; }
    public decimal QTitMeOf { get; init; }
}

internal sealed record InstrumentInfoEnvelope(InstrumentInfoDto? InstrumentInfo);

internal sealed record InstrumentIdentityEnvelope(InstrumentInfoDto? InstrumentIdentity);

internal sealed record InstrumentInfoDto
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? InsCode { get; init; }

    public string? LVal18 { get; init; }
    public string? LVal18AFC { get; init; }
    public string? LVal30 { get; init; }
    public string? YVal { get; init; }
    public int Flow { get; init; }
    public string? FlowTitle { get; init; }
    public string? CIsin { get; init; }
    public string? InstrumentID { get; init; }
    public string? CgrValCot { get; init; }
    public string? CgrValCotTitle { get; init; }
    public decimal ZTitad { get; init; }
    public decimal BaseVol { get; init; }

    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal? KAjCapValCpsIdx { get; init; }

    public SectorDto? Sector { get; init; }
    public SubSectorDto? SubSector { get; init; }
    public StaticThresholdDto? StaticThreshold { get; init; }
    public int LastDate { get; init; }
}

internal sealed record SectorDto(string? CSecVal, string? LSecVal);

internal sealed record SubSectorDto(int? CSoSecVal, string? LSoSecVal);

internal sealed record StaticThresholdDto(decimal PsGelStaMax, decimal PsGelStaMin);

internal sealed record InstrumentSearchEnvelope(List<InstrumentSearchDto>? InstrumentSearch);

internal sealed record InstrumentSearchDto
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? InsCode { get; init; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? InsCode2 { get; init; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? InsCode3 { get; init; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? InsCode4 { get; init; }

    public string? LVal18AFC { get; init; }
    public string? LVal30 { get; init; }
    public int Flow { get; init; }
    public string? FlowTitle { get; init; }
    public string? CgrValCot { get; init; }
    public string? CgrValCotTitle { get; init; }
    public int LastDate { get; init; }
    public int SourceID { get; init; }
}

internal sealed record StaticDataEnvelope(List<StaticDataDto>? StaticData);

internal sealed record StaticDataDto(int Id, int Code, string? Type, string? Name, string? Description);

internal sealed record MarketWatchEnvelope(List<MarketWatchDto>? Marketwatch);

/// <summary>Market-watch row with TSETMC's compact keys; see <see cref="Domain.MarketWatchRow"/> for meanings.</summary>
internal sealed record MarketWatchDto
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? InsCode { get; init; }

    public string? Lva { get; init; }
    public string? Lvc { get; init; }

    [JsonPropertyName("insID")] public string? InsId { get; init; }

    public string? Csv { get; init; }

    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal? Eps { get; init; }

    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal? Pe { get; init; }

    public decimal Pmd { get; init; }
    public decimal Pmo { get; init; }
    public decimal Qtj { get; init; }
    public decimal Pdv { get; init; }
    public decimal Ztt { get; init; }
    public decimal Qtc { get; init; }
    public decimal Bv { get; init; }
    public decimal Pc { get; init; }
    public decimal Pcpc { get; init; }
    public decimal Pmn { get; init; }
    public decimal Pmx { get; init; }
    public decimal Py { get; init; }
    public decimal Pf { get; init; }
    public decimal Pcl { get; init; }
    public int Vc { get; init; }

    [JsonPropertyName("pMax")] public decimal PMax { get; init; }
    [JsonPropertyName("pMin")] public decimal PMin { get; init; }

    public decimal Ztd { get; init; }
    public int HEven { get; init; }
    public List<MarketWatchBestLimitDto>? BlDs { get; init; }

    // Present only in the "extended" response variant (see MarketWatchRow.YVal).
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? YVal { get; init; }

    public int? Flow { get; init; }

    [JsonPropertyName("cGrValCot")] public string? CGrValCot { get; init; }
}

internal sealed record MarketWatchBestLimitDto
{
    public int N { get; init; }
    public decimal Qmd { get; init; }
    public decimal Zmd { get; init; }
    public decimal Pmd { get; init; }
    public decimal Pmo { get; init; }
    public decimal Zmo { get; init; }
    public decimal Qmo { get; init; }
    public long Rid { get; init; }
}

internal sealed record ClientTypeAllEnvelope(List<ClientTypeAllDto>? ClientTypeAllDto);

/// <summary>Today's flows for every instrument (volumes and counts only, no rial values).</summary>
internal sealed record ClientTypeAllDto
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? InsCode { get; init; }

    [JsonPropertyName("buy_I_Volume")] public decimal BuyIVolume { get; init; }
    [JsonPropertyName("buy_N_Volume")] public decimal BuyNVolume { get; init; }
    [JsonPropertyName("buy_CountI")] public decimal BuyCountI { get; init; }
    [JsonPropertyName("buy_CountN")] public decimal BuyCountN { get; init; }
    [JsonPropertyName("sell_I_Volume")] public decimal SellIVolume { get; init; }
    [JsonPropertyName("sell_N_Volume")] public decimal SellNVolume { get; init; }
    [JsonPropertyName("sell_CountI")] public decimal SellCountI { get; init; }
    [JsonPropertyName("sell_CountN")] public decimal SellCountN { get; init; }
}

internal sealed record IndexB1Envelope(List<IndexB1Dto>? IndexB1);

internal sealed record IndexB1Dto
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? InsCode { get; init; }

    public int DEven { get; init; }
    public int HEven { get; init; }
    public string? LVal30 { get; init; }

    /// <summary>Last value.</summary>
    public decimal XDrNivJIdx004 { get; init; }

    /// <summary>High.</summary>
    public decimal XPhNivJIdx004 { get; init; }

    /// <summary>Low.</summary>
    public decimal XPbNivJIdx004 { get; init; }

    /// <summary>Percent change.</summary>
    public decimal XVarIdxJRfV { get; init; }

    public decimal IndexChange { get; init; }
}

internal sealed record MessageEnvelope(List<MessageDto>? Msg);

internal sealed record MessageDto
{
    public long TseMsgIdn { get; init; }
    public int DEven { get; init; }
    public int HEven { get; init; }
    public string? TseTitle { get; init; }
    public string? TseDesc { get; init; }
    public int Flow { get; init; }
}

internal sealed record ShareholderEnvelope(List<ShareholderDto>? ShareHolder);

internal sealed record ShareholderDto
{
    public long ShareHolderID { get; init; }
    public string? ShareHolderName { get; init; }
    public string? CIsin { get; init; }
    public int DEven { get; init; }
    public decimal NumberOfShares { get; init; }
    public decimal PerOfShares { get; init; }
    public int Change { get; init; }
    public decimal ChangeAmount { get; init; }
    public long ShareHolderShareID { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ClosingPriceDailyEnvelope))]
[JsonSerializable(typeof(InstrumentsHistoryInDayEnvelope))]
[JsonSerializable(typeof(ClosingPriceInfoEnvelope))]
[JsonSerializable(typeof(ClientTypeHistoryEnvelope))]
[JsonSerializable(typeof(PriceAdjustEnvelope))]
[JsonSerializable(typeof(ShareChangeEnvelope))]
[JsonSerializable(typeof(IndexB2Envelope))]
[JsonSerializable(typeof(BestLimitsEnvelope))]
[JsonSerializable(typeof(InstrumentInfoEnvelope))]
[JsonSerializable(typeof(InstrumentIdentityEnvelope))]
[JsonSerializable(typeof(InstrumentSearchEnvelope))]
[JsonSerializable(typeof(StaticDataEnvelope))]
[JsonSerializable(typeof(MarketWatchEnvelope))]
[JsonSerializable(typeof(ClientTypeAllEnvelope))]
[JsonSerializable(typeof(IndexB1Envelope))]
[JsonSerializable(typeof(MessageEnvelope))]
[JsonSerializable(typeof(ShareholderEnvelope))]
internal sealed partial class TsetmcJsonContext : JsonSerializerContext;
