using Boors.Market.Domain;
using Boors.Market.RawCache;
using Boors.Market.Sources.Json;
using Boors.Market.Text;
using Microsoft.Extensions.Logging;

namespace Boors.Market.Sources.Funds;

/// <summary>One fund as reported by fipiran's comparison list (one row per fund, values as of <see cref="Date"/>).</summary>
public sealed record FundRecord
{
    public required string RegNo { get; init; }
    public long? InsCode { get; init; }
    public required string Name { get; init; }
    public string? Symbol { get; init; }

    /// <summary>fipiran fundType: 4 fixed income, 5 commodity (gold), 6 equity, 7 mixed, 13 project, 14 land and building,
    /// 17 fund of funds, 18 real estate, 21 sector equity, 22 leveraged, 23 index, 24 principal-guaranteed, ...</summary>
    public int FundType { get; init; }

    /// <summary>"Negotiable" = exchange-traded (ETF); "IssuanceAndCancellation" = issue/redeem only.</summary>
    public string? TypeOfInvest { get; init; }

    public DateOnly? Date { get; init; }
    public decimal? IssueNav { get; init; }
    public decimal? CancelNav { get; init; }
    public decimal? StatisticalNav { get; init; }
    public decimal? NetAsset { get; init; }
    public decimal? InvestedUnits { get; init; }
    public decimal? DailyReturn { get; init; }
    public decimal? WeeklyReturn { get; init; }
    public decimal? MonthlyReturn { get; init; }
    public decimal? QuarterlyReturn { get; init; }
    public decimal? SixMonthReturn { get; init; }
    public decimal? AnnualReturn { get; init; }
    public decimal? StockShare { get; init; }
    public decimal? BondShare { get; init; }
    public decimal? CashShare { get; init; }
    public decimal? DepositShare { get; init; }
    public decimal? OtherShare { get; init; }
    public string? Manager { get; init; }
    public DateOnly? InitiationDate { get; init; }

    public bool IsExchangeTraded => string.Equals(TypeOfInvest, "Negotiable", StringComparison.OrdinalIgnoreCase);

    /// <summary>Instrument class implied by the fund type, for ETFs; null when the type does not change the rules.</summary>
    public InstrumentClass? ImpliedClass => FundType switch
    {
        4 or 24 => InstrumentClass.EtfFixedIncome,
        5 => InstrumentClass.FundGoldSilver,
        6 or 21 or 23 => InstrumentClass.EtfEquity,
        7 => InstrumentClass.EtfMixed,
        13 or 14 or 18 => InstrumentClass.EtfRealEstate,
        17 => InstrumentClass.FundOfFunds,
        22 => InstrumentClass.FundLeveraged,
        _ => null,
    };
}

internal sealed record FundCompareEnvelope(List<FundCompareDto>? Items);

internal sealed record FundCompareDto
{
    public string? RegNo { get; init; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public long? InsCode { get; init; }
    public string? Name { get; init; }
    public string? SmallSymbolName { get; init; }
    public int FundType { get; init; }
    public string? TypeOfInvest { get; init; }
    public string? Date { get; init; }
    [JsonConverter(typeof(FlexibleDecimalConverter))] public decimal? IssueNav { get; init; }
    [JsonConverter(typeof(FlexibleDecimalConverter))] public decimal? CancelNav { get; init; }
    [JsonConverter(typeof(FlexibleDecimalConverter))] public decimal? StatisticalNav { get; init; }
    [JsonConverter(typeof(FlexibleDecimalConverter))] public decimal? NetAsset { get; init; }
    [JsonConverter(typeof(FlexibleDecimalConverter))] public decimal? InvestedUnits { get; init; }
    [JsonConverter(typeof(FlexibleDecimalConverter))] public decimal? DailyEfficiency { get; init; }
    [JsonConverter(typeof(FlexibleDecimalConverter))] public decimal? WeeklyEfficiency { get; init; }
    [JsonConverter(typeof(FlexibleDecimalConverter))] public decimal? MonthlyEfficiency { get; init; }
    [JsonConverter(typeof(FlexibleDecimalConverter))] public decimal? QuarterlyEfficiency { get; init; }
    [JsonConverter(typeof(FlexibleDecimalConverter))] public decimal? SixMonthEfficiency { get; init; }
    [JsonConverter(typeof(FlexibleDecimalConverter))] public decimal? AnnualEfficiency { get; init; }
    [JsonConverter(typeof(FlexibleDecimalConverter))] public decimal? Stock { get; init; }
    [JsonConverter(typeof(FlexibleDecimalConverter))] public decimal? Bond { get; init; }
    [JsonConverter(typeof(FlexibleDecimalConverter))] public decimal? Cash { get; init; }
    [JsonConverter(typeof(FlexibleDecimalConverter))] public decimal? Deposit { get; init; }
    [JsonConverter(typeof(FlexibleDecimalConverter))] public decimal? Other { get; init; }
    public string? Manager { get; init; }
    public string? InitiationDate { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FundCompareEnvelope))]
internal sealed partial class FipiranJsonContext : JsonSerializerContext;

/// <summary>fipiran.com fund data (base <c>https://www.fipiran.com/services/</c>; needs a fipiran Referer).</summary>
public sealed class FipiranClient(HttpClient http, IRawCache cache, TimeProvider timeProvider, ILogger<FipiranClient> logger)
    : RawSourceClient(http, cache, timeProvider, logger)
{
    public const string Source = "fipiran";

    private static readonly IReadOnlyDictionary<string, string> Headers = new Dictionary<string, string> { ["Referer"] = "https://www.fipiran.com/" };

    public static RawRequest FundCompareRequest() => new(Source, "fund/fundcompare", "https://www.fipiran.com/services/fund/fundcompare/", "regNos=[]&showMarketMakers=false")
    {
        JsonBody = """{"regNos":[],"showMarketMakers":false}""",
        Headers = Headers,
    };

    public async Task<IReadOnlyList<FundRecord>> GetFundsAsync(CachePolicy policy = CachePolicy.Network, CancellationToken cancellationToken = default) =>
        ParseFundCompare(await GetBodyAsync(FundCompareRequest(), policy, cancellationToken));

    public static IReadOnlyList<FundRecord> ParseFundCompare(ReadOnlySpan<byte> json)
    {
        var envelope = JsonSerializer.Deserialize(json, FipiranJsonContext.Default.FundCompareEnvelope);
        return (envelope?.Items ?? [])
            .Where(d => !string.IsNullOrWhiteSpace(d.RegNo))
            .Select(d => new FundRecord
            {
                RegNo = d.RegNo!.Trim(),
                InsCode = d.InsCode is > 0 ? d.InsCode : null,
                Name = PersianText.Normalize(d.Name) ?? d.RegNo!,
                Symbol = PersianText.Normalize(d.SmallSymbolName),
                FundType = d.FundType,
                TypeOfInvest = d.TypeOfInvest,
                Date = ParseDate(d.Date),
                IssueNav = d.IssueNav,
                CancelNav = d.CancelNav,
                StatisticalNav = d.StatisticalNav,
                NetAsset = d.NetAsset,
                InvestedUnits = d.InvestedUnits,
                DailyReturn = d.DailyEfficiency,
                WeeklyReturn = d.WeeklyEfficiency,
                MonthlyReturn = d.MonthlyEfficiency,
                QuarterlyReturn = d.QuarterlyEfficiency,
                SixMonthReturn = d.SixMonthEfficiency,
                AnnualReturn = d.AnnualEfficiency,
                StockShare = d.Stock,
                BondShare = d.Bond,
                CashShare = d.Cash,
                DepositShare = d.Deposit,
                OtherShare = d.Other,
                Manager = PersianText.Normalize(d.Manager),
                InitiationDate = ParseDate(d.InitiationDate),
            })
            .ToList();
    }

    /// <summary>fipiran dates are ISO strings, sometimes with an offset and sometimes "0001-01-01T00:00:00" for unknown.</summary>
    private static DateOnly? ParseDate(string? value) =>
        value is { Length: >= 10 } && DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date.Year >= 1900
            ? date
            : null;
}
