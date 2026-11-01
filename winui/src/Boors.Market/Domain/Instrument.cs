namespace Boors.Market.Domain;

/// <summary>
/// Instrument classes drive every class-dependent rule (price limit, close method, session).
/// Names map 1:1 to the snake_case keys in <c>rules/rules.yaml</c>.
/// </summary>
public enum InstrumentClass
{
    Other,
    TseStock,
    IfbStock,
    IfbBaseYellow,
    IfbBaseOrange,
    IfbBaseRed,
    Rights,
    EtfEquity,
    EtfFixedIncome,
    EtfRealEstate,
    EtfMixed,
    FundLeveraged,
    FundGoldSilver,
    FundOfFunds,
    Option,
    Debt,
}

/// <summary>An instrument in the master list. <see cref="InsCode"/> is the join key everywhere; symbol names never are.</summary>
public sealed record Instrument
{
    public required long InsCode { get; init; }

    /// <summary>Short Persian ticker (lVal18AFC / lva), normalized (Persian ی/ک).</summary>
    public required string Symbol { get; init; }

    /// <summary>Persian name (lVal30 / lvc), normalized.</summary>
    public string? Name { get; init; }

    public string? LatinName { get; init; }

    /// <summary>Instrument ISIN (instrumentID / insID), e.g. IRO1FOLD0001.</summary>
    public string? Isin { get; init; }

    /// <summary>Company ISIN (cIsin), e.g. IRO1FOLD0009.</summary>
    public string? CompanyIsin { get; init; }

    /// <summary>TSETMC instrument type code (yVal), e.g. 300 TSE share, 303 IFB share, 305 ETF, 320 option, 208 sukuk, 400 rights.</summary>
    public int? YVal { get; init; }

    /// <summary>Market code (flow): 1 TSE, 2 IFB, 3 derivatives.</summary>
    public int? Flow { get; init; }

    /// <summary>Board code (cgrValCot), e.g. N1, N2, Z1, H1.</summary>
    public string? Board { get; init; }

    public string? BoardTitle { get; init; }

    /// <summary>Two-character sector code (cSecVal, trimmed), e.g. "27" basic metals.</summary>
    public string? SectorCode { get; init; }

    public string? SectorName { get; init; }

    public int? SubSectorCode { get; init; }

    public string? SubSectorName { get; init; }

    public long? BaseVolume { get; init; }

    public decimal? TotalShares { get; init; }

    /// <summary>kAjCapValCpsIdx; probably the free-float percentage (unverified).</summary>
    public decimal? FreeFloatPercent { get; init; }

    public InstrumentClass Class { get; init; }

    /// <summary>
    /// For a secondary listing of a stock (TAL closing-price board, block board; ISIN not ending in 0001), the primary
    /// listing's InsCode. Secondary listings mirror the parent's price and are excluded from history collection.
    /// Inferred rule: stock ISINs ending in 0001 are primary listings (true for every sample checked on 2026-10-17).
    /// </summary>
    public long? ParentInsCode { get; init; }
}

/// <summary>
/// Derives <see cref="InstrumentClass"/> from TSETMC codes. yVal is authoritative; the ISIN prefix is a fallback
/// for rows (such as market-watch rows) that carry no yVal. ETF sub-classes need the fipiran fund type (Phase 1),
/// so ETFs default to <see cref="InstrumentClass.EtfEquity"/> until refined.
/// </summary>
public static class InstrumentClassifier
{
    public static InstrumentClass Classify(int? yVal, int? flow, string? isin)
    {
        // Verified live: 300, 303, 305, 320, 208, 400. From search snippets / tse-client only: 301, 309, 403, 404.
        if (yVal is { } y)
        {
            switch (y)
            {
                case 300: return InstrumentClass.TseStock;
                case 303: return InstrumentClass.IfbStock;
                case 309: return InstrumentClass.IfbBaseYellow; // board colour not yet resolved; yellow is the widest base-market limit
                case 400 or 403 or 404: return InstrumentClass.Rights;
                case 305: return InstrumentClass.EtfEquity;
                case 320: return InstrumentClass.Option;
                case 208 or 301: return InstrumentClass.Debt;
            }
        }

        return ClassifyByIsin(isin, flow);
    }

    public static InstrumentClass ClassifyByIsin(string? isin, int? flow = null)
    {
        if (string.IsNullOrWhiteSpace(isin) || isin.Length < 4)
            return InstrumentClass.Other;

        var prefix = isin[..4].ToUpperInvariant();
        return prefix switch
        {
            "IRO1" => InstrumentClass.TseStock,
            "IRO3" => InstrumentClass.IfbStock,
            "IRR1" or "IRR3" => InstrumentClass.Rights,
            "IRT1" or "IRT3" => InstrumentClass.EtfEquity,
            "IRO9" or "IROA" or "IROB" or "IROF" => InstrumentClass.Option,
            _ when prefix.StartsWith("IRB", StringComparison.Ordinal) => InstrumentClass.Debt,
            _ => flow == 3 ? InstrumentClass.Option : InstrumentClass.Other,
        };
    }

    public static InstrumentClass FromRuleKey(string key) =>
        Enum.GetValues<InstrumentClass>().FirstOrDefault(c => c.ToRuleKey() == key);

    /// <summary>The snake_case key used for this class in rules.yaml.</summary>
    public static string ToRuleKey(this InstrumentClass instrumentClass) => instrumentClass switch
    {
        InstrumentClass.TseStock => "tse_stock",
        InstrumentClass.IfbStock => "ifb_stock",
        InstrumentClass.IfbBaseYellow => "ifb_base_yellow",
        InstrumentClass.IfbBaseOrange => "ifb_base_orange",
        InstrumentClass.IfbBaseRed => "ifb_base_red",
        InstrumentClass.Rights => "rights",
        InstrumentClass.EtfEquity => "etf_equity",
        InstrumentClass.EtfFixedIncome => "etf_fixed_income",
        InstrumentClass.EtfRealEstate => "etf_real_estate",
        InstrumentClass.EtfMixed => "etf_mixed",
        InstrumentClass.FundLeveraged => "fund_leveraged",
        InstrumentClass.FundGoldSilver => "fund_gold_silver",
        InstrumentClass.FundOfFunds => "fund_of_funds",
        InstrumentClass.Option => "option",
        InstrumentClass.Debt => "debt",
        _ => "other",
    };
}
