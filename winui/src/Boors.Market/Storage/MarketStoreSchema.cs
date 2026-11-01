namespace Boors.Market.Storage;

/// <summary>
/// DuckDB schema of the market store. The store is a projection of the raw cache: it can be deleted and rebuilt
/// with <c>boors rebuild</c>, so schema changes bump <see cref="Version"/> and rebuild rather than migrate.
/// </summary>
internal static class MarketStoreSchema
{
    public const int Version = 3;

    public const string Ddl = """
        CREATE TABLE IF NOT EXISTS schema_info (version INTEGER NOT NULL);

        CREATE TABLE IF NOT EXISTS instrument (
            ins_code BIGINT PRIMARY KEY,
            symbol VARCHAR NOT NULL,
            name VARCHAR,
            latin_name VARCHAR,
            isin VARCHAR,
            company_isin VARCHAR,
            y_val INTEGER,
            flow INTEGER,
            board VARCHAR,
            board_title VARCHAR,
            sector_code VARCHAR,
            sector_name VARCHAR,
            sub_sector_code INTEGER,
            sub_sector_name VARCHAR,
            base_volume BIGINT,
            total_shares DECIMAL(24,0),
            free_float DECIMAL(9,4),
            instrument_class VARCHAR NOT NULL,
            class_source VARCHAR NOT NULL,      -- isin < yval < fund (higher wins)
            fund_type INTEGER,
            parent_ins_code BIGINT,             -- primary listing of a TAL/block sibling (see Instrument.ParentInsCode)
            details_at TIMESTAMPTZ,
            first_seen DATE,
            last_seen DATE
        );

        CREATE TABLE IF NOT EXISTS daily_bar (
            ins_code BIGINT NOT NULL,
            d DATE NOT NULL,
            open DECIMAL(24,4), high DECIMAL(24,4), low DECIMAL(24,4), close DECIMAL(24,4), last DECIMAL(24,4), yesterday DECIMAL(24,4),
            trade_count BIGINT, volume BIGINT, value DECIMAL(28,4),
            h_even INTEGER,
            day_status VARCHAR,
            halt_reason VARCHAR,                -- set on SymbolHalted days when a supervisor message explains the halt
            source VARCHAR NOT NULL,            -- history | marketwatch
            -- End-of-day market-watch snapshot (null on history-only days): band, valuation and top of the book at the close.
            eps DECIMAL(24,4), pe DECIMAL(18,4),
            allowed_low DECIMAL(24,4), allowed_high DECIMAL(24,4),
            bid1_price DECIMAL(24,4), bid1_volume BIGINT, bid1_orders INTEGER,
            ask1_price DECIMAL(24,4), ask1_volume BIGINT, ask1_orders INTEGER,
            PRIMARY KEY (ins_code, d)
        );

        CREATE TABLE IF NOT EXISTS client_type_daily (
            ins_code BIGINT NOT NULL,
            d DATE NOT NULL,
            buy_i_volume BIGINT, buy_n_volume BIGINT, sell_i_volume BIGINT, sell_n_volume BIGINT,
            buy_i_value DECIMAL(28,4), buy_n_value DECIMAL(28,4), sell_i_value DECIMAL(28,4), sell_n_value DECIMAL(28,4),
            buy_i_count INTEGER, buy_n_count INTEGER, sell_i_count INTEGER, sell_n_count INTEGER,
            values_estimated BOOLEAN NOT NULL,
            PRIMARY KEY (ins_code, d)
        );

        -- source 'published' = TSETMC GetPriceAdjustList (incomplete, meeting-dated);
        -- source 'implied' = derived from bars where Yesterday differs from the previous close (complete, effective-dated).
        CREATE TABLE IF NOT EXISTS price_adjustment (
            ins_code BIGINT NOT NULL, d DATE NOT NULL, source VARCHAR NOT NULL,
            adjusted_close DECIMAL(24,4), unadjusted_close DECIMAL(24,4),
            PRIMARY KEY (ins_code, d, source)
        );

        CREATE TABLE IF NOT EXISTS share_change (
            ins_code BIGINT NOT NULL, d DATE NOT NULL,
            shares_before DECIMAL(24,0), shares_after DECIMAL(24,0),
            PRIMARY KEY (ins_code, d)
        );

        CREATE TABLE IF NOT EXISTS index_info (
            ins_code BIGINT PRIMARY KEY,
            name VARCHAR NOT NULL,
            flow INTEGER
        );

        CREATE TABLE IF NOT EXISTS index_daily (
            ins_code BIGINT NOT NULL, d DATE NOT NULL,
            close DECIMAL(24,4), low DECIMAL(24,4), high DECIMAL(24,4),
            source VARCHAR NOT NULL,            -- history | snapshot
            PRIMARY KEY (ins_code, d)
        );

        CREATE TABLE IF NOT EXISTS shareholder (
            ins_code BIGINT NOT NULL, snapshot_date DATE NOT NULL, holder_share_id BIGINT NOT NULL,
            holder_name VARCHAR, shares DECIMAL(24,0), percent DECIMAL(9,4), change INTEGER, change_amount DECIMAL(24,0),
            PRIMARY KEY (ins_code, snapshot_date, holder_share_id)
        );

        CREATE TABLE IF NOT EXISTS supervisor_message (
            msg_id BIGINT PRIMARY KEY,
            d DATE NOT NULL, h_even INTEGER,
            title VARCHAR, body VARCHAR, flow INTEGER,
            kind VARCHAR NOT NULL, reason VARCHAR NOT NULL, reopens_without_limit BOOLEAN NOT NULL
        );

        CREATE TABLE IF NOT EXISTS message_instrument (
            msg_id BIGINT NOT NULL, ins_code BIGINT NOT NULL,
            PRIMARY KEY (msg_id, ins_code)
        );

        CREATE TABLE IF NOT EXISTS instrument_state (
            ins_code BIGINT NOT NULL, d DATE NOT NULL, state VARCHAR,
            PRIMARY KEY (ins_code, d)
        );

        CREATE TABLE IF NOT EXISTS codal_letter (
            tracing_no BIGINT PRIMARY KEY,
            ins_code BIGINT,
            symbol VARCHAR, company_name VARCHAR, title VARCHAR NOT NULL, letter_code VARCHAR, letter_type_id INTEGER,
            published_at TIMESTAMPTZ, sent_at TIMESTAMPTZ, disclosure_group VARCHAR,
            has_html BOOLEAN, has_excel BOOLEAN, has_pdf BOOLEAN, has_xbrl BOOLEAN, has_attachment BOOLEAN, is_estimate BOOLEAN,
            under_supervision INTEGER,
            report_url VARCHAR, excel_url VARCHAR, pdf_url VARCHAR, attachment_url VARCHAR
        );

        CREATE TABLE IF NOT EXISTS fund (
            reg_no VARCHAR PRIMARY KEY,
            ins_code BIGINT, name VARCHAR NOT NULL, symbol VARCHAR, fund_type INTEGER, type_of_invest VARCHAR,
            manager VARCHAR, initiation_date DATE
        );

        CREATE TABLE IF NOT EXISTS fund_daily (
            reg_no VARCHAR NOT NULL, d DATE NOT NULL,
            issue_nav DECIMAL(24,4), cancel_nav DECIMAL(24,4), statistical_nav DECIMAL(24,4),
            net_asset DECIMAL(28,4), invested_units DECIMAL(24,0),
            daily_return DECIMAL(14,4), weekly_return DECIMAL(14,4), monthly_return DECIMAL(14,4),
            quarterly_return DECIMAL(14,4), six_month_return DECIMAL(14,4), annual_return DECIMAL(14,4),
            stock_share DECIMAL(9,4), bond_share DECIMAL(9,4), cash_share DECIMAL(9,4), deposit_share DECIMAL(9,4), other_share DECIMAL(9,4),
            PRIMARY KEY (reg_no, d)
        );

        CREATE TABLE IF NOT EXISTS macro_daily (
            series VARCHAR NOT NULL, d DATE NOT NULL, source VARCHAR NOT NULL,
            open DECIMAL(24,4), low DECIMAL(24,4), high DECIMAL(24,4), close DECIMAL(24,4),
            PRIMARY KEY (series, d, source)
        );

        CREATE TABLE IF NOT EXISTS macro_monthly (
            series VARCHAR NOT NULL, period VARCHAR NOT NULL, source VARCHAR NOT NULL,
            period_end DATE, value DECIMAL(18,6), note VARCHAR,
            PRIMARY KEY (series, period, source)
        );

        -- TSETMC per-instrument statistics (MarketData/GetInstValueAllInstAllParam); data_type N is the filter variable [isN].
        CREATE TABLE IF NOT EXISTS inst_value (
            ins_code BIGINT NOT NULL, data_type INTEGER NOT NULL, d DATE, value DOUBLE,
            PRIMARY KEY (ins_code, data_type)
        );

        -- Sector (industrial group) names from TSETMC static data, keyed by the two-character cSecVal.
        CREATE TABLE IF NOT EXISTS sector (
            code VARCHAR PRIMARY KEY, name VARCHAR NOT NULL
        );

        CREATE TABLE IF NOT EXISTS market_closure (
            d DATE PRIMARY KEY, reason VARCHAR, origin VARCHAR NOT NULL   -- rules | index
        );

        CREATE TABLE IF NOT EXISTS collection_run (
            run_id VARCHAR PRIMARY KEY,
            job VARCHAR NOT NULL, node VARCHAR NOT NULL,
            started_at TIMESTAMPTZ NOT NULL, finished_at TIMESTAMPTZ,
            status VARCHAR NOT NULL, items INTEGER, failures INTEGER, message VARCHAR
        );
        """;
}
