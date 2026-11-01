using System.Data;
using Boors.Market.Domain;
using Boors.Market.Filters;
using Boors.Market.Storage;
using DuckDB.NET.Data;

namespace Boors.Market.Analytics;

/// <summary>One row of the market-watch grid (دیده‌بان) for a session.</summary>
public sealed record MarketWatchEntry
{
    public required long InsCode { get; init; }
    public required string Symbol { get; init; }
    public string? Name { get; init; }
    public required string InstrumentClass { get; init; }
    public string? SectorCode { get; init; }
    public string? DayStatus { get; init; }
    public decimal Close { get; init; }
    public double? ChangePercent { get; init; }
    public decimal Last { get; init; }
    public double? LastChangePercent { get; init; }
    public long Volume { get; init; }
    public decimal Value { get; init; }
    public long Trades { get; init; }
    public decimal? AllowedLow { get; init; }
    public decimal? AllowedHigh { get; init; }

    /// <summary>"buy" or "sell" when the close snapshot shows a queue at the band, otherwise null.</summary>
    public string? Queue { get; init; }

    public decimal? QueueValue { get; init; }
    public decimal? PriceEarnings { get; init; }
    public decimal? MarketCap { get; init; }
    public decimal? IndividualNetValue { get; init; }
    public decimal? PerCapitaBuy { get; init; }
    public decimal? PerCapitaSell { get; init; }
    public decimal? BuyerPower { get; init; }

    /// <summary>Share of the day's volume bought by individuals (0–1).</summary>
    public double? IndividualBuyShare { get; init; }
}

public sealed record MarketWatchRequest
{
    public DateOnly? Session { get; init; }

    /// <summary>A TSETMC filter expression, e.g. <c>(pc)>(py) &amp;&amp; (ct).Buy_I_Volume>(ct).Sell_I_Volume</c>.</summary>
    public string? Filter { get; init; }

    /// <summary>Rules-class keys to include (null = all); secondary listings are always excluded.</summary>
    public IReadOnlySet<string>? Classes { get; init; }

    public bool TradedOnly { get; init; }
}

public sealed record MarketWatchResult(DateOnly? Session, IReadOnlyList<MarketWatchEntry> Entries, FilterResult? Filter);

/// <summary>Builds the market watch for a session from the store and applies TSETMC filters to it.</summary>
public sealed class MarketWatchService(MarketStore store, MarketAnalytics analytics)
{
    public MarketWatchResult Query(MarketWatchRequest request, CancellationToken cancellationToken = default)
    {
        if ((request.Session ?? analytics.LatestSession()) is not { } session)
            return new MarketWatchResult(null, [], null);

        var filter = string.IsNullOrWhiteSpace(request.Filter) ? null : TsetmcFilter.Compile(request.Filter);
        var rows = LoadRows(session, filter?.UsesHistory ?? false, filter?.StatisticTypes ?? [])
            .Where(r => r.ParentInsCode is null)
            .Where(r => request.Classes is null || request.Classes.Contains(r.Entry.InstrumentClass))
            .Where(r => !request.TradedOnly || r.Entry.Trades > 0)
            .ToList();

        FilterResult? filterResult = null;
        if (filter is not null)
        {
            filterResult = filter.Apply(rows.Select(r => r.Filter), cancellationToken);
            var matches = filterResult.Matches.ToHashSet();
            rows = rows.Where(r => matches.Contains(r.Entry.InsCode)).ToList();
        }

        return new MarketWatchResult(session, rows.Select(r => r.Entry).OrderByDescending(e => e.Value).ToList(), filterResult);
    }

    /// <summary>The variables a filter sees for every instrument of the session (exposed for tests and the CLI).</summary>
    public IReadOnlyList<FilterRow> LoadFilterRows(DateOnly session, bool includeHistory, IReadOnlyList<int> statistics) =>
        LoadRows(session, includeHistory, statistics).Select(r => r.Filter).ToList();

    private List<(MarketWatchEntry Entry, FilterRow Filter, long? ParentInsCode)> LoadRows(DateOnly session, bool includeHistory, IReadOnlyList<int> statistics)
    {
        var history = includeHistory ? LoadHistory(session) : new Dictionary<long, List<DailyBar>>();
        var stats = statistics.Count > 0 ? LoadStatistics(statistics) : new Dictionary<long, Dictionary<int, double>>();

        return store.Query("""
            SELECT i.ins_code, i.symbol, i.name, i.instrument_class, i.sector_code, i.total_shares, i.base_volume, i.parent_ins_code,
                   b.open, b.high, b.low, b.close, b.last, b.yesterday, b.trade_count, b.volume, b.value, b.day_status,
                   b.eps, b.pe, b.allowed_low, b.allowed_high, b.bid1_price, b.bid1_volume, b.bid1_orders, b.ask1_price, b.ask1_volume, b.ask1_orders,
                   c.buy_i_volume, c.buy_n_volume, c.sell_i_volume, c.sell_n_volume, c.buy_i_value, c.buy_n_value, c.sell_i_value, c.sell_n_value,
                   c.buy_i_count, c.buy_n_count, c.sell_i_count, c.sell_n_count, c.values_estimated
            FROM daily_bar b JOIN instrument i USING (ins_code)
            LEFT JOIN client_type_daily c ON c.ins_code = b.ins_code AND c.d = b.d
            WHERE b.d = $d
            """, [("d", session)], r =>
        {
            var insCode = r.GetInt64("ins_code");
            var bar = new DailyBar
            {
                InsCode = insCode,
                Date = session,
                Open = r.GetDecimal("open"),
                High = r.GetDecimal("high"),
                Low = r.GetDecimal("low"),
                Close = r.GetDecimal("close"),
                Last = r.GetDecimal("last"),
                Yesterday = r.GetDecimal("yesterday"),
                TradeCount = r.GetInt64("trade_count"),
                Volume = r.GetInt64("volume"),
                Value = r.GetDecimal("value"),
            };
            var flows = r.IsDBNull(r.GetOrdinal("buy_i_volume")) ? null : new ClientTypeDay
            {
                InsCode = insCode,
                Date = session,
                BuyIndividualVolume = r.GetInt64("buy_i_volume"),
                BuyInstitutionalVolume = r.GetInt64("buy_n_volume"),
                SellIndividualVolume = r.GetInt64("sell_i_volume"),
                SellInstitutionalVolume = r.GetInt64("sell_n_volume"),
                BuyIndividualValue = r.GetDecimal("buy_i_value"),
                BuyInstitutionalValue = r.GetDecimal("buy_n_value"),
                SellIndividualValue = r.GetDecimal("sell_i_value"),
                SellInstitutionalValue = r.GetDecimal("sell_n_value"),
                BuyIndividualCount = r.GetInt32("buy_i_count"),
                BuyInstitutionalCount = r.GetInt32("buy_n_count"),
                SellIndividualCount = r.GetInt32("sell_i_count"),
                SellInstitutionalCount = r.GetInt32("sell_n_count"),
                ValuesEstimated = r.GetBoolean("values_estimated"),
            };

            var allowedLow = r.GetNullableDecimal("allowed_low");
            var allowedHigh = r.GetNullableDecimal("allowed_high");
            BestLimitLevel? top = r.IsDBNull(r.GetOrdinal("bid1_price")) && r.IsDBNull(r.GetOrdinal("ask1_price")) ? null : new BestLimitLevel
            {
                Level = 1,
                BidPrice = r.GetNullableDecimal("bid1_price") ?? 0,
                BidVolume = r.IsDBNull(r.GetOrdinal("bid1_volume")) ? 0 : r.GetInt64("bid1_volume"),
                BidOrders = r.GetNullableInt32("bid1_orders") ?? 0,
                AskPrice = r.GetNullableDecimal("ask1_price") ?? 0,
                AskVolume = r.IsDBNull(r.GetOrdinal("ask1_volume")) ? 0 : r.GetInt64("ask1_volume"),
                AskOrders = r.GetNullableInt32("ask1_orders") ?? 0,
            };
            var queue = top is null ? null
                : allowedHigh > 0 && top.BidPrice >= allowedHigh && top.AskVolume == 0 ? "buy"
                : allowedLow > 0 && top.AskPrice > 0 && top.AskPrice <= allowedLow && top.BidVolume == 0 ? "sell"
                : null;
            var totalShares = r.GetNullableDecimal("total_shares") ?? 0;
            var symbol = r.GetString("symbol");

            var entry = new MarketWatchEntry
            {
                InsCode = insCode,
                Symbol = symbol,
                Name = r.GetNullableString("name"),
                InstrumentClass = r.GetString("instrument_class"),
                SectorCode = r.GetNullableString("sector_code"),
                DayStatus = r.GetNullableString("day_status"),
                Close = bar.Close,
                ChangePercent = bar.Yesterday > 0 ? (double)(bar.Close / bar.Yesterday - 1) * 100 : null,
                Last = bar.Last,
                LastChangePercent = bar.Yesterday > 0 && bar.Last > 0 ? (double)(bar.Last / bar.Yesterday - 1) * 100 : null,
                Volume = bar.Volume,
                Value = bar.Value,
                Trades = bar.TradeCount,
                AllowedLow = allowedLow,
                AllowedHigh = allowedHigh,
                Queue = queue,
                QueueValue = queue switch
                {
                    "buy" => top!.BidVolume * top.BidPrice,
                    "sell" => top!.AskVolume * top.AskPrice,
                    _ => null,
                },
                PriceEarnings = r.GetNullableDecimal("pe"),
                MarketCap = totalShares > 0 ? bar.Close * totalShares : null,
                IndividualNetValue = flows?.IndividualNetValue,
                PerCapitaBuy = flows?.PerCapitaBuy,
                PerCapitaSell = flows?.PerCapitaSell,
                BuyerPower = flows?.BuyerPower,
                IndividualBuyShare = flows is not null && bar.Volume > 0 ? flows.BuyIndividualVolume / (double)bar.Volume : null,
            };

            var filterRow = new FilterRow
            {
                InsCode = insCode,
                Symbol = symbol,
                Name = entry.Name,
                TradeCount = bar.TradeCount,
                Volume = bar.Volume,
                Value = bar.Value,
                Yesterday = bar.Yesterday,
                First = bar.Open,
                Low = bar.Low,
                High = bar.High,
                Last = bar.Last,
                Close = bar.Close,
                Eps = r.GetNullableDecimal("eps"),
                PriceEarnings = entry.PriceEarnings,
                AllowedLow = allowedLow ?? 0,
                AllowedHigh = allowedHigh ?? 0,
                TotalShares = totalShares,
                BaseVolume = r.IsDBNull(r.GetOrdinal("base_volume")) ? 0 : r.GetInt64("base_volume"),
                SectorCode = int.TryParse(entry.SectorCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sector) ? sector : 0,
                BestLimits = top is null ? [] : [top],
                ClientType = flows,
                History = history.TryGetValue(insCode, out var past) ? past : [],
                Statistics = stats.TryGetValue(insCode, out var s) ? s : new Dictionary<int, double>(),
            };

            return (entry, filterRow, r.IsDBNull(r.GetOrdinal("parent_ins_code")) ? (long?)null : r.GetInt64("parent_ins_code"));
        });
    }

    /// <summary>Up to 60 previous trading rows per instrument, newest first (market-closed rows are not trading days).</summary>
    private Dictionary<long, List<DailyBar>> LoadHistory(DateOnly session)
    {
        var sessions = analytics.GetSessions(62).Where(s => s < session).ToList();
        if (sessions.Count == 0)
            return [];

        return store.Query("""
                SELECT ins_code, d, open, high, low, close, last, yesterday, trade_count, volume, value FROM daily_bar
                WHERE d < $session AND d >= $from AND day_status IS DISTINCT FROM 'MarketClosed'
                ORDER BY ins_code, d DESC
                """, [("session", session), ("from", sessions[^1])], r => new DailyBar
            {
                InsCode = r.GetInt64(0),
                Date = r.GetFieldValue<DateOnly>(1),
                Open = r.GetDecimal(2),
                High = r.GetDecimal(3),
                Low = r.GetDecimal(4),
                Close = r.GetDecimal(5),
                Last = r.GetDecimal(6),
                Yesterday = r.GetDecimal(7),
                TradeCount = r.GetInt64(8),
                Volume = r.GetInt64(9),
                Value = r.GetDecimal(10),
            })
            .GroupBy(b => b.InsCode)
            .ToDictionary(g => g.Key, g => g.Take(60).ToList());
    }

    private Dictionary<long, Dictionary<int, double>> LoadStatistics(IReadOnlyList<int> types) =>
        store.Query($"SELECT ins_code, data_type, value FROM inst_value WHERE data_type IN ({string.Join(',', types)})", [],
                r => (InsCode: r.GetInt64(0), Type: r.GetInt32(1), Value: r.GetDouble(2)))
            .GroupBy(x => x.InsCode)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.Type, x => x.Value));
}
