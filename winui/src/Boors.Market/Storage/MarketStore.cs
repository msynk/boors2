using System.Data;
using Boors.Market.Domain;
using Boors.Market.Sources.Codal;
using Boors.Market.Sources.Funds;
using Boors.Market.Sources.Macro;
using Boors.Market.Text;
using DuckDB.NET.Data;

namespace Boors.Market.Storage;

public sealed record StoredBar(DailyBar Bar, DayStatus? Status, string Source);

public sealed record CollectionRun(string RunId, string Job, string Node, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string Status, int Items, int Failures, string? Message);

public sealed record DatasetFreshness(string Dataset, long Rows, DateOnly? LatestDate);

/// <summary>
/// Embedded DuckDB store for market data. Single file, no server, so the same engine runs on the Iran node, the
/// abroad node and the offline desktop copy. It is a projection of the raw cache: every write is an idempotent merge
/// by primary key, and the whole file can be rebuilt from the cache.
/// </summary>
public sealed class MarketStore : IDisposable
{
    private const string InMemory = ":memory:";

    private readonly string connectionString;
    private readonly bool inMemory;

    // Keeps the database instance alive (required for :memory:, harmless for files).
    private readonly DuckDBConnection keepAlive;

    private MarketStore(string path)
    {
        inMemory = path == InMemory;
        if (!inMemory)
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        Path_ = inMemory ? InMemory : Path.GetFullPath(path);
        connectionString = $"Data Source={Path_}";
        keepAlive = new DuckDBConnection(connectionString);
        keepAlive.Open();
        EnsureSchema();
    }

    public string Path_ { get; }

    public static MarketStore Open(string path) => new(path);

    public static MarketStore OpenInMemory() => new(InMemory);

    public void Dispose() => keepAlive.Dispose();

    // ---------------------------------------------------------------- instruments

    /// <summary>
    /// Merges instruments. Missing (null) fields never erase known values, and the instrument class only changes when
    /// the new source ranks at least as high (isin &lt; yval &lt; fund).
    /// </summary>
    public void UpsertInstruments(IEnumerable<Instrument> instruments, DateOnly seenOn, string classSource, DateTimeOffset? detailsAt = null)
    {
        string[] columns = ["ins_code", "symbol", "name", "latin_name", "isin", "company_isin", "y_val", "flow", "board", "board_title", "sector_code", "sector_name",
            "sub_sector_code", "sub_sector_name", "base_volume", "total_shares", "free_float", "instrument_class", "class_source", "fund_type", "details_at", "first_seen", "last_seen"];

        var nullable = columns.Except(["ins_code", "symbol", "instrument_class", "class_source", "first_seen", "last_seen"])
            .Select(c => $"{c} = COALESCE(EXCLUDED.{c}, instrument.{c})");
        var onConflict = $"""
            DO UPDATE SET
                symbol = EXCLUDED.symbol,
                {string.Join(",\n    ", nullable)},
                instrument_class = CASE WHEN {Rank("EXCLUDED.class_source")} >= {Rank("instrument.class_source")} THEN EXCLUDED.instrument_class ELSE instrument.instrument_class END,
                class_source = CASE WHEN {Rank("EXCLUDED.class_source")} >= {Rank("instrument.class_source")} THEN EXCLUDED.class_source ELSE instrument.class_source END,
                first_seen = LEAST(instrument.first_seen, EXCLUDED.first_seen),
                last_seen = GREATEST(instrument.last_seen, EXCLUDED.last_seen)
            """;

        Merge("instrument", columns, ["ins_code"], instruments.DistinctBy(i => i.InsCode), (row, i) => row
            .AppendValue(i.InsCode).AppendValue(i.Symbol).AppendValue(i.Name).AppendValue(i.LatinName).AppendValue(i.Isin).AppendValue(i.CompanyIsin)
            .AppendValue(i.YVal).AppendValue(i.Flow).AppendValue(i.Board).AppendValue(i.BoardTitle).AppendValue(i.SectorCode).AppendValue(i.SectorName)
            .AppendValue(i.SubSectorCode).AppendValue(i.SubSectorName).AppendValue(i.BaseVolume).AppendValue(i.TotalShares).AppendValue(i.FreeFloatPercent)
            .AppendValue(i.Class.ToRuleKey()).AppendValue(classSource).AppendValue((int?)null).AppendValue(detailsAt).AppendValue(seenOn).AppendValue(seenOn)
            .EndRow(), onConflict);

        static string Rank(string column) => $"(CASE {column} WHEN 'fund' THEN 3 WHEN 'yval' THEN 2 ELSE 1 END)";

        LinkSecondaryListings();
    }

    /// <summary>Links stock listings whose ISIN does not end in 0001 to the listing with the same ISIN root ending in 0001.</summary>
    private void LinkSecondaryListings()
    {
        using var connection = Connect();
        Execute(connection, """
            UPDATE instrument AS c SET parent_ins_code = p.ins_code
            FROM instrument AS p
            WHERE c.isin IS NOT NULL AND length(c.isin) = 12 AND right(c.isin, 4) <> '0001'
              AND c.instrument_class IN ('tse_stock', 'ifb_stock')
              AND p.isin = left(c.isin, 8) || '0001' AND p.ins_code <> c.ins_code
              AND c.parent_ins_code IS DISTINCT FROM p.ins_code
            """);
    }

    /// <summary>Applies fund types from fipiran to ETFs: sets fund_type and, when the type implies a rules class, the class.</summary>
    public int ApplyFundTypes(IEnumerable<FundRecord> funds)
    {
        var updated = 0;
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        foreach (var fund in funds.Where(f => f.InsCode is > 0).DistinctBy(f => f.InsCode))
        {
            var implied = fund.ImpliedClass;
            updated += Execute(connection, """
                UPDATE instrument SET fund_type = $fundType,
                    instrument_class = COALESCE($class, instrument_class),
                    class_source = CASE WHEN $class IS NULL THEN class_source ELSE 'fund' END
                WHERE ins_code = $insCode
                """, ("fundType", fund.FundType), ("class", implied?.ToRuleKey()), ("insCode", fund.InsCode!.Value));
        }

        transaction.Commit();
        return updated;
    }

    public IReadOnlyList<Instrument> GetInstruments() => Query("SELECT * FROM instrument ORDER BY symbol", [], ReadInstrument);

    public Instrument? GetInstrument(long insCode) =>
        Query("SELECT * FROM instrument WHERE ins_code = $insCode", [("insCode", insCode)], ReadInstrument).FirstOrDefault();

    /// <summary>Instruments whose details (yVal, flow, board) have never been fetched.</summary>
    public IReadOnlyList<long> GetInstrumentsWithoutDetails() =>
        Query("SELECT ins_code FROM instrument WHERE details_at IS NULL ORDER BY ins_code", [], r => r.GetInt64(0));

    // ---------------------------------------------------------------- daily bars

    /// <summary>Writes bars with their classification. <paramref name="source"/> is "history" or "marketwatch"; a
    /// market-watch row never replaces a history row for the same day (history is the official end-of-day record).</summary>
    public void UpsertDailyBars(IEnumerable<ClassifiedBar> bars, string source, IReadOnlyDictionary<DateOnly, HaltReason>? haltReasons = null) =>
        Merge("daily_bar",
            ["ins_code", "d", "open", "high", "low", "close", "last", "yesterday", "trade_count", "volume", "value", "h_even", "day_status", "halt_reason", "source"],
            ["ins_code", "d"],
            bars.DistinctBy(b => (b.Bar.InsCode, b.Bar.Date)),
            (row, c) => row
                .AppendValue(c.Bar.InsCode).AppendValue(c.Bar.Date).AppendValue(c.Bar.Open).AppendValue(c.Bar.High).AppendValue(c.Bar.Low)
                .AppendValue(c.Bar.Close).AppendValue(c.Bar.Last).AppendValue(c.Bar.Yesterday).AppendValue(c.Bar.TradeCount).AppendValue(c.Bar.Volume)
                .AppendValue(c.Bar.Value).AppendValue(c.Bar.LastTradeTime is { } t ? t.Hour * 10000 + t.Minute * 100 + t.Second : (int?)null)
                .AppendValue(c.Status.ToString())
                .AppendValue(c.Status == DayStatus.SymbolHalted && haltReasons is not null && haltReasons.TryGetValue(c.Bar.Date, out var reason) ? reason.ToString() : null)
                .AppendValue(source)
                .EndRow(),
            onConflict: """
                DO UPDATE SET open = EXCLUDED.open, high = EXCLUDED.high, low = EXCLUDED.low, close = EXCLUDED.close, last = EXCLUDED.last,
                    yesterday = EXCLUDED.yesterday, trade_count = EXCLUDED.trade_count, volume = EXCLUDED.volume, value = EXCLUDED.value,
                    h_even = COALESCE(EXCLUDED.h_even, daily_bar.h_even), day_status = EXCLUDED.day_status, halt_reason = COALESCE(EXCLUDED.halt_reason, daily_bar.halt_reason),
                    source = EXCLUDED.source
                WHERE NOT (EXCLUDED.source = 'marketwatch' AND daily_bar.source = 'history')
                """);

    /// <summary>Adds the end-of-day market-watch snapshot (band, EPS/PE, top of the book) to bars already stored.</summary>
    public void UpdateSessionSnapshots(IEnumerable<SessionSnapshot> snapshots) =>
        UpdateFrom("daily_bar",
            ["ins_code", "d", "eps", "pe", "allowed_low", "allowed_high", "bid1_price", "bid1_volume", "bid1_orders", "ask1_price", "ask1_volume", "ask1_orders"],
            ["ins_code", "d"],
            snapshots.DistinctBy(s => (s.InsCode, s.Date)),
            (row, s) => row.AppendValue(s.InsCode).AppendValue(s.Date).AppendValue(s.Eps).AppendValue(s.PriceEarnings)
                .AppendValue(s.AllowedLow).AppendValue(s.AllowedHigh)
                .AppendValue(s.Top?.BidPrice).AppendValue(s.Top?.BidVolume).AppendValue(s.Top?.BidOrders)
                .AppendValue(s.Top?.AskPrice).AppendValue(s.Top?.AskVolume).AppendValue(s.Top?.AskOrders)
                .EndRow());

    public void UpsertSectors(IEnumerable<StaticDataItem> items) =>
        Merge("sector", ["code", "name"], ["code"],
            items.Where(i => i.Type == "IndustrialGroup").DistinctBy(i => i.Code),
            (row, i) => row.AppendValue(i.Code.ToString("00", CultureInfo.InvariantCulture)).AppendValue(i.Name).EndRow());

    public void UpsertInstrumentStatistics(IEnumerable<InstrumentStatistic> statistics) =>
        Merge("inst_value", ["ins_code", "data_type", "d", "value"], ["ins_code", "data_type"],
            statistics.DistinctBy(s => (s.InsCode, s.DataType)),
            (row, s) => row.AppendValue(s.InsCode).AppendValue(s.DataType).AppendValue(s.Date).AppendValue(s.Value).EndRow());

    /// <summary>Updates only the classification of already stored bars (after halts are resolved or the calendar changes).</summary>
    public void UpdateDayStatuses(long insCode, IEnumerable<(DateOnly Date, DayStatus Status, HaltReason? Reason)> statuses)
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        foreach (var (date, status, reason) in statuses)
        {
            Execute(connection, "UPDATE daily_bar SET day_status = $status, halt_reason = $reason WHERE ins_code = $insCode AND d = $d",
                ("status", status.ToString()), ("reason", reason?.ToString()), ("insCode", insCode), ("d", date));
        }

        transaction.Commit();
    }

    public IReadOnlyList<StoredBar> GetDailyBars(long insCode, DateOnly? from = null) =>
        Query("SELECT * FROM daily_bar WHERE ins_code = $insCode AND d >= $from ORDER BY d",
            [("insCode", insCode), ("from", from ?? DateOnly.MinValue)], ReadBar);

    /// <summary>The last <paramref name="count"/> stored bars of every instrument (for re-classifying after an EOD append).</summary>
    public IReadOnlyDictionary<long, IReadOnlyList<StoredBar>> GetRecentBars(int count) =>
        Query("SELECT * FROM daily_bar QUALIFY row_number() OVER (PARTITION BY ins_code ORDER BY d DESC) <= $n ORDER BY ins_code, d",
                [("n", count)], ReadBar)
            .GroupBy(b => b.Bar.InsCode)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<StoredBar>)g.ToList());

    /// <summary>Every stored bar dated <paramref name="from"/>..<paramref name="to"/>, per instrument in date order.</summary>
    public IReadOnlyDictionary<long, IReadOnlyList<StoredBar>> GetBarsBetween(DateOnly from, DateOnly to) =>
        Query("SELECT * FROM daily_bar WHERE d BETWEEN $from AND $to ORDER BY ins_code, d", [("from", from), ("to", to)], ReadBar)
            .GroupBy(b => b.Bar.InsCode)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<StoredBar>)g.ToList());

    /// <summary>The dates in <paramref name="from"/>..<paramref name="to"/> on which at least one instrument has a bar.</summary>
    public IReadOnlySet<DateOnly> GetBarDates(DateOnly from, DateOnly to) =>
        Query("SELECT DISTINCT d FROM daily_bar WHERE d BETWEEN $from AND $to", [("from", from), ("to", to)], r => r.GetFieldValue<DateOnly>(0)).ToHashSet();

    public IReadOnlyDictionary<long, DateOnly> GetLatestBarDates() =>
        Query("SELECT ins_code, max(d) FROM daily_bar GROUP BY ins_code", [], r => (r.GetInt64(0), r.GetFieldValue<DateOnly>(1)))
            .ToDictionary(x => x.Item1, x => x.Item2);

    // ---------------------------------------------------------------- flows and corporate events

    /// <summary>Merges flows; an estimated row never overwrites an official one.</summary>
    public void UpsertClientTypes(IEnumerable<ClientTypeDay> days) =>
        Merge("client_type_daily",
            ["ins_code", "d", "buy_i_volume", "buy_n_volume", "sell_i_volume", "sell_n_volume", "buy_i_value", "buy_n_value", "sell_i_value", "sell_n_value",
             "buy_i_count", "buy_n_count", "sell_i_count", "sell_n_count", "values_estimated"],
            ["ins_code", "d"],
            days.DistinctBy(d => (d.InsCode, d.Date)),
            (row, d) => row
                .AppendValue(d.InsCode).AppendValue(d.Date)
                .AppendValue(d.BuyIndividualVolume).AppendValue(d.BuyInstitutionalVolume).AppendValue(d.SellIndividualVolume).AppendValue(d.SellInstitutionalVolume)
                .AppendValue(d.BuyIndividualValue).AppendValue(d.BuyInstitutionalValue).AppendValue(d.SellIndividualValue).AppendValue(d.SellInstitutionalValue)
                .AppendValue(d.BuyIndividualCount).AppendValue(d.BuyInstitutionalCount).AppendValue(d.SellIndividualCount).AppendValue(d.SellInstitutionalCount)
                .AppendValue(d.ValuesEstimated)
                .EndRow(),
            onConflict: "DO UPDATE SET " + string.Join(", ", new[] { "buy_i_volume", "buy_n_volume", "sell_i_volume", "sell_n_volume", "buy_i_value", "buy_n_value", "sell_i_value", "sell_n_value", "buy_i_count", "buy_n_count", "sell_i_count", "sell_n_count", "values_estimated" }
                .Select(c => $"{c} = EXCLUDED.{c}")) + " WHERE NOT (EXCLUDED.values_estimated AND NOT client_type_daily.values_estimated)");

    public IReadOnlyList<ClientTypeDay> GetClientTypes(long insCode) =>
        Query("SELECT * FROM client_type_daily WHERE ins_code = $insCode ORDER BY d", [("insCode", insCode)], r => new ClientTypeDay
        {
            InsCode = r.GetInt64("ins_code"),
            Date = r.GetFieldValue<DateOnly>("d"),
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
        });

    /// <summary>Stores adjustments; <paramref name="source"/> is "published" (TSETMC list) or "implied" (from bars).</summary>
    public void UpsertPriceAdjustments(IEnumerable<PriceAdjustment> adjustments, string source) =>
        Merge("price_adjustment", ["ins_code", "d", "source", "adjusted_close", "unadjusted_close"], ["ins_code", "d", "source"],
            adjustments.DistinctBy(a => (a.InsCode, a.Date)),
            (row, a) => row.AppendValue(a.InsCode).AppendValue(a.Date).AppendValue(source).AppendValue(a.AdjustedClose).AppendValue(a.UnadjustedClose).EndRow());

    public IReadOnlyList<ShareChange> GetShareChanges(long insCode) =>
        Query("SELECT * FROM share_change WHERE ins_code = $insCode ORDER BY d", [("insCode", insCode)], r => new ShareChange
        {
            InsCode = r.GetInt64("ins_code"),
            Date = r.GetFieldValue<DateOnly>("d"),
            SharesBefore = r.GetDecimal("shares_before"),
            SharesAfter = r.GetDecimal("shares_after"),
        });

    public IReadOnlyList<PriceAdjustment> GetPriceAdjustments(long insCode, string source) =>
        Query("SELECT * FROM price_adjustment WHERE ins_code = $insCode AND source = $source ORDER BY d", [("insCode", insCode), ("source", source)], r => new PriceAdjustment
        {
            InsCode = r.GetInt64("ins_code"),
            Date = r.GetFieldValue<DateOnly>("d"),
            AdjustedClose = r.GetDecimal("adjusted_close"),
            UnadjustedClose = r.GetDecimal("unadjusted_close"),
        });

    public void UpsertShareChanges(IEnumerable<ShareChange> changes) =>
        Merge("share_change", ["ins_code", "d", "shares_before", "shares_after"], ["ins_code", "d"],
            changes.DistinctBy(c => (c.InsCode, c.Date)),
            (row, c) => row.AppendValue(c.InsCode).AppendValue(c.Date).AppendValue(c.SharesBefore).AppendValue(c.SharesAfter).EndRow());

    public void UpsertShareholders(IEnumerable<ShareholderPosition> positions) =>
        Merge("shareholder", ["ins_code", "snapshot_date", "holder_share_id", "holder_name", "shares", "percent", "change", "change_amount"], ["ins_code", "snapshot_date", "holder_share_id"],
            positions.DistinctBy(p => (p.InsCode, p.SnapshotDate, p.HolderShareId)),
            (row, p) => row.AppendValue(p.InsCode).AppendValue(p.SnapshotDate).AppendValue(p.HolderShareId).AppendValue(p.HolderName)
                .AppendValue(p.Shares).AppendValue(p.Percent).AppendValue(p.Change).AppendValue(p.ChangeAmount).EndRow());

    // ---------------------------------------------------------------- indices

    public void UpsertIndexInfo(IEnumerable<(long InsCode, string Name, int Flow)> indices) =>
        Merge("index_info", ["ins_code", "name", "flow"], ["ins_code"], indices.DistinctBy(i => i.InsCode),
            (row, i) => row.AppendValue(i.InsCode).AppendValue(i.Name).AppendValue(i.Flow).EndRow());

    public IReadOnlyList<(long InsCode, string Name, int? Flow)> GetIndexInfo() =>
        Query("SELECT ins_code, name, flow FROM index_info ORDER BY name", [], r => (r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? (int?)null : r.GetInt32(2)));

    /// <summary>Stores index bars; a same-day "snapshot" row never replaces a "history" row.</summary>
    public void UpsertIndexBars(IEnumerable<IndexBar> bars, string source) =>
        Merge("index_daily", ["ins_code", "d", "close", "low", "high", "source"], ["ins_code", "d"],
            bars.DistinctBy(b => (b.InsCode, b.Date)),
            (row, b) => row.AppendValue(b.InsCode).AppendValue(b.Date).AppendValue(b.Close).AppendValue(b.Low).AppendValue(b.High).AppendValue(source).EndRow(),
            onConflict: "DO UPDATE SET close = EXCLUDED.close, low = EXCLUDED.low, high = EXCLUDED.high, source = EXCLUDED.source WHERE NOT (EXCLUDED.source = 'snapshot' AND index_daily.source = 'history')");

    /// <summary>Instruments that already have official (history-sourced) bars.</summary>
    public IReadOnlySet<long> GetInstrumentsWithHistory() =>
        Query("SELECT DISTINCT ins_code FROM daily_bar WHERE source = 'history'", [], r => r.GetInt64(0)).ToHashSet();

    public IReadOnlyList<IndexBar> GetIndexBars(long insCode) =>
        Query("SELECT * FROM index_daily WHERE ins_code = $insCode ORDER BY d", [("insCode", insCode)], r => new IndexBar
        {
            InsCode = r.GetInt64("ins_code"),
            Date = r.GetFieldValue<DateOnly>("d"),
            Close = r.GetDecimal("close"),
            Low = r.GetDecimal("low"),
            High = r.GetDecimal("high"),
        });

    // ---------------------------------------------------------------- messages, states, closures

    public void UpsertMessages(IEnumerable<SupervisorMessage> messages, IReadOnlyDictionary<string, long> symbolToInsCode)
    {
        var list = messages.DistinctBy(m => m.Id).ToList();
        Merge("supervisor_message", ["msg_id", "d", "h_even", "title", "body", "flow", "kind", "reason", "reopens_without_limit"], ["msg_id"], list,
            (row, m) => row.AppendValue(m.Id).AppendValue(m.Date).AppendValue(m.Time is { } t ? t.Hour * 10000 + t.Minute * 100 + t.Second : (int?)null)
                .AppendValue(m.Title).AppendValue(m.Body).AppendValue(m.Flow).AppendValue(m.Kind.ToString()).AppendValue(m.Reason.ToString())
                .AppendValue(m.ReopensWithoutLimit).EndRow());

        // Messages often name a numbered sibling listing ("فولاد1"); fall back to the symbol without trailing digits.
        var links = list
            .SelectMany(m => m.MentionedSymbols.Select(s => (m.Id, Key: PersianText.MatchKey(s))))
            .Select(x => (x.Id, InsCode: symbolToInsCode.TryGetValue(x.Key, out var exact) ? exact
                : symbolToInsCode.TryGetValue(x.Key.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9'), out var parent) ? parent
                : (long?)null))
            .Where(x => x.InsCode is not null)
            .Select(x => (x.Id, InsCode: x.InsCode!.Value))
            .Distinct();
        Merge("message_instrument", ["msg_id", "ins_code"], ["msg_id", "ins_code"], links,
            (row, l) => row.AppendValue(l.Id).AppendValue(l.InsCode).EndRow(), onConflict: "DO NOTHING");
    }

    public IReadOnlyList<SupervisorMessage> GetMessages(long insCode) =>
        Query("""
            SELECT m.* FROM supervisor_message m JOIN message_instrument mi ON mi.msg_id = m.msg_id
            WHERE mi.ins_code = $insCode ORDER BY m.d, m.h_even
            """, [("insCode", insCode)], ReadMessage);

    public void UpsertInstrumentStates(IEnumerable<InstrumentStateOnDate> states) =>
        Merge("instrument_state", ["ins_code", "d", "state"], ["ins_code", "d"], states.DistinctBy(s => (s.InsCode, s.Date)),
            (row, s) => row.AppendValue(s.InsCode).AppendValue(s.Date).AppendValue(s.State).EndRow());

    public IReadOnlyDictionary<DateOnly, string?> GetInstrumentStates(long insCode) =>
        Query("SELECT d, state FROM instrument_state WHERE ins_code = $insCode", [("insCode", insCode)],
                r => (r.GetFieldValue<DateOnly>(0), r.IsDBNull(1) ? null : r.GetString(1)))
            .ToDictionary(x => x.Item1, x => x.Item2);

    public void ReplaceClosures(IEnumerable<(DateOnly Date, string Reason, string Origin)> closures)
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        Execute(connection, "DELETE FROM market_closure");
        using (var appender = connection.CreateAppender("market_closure"))
        {
            foreach (var c in closures.DistinctBy(c => c.Date))
                appender.CreateRow().AppendValue(c.Date).AppendValue(c.Reason).AppendValue(c.Origin).EndRow();
        }

        transaction.Commit();
    }

    public IReadOnlyList<DateOnly> GetClosureDays() =>
        Query("SELECT d FROM market_closure ORDER BY d", [], r => r.GetFieldValue<DateOnly>(0));

    // ---------------------------------------------------------------- codal, funds, macro

    public void UpsertCodalLetters(IEnumerable<CodalLetter> letters, IReadOnlyDictionary<string, long> symbolToInsCode) =>
        Merge("codal_letter",
            ["tracing_no", "ins_code", "symbol", "company_name", "title", "letter_code", "letter_type_id", "published_at", "sent_at", "disclosure_group",
             "has_html", "has_excel", "has_pdf", "has_xbrl", "has_attachment", "is_estimate", "under_supervision", "report_url", "excel_url", "pdf_url", "attachment_url"],
            ["tracing_no"],
            letters.DistinctBy(l => l.TracingNo),
            (row, l) => row
                .AppendValue(l.TracingNo)
                .AppendValue(l.Symbol is { } s && symbolToInsCode.TryGetValue(PersianText.MatchKey(s), out var ins) ? ins : (long?)null)
                .AppendValue(l.Symbol).AppendValue(l.CompanyName).AppendValue(l.Title).AppendValue(l.LetterCode).AppendValue(l.LetterTypeId)
                .AppendValue(l.PublishedAt).AppendValue(l.SentAt).AppendValue(l.DisclosureGroup)
                .AppendValue(l.HasHtml).AppendValue(l.HasExcel).AppendValue(l.HasPdf).AppendValue(l.HasXbrl).AppendValue(l.HasAttachment).AppendValue(l.IsEstimate)
                .AppendValue(l.UnderSupervision).AppendValue(l.ReportUrl).AppendValue(l.ExcelUrl).AppendValue(l.PdfUrl).AppendValue(l.AttachmentUrl)
                .EndRow());

    public IReadOnlyList<CodalLetter> GetCodalLetters(long insCode, int take) =>
        Query("SELECT * FROM codal_letter WHERE ins_code = $insCode ORDER BY published_at DESC NULLS LAST LIMIT $take", [("insCode", insCode), ("take", take)], r => new CodalLetter
        {
            TracingNo = r.GetInt64("tracing_no"),
            Symbol = r.GetNullableString("symbol"),
            CompanyName = r.GetNullableString("company_name"),
            Title = r.GetString("title"),
            LetterCode = r.GetNullableString("letter_code"),
            LetterTypeId = r.GetNullableInt32("letter_type_id"),
            PublishedAt = r.IsDBNull(r.GetOrdinal("published_at")) ? null : r.GetFieldValue<DateTimeOffset>("published_at"),
            DisclosureGroup = r.GetNullableString("disclosure_group"),
            HasExcel = !r.IsDBNull(r.GetOrdinal("has_excel")) && r.GetBoolean("has_excel"),
            ReportUrl = r.GetNullableString("report_url"),
            ExcelUrl = r.GetNullableString("excel_url"),
            PdfUrl = r.GetNullableString("pdf_url"),
            AttachmentUrl = r.GetNullableString("attachment_url"),
        });

    /// <summary>The most recent shareholder snapshot of an instrument.</summary>
    public IReadOnlyList<ShareholderPosition> GetLatestShareholders(long insCode) =>
        Query("""
            SELECT * FROM shareholder WHERE ins_code = $insCode
              AND snapshot_date = (SELECT max(snapshot_date) FROM shareholder WHERE ins_code = $insCode)
            ORDER BY percent DESC
            """, [("insCode", insCode)], r => new ShareholderPosition
        {
            InsCode = r.GetInt64("ins_code"),
            SnapshotDate = r.GetFieldValue<DateOnly>("snapshot_date"),
            HolderShareId = r.GetInt64("holder_share_id"),
            HolderName = r.GetNullableString("holder_name") ?? string.Empty,
            Shares = r.GetDecimal("shares"),
            Percent = r.GetDecimal("percent"),
            Change = r.GetInt32("change"),
            ChangeAmount = r.GetDecimal("change_amount"),
        });

    public IReadOnlySet<long> GetKnownTracingNumbers(IEnumerable<long> candidates)
    {
        var list = candidates.Distinct().ToList();
        if (list.Count == 0)
            return new HashSet<long>();

        return Query($"SELECT tracing_no FROM codal_letter WHERE tracing_no IN ({string.Join(',', list)})", [], r => r.GetInt64(0)).ToHashSet();
    }

    public void UpsertFunds(IEnumerable<FundRecord> funds)
    {
        var list = funds.DistinctBy(f => f.RegNo).ToList();
        Merge("fund", ["reg_no", "ins_code", "name", "symbol", "fund_type", "type_of_invest", "manager", "initiation_date"], ["reg_no"], list,
            (row, f) => row.AppendValue(f.RegNo).AppendValue(f.InsCode).AppendValue(f.Name).AppendValue(f.Symbol).AppendValue(f.FundType)
                .AppendValue(f.TypeOfInvest).AppendValue(f.Manager).AppendValue(f.InitiationDate).EndRow());

        Merge("fund_daily",
            ["reg_no", "d", "issue_nav", "cancel_nav", "statistical_nav", "net_asset", "invested_units", "daily_return", "weekly_return", "monthly_return",
             "quarterly_return", "six_month_return", "annual_return", "stock_share", "bond_share", "cash_share", "deposit_share", "other_share"],
            ["reg_no", "d"],
            list.Where(f => f.Date is not null),
            (row, f) => row.AppendValue(f.RegNo).AppendValue(f.Date).AppendValue(f.IssueNav).AppendValue(f.CancelNav).AppendValue(f.StatisticalNav)
                .AppendValue(f.NetAsset).AppendValue(f.InvestedUnits).AppendValue(f.DailyReturn).AppendValue(f.WeeklyReturn).AppendValue(f.MonthlyReturn)
                .AppendValue(f.QuarterlyReturn).AppendValue(f.SixMonthReturn).AppendValue(f.AnnualReturn).AppendValue(f.StockShare).AppendValue(f.BondShare)
                .AppendValue(f.CashShare).AppendValue(f.DepositShare).AppendValue(f.OtherShare).EndRow());
    }

    public void UpsertMacroDaily(IEnumerable<MacroBar> bars) =>
        Merge("macro_daily", ["series", "d", "source", "open", "low", "high", "close"], ["series", "d", "source"],
            bars.DistinctBy(b => (b.Series, b.Date, b.Source)),
            (row, b) => row.AppendValue(b.Series).AppendValue(b.Date).AppendValue(b.Source).AppendValue(b.Open).AppendValue(b.Low).AppendValue(b.High).AppendValue(b.Close).EndRow());

    public IReadOnlyList<MacroBar> GetMacroDaily(string series, string? source = null) =>
        Query("SELECT * FROM macro_daily WHERE series = $series AND ($source IS NULL OR source = $source) ORDER BY d",
            [("series", series), ("source", source)],
            r => new MacroBar(r.GetString("series"), r.GetFieldValue<DateOnly>("d"), r.GetDecimal("open"), r.GetDecimal("low"), r.GetDecimal("high"), r.GetDecimal("close"), r.GetString("source")));

    public void UpsertMacroMonthly(IEnumerable<MacroMonthlyValue> values) =>
        Merge("macro_monthly", ["series", "period", "source", "period_end", "value", "note"], ["series", "period", "source"],
            values.DistinctBy(v => (v.Series, v.Period, v.Source)),
            (row, v) => row.AppendValue(v.Series).AppendValue(v.Period).AppendValue(v.Source).AppendValue(v.PeriodEnd).AppendValue(v.Value).AppendValue(v.Note).EndRow());

    public IReadOnlyList<MacroMonthlyValue> GetMacroMonthly(string series) =>
        Query("SELECT * FROM macro_monthly WHERE series = $series ORDER BY period_end, source", [("series", series)],
            r => new MacroMonthlyValue(r.GetString("series"), r.GetString("period"), r.GetFieldValue<DateOnly>("period_end"),
                r.GetDecimal("value"), r.GetString("source"), r.IsDBNull(r.GetOrdinal("note")) ? null : r.GetString("note")));

    // ---------------------------------------------------------------- runs and health

    public void RecordRun(CollectionRun run) =>
        Merge("collection_run", ["run_id", "job", "node", "started_at", "finished_at", "status", "items", "failures", "message"], ["run_id"], [run],
            (row, r) => row.AppendValue(r.RunId).AppendValue(r.Job).AppendValue(r.Node).AppendValue(r.StartedAt).AppendValue(r.FinishedAt)
                .AppendValue(r.Status).AppendValue(r.Items).AppendValue(r.Failures).AppendValue(r.Message).EndRow());

    public IReadOnlyList<CollectionRun> GetLatestRuns() =>
        Query("""
            SELECT * FROM collection_run QUALIFY row_number() OVER (PARTITION BY job ORDER BY started_at DESC) = 1 ORDER BY job
            """, [], ReadRun);

    /// <summary>Runs started at or after <paramref name="since"/>, newest first, optionally of one job.</summary>
    public IReadOnlyList<CollectionRun> GetRuns(DateTimeOffset since, string? job, int limit) =>
        Query($"SELECT * FROM collection_run WHERE started_at >= $since{(job is null ? string.Empty : " AND job = $job")} ORDER BY started_at DESC LIMIT $limit",
            job is null ? [("since", since), ("limit", limit)] : [("since", since), ("job", job), ("limit", limit)], ReadRun);

    private static CollectionRun ReadRun(DuckDBDataReader r) => new(
        r.GetString("run_id"), r.GetString("job"), r.GetString("node"),
        r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("started_at")),
        r.IsDBNull(r.GetOrdinal("finished_at")) ? null : r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("finished_at")),
        r.GetString("status"), r.GetInt32("items"), r.GetInt32("failures"),
        r.IsDBNull(r.GetOrdinal("message")) ? null : r.GetString("message"));

    public IReadOnlyList<DatasetFreshness> GetFreshness() =>
        Query("""
            SELECT 'instrument', count(*), max(last_seen) FROM instrument
            UNION ALL SELECT 'daily_bar', count(*), max(d) FROM daily_bar
            UNION ALL SELECT 'client_type_daily', count(*), max(d) FROM client_type_daily
            UNION ALL SELECT 'price_adjustment', count(*), max(d) FROM price_adjustment
            UNION ALL SELECT 'share_change', count(*), max(d) FROM share_change
            UNION ALL SELECT 'index_daily', count(*), max(d) FROM index_daily
            UNION ALL SELECT 'shareholder', count(*), max(snapshot_date) FROM shareholder
            UNION ALL SELECT 'supervisor_message', count(*), max(d) FROM supervisor_message
            UNION ALL SELECT 'codal_letter', count(*), CAST(max(published_at) AS DATE) FROM codal_letter
            UNION ALL SELECT 'fund_daily', count(*), max(d) FROM fund_daily
            UNION ALL SELECT 'macro_daily', count(*), max(d) FROM macro_daily
            UNION ALL SELECT 'macro_monthly', count(*), max(period_end) FROM macro_monthly
            UNION ALL SELECT 'market_closure', count(*), max(d) FROM market_closure
            """, [], r => new DatasetFreshness(r.GetString(0), r.GetInt64(1), r.IsDBNull(2) ? null : r.GetFieldValue<DateOnly>(2)));

    /// <summary>Runs an arbitrary read-only query (CLI/research use). Returns column names and rows.</summary>
    public (IReadOnlyList<string> Columns, IReadOnlyList<object?[]> Rows) RunQuery(string sql, int maxRows = 1000)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        var rows = new List<object?[]>();
        while (rows.Count < maxRows && reader.Read())
        {
            var values = new object?[reader.FieldCount];
            for (var i = 0; i < values.Length; i++)
                values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(values);
        }

        return (columns, rows);
    }

    // ---------------------------------------------------------------- plumbing

    private void EnsureSchema()
    {
        using var connection = Connect();
        Execute(connection, MarketStoreSchema.Ddl);
        var version = Query("SELECT max(version) FROM schema_info", [], r => r.IsDBNull(0) ? (int?)null : r.GetInt32(0)).Single();
        if (version is null)
            Execute(connection, $"INSERT INTO schema_info VALUES ({MarketStoreSchema.Version})");
        else if (version != MarketStoreSchema.Version)
            throw new InvalidOperationException($"Market store '{Path_}' has schema version {version}, this build expects {MarketStoreSchema.Version}. Delete the file and run 'boors rebuild'.");
    }

    private DuckDBConnection Connect()
    {
        var connection = inMemory ? keepAlive.Duplicate() : new DuckDBConnection(connectionString);
        if (connection.State != ConnectionState.Open)
            connection.Open();
        return connection;
    }

    /// <summary>
    /// Idempotent bulk merge: rows are appended into a staging table, then inserted with ON CONFLICT handling in one
    /// transaction. Callers must de-duplicate keys (DuckDB cannot update the same row twice in one statement).
    /// </summary>
    private void Merge<T>(string table, string[] columns, string[] key, IEnumerable<T> rows, Action<IDuckDBAppenderRow, T> write, string? onConflict = null)
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        var stage = $"stage_{table}_{Guid.NewGuid():N}";
        var columnList = string.Join(", ", columns);
        Execute(connection, $"CREATE TABLE {stage} AS SELECT {columnList} FROM {table} LIMIT 0");

        var count = 0;
        using (var appender = connection.CreateAppender(stage))
        {
            foreach (var row in rows)
            {
                write(appender.CreateRow(), row);
                count++;
            }
        }

        if (count > 0)
        {
            var conflict = onConflict ?? (columns.Length > key.Length
                ? "DO UPDATE SET " + string.Join(", ", columns.Except(key).Select(c => $"{c} = EXCLUDED.{c}"))
                : "DO NOTHING");
            Execute(connection, $"INSERT INTO {table} ({columnList}) SELECT {columnList} FROM {stage} ON CONFLICT ({string.Join(", ", key)}) {conflict}");
        }

        Execute(connection, $"DROP TABLE {stage}");
        transaction.Commit();
    }

    /// <summary>Bulk UPDATE of existing rows from a staging table (rows that do not exist are ignored).</summary>
    private void UpdateFrom<T>(string table, string[] columns, string[] key, IEnumerable<T> rows, Action<IDuckDBAppenderRow, T> write)
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        var stage = $"stage_{table}_{Guid.NewGuid():N}";
        var columnList = string.Join(", ", columns);
        Execute(connection, $"CREATE TABLE {stage} AS SELECT {columnList} FROM {table} LIMIT 0");
        using (var appender = connection.CreateAppender(stage))
        {
            foreach (var row in rows)
                write(appender.CreateRow(), row);
        }

        var set = string.Join(", ", columns.Except(key).Select(c => $"{c} = s.{c}"));
        var join = string.Join(" AND ", key.Select(k => $"{table}.{k} = s.{k}"));
        Execute(connection, $"UPDATE {table} SET {set} FROM {stage} AS s WHERE {join}");
        Execute(connection, $"DROP TABLE {stage}");
        transaction.Commit();
    }

    private static int Execute(DuckDBConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.Add(new DuckDBParameter(name, value ?? DBNull.Value));
        return command.ExecuteNonQuery();
    }

    internal List<T> Query<T>(string sql, (string Name, object? Value)[] parameters, Func<DuckDBDataReader, T> read)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.Add(new DuckDBParameter(name, value ?? DBNull.Value));

        using var reader = command.ExecuteReader();
        var result = new List<T>();
        while (reader.Read())
            result.Add(read(reader));
        return result;
    }

    private static Instrument ReadInstrument(DuckDBDataReader r) => new()
    {
        InsCode = r.GetInt64("ins_code"),
        Symbol = r.GetString("symbol"),
        Name = r.GetNullableString("name"),
        LatinName = r.GetNullableString("latin_name"),
        Isin = r.GetNullableString("isin"),
        CompanyIsin = r.GetNullableString("company_isin"),
        YVal = r.GetNullableInt32("y_val"),
        Flow = r.GetNullableInt32("flow"),
        Board = r.GetNullableString("board"),
        BoardTitle = r.GetNullableString("board_title"),
        SectorCode = r.GetNullableString("sector_code"),
        SectorName = r.GetNullableString("sector_name"),
        SubSectorCode = r.GetNullableInt32("sub_sector_code"),
        SubSectorName = r.GetNullableString("sub_sector_name"),
        BaseVolume = r.IsDBNull(r.GetOrdinal("base_volume")) ? null : r.GetInt64("base_volume"),
        TotalShares = r.GetNullableDecimal("total_shares"),
        FreeFloatPercent = r.GetNullableDecimal("free_float"),
        Class = InstrumentClassifier.FromRuleKey(r.GetString("instrument_class")),
        ParentInsCode = r.IsDBNull(r.GetOrdinal("parent_ins_code")) ? null : r.GetInt64("parent_ins_code"),
    };

    private static StoredBar ReadBar(DuckDBDataReader r)
    {
        var hEven = r.GetNullableInt32("h_even");
        var bar = new DailyBar
        {
            InsCode = r.GetInt64("ins_code"),
            Date = r.GetFieldValue<DateOnly>("d"),
            Open = r.GetDecimal("open"),
            High = r.GetDecimal("high"),
            Low = r.GetDecimal("low"),
            Close = r.GetDecimal("close"),
            Last = r.GetDecimal("last"),
            Yesterday = r.GetDecimal("yesterday"),
            TradeCount = r.GetInt64("trade_count"),
            Volume = r.GetInt64("volume"),
            Value = r.GetDecimal("value"),
            LastTradeTime = hEven is { } h ? DEven.ToTime(h) : null,
        };
        var status = r.GetNullableString("day_status") is { } s && Enum.TryParse<DayStatus>(s, out var parsed) ? parsed : (DayStatus?)null;
        return new StoredBar(bar, status, r.GetString("source"));
    }

    private static SupervisorMessage ReadMessage(DuckDBDataReader r) => new()
    {
        Id = r.GetInt64("msg_id"),
        Date = r.GetFieldValue<DateOnly>("d"),
        Time = r.GetNullableInt32("h_even") is { } h ? DEven.ToTime(h) : null,
        Title = r.GetNullableString("title") ?? string.Empty,
        Body = r.GetNullableString("body"),
        Flow = r.GetNullableInt32("flow") ?? 0,
        Kind = Enum.Parse<MessageKind>(r.GetString("kind")),
        Reason = Enum.Parse<HaltReason>(r.GetString("reason")),
        ReopensWithoutLimit = r.GetBoolean("reopens_without_limit"),
    };
}

public sealed record MacroMonthlyValue(string Series, string Period, DateOnly PeriodEnd, decimal Value, string Source, string? Note);

internal static class DataReaderNullableExtensions
{
    public static string? GetNullableString(this DuckDBDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static int? GetNullableInt32(this DuckDBDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    public static decimal? GetNullableDecimal(this DuckDBDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    }
}
