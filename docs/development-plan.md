# Boors — Iran Stock Market Analysis App: Development Plan

Version 2.4 · 2026-10-17 (25 Mehr 1405) — v2.1 recorded Phase 0; v2.2 Phase 1 (DuckDB market store §4, findings §9.4, status §9.2); v2.3 closed Phase 1 and recorded the Phase 2 backend; v2.4 records the first Phase 2 pages (§9.3)
Supersedes v1.0 (same day). Basis:
- Round 1: [reports/Iran stock market app feasibility.md](../reports/Iran%20stock%20market%20app%20feasibility.md)
- Round 2: [reports/Iran stock app plan round 2.md](../reports/Iran%20stock%20app%20plan%20round%202.md) (stack verification, Iran infrastructure, field-level data specs, UX benchmark, Mehr 1405 rules)

Decisions already made:

| Decision | Choice |
|---|---|
| Stack | .NET 10 LTS + Blazor WebAssembly, Bit.BlazorUI / Bit.Boilerplate 10.6.x (MIT); Python only as a research sidecar |
| Audience | Personal first, designed so it can go public later (public launch gated on an SEO licence) |
| Hosting | Hybrid: one node inside Iran, one node abroad, plus a full offline copy on the desktop client |
| Clients | Web (desktop browser), mobile (PWA plus .NET MAUI Blazor Hybrid), desktop app, Telegram bot (abroad node) and Bale bot (Iran node) |

### What changed since v1.0

1. **TradingView Advanced Charts is out.** Its June 2026 free-licence agreement excludes personal and internal use, requires a company running a public free service, bans public repositories and demands marketing backlinks. **Lightweight Charts 5.2.1** (Apache-2.0, mandatory attribution logo) is the only finance-native free option. Open decision 3 is closed.
2. **Render mode decided: Blazor WebAssembly** (open decision 4 closed). Market diffs every few seconds render client-side instead of through a Server circuit.
3. **Versions pinned and licences checked** for every library (§5.3). PostgreSQL 18 instead of 17. (v2.0 excluded Hangfire in favour of Quartz.NET; P0 reversed that because the template already ships Hangfire on Postgres storage, see §5.3.) ML.NET out of the critical path; ONNX Runtime 1.31 serves models. Microsoft Agent Framework 1.x if agents are needed; Semantic Kernel is legacy.
4. **Local LLM fallback changed** from Dorna2-8B to Qwen3-32B or Gemma-3-27B (MELAC Persian benchmark: 60.3 vs 47.7). Direct OpenAI/Anthropic use from Iran violates their terms; the Iran node uses the Toman-billed **AvalAI** gateway, the abroad node uses vendor APIs directly.
5. **Iran infrastructure specifics added**: Toman price anchors for Liara and Hamravesh, the ParsPack NuGet/npm/Docker mirror, the Telegram Bot API being unreachable from Iran-hosted servers (so the Telegram bot lives abroad, mandatory), Bale Bot API details, FCM/web push unreliable in Iran, SMS via Kavenegar as the dependable channel.
6. **Field-level data specs added** (§3.6–3.9): Codal category/LetterType IDs and the embedded `datasource` JSON in monthly reports (which reverses "Excel first"), the full TSETMC filter-language variable set, `GetMarketWatch` parameters and compact keys, verified `yVal`/`flow`/`cgrValCot`/`cSecVal` codes, the fipiran fund API, tgju live-feed codes.
7. **Rules refined** (§6.3): price limits per instrument class (3% / 4% / 10% / base-market 3-2-1), base volume = 1 only on the main markets (the IFB base market keeps a dynamic base volume), non-equity ETFs trade until 18:00 from 18 Mehr 1405, supervisory halts modelled separately, decomposed fee table (1.25% vs 1.43% round trip), A-share-style tradability filter, purged CV with embargo, LightGBM → ONNX pipeline.
8. **UX benchmark folded in** (§7): multi-chart linked workspaces, command palette, Lite/Pro toggle, explainable natural-language screener with quota, incident-transparent status banner, and a "nothing free moves behind a paywall later" launch principle.

---

## 1. Product vision

Boors is a professional workstation for the Tehran Stock Exchange (TSE) and Iran Fara Bourse (IFB). It collects market data, disclosures and macro data, cleans and versions them, and shows them through live dashboards, charts, screeners, a backtest lab and a portfolio view. The goal is that you can answer three questions quickly and honestly:

1. **What is the market doing right now?** Breadth, money flow, queues, sectors and the index in rial, USD and real terms.
2. **What is happening to a given company, and why?** Price, flows, the order book, disclosures, monthly sales, fundamentals and corporate events.
3. **Would this idea actually have made money here?** Backtests that respect ±3% limits, queues, T+2 settlement, closures, costs and the rial's collapse.

### Guiding principles

- **The data layer is owned infrastructure.** Every response goes into a raw cache, so the app can rebuild everything offline. The endpoints are unofficial and the country's internet can disappear for months.
- **Honest numbers before pretty numbers.** Closure days, halts and queue-locked days are flagged, never silently zero. Adjustment is point-in-time. Returns are always available in rial, USD and CPI-deflated terms, against fixed-income, USD and gold-fund hurdles.
- **Rules are data, not code.** Price limits, close-price method, sessions and fees live in a dated rules table with an instrument-class dimension.
- **Analysis and alerts, not auto-trading.** Orders are placed manually through your broker. Automated retail order placement is not legal outside SEO-approved broker algo platforms.
- **No buy/sell/hold recommendations from the AI layer.** The AI explains, summarises and compiles filters; it never issues signals. That is the regulatory-safe shape (the one Sahmeto uses with a licensed partner).
- **Personal by default, multi-user by design.** Identity, tenancy and licensing hooks exist from day one, but nothing public ships without an SEO type-2 adviser licence or a licensed partner.
- **Public, stable pricing when public.** Nothing offered free at launch moves behind a paywall later. Paywall creep is the top complaint about global tools.

---

## 2. Scope: the complete feature set

Each feature is tagged with its phase (see §9). The list is complete on purpose; the phases decide the order.

### 2.1 Market overview (home) — P2, live in P3
- **KPI row (stat tiles):** TEDPIX, equal-weight index, IFB index, total value traded, individual (حقیقی) net money flow, advancers/decliners, buy-queue and sell-queue counts and values, free-market USD, Exchange Centre USD and the gap between them, gold coin (سکه امامی), gold-fund aggregate NAV and weekly retail inflow, CPI (SCI and CBI, labelled). Each tile shows value, change and a sparkline.
- **Index chart:** TEDPIX with a denomination toggle (rial / USD / CPI-real) and regime shading (war closures, rules eras, Stabilization Fund support weeks).
- **Market map (treemap):** sized by market cap, coloured by % change on a fixed ±limit scale, grouped by sector, paired with a table view. Halted symbols are shown greyed and hatched, never hidden. Tree, bubble and scatter variants (parity with Mofid EasyTrader).
- **Sector performance:** diverging bars of today's sector returns and a separate bar chart of sector individual net flow.
- **Breadth panel:** advancers/decliners/unchanged/halted as a 100% stacked bar per day over 60 days, plus queue counts.
- **Movers tables:** gainers, losers, value traded, individual inflow and outflow, new buy and sell queues, with sparklines.
- **Status banner:** pre-open, open, TAL, extended ETF session, closed; holiday notices; supervisor messages (پیام ناظر); and explicit **data freshness / stale / degraded** states with incident text. Silent outages at the open are the top complaint about Iranian broker apps.

### 2.2 Market watch (دیده‌بان) — P2, live in P3
- A virtualized grid of about 3,100 instruments with configurable columns: last, close, % change, volume, value, individual/institutional split, per-capita buy and sell, buyer power, queue state, P/E, market cap, range position, allowed min/max price.
- Instrument-type tabs driven by `yVal`: shares (300 TSE, 303 IFB), rights (400), ETFs (305), options (320, P7), corporate sukuk (208) and other debt, plus board filters via `cgrValCot`.
- **Filter language:** a sandboxed JavaScript-compatible evaluator implementing TSETMC's official variable set (§3.7) so published community filters paste in unchanged. Filters can be saved, synced across devices and turned into alerts.
- Row sparklines and conditional formatting with text tokens plus coloured markers, never coloured text alone.

### 2.3 Symbol workspace — P2–P4
- **Price chart:** candlesticks with a separate volume pane, daily limit bands, raw/point-in-time-adjusted toggle, event markers (capital increases, DPS, AGMs, halts and reopenings, Codal letters), saved chart templates, indicators (SMA/EMA, RSI, MACD, Bollinger, ATR, OBV, VWAP, Ichimoku) and drawing tools (P2 basic, P5 advanced).
- **Individual/institutional panel (حقیقی/حقوقی):** daily net individual flow (diverging bars), per-capita buy vs sell, buyer power with its historical percentile, 20-day cumulative flow. Separate panes, never dual axes.
- **Order book (live):** five-level best limits as a depth ladder with queue size, value and change; queue detection and queue-break alerts.
- **Trades tape:** intraday trades, labelled as incomplete (the feed under-reports by about 36–38%); official totals come from the closing-price fields.
- **Shareholders:** major holders and changes over time, alerts on large moves.
- **Corporate timeline:** capital increases (with type and stage), DPS, AGM dates and decisions, halts with reason class (event, supervisory, war).
- **Fundamentals (P4):** EPS (TTM and forecast), P/E, P/S, P/B, margins, ROA/ROE, monthly sales (MoM, YoY, cumulative vs last year), production volumes, peer comparison, a visual multi-axis fundamentals score (Simply Wall St-style).
- **Codal feed (P4):** this symbol's letters, classified by LetterType, with parsed numbers and optional AI summaries with jump-to-source.
- **Returns card:** 1-week to 3-year returns in rial, USD and real terms against fixed-income, USD and gold-fund hurdles.

### 2.4 Sectors and rotation — P2/P5
- Sector indices and relative strength vs TEDPIX as small multiples with a common y-scale.
- Sector money flow heatmap (sectors × days, diverging).
- Emphasis line chart: selected sector highlighted, others grey.
- Sector vs driver views indexed to 100 on a common base: steel vs the Exchange Centre dollar (lagged to the weekly IME cycle), petrochemicals vs urea/methanol, copper vs LME. The free-vs-official dollar gap is a regime variable.

### 2.5 Screeners and rankings — P2 basic, P5 research-grade
- Momentum (3/6/12-month), 52-week-high proximity, residual momentum, low volatility.
- Flow signals: net individual trade per account (Moradi 2020 replication), buyer-power persistence, per-capita ratios.
- Fundamentals (P4): P/E, monthly-sales YoY surprise, EPS revisions, ROA/ROE.
- **Explainable natural-language screener (P8):** natural language compiles to the filter language; the UI shows every filter it applied with a per-filter explanation; requests are quota-metered per month (TradingView's 2026 AI Screener pattern).
- Every ranking links to its **backtest verdict**, including "this signal does not work".

### 2.6 Codal centre — P4
- Live feed of all letters with category and LetterType decoding (§3.6), symbol linking and search.
- **Parsed monthly activity reports (ن-۳۰, LetterType 58):** sales and production by product, MoM/YoY, cumulative vs prior year, from the embedded `datasource` JSON.
- Parsed interim and annual statements (ن-۱۰, LetterType 6); the management interpretive report (گزارش تفسیری) found as an attachment of ن-۱۰.
- Material disclosures (ن-۲۰, LetterType 11) with group الف/ب parsed from the title.
- AGM invitations and decisions (LetterTypes 16–18, 20–22), DPS schedule (30), capital-increase chain (23 → 24 → 55/80/56 → 45 → 53 → 57 → 25 → 27 → 119 → 28 → 164).
- Event study of monthly-report surprises vs subsequent returns (P5).
- AI summaries (P8) built only from numbers the parser has already extracted, with jump-to-source links.

### 2.7 Funds — P6
- ETF NAV vs price, premium/discount history (webgw `navValue`, fipiran NAV history).
- Fund types from fipiran `fundType` codes: 4 fixed income, 6 equity, 7 mixed, 5 gold/commodity, 17 fund-of-funds, 18 real estate, 22 leveraged, 23 index, 24 guaranteed.
- Fixed-income fund yields as the **hurdle rate**; gold-fund NAV and flows (`unitsSubDAY`/`unitsRedDAY`).
- Extended-session awareness: non-equity ETFs trade until 18:00 (§3.4).

### 2.8 Macro and alternative assets — P2 basic, P6 full
- Free-market USD (tgju; bonbast fallback), Exchange Centre (مرکز مبادله) remittance dollar as a second series, gold coin, 18k/24k gold, ounce, EUR, AED.
- CPI from both series (SCI nationwide, CBI urban), each labelled.
- Correlation dashboard (rolling correlation of sectors vs USD and gold) and "everything indexed to 100".
- IME / Energy Exchange commodity data through BrsApi (P7).

### 2.9 Backtest lab — P5
- Strategy definition via built-in signals, filter-language expressions or C# plugins.
- Iran-aware engine (§6.3): dated rules table with instrument class, limits, no-limit reopen auctions, queue-untradable days, tradability filter, fill-fraction sweep, T+2 cash, decomposed fees and tax, closure and halt freezing.
- Point-in-time universe (delisted symbols included) and point-in-time adjustment.
- Purged walk-forward with embargo; regime splits; DEV vs FINAL reporting.
- **Tear sheet:** equity curve (rial), USD and real curves in separate panes, drawdown, turnover, cost drag at both fee levels, hit rate, exposure, RankIC series, fill-fraction sensitivity table, excess return vs fixed-income, USD and gold-fund benchmarks.

### 2.10 Portfolio and risk — P6
- Holdings entered manually or imported from broker statements (Excel) — Mofid, Agah, Farabi first.
- P&L in rial, USD and real terms; cost basis accounting for capital increases and rights.
- Cash under T+2; fees and sale tax.
- Risk: weekly volatility, beta, Ledoit-Wolf covariance with halted periods as missing, sector and USD-exposure concentration, liquidity check (position vs 20-day value traded), stress replays (June 2025, Feb–May 2026).
- Sizing suggestions (inverse-vol, risk parity), personal use only.
- Trade journal: entry reason, linked signal, post-trade review.

### 2.11 Alerts and notifications — P3, extended later
- **Triggers:** price or % move; queue formed or broke; individual inflow above threshold; new Codal letter of a given LetterType; monthly-report surprise; halt or reopen; AGM scheduled; filter match; portfolio drawdown; data-health incident.
- **Channels:**
  - in-app via SignalR;
  - **Telegram bot — abroad node only, mandatory.** Iran-hosted servers cannot reach `api.telegram.org` or receive its webhooks. Iran-node alerts reach Telegram through a relay to the abroad node when the link is up.
  - **Bale (بله) bot — Iran node.** Telegram-Bot-API clone at `https://tapi.bale.ai/bot<token>/` (§3.9).
  - **SMS via Kavenegar** as the dependable out-of-app channel inside Iran.
  - web push for users abroad only (FCM token issuance has been broken in Iran since Aug 2022);
  - optional send-only Eitaa broadcasts via eitaayar.ir. No interactive Eitaa or Rubika bots.
  - email.
- Quiet hours, de-duplication, alert history, alerts reachable in ≤2 taps on mobile.

### 2.12 Market calendar — P4
- AGMs (مجامع), DPS payment dates, IPOs (عرضه اولیه), capital-increase deadlines, official holidays (lunar holidays move each year; DNTPersianUtils holiday tables cross-checked against observed non-trading days), known closures, extended ETF sessions.

### 2.13 Data health and operations — P1 (built first)
- Freshness per dataset, endpoint status and latency per node, last successful fetch, schema-drift alerts from contract tests.
- Gap detection; reconciliation diffs against TSETMC's displayed values.
- Node sync status (Iran node, abroad node, desktop clients) and the public-facing status banner feed.

### 2.14 Research sidecar — P5
- Nightly Parquet exports and a Python environment (Polars, DuckDB, LightGBM, statsmodels).
- Models trained in Python export to ONNX (onnxmltools, ZipMap disabled) and are served in .NET by ONNX Runtime.

### Explicitly out of scope
- Automated order placement and "sarkhati" queue-sniping bots.
- Publishing buy/sell signals to other people before a licence is in hand.
- AI-generated buy/sell/hold recommendations in any phase.
- LSTM-style price-level forecasting as a trading signal. It may exist only as a research experiment that must beat the momentum baseline out of sample, net of frictions.
- TradingView Advanced Charts / Trading Platform (licence).

---

## 3. Data sources and collection plan

### 3.1 Source map

| Data | Primary source | Fallback | Frequency | Collected by |
|---|---|---|---|---|
| Instrument master, search, aliases, TAL siblings (suffix ۳/۴), static code lists | `cdn.tsetmc.com/api/Instrument/*`, `api/StaticData/GetStaticData` | webgw (ISIN), old.tsetmc | Daily 07:30, plus on unknown InsCode | Both nodes |
| Daily OHLCV history | `ClosingPrice/GetClosingPriceDailyList/{ins}/0` | old.tsetmc `InstTradeHistory` | Daily after ~13:30 (18:30 for extended-session ETFs), re-run evening | Both |
| Live price snapshot | `ClosingPrice/GetClosingPriceInfo/{ins}` | — | 2–30 s (§3.3) | Iran primary, abroad standby |
| Market watch (bulk) | `GetMarketWatch` with `hEven`/`RefID` incremental polling (§3.8) | webgw market watch; `old.tsetmc.com/tsev2/excel/MarketWatchPlus.aspx?d=0` | 5–10 s in session; snapshot at close | Iran primary |
| Individual/institutional flows | `ClientType/GetClientTypeHistory`, `GetClientTypeInfo` (live), `GetClientTypeAll` (bulk) | old.tsetmc `clienttype` | Live for watchlist; full EOD | Both |
| Order book | `BestLimits/{ins}` (live), `BestLimits/{ins}/{DEven}` (historical delta feed) | `GetMarketWatch` `blDs` | 2–5 s for focused symbols | Iran primary |
| All-instrument values | `MarketData/GetInstValueAllInstAllParam` | — | EOD | Both |
| Intraday trades | `Trade/GetTradeHistory` | — | EOD all; live focused | Both |
| Adjustments, share changes | `GetPriceAdjustList`, `GetInstrumentShareChange` | Rebuild from Codal capital-increase chain | Daily | Both |
| Shareholders | `Shareholder/*` | — | Daily | Both |
| Halts and state | `cEtaval` ("A " = مجاز, "IS" = halted), webgw `CompanyState`, IFB live halted list | Supervisor messages | Live and daily | Both |
| Indices | `GetIndexB2History`, `GetIndexB1LastAll` | webgw | Live 10 s; EOD | Both |
| ETF NAV, bonds YTM, options | webgw market watches; fipiran (§3.9) | BrsApi | 1 min / EOD | Iran primary |
| Fund master, NAV history, flows, yields | fipiran `www.fipiran.com/services/` | TSETMC | Daily | Both |
| Codal letters | `search.codal.ir/api/search/v2/q`; taxonomy from `api/search/v1/categories` | TSETMC `Codal/GetPreparedDataByInsCode` | Every 2 min in the day, 15 min otherwise | Both |
| Codal letter bodies | `Decision.aspx` page (embedded `datasource` JSON) | `excel.codal.ir/service/Excel/GetAll/{serial}/0` (GET), attachments | On new letter | Both |
| USD, gold, coin, EUR, AED | tgju `https://call1.tgju.org/ajax.json` | tgju summary-table JSON; bonbast (token POST); BrsApi | 5 min in the day; EOD | Both |
| Exchange Centre dollar | Exchange Centre / IME weekly base-rate publications | Manual entry | Weekly | Abroad (plus manual) |
| CPI | SCI and CBI releases | Manual entry form | Monthly | Abroad (plus manual) |
| IME / Energy Exchange | BrsApi | ime.co.ir scrape | EOD | Iran |

### 3.2 Client rules (one shared `TsetmcClient`)
- Send a Chrome User-Agent plus `Referer` and `Origin: https://www.tsetmc.com`. Without them the API returns 403. fipiran needs a `Referer` too.
- Per-host token bucket, default **≤2 req/s** for cdn.tsetmc.com, exponential backoff with jitter on 403/429/5xx, circuit breaker that reports to the data-health page (Microsoft.Extensions.Http.Resilience 10.11, Polly v8 pipelines; `Http.Polly` is deprecated).
- 90 s timeout for the Excel market watch. Relax certificate validation **only** for `old.tsetmc.com`, with a pinned-thumbprint check.
- **Raw cache first:** every response is written once, gzip, keyed by `(source, endpoint, params, fetched_at, node)`, *before* parsing. Parsers are pure functions over the raw cache.
- **Contract tests:** nightly, every endpoint for three reference symbols (فولاد, فملی, خودرو) plus one ETF, one option, one sukuk and one rights ticker; schema drift raises a high-priority alert.

### 3.3 Real-time budget (polling; TSETMC has no public push feed)

| Tier | What | Interval | Cost at 2 req/s |
|---|---|---|---|
| Market | `GetMarketWatch` incremental (`hEven`/`RefID`), indices, `GetClientTypeAll` | 5–10 s | ~0.3 req/s |
| Focused | Symbol on screen: price, best limits, client type | 2–3 s | ~1 req/s |
| Watchlist | Up to ~30 symbols, round-robin | ~30–60 s | the remainder |
| EOD | Everything for ~3,100 instruments | After close | ~30–60 min nightly |

A full `GetMarketWatch` snapshot is about 838 KB for shares alone; incremental polling keeps the steady state small. The server polls once and fans out field diffs over SignalR; clients never call TSETMC directly (also avoids CORS). Display latency is about 2–10 s — enough for decisions, not exchange-grade. The UI says so.

### 3.4 Market calendar, sessions and time
- Timezone `Asia/Tehran` (UTC+03:30, no DST since 2022). Store `DEven` ints and UTC timestamps; convert to Jalali only for display (`PersianCalendar` for storage-side conversions, DNTPersianUtils.Core for formatting).
- **Sessions are per instrument class** and come from the rules table:
  - shares, rights, equity funds: Saturday–Wednesday 09:00–12:30 continuous, TAL 12:45–13:00 (from 6 Jun 2026);
  - fixed-income, real-estate, land-and-building, project and fund-of-funds ETFs: until **18:00** from 18 Mehr 1405 (10 Oct 2026) on TSE and 19 Mehr on IFB. Pollers and "market open" status extend accordingly.
- **Closure calendar:** 15–25 Jun 2025 and 28 Feb–18 May 2026 seeded; future closures detected automatically (whole market `zTotTran = 0`, TEDPIX flat).
- Holidays: DNTPersianUtils official-holiday tables per Jalali year, cross-checked against observed non-trading days.

### 3.5 Text normalization
- DNTPersianUtils.Core 7.0.2 (Apache-2.0) for Arabic ي/ك → Persian ی/ک, Persian/Arabic digit conversion, Jalali formatting and business-day tables. Strip ZWNJ variants for matching. Keep a display copy with the original text.
- Symbol names are never the join key; InsCode is (ISIN secondary). ISIN prefixes: IRO1 (TSE share), IRO3 (IFB share), IRO5, IRT1 (ETF), IRO9, IROA, IRR1 (rights), IRB6, IRB3TR.

### 3.6 Codal specification
- **Taxonomy lookup:** `https://search.codal.ir/api/search/v1/categories` returns `Category{Code,Name} → PublisherTypes → LetterTypes{Id,Name}`. The numeric key is `Id` (`Code` is empty).
- **LetterType IDs used:**

| Id | Letter | Form |
|---|---|---|
| 58 | Monthly activity report (گزارش فعالیت ماهانه) | ن-۳۰ |
| 6 | Interim and annual financial statements | ن-۱۰ |
| 32 | Board activity report | — |
| 90 | Internal controls | — |
| 11 | Material disclosure (افشای اطلاعات بااهمیت) | ن-۲۰ |
| 1 / 2 | EPS forecasts (legacy) | — |
| 128 / 147 / 174 | Clarifications | ن-۲۶ (174) |
| 8 | Portfolio statement (investment companies) | — |
| 16 / 17 / 18 | AGM invitations | — |
| 20 / 21 / 22 | AGM decisions (2020/2121/2222 summaries) | ن-۵۲ (20) |
| 30 | DPS schedule (attachment-only) | ن-۱۳ |
| 23 → 24 → 55/80/56 → 45 → 53 → 57 → 25 → 27 → 119 → 28 → 164 | Capital-increase chain | — |

- **Parsing rules:**
  - "گزارش تفسیری مدیریت" is not a letter type; find it by filtering ن-۱۰ `Attachment.aspx` titles for `تفسیری`.
  - Group الف/ب of material disclosures is parsed from the title with a regex tolerant of `-` vs `–` before `گروه (الف|ب)`.
  - URL patterns: `Decision.aspx?LetterSerial=&rt=&let=&ct=&ft=`, `DownloadFile.aspx?hs=&ft=1005&let=`, Excel `https://excel.codal.ir/service/Excel/GetAll/{serial}/0` (GET only). v2 search parameters use `-1` for "no filter".
- **Monthly report (ن-۳۰) structure:** the `Decision.aspx` page embeds `var datasource = {...};` JSON. Manufacturing template "گزارش فعالیت ماهانه تولیدی - V4" tables, matched by **`aliasName`, not numeric code**: `BuyRawMaterial`, `ProductionAndSales`, `Energy`, `ProductMonthlyActivityDesc1`, `SourceUsesCurrency`. Codes vary between letters: the P0 spike on TracingNo 1605314 found 3464 / 1197 / 3195 / 1199 / 3640, where the research notes had 3464 / 3465 / 3466 / 3467 / 3640. Columns: نام محصول / واحد / تعداد تولید / تعداد فروش / نرخ فروش (ریال) / مبلغ فروش (میلیون ریال). Column groups: cumulative to previous month, corrections, corrected, current one-month period, cumulative to month, prior-year cumulative. **Split periods by each cell's `periodEndToDate`, not header text.** Every cell carries `rowCode`, `columnCode`, `rowTypeName`, `valueTypeName`, `dataTypeName`. Excel export is the fallback. Bank, leasing, service and investment-company templates need their own golden files (not yet inspected).
- Stored columns: `letter_type_id`, `category_code`, `letter_code`, `disclosure_group`, `tracing_no`, `publish_at`, `period_end`, `parse_status`.

### 3.7 Filter language specification (TSETMC-compatible)
Source: TSETMC official help (old.tsetmc.com `ParTree=151715`, `LnkIdn` 3196–3199).
- Scalars: `(l18) (l30) (tno) (tvol) (tval) (py) (pf) (pmin) (pmax) (pl) (plc) (plp) (pc) (pcc) (pcp) (eps) (pe) (tmin) (tmax) (z) (mv) (bvol) (cs) (buyop) (predtran)`.
- Order book: `pd1..pd5 zd1..zd5 qd1..qd5 po1..po5 zo1..zo5 qo1..qo5`.
- Client type: `(ct).Buy_CountI (ct).Buy_CountN (ct).Buy_I_Volume (ct).Buy_N_Volume (ct).Sell_CountI (ct).Sell_CountN (ct).Sell_I_Volume (ct).Sell_N_Volume`.
- History: `[ih][n].PClosing PDrCotVal ZTotTran QTotTran5J QTotCap PriceMin PriceMax PriceYesterday PriceFirst`, where `[ih][0]` is the previous trading day, about 60 days deep, zeros on no-trade days.
- Statistics: `[is1]..[is90]` (e.g., `is5`/`is6` average volume 3/12 months, `is9`/`is10` average trade count, `is50+` client-type averages).
- JavaScript operators, `Math.*`, and the `true==function(){...}()` code mode.
- Implementation: a sandboxed JS-compatible evaluator (no reflection or IO, CPU timeout) with lazy loading of `[ih]`, `(ct)` and `[is]`. `(ob1).*`-style names are unverified and not supported until confirmed.

### 3.8 TSETMC market watch and instrument field semantics
- **`GetMarketWatch` working call:** `market=0&industrialGroup=&paperTypes[0..8]=1..9&showTraded=false&withBestLimits=true&hEven=0&RefID=0`; response key `marketwatch`. Poll incrementally with the last `hEven`/`RefID`.
- **Compact keys (verified 2026-10-17: 19/19 fields matched on the top 10 traded shares after the close, via `boors verify mw-keys`):** `lva` symbol, `lvc` name, `pdv` last, `pcl` close, `py` yesterday, `pf` first, `pmn`/`pmx` day min/max, `pMin`/`pMax` allowed thresholds, `qtj` volume, `qtc` value, `ztt` trade count, `bv` base volume, `csv` sector, `ztd` shares, `insID` ISIN, `pmd`/`pmo` best bid/ask, `hEven`, `blDs[{n,qmd,zmd,pmd,pmo,zmo,qmo,rid}]` order-book levels.
- **`GetClosingPriceInfo`:** `pDrCotVal` last, `pClosing` close, `priceMin`/`priceMax`, `priceYesterday`, `priceFirst`, `zTotTran`, `qTotTran5J`, `qTotCap`, `hEven`, `last`.
- **`GetInstrumentInfo`:** `yVal` (300 TSE share, 303 IFB share, 305 ETF, 320 option, 208 corporate sukuk, 400 rights; 301/304/309/403 unverified), `flow` (1 بورس, 2 فرابورس, 3 مشتقه; 4 = base market unverified), `cgrValCot` board (N1/N2/N4/NS/Z1/1Z/H1/51/13/17 with `cgrValCotTitle`), `cSecVal` two-character sector code (full list from `GetStaticData`), `baseVol`, `psGelStaMax`/`psGelStaMin` allowed range, `cEtaval`, `kAjCapValCpsIdx` (probable free-float %). `paperTypes` 1–9 from `GetStaticData`.

### 3.9 Funds, macro and messaging endpoints
- **fipiran:** base `https://www.fipiran.com/services/` (use `www.fipiran.com`, not `fund.fipiran.ir`), `Referer` required. `fund/fundtype` (GET), `fund/fundcompare/` (POST), `fund/getfund`, `chart/getfundchart` (NAV history), `chart/getfundnetassetchart` (net assets, `unitsSubDAY`/`unitsRedDAY`), `chart/portfoliochart`, `chart/alphabetachart`. Fields: `regNo`, `netAsset`, `dailyEfficiency` … `annualEfficiency`, `issueNav`, `cancelNav`, `statisticalNav`, `fundType`, `typeOfInvest`.
- **tgju live feed:** `https://call1.tgju.org/ajax.json` → `current.{code}.{p,h,l,d,dp,dt,t,ts}`; `p` is a thousands-separated rial string. Codes: `price_dollar_rl`, `price_eur`, `price_aed`, `geram18`, `geram24`, `sekee` (سکه امامی, verified), `sekeb`, `nim`, `rob`, `gerami`, `mesghal`, `ons`, `retail_sekee`.
- **Bale Bot API:** `https://tapi.bale.ai/bot<token>/METHOD`, Telegram method names; webhooks on ports 443/88; last 2,000 updates kept 24 h; `sendMessage` ≤4,096 chars; always Markdown (no `parse_mode`); interactive rate limits with `retry_after`; Bale-only `askReview`/`inquireTransaction` via raw HTTP; prepaid Business API at `/business/bot<TOKEN>/` for bulk; IRR payments via `sendInvoice` (test token `WALLET-TEST-1111111111111111`). Implement with Telegram.Bot's base-URL override (or HexaBale/BaleSharp); prefer user-initiated chats to stay in the free tier.
- **Kavenegar SMS:** Advanced plan about 600,000 T/year for OTP lookup; operator cost 11.6–34.6 T per 70-char part (1405).

---

## 4. Data model (core)

**Changed in P1.** Market data lives in an **embedded DuckDB file per node** (`{DataRoot}/market.duckdb`, `Boors.Market.Storage.MarketStore`), not in PostgreSQL/TimescaleDB. PostgreSQL 18 remains the app database: identity, tenants, watchlists, alerts and Hangfire jobs, via the template's `AppDbContext`.

Reasons:
- The same engine runs on the Iran node, the abroad node and the offline desktop copy (P7), with no server to operate.
- The store is a rebuildable projection of the raw cache (`boors rebuild` reconstructs it in seconds), so it needs no replication: nodes exchange raw-cache bundles instead.
- At this volume (about 15 million daily rows when complete), columnar DuckDB answers analytical queries faster than a row store, and the Timescale licence caveats go away.
- It can be tested without Docker.

The cost: one writing process per file (collection jobs run one at a time per node), and multi-user serving at scale would need a read replica or Postgres later. That is acceptable for a personal-first app and can be revisited in P9.

The tables below are the DuckDB schema as implemented (schema version 2, `MarketStoreSchema.cs`); the user/app tables stay in Postgres.

| Table | Key | Notes |
|---|---|---|
| `instrument` | ins_code | ISIN, symbol, name, `y_val`, `flow`, `cgr_val_cot`, `c_sec_val`, `paper_type`, instrument class (for rules), listing/delisting dates, TAL sibling map, free-float |
| `instrument_name_history` | ins_code, valid_from | Symbol renames |
| `daily_raw` (hypertable) | ins_code, d_even | **Unadjusted** OHLC, close, last, VWAP, volume, value, trade count, allowed min/max, base volume, `day_status`, `halt_reason`, `rules_version` |
| `client_type_daily` | ins_code, d_even | Individual/institutional buy and sell: volume, value, count |
| `intraday_snapshot` (hypertable) | ins_code, ts | Live snapshots (2-year retention, compressed) |
| `best_limits` (hypertable) | ins_code, ts, level | Order book; reconstructed history from delta feeds |
| `trade_tick` (hypertable) | ins_code, ts, n | Tape, marked incomplete |
| `corporate_event` | ins_code, effective_date, type | Adjustment factor, share counts, DPS, `known_at`, linked Codal letter |
| `instrument_state` | ins_code, ts | `cEtaval`, halt reason class, reopen-auction flag |
| `shareholder_snapshot` | ins_code, d_even, holder | % and shares |
| `index_daily` / `index_intraday` | index_code, d_even / ts | — |
| `codal_letter` | tracing_no | ins_code, `letter_type_id`, `category_code`, `letter_code`, `disclosure_group`, publish_at, URLs, `parse_status` |
| `monthly_activity` | ins_code, period_end, table_code, row_code | Product, unit, production qty, sales qty, rate, amount; period class (current / cumulative / prior-year) |
| `financial_statement` | ins_code, period, item | Normalized line items, audited, consolidated |
| `fund` / `fund_daily` | reg_no / reg_no, d_even | fipiran master; NAV (issue/cancel/statistical), net assets, units in/out, returns |
| `macro_daily` / `macro_monthly` | series, date | Free USD, Exchange Centre USD, gold, coin; CPI by source |
| `market_calendar` | date, instrument_class | trading / closed / holiday / extended session |
| `rules_version` | id, from_date, instrument_class | Limits, close method, session, fees (YAML in git, mirrored into the DB) |
| `signal_value` | signal, ins_code, d_even | Precomputed factors |
| `user`, `watchlist`, `filter`, `alert`, `portfolio`, `transaction`, `journal` | tenant_id + id | Multi-tenant from day one |

**`day_status` values:** `traded`, `no_trade`, `symbol_halted`, `market_closed`, `reopen_auction`, `queue_locked_up`, `queue_locked_down`.
**`halt_reason` classes:** `event` (AGM, disclosure), `supervisory` (transparency review, suspected manipulation), `war`, `other`.
Every statistic uses traded days only. Non-traded days get null returns, never zero; the next traded day carries the move and is tagged.

**Point-in-time adjustment:** raw prices kept permanently. The adjusted series is built on demand as of date *t* from `corporate_event` rows with `effective_date ≤ t`, cached per (instrument, as_of), and validated against TSETMC's adjusted output and oxtapus.

---

## 5. Architecture

### 5.1 Topology (hybrid)

```
 ┌──────────────── Iran node (Liara / Hamravesh Darkube / home server) ─────────────────────────────────────┐
 │ TSETMC/webgw, Codal, fipiran, tgju, BrsApi ─► Collectors ─► Raw cache (MinIO) ─► Parsers ─► Postgres 18 │
 │   ─► API + SignalR ─► Web/PWA clients in Iran ─► Bale bot, Kavenegar SMS ─► AvalAI LLM gateway            │
 └──────────────────────────────────▲────────────────────────────────────────────────────────────────────────┘
                                    │ outbound-from-Iran sync over plain TLS to a non-blocked domain,
                                    │ queue + retry; inbound fallback when reachable
 ┌──────────────────────────────────▼──── Abroad node (EU VPS, or Hamravesh Germany region) ─────────────────┐
 │ Independent collectors ─► Raw cache ─► Parsers ─► Postgres ─► API ─► Web for use abroad                  │
 │ Telegram bot (the ONLY place it can run), vendor LLM APIs, CI/CD, container registry, backups, research   │
 └───────────────────────────────────────────────────────────────────────────────────────────────────────────┘
 Desktop app (MAUI Blazor Hybrid): local SQLite + DuckDB replica synced from whichever node is reachable; fully usable offline.
```

**Design rules:**
- **The Iran node runs autonomously for weeks.** On 8 Jan 2026 the domestic network was largely inaccessible and international traffic ran at 1–4% through March–April 2026. It never needs foreign package feeds at runtime.
- Both nodes collect independently; the raw cache is the source of truth; sync is idempotent on `(source, endpoint, params, fetched_at)`.
- **Telegram Bot API is unreachable from Iranian hosts**; the Telegram bot lives abroad, with an Iran → abroad alert relay.
- "Internet Pro" / Pro SIM is a mobile APN product (about 2,000,000 T per 40–50 GB, 2–5 GB/day caps, registered businesses only), **not** a data-centre egress guarantee. Ask the hosting provider in writing what international egress it guarantees.

**Build and deploy constraints:**
- CI runs abroad, builds self-contained images and pushes them to a registry reachable from Iran (domestic registry or image export to the Iran node).
- Package mirrors from Iran: NuGet `https://mirror.abrha.net/repository/nuget/index.json` (`dotnet nuget add source … --name ParspackMirror`), npm `https://mirror.abrha.net/repository/npm/`, plus ParsPack Docker registry/engine mirrors; nuget.org as fallback; vendor the .NET SDK installer.
- Hosting price anchors (Oct 2026 list prices): Liara Saturn 4,125,000 T/month (4 GB / 2 vCPU) with managed PostgreSQL on the same tariff and S3 storage 475,000 T per 20 GB; Hamravesh Darkube 595,000 T/vCore and 290,000 T/GB RAM in Iran (990,000 / 480,000 T in Germany), external traffic 500 T/GB, dedicated IP 4,800,000 T/year. Estimate **8–9 million T/month** for the Iran node.

### 5.2 .NET solution layout

Generated from **Bit.Boilerplate 10.6.2** in P0 (`dotnet new bit-bp --database PostgreSQL --filesStorage S3 --api Integrated --multitenant true --aspire true --signalR true --offlineDb true --notification true --cloudflare false`), then extended with the market-data projects. The template's own structure is kept; market code lives under `src/Market` so it stays independent of the Blazor stack:

```
Boors.slnx / Boors.sln / Boors.Web.slnf
├─ rules/rules.yaml                      # dated market rules per instrument class (embedded into Boors.Market)
├─ docs/                                 # this plan
├─ src/
│  ├─ Market/                            # added in P0; no dependency on Boors.Shared or Blazor
│  │  ├─ Boors.Market                    # domain, raw cache, TSETMC/Codal clients + parsers, rules, day status, calendar
│  │  ├─ Boors.Market.Cli                # `boors` developer CLI: probe, fetch …, verify mw-keys
│  │  └─ Boors.Market.Tests              # MSTest unit + golden-file tests (real responses, 2026-10-17)
│  ├─ Server/
│  │  ├─ Boors.Server.Api                # API controllers, AppDbContext (Postgres), SignalR hubs, Hangfire jobs
│  │  ├─ Boors.Server.Web                # startup project: Blazor host, SSR
│  │  ├─ Boors.Server.Shared             # Aspire service defaults
│  │  └─ Boors.Server.AppHost            # Aspire orchestration (Postgres, S3, Mailpit, …)
│  ├─ Client/
│  │  ├─ Boors.Client.Core               # shared Blazor UI; offline DB (Bit.Besql) + CommunityToolkit.Datasync sync
│  │  ├─ Boors.Client.Web                # Blazor WebAssembly + PWA (Bswup)
│  │  ├─ Boors.Client.Maui               # Blazor Hybrid: Android, iOS, macOS, Windows
│  │  └─ Boors.Client.Windows            # Windows Forms Blazor Hybrid
│  ├─ Shared/Boors.Shared                # DTOs, resources (.resx), shared services
│  └─ Tests/Boors.Tests                  # template UI/integration tests (Playwright, bUnit, Aspire testing)
└─ research/                             # (P5) Python sidecar: notebooks, LightGBM training → ONNX
```

`Boors.Market` will split along its folders (Analytics, Backtest, Filters, Alerts) only when a folder needs dependencies of its own; v2.0's one-project-per-concern layout was dropped as premature.

Offline sync, checked in P0 at the code level: the client keeps an EF Core SQLite database in the browser cache via Bit.Besql and syncs through CommunityToolkit.Datasync table controllers with `Version`-based conflict detection (sample: `OfflineTodoPage`). End-to-end conflict behaviour is tested in P7.

### 5.3 Key libraries (versions and licences as of 2026-10-17)

| Concern | Choice | Version | Licence / note |
|---|---|---|---|
| Runtime | .NET 10 LTS | GA 11 Nov 2025, supported to 10 Nov 2028 | — |
| UI components | Bit.BlazorUI / Bit.Boilerplate | 10.6.2 (11.0 pre) | MIT; native Persian calendar, RTL `BitDataGrid` with row/column virtualization and `OnRead` |
| Financial charts | **TradingView Lightweight Charts** | 5.2.1 | Apache-2.0 **with mandatory attribution** (`attributionLogo`); native multi-pane, price-positioned markers, `tickMarkFormatter`/`timeFormatter` for Jalali; no RTL axis option (not needed) |
| Analytical charts | Apache ECharts | 6.1.0 | Apache-2.0; treemap, heatmap, calendar, matrix, big-data mode |
| Grids | BitDataGrid (QuickGrid as fallback) | 10.6.2 | Cell-level patching is an app-level pattern: mutate rows in place, one `StateHasChanged` per batch, CSS flash keyed on change timestamp. P3 spike: 3,000 rows × 5 s |
| HTTP resilience | Microsoft.Extensions.Http.Resilience | 10.11.0 | Polly v8 pipelines; `Http.Polly` deprecated |
| Scheduling | **Hangfire** (template-integrated) for scheduled jobs; `BackgroundService` + `PeriodicTimer` for in-process pollers | Hangfire.AspNetCore 1.8.25 + Hangfire.EntityFrameworkCore 0.9.0 | **Changed in P0.** The boilerplate already wires Hangfire with EF Core storage in the app's own Postgres database (schema `jobs`), plus health checks and OpenTelemetry. The research objection was paid Redis storage, which is not used; Hangfire core is LGPL-3.0, which permits use as a library. Adding Quartz would mean running two schedulers, so it is not added. |
| Indicators | Skender.Stock.Indicators | current | — |
| Statistics | MathNet.Numerics | current | MIT |
| Excel/HTML | ExcelDataReader / ClosedXML; AngleSharp | current | — |
| Persian utilities | DNTPersianUtils.Core | 7.0.2 | Apache-2.0 |
| DB (app) | PostgreSQL 18 (template `AppDbContext`) | 18.6 | Identity, tenants, user data, Hangfire storage. TimescaleDB dropped in P1 (market data moved to DuckDB, see §4) |
| DB (market data) | DuckDB via DuckDB.NET.Data.Full | 1.5.6.1 | MIT; one file per node; rebuildable from the raw cache |
| ORM | EF Core 10 + Npgsql | Npgsql EF 10.0.3 | JSON complex types, LeftJoin, named query filters |
| Analytics engine | DuckDB.NET.Data.Full + Parquet.Net | 1.5.6.1 / 6.1.0 | MIT |
| Filter language | **Jint** (JavaScript interpreter) | 4.17.0 | BSD-2-Clause. Added in P2: TSETMC filters are JavaScript, so an embedded, sandboxed interpreter gives exact compatibility, including code mode |
| Cache / pub-sub | Redis (in-process cache while single-node) | — | — |
| ML serving | Microsoft.ML.OnnxRuntime | 1.31.0 | Supports LightGBM graphs. **ML.NET stays out of the critical path** (6.0 previews only) |
| LLM abstraction | Microsoft.Extensions.AI `IChatClient` | 10.10.x | OllamaSharp 5.4.25 as local provider; Microsoft Agent Framework 1.x (GA Apr 2026) only if multi-step agents are needed; Semantic Kernel legacy |
| Bots | Telegram.Bot | 22.10.3.3 | MIT; Bot API 10.3; base-URL override for Bale |
| Observability | OpenTelemetry → Aspire dashboard (dev); Grafana/Loki/Tempo or Seq (prod) | Aspire 13.6 | — |
| Desktop/mobile | .NET MAUI 10 Blazor Hybrid | 10 | Windows + Android first; no Linux target; Hot Reload instability accepted. Photino.Blazor and Electron.NET are stale |
| Local store (desktop) | SQLite via EF Core 10 (app state) + DuckDB (analytics) | — | LiteDB avoided (stale stable) |
| Font | Vazirmatn | v33.003 | OFL-1.1, variable font |
| Tests | MSTest 4 on Microsoft.Testing.Platform (template standard), Playwright, bUnit | 4.4 | **Changed in P0** from xUnit to match the template. Golden files are JSON under `Boors.Market.Tests/GoldenFiles`. |

**Licence blockers:** TradingView Advanced Charts / Trading Platform (excluded). **Cautions:** Lightweight Charts attribution; Timescale TSL constraints; Syncfusion Community thresholds if ever considered; SciChart per-developer pricing; AG Grid Enterprise.

### 5.4 Real-time pipeline

```
Pollers (per tier, PeriodicTimer) ─► raw cache ─► parser ─► in-memory MarketState (immutable snapshots)
                                                      │
                   diff engine (changed fields only) ─► SignalR groups: market, symbol:{ins}, watchlist:{user}
                                                      │
                   alert evaluator (filters, queues, flows, Codal, health) ─► channels
                                                      │
                   snapshot writer ─► intraday_snapshot / best_limits (Timescale)
```
- Clients (WASM) subscribe to groups, receive compact field diffs every tick and a full snapshot on (re)connect; grid rows are mutated in place and re-rendered once per batch.
- Queue detection: queue-locked up when best bid = upper limit, zero ask volume and bid volume above threshold; mirror for down. Track queue value over time and the traded-to-queue ratio; alert when a queue weakens past X%.

---

## 6. Analytics specification

### 6.1 Market-flow metrics (traded days only)
- **Individual net flow** = individual buy value − individual sell value, by symbol, sector and market.
- **Per-capita buy/sell** = individual buy (sell) value ÷ individual buyer (seller) count.
- **Buyer power** = per-capita buy ÷ per-capita sell, shown with its historical percentile per symbol.
- **Net individual trade per account** (Moradi 2020), replicated exactly as a factor.
- **Smart-money accumulation** heuristic, labelled as such until validated in the lab.
- Confirmed by a 1405 XGBoost/SHAP study on TEDPIX direction: real/legal flow, gold price, RSI, volume, MACD, ROA and ROE were the top features — so gold and ROA/ROE join the factor library.

### 6.2 Factors (P5)
Momentum (skipping the last month), 52-week-high proximity, residual momentum, low volatility, size, value (E/P, S/P, B/P), ROA/ROE, monthly-sales YoY surprise, flow factors (net per account, per-capita ratios), gold and USD regime, calendar context. Stored daily in `signal_value`.

### 6.3 Backtest engine rules
- Signals use information to the close of *t*; fills on *t+1* at open or VWAP, never the close of *t*.
- **Tradability filter at the signal date** (A-share practice): drop names locked or halted on the next day. Infer locks as close = ceiling/floor with a queue present.
- **Buys blocked** on days locked at the upper limit; **sells blocked** on days locked at the lower limit. Days that touched the limit but traded across a range use a fill fraction swept over {0, 10, 20, 30}%. Where reconstructed `best_limits` exist, use actual queue depth; the IFB base market's traded-to-queue ratio is a fill-probability feature.
- Test every momentum signal **with and without limit-locked days** (AEA 2025: continuation halves when limit-hit observations are excluded).
- Positions freeze through `market_closed` and `symbol_halted` days. Reopenings happen via an **unlimited-band discontinuous auction**, modelled as a single auction print, not a sequence of ±3% days. Disclosure halts reopen within two business days; supervisory halts are multi-day.
- Cash reusable after T+2 by default, with a broker buying-power switch.
- Regimes: pre-Jun-2025, the ±3% era, post-22-Dec-2025 VWAP close (main markets), post-May-2026, Stabilization Fund support weeks.
- Reports always in rial, USD-deflated (free rate) and CPI-deflated (SCI and CBI), with excess return vs fixed-income, USD and gold-fund benchmarks — a 35–41% nominal fixed-income hurdle at ~90% inflation flatters equities.
- Anti-bias guards: point-in-time universe and adjustment; **purged walk-forward CV with an embargo at least as long as the label horizon** (20 trading days for monthly labels); hard check that no feature timestamp exceeds decision time; DEV vs FINAL reporting (a 2026 study saw RankIC decay 0.072 → 0.010).

The source of truth is [rules/rules.yaml](../rules/rules.yaml), embedded into `Boors.Market` and loaded by `MarketRules`. Summary:

```yaml
# rules/rules.yaml — each block applies from its start date until superseded, per instrument class
- from: 2025-01-01            # exact ±5%→±3% switch date unknown; infer from psGelStaMax/Min vs previous close
  price_limit:
    tse_stock: 0.03
    ifb_stock: 0.03
    rights: 0.03
    etf_equity: 0.03
    etf_fixed_income: 0.03
    etf_real_estate: 0.03
    fund_leveraged: 0.04
    fund_gold_silver: 0.10
    ifb_base: {yellow: 0.03, orange: 0.02, red: 0.01}
  close_method: {default: base_volume_weighted}
- from: 2025-09-23            # 1 Mehr 1404 — IFB base market dynamic base volume (v2.0 of this plan wrongly said 2024-09-22)
  close_method: {ifb_base: dynamic_base_volume}
- from: 2025-12-22            # base volume = 1 share on TSE and IFB main markets only
  close_method: {tse_stock: vwap, ifb_stock: vwap, rights: vwap, etf_equity: vwap}
- from: 2026-06-06
  session:
    default: {continuous: "09:00-12:30", tal: "12:45-13:00", tal_suffix: "۳"}
- from: 2026-10-10            # 18 Mehr 1405 TSE (19 Mehr IFB)
  session:
    etf_fixed_income: {continuous: "09:00-18:00"}
    etf_real_estate: {continuous: "09:00-18:00"}
    fund_of_funds: {continuous: "09:00-18:00"}
costs:
  exchange: 0.00155
  csd: 0.00100
  technology: 0.00065
  brokerage: {min: 0.0, max: 0.00144}   # parameter; report at discounted (~1.25%) and list (~1.43%) round trips
  sale_tax: 0.005                        # uncapped, on gross sale value
  ifb_round_trip_total: 0.01408
settlement_days: 2
reopen: {method: unlimited_auction, disclosure_halt_max_business_days: 2}
```

### 6.4 Portfolio and risk
Weekly returns; Ledoit-Wolf shrinkage with **halted periods treated as missing, not zero**; inverse-vol and risk-parity weights; sector and USD-exposure limits; max drawdown; stress replays; liquidity check (position vs 20-day value traded).

### 6.5 AI layer (P8, guarded)
- **Codal summaries:** code extracts numbers; the LLM receives structured JSON of changes and writes a Persian or English explanation with citations to letter IDs and jump-to-source links. Any number not present in the input is rejected.
- **Natural-language screener:** NL → filter language; the UI shows the compiled filters and a per-filter explanation; monthly quota per user. **Never a buy/sell/hold recommendation.**
- **Ask-the-data:** NL → filter language or SQL over a read-only view, previewed before running.
- **Sentiment (optional):** self-labelled dataset, attention/contrarian gauge only.
- **Access policy (two routes):** vendor APIs directly from the abroad node; the Toman-billed OpenAI-compatible **AvalAI** gateway (`https://api.avalai.ir/v1`) from the Iran node during link outages. OpenAI and Anthropic exclude Iran from supported regions (OpenAI geo-block since July 2024; Anthropic's September 2025 ownership terms), so direct use from Iranian IPs or Iranian-owned entities is a terms violation. Local fallback: **Qwen3-32B or Gemma-3-27B** via Ollama (MELAC Persian benchmark 60.3; Dorna2 scores 47.7, below its Llama-3.1-8B base).
- **Persian LLM caveat:** no Persian financial benchmark exists; evaluate on 200 sample Codal letters before enabling.

---

## 7. Visualization and workstation design

### 7.1 Chart catalog

| Need | Form | Library | Colour job | Rules |
|---|---|---|---|---|
| Headline numbers | **Stat tiles** in a KPI row (value + delta + sparkline) | Blazor + tiny SVG | Text tokens; ▲▼ sign | No one-bar charts |
| Price history | Candlesticks + **separate** volume pane | Lightweight Charts | Up/down pair | Limit bands dashed; event markers; raw/adjusted toggle; templates; attribution logo visible |
| Index rial vs USD vs real | **Denomination toggle**, or 3 lines indexed to 100 | Lightweight Charts | Categorical slots 1–3 | **Never a dual axis** |
| Whole-market snapshot | **Treemap** (cap × % change) by sector, with table view; bubble and scatter variants | ECharts | Diverging, fixed −limit…+limit, grey midpoint | Halted hatched grey; treemap for overview, table for exact comparison |
| Sector returns today | Horizontal **diverging bars**, sorted | ECharts | Diverging | Direct labels |
| Money flow over time | **Heatmap** (sector × day) | ECharts | Diverging | Fixed symmetric scale; table view |
| Individual net flow | Diverging columns + cumulative line in own pane | Lightweight Charts | Diverging | Zero baseline emphasized |
| Breadth | 100% stacked bars (≤4 categories, direct-labelled) | ECharts | Categorical | 2px gaps |
| Order book | Horizontal depth ladder | Blazor + CSS | Low-saturation up/down | Queue value as text, not only bar length |
| Sector rotation | **Small multiples** (common y) or emphasis line | ECharts | Emphasis: 1 hue + grey | Never 12 coloured lines |
| Seasonality | **Calendar heatmap** (Jalali months × years) | ECharts | Diverging | Jalali labels |
| Value vs growth | Scatter, ≤3 groups else one hue | ECharts | Sequential / ≤3 categorical | Labelled outliers only |
| Monthly sales | Columns (this vs last year) + YoY % in a small separate chart | ECharts | 1 hue, 2 shades | — |
| Fundamentals score | Radar/“snowflake” 5-axis visual | ECharts | 1 hue | Paired with numbers |
| Backtest equity | Line + drawdown pane + RankIC pane + sensitivity table | Lightweight Charts / ECharts | 1–3 categorical | Shaded regimes |
| Correlation | Matrix heatmap | ECharts | Diverging (−1…+1) | Values on hover and in table |
| Allocation | Horizontal stacked or sorted bars (no pies > 2 slices) | ECharts | Categorical ≤8, rest "Other" | — |

### 7.2 Colour rules
- **Up/down:** Iranian convention (green up, red down) by default, always with secondary encoding (sign, ▲/▼, position vs baseline). Colour-blind-safe preset (blue up, orange/red down) in settings. Both pairs validated with the dataviz palette validator in light and dark modes before release.
- **Diverging scales:** neutral grey midpoint, symmetric fixed domains; for % change the domain is the instrument's daily limit with an overflow colour for reopen-auction days.
- **Sequential:** one hue, light to dark (value traded, market cap).
- **Categorical:** fixed order, follows the entity (a sector keeps its colour everywhere), ≤8 slots, rest folds into "Other".
- **Status colours** reserved for data health and alerts, always with icon and label.
- Text stays in text tokens, never series colours.

### 7.3 Workstation interaction and layout
- **Multi-chart grid layouts** with **colour-linked symbol groups** (Quantower/TradingView pattern) and **lockable, saved workspaces** per user.
- **Command palette** (keyboard-first symbol and function search, Bloomberg-style command line).
- **Lite/Pro toggle**: Lite hides dense panels for quick decisions; Pro exposes everything.
- Crosshair and tooltip on every time series; synchronized crosshairs across panes; per-mark tooltips on bars, cells and tiles.
- One filter row above charts (Jalali date range, market, sector, denomination).
- "View as table" and CSV/Excel export on every chart.
- **Dark mode default** with its own validated steps (no automatic inversion).
- **RTL shell, LTR time axes**; numerals stay LTR; Jalali ticks via `Intl.DateTimeFormat('fa-IR-u-ca-persian')` or pre-formatted labels from C# (Day.js Jalali plugins are stale); Persian/Latin digits toggle.
- **Status and incident transparency**: explicit loading, stale, degraded and incident states on every live screen.
- Mobile: big live price on top, 3-tab bottom navigation, alerts in ≤2 taps, Lite mode default, dense views limited to watchlist, symbol summary and alerts.
- No abrupt UI redesigns once in daily use; layout changes ship behind a toggle first.
- Before any chart ships, render it and check by eye for label collisions, overflow and RTL mirroring bugs.

### 7.4 Typography
Vazirmatn v33.003 (OFL) variable font, tabular figures in grids and tickers. Persian digits optional for display; storage and exports always ASCII.

---

## 8. Cross-cutting concerns

| Concern | Plan |
|---|---|
| Security | ASP.NET Identity (passwordless + 2FA from the boilerplate); per-tenant isolation; filter-language sandbox (no reflection or IO, CPU timeout); secrets in vault/env; TLS everywhere; rate limits on the public API |
| Legal | Personal mode ungated. Public mode behind a feature flag hiding rankings and signals until a licence exists. TradingView attribution logo always visible. Timescale TSL: tenants never get schema or raw SQL access. Data redistribution reviewed with a lawyer (financial-information-processor licence: 60 billion rials paid-in capital, vs 50 million T for a type-2 adviser private JSC). Disclaimers on every analytic page. Design for consent and deletion now; a personal-data-protection law passed cabinet in July 2024 and may arrive mid-project. |
| Resilience | Two collectors, raw-cache replay, circuit breakers, offline desktop replica, daily Postgres dump + raw cache to both nodes, Iran node autonomous for weeks |
| Performance | Timescale continuous aggregates for rollups; precomputed factors; DuckDB for backtests; SignalR diffs; incremental market watch |
| Testing | Golden-file parser tests from real raw responses (all instrument types; all Codal templates); nightly contract tests; reconciliation tests (20 symbols vs TSETMC); property tests for the rules engine (no fill outside limits, no look-ahead); Playwright e2e for key screens; 3,000-row grid spike |
| Localization | fa-IR (default) and en-US; Jalali/Gregorian toggle; all strings in resources |
| Observability | Freshness SLOs (EOD complete by 14:30, or 18:30 for extended-session ETFs; live tick age < 15 s in session) surfaced on the data-health page and status banner |

---

## 9. Phased roadmap

Durations assume **one full-time developer**; multiply by about 2 for part-time. Every phase ends with a usable app.

| # | Phase | Weeks | Main deliverables | Exit criteria |
|---|---|---|---|---|
| 0 | **Foundations** | 2 | Solution from Bit.Boilerplate 10.6.2; Aspire AppHost (Postgres 18 + Timescale, MinIO, Redis); CI abroad with image export + ParsPack mirrors; `rules.yaml` with instrument classes; `TsetmcClient` with raw cache; instrument master with `yVal`/`flow`/`cgrValCot`/`cSecVal`; verify `GetMarketWatch` compact keys and boilerplate offline sync | Instrument list and one symbol's full history fetched from both nodes; compact-key mapping confirmed |
| 1 | **Data platform** | 4 | All EOD collectors (prices, flows, events, shareholders, indices, fipiran, tgju, Exchange Centre rate, CPI); parsers; schema; `day_status` + `halt_reason`; closure calendar; PIT adjustment; contract tests; **data-health page**; raw-cache sync | Unattended nightly rebuild; 20-symbol spot checks match TSETMC; closures and halts flagged; nodes in sync |
| 2 | **MVP workstation** | 5 | Market overview (EOD), market watch grid with TSETMC-compatible filters, symbol workspace (Lightweight Charts with templates and events, flows, shareholders), sectors, basic screeners, watchlists, Jalali/RTL, dark mode, command palette, PWA, auth | You use it daily instead of TSETMC for end-of-day review |
| 3 | **Live layer and alerts** | 3 | Tiered pollers with incremental market watch, MarketState, SignalR diffs, live grid (3,000 × 5 s spike), live order book, queue detection, alert engine, Telegram bot (abroad) + Bale bot (Iran) + Kavenegar SMS, status banner with incident states | Live screens lag < 10 s; alerts fire correctly on a replayed day; Iran-node alerts reach Telegram via relay |
| 4 | **Codal and fundamentals** | 6 | Letter poller with taxonomy; `datasource` JSON parser for ن-۳۰ (manufacturing first, then bank/leasing/service/investment templates); financial statements; interpretive-report attachment detection; AGM/DPS/capital-increase calendar; fundamentals tab incl. ROA/ROE and snowflake | Monthly sales series for the top 100 companies; parse success > 90% on ن-۳۰ pages |
| 5 | **Backtest lab and research** | 6 | Rules-table engine with tradability filter and auction reopenings; fill model; tear sheet with RankIC; factor library; Moradi replication; monthly-report event study; filter language → strategy; Python sidecar with purged CV; LightGBM → ONNX → ONNX Runtime | Every built-in signal has a published net-of-friction verdict at both fee levels |
| 6 | **Portfolio, funds and macro** | 4 | Portfolio import (Mofid, Agah, Farabi), P&L in rial/USD/real, T+2 cash, risk report, sizing, journal; ETF premium/discount; fixed-income hurdle; gold-fund flows; macro and correlation dashboards | Your real portfolio tracked with rial/USD/real P&L vs three hurdles |
| 7 | **Clients and resilience** | 4 | MAUI Blazor Hybrid (Windows, Android) with local replica and offline mode; multi-chart linked workspaces; Lite/Pro toggle; IME via BrsApi; options and bonds; **blackout drill** | Desktop usable with the network unplugged; drill passes both ways |
| 8 | **AI layer** | 4 | Codal summaries with number verification and jump-to-source; explainable NL screener with quota; ask-the-data; AvalAI + abroad-vendor routing; local Qwen3/Gemma-3 fallback | Summaries pass number checks on 200 sample letters; zero hallucinated figures |
| 9 | **Public readiness** (gated) | 6+ | Multi-tenant hardening; Zarinpal billing (0.5% capped 16,000 T + 500 T) or Zibal (1% capped 20,000 T); Enamad (600,000 T, FATA vetting); PWA + Cafe Bazaar (15% on first 1 bn T/year, then 30%) + Myket; licence-gated signal features; public stable Toman price list; legal review; load testing | SEO type-2 adviser licence (private JSC, 50 million T capital) or licensed partner in hand |

**Total to a complete personal app (P0–P8): about 38 weeks full-time.** P2 (week 11) replaces TSETMC for daily review; P3 (week 14) gives live monitoring and alerts.

### 9.1 Phase 0 status (2026-10-17)

| # | Task | Status |
|---|---|---|
| 1 | Solution from Bit.Boilerplate 10.6.2; Aspire; package mirror | **Done.** Generated with PostgreSQL, S3 storage, Aspire, SignalR and offline DB. Microsoft packages bumped from 10.0.12 to 10.0.13, which fixes the NU1902/NU1903 vulnerability warnings reported at restore. The ParsPack NuGet mirror is reachable but not yet added as a package source. Docker/Podman is not installed on the dev machine, so `aspire start` (Postgres, S3) cannot run yet. |
| 2 | `TsetmcClient` + raw cache + CLI | **Done.** `Boors.Market` has the resilience pipeline (total timeout → retry including 403 → circuit breaker → 2 req/s token bucket → per-attempt timeout) and a write-once gzip raw cache with latest-pointers. The `boors` CLI has `probe`, `fetch instruments/history/marketwatch/index/static/codal-categories/codal-letters` and `verify mw-keys`, with `--cache`/`--offline` replay. |
| 3 | Static data + compact-key verification | **Done.** 19/19 market-watch keys verified, 10/10 symbols each. |
| 4 | `rules.yaml`, day-status classifier, closure calendar | **Done**, except `halt_reason`, which needs instrument-state history (P1). Index-based detection also found an unexplained pair of flat TEDPIX rows on 2018-07-26/27 (Thursday/Friday) to investigate. |
| 5 | Golden-file tests | **Done** for فولاد (all endpoints), TEDPIX and Codal, plus GetInstrumentInfo for ذوب, کرمان (IFB), بلوط (ETF), an option, a sukuk and a rights ticker. فملی/خودرو histories not yet captured. 74 tests passing. |
| 6 | Codal taxonomy + one ن-۳۰ `datasource` | **Done.** Taxonomy parsed; the datasource spike succeeded and is saved as a P4 golden file; table codes corrected in §3.6. |
| 7 | Deploy the collector to both nodes; reachability | **Partly done.** Probe from the dev machine: all 10 market-data sources OK; Telegram reachable (so this machine is not behind Iran's filter); Bale and AvalAI reachable. Needs the Iran and abroad nodes to exist. |

### 9.2 Phase 1 status (2026-10-17)

| Deliverable | Status |
|---|---|
| EOD collectors | **Done**, in `Boors.Market.Ingestion.MarketCollector`. Jobs: instruments (market watch + fipiran fund types + GetInstrumentInfo details), indices (57 TSE + IFB index list, history of 7 main indices), backfill (history, flows, published and implied adjustments, share changes; optional supervisor messages), end of day (3 bulk calls: market watch, same-day flows, index snapshots), funds, macro (tgju: USD, coin, 18k gold, ounce, EUR), Codal letters, supervisor messages, halt resolution. Exchange Centre rate and CPI come in through `boors import macro <csv>` (no machine-readable source). |
| Parsers | **Done** for every endpoint above; all tolerate the shapes seen live (e.g. `instrumentState: []`, fipiran's `0001-01-01` dates). |
| Schema | **Done**, as a DuckDB store (see §4); idempotent merges with precedence rules (history beats market watch; official flows beat estimated flows; class source fund > yVal > ISIN). |
| `day_status` + `halt_reason` | **Done.** Halts are resolved by asking `GetInstrumentState` for the first day of each zero-trade run; the reason comes from the nearest supervisor message (disclosure, general meeting, supervisory, underlying). Reasons stay "Unknown" where the message archive does not reach. |
| Closure calendar | **Done:** rules.yaml closures plus days where TEDPIX repeated its close (106 days recorded). |
| PIT adjustment | **Done, with a change of method** (§9.4): returns are `Close / Yesterday − 1`, and adjusted series use factors implied by the bars. Verified on فولاد: compounding returns reproduces the adjusted path within 0.034% over 19 years. |
| Contract tests | **Done:** `boors verify contracts` checks 19 endpoints against `Contracts/baseline.json`, with known-optional fields and re-sampling. |
| Data-health page | **Backend done:** `DataHealthService`, the `boors health` CLI, `GET api/v1/MarketData/GetHealth` (Operations_View policy), a `marketData` health check, and `POST RunJob/{job}`. The Blazor page itself moves to P2, when the bit MCP tools are available. |
| Raw-cache sync | **Done:** `boors cache export/import` bundles (a union import; latest-pointers only move forward). |
| Server integration | **Done, compile-verified only** (no local Postgres/Docker to run the server): services registered in `Program.Services.cs`; Hangfire schedules in Iran time when `MarketData:Collect` is true (instruments 07:30, EOD 13:35 and 18:35 Sat–Wed, messages every 30 min, Codal every 15 min, macro 4×/day, funds 20:00, indices 01:15, backfill 02:00, halts 03:00). |
| Exit criteria | Unattended rebuild: **yes** (`boors rebuild`, cache-only; 352 instruments in 87 s). 20-symbol spot checks vs TSETMC: **yes**, `boors verify reconcile --count 20` matched **71/71 symbol-days** (history rows against a fresh history fetch, today's market-watch rows against GetClosingPriceInfo). Closures and halts flagged: **yes**. Nodes in sync: **mechanism done**, untested between real nodes. **Phase 1 is complete** except the real two-node test. |

Live state on the dev machine: 3,298 instruments, ~60 instruments backfilled (the full ~1,000 takes ~35 min with `boors collect backfill`), today's session appended, 27.5k index rows, 27.7k macro rows, 516 funds, 400 messages. 104 unit/integration tests pass.

### 9.3 Phase 2 status (2026-10-17): backend and first pages done

The read side and the first five pages are built. The app itself has not been run end to end yet: that needs Docker/Podman for Aspire or a local PostgreSQL. The pages were checked instead by bUnit render tests with faked controllers and by screenshots of their rendered markup with the real Bit.BlazorUI and app stylesheets, in light, dark and Persian RTL.

| Deliverable | Status |
|---|---|
| Market overview data | **Done:** `MarketAnalytics.GetOverview`. Covers headline indices (TEDPIX, equal-weight, IFB) with changes, equity and fund value, breadth, halts, buy/sell queues and their values from the close snapshot, individual net flow and per-capita figures (flagged when estimated), USD and coin, and TEDPIX change in dollars. |
| Breadth, sectors, movers | **Done:** `GetBreadth`, `GetSectors` (cap-weighted and median returns, flows, advancers/decliners, names from TSETMC static data), `GetMovers` (gainers, losers, value, inflow, outflow). |
| Symbol workspace data | **Done:** `GetInstrumentMetrics` gives point-in-time adjusted 52-week range, 1w–1y returns, the 1-year return in USD, 20-day value and flows, and buyer power with its own-history percentile. The API serves bars (raw + PIT-adjusted close + status + return), flows, corporate events (implied and published), shareholders (refreshed on demand at most daily), Codal letters and supervisor messages. |
| Screeners (basic) | **Done:** momentum 6-1, 52-week-high proximity, individual-flow intensity (20 sessions), buyer power (5 sessions). Computed in DuckDB SQL with the backtest return convention. |
| Market watch + TSETMC filters | **Done.** `MarketWatchService` builds the grid for a session (queue state and value, P/E, market cap, per-capita figures, individual buy share). `TsetmcFilter` runs TSETMC filter expressions in the **Jint** JavaScript interpreter: `(pl)`, `(ct).Buy_I_Volume`, `pd1…qo5`, `[ih][n].PClosing`, `[isN]` (from `GetInstValueAllInstAllParam`, collected at EOD), and the `true==function(){...}()` code mode. It is sandboxed: no .NET access, a statement cap, a timeout and a memory cap; a runaway loop is stopped. Verified live: 3,023 rows per evaluation. |
| Command palette search | **Done:** `MarketAnalytics.Search` (exact symbol, then prefix, then name; primary listings first). |
| API | **Done, compile-verified:** `IMarketController` (overview, breadth, sectors, movers, screen, search, market watch POST) and `IInstrumentController` (metrics, bars, flows, events, shareholders, letters, messages) with shared DTOs, Mapperly mapping, JSON source-gen registration and generated client proxies. |
| CLI preview | `boors market overview | breadth | sectors | movers <kind> | screen <kind> | metrics <symbol> | search <text> | watch --filter "…"` shows the same numbers in the terminal. |
| Market overview page (`/market`) | **Done.** KPI tiles (indices, TEDPIX in dollars, value traded, breadth, queues, individual flow, dollar, coin); TEDPIX 1-year line with a rial/dollar toggle (dollar view indexed to 100, never a dual axis); 20-session 100% breadth bars; sector returns as sorted diverging horizontal bars; movers grid with five lists. |
| Market watch page (`/market/watch`) | **Done.** TSETMC filter box with nine example filters, class groups (shares, funds, rights, all), traded-only switch, virtualized grid with frozen symbol column, quick search, CSV/Excel export, queue tags, per-instrument filter errors listed and syntax errors shown next to the box. |
| Symbol workspace (`/symbol/{insCode}`) | **Done.** Header, 1w–1y and USD returns, 52-week range, market cap, 20-day value and flow, buyer power with percentile; candlesticks (raw or point-in-time adjusted) with value and individual-flow panes; 1M–5Y ranges; tabs for capital events, shareholders, Codal letters (links out) and supervisor messages. |
| Screens page (`/market/screens`) | **Done** for the four basic screens, each with a one-line description and a reminder that a rank is not a signal. |
| Data-health page (`/market/health`) | **Done.** Datasets, last runs, source reachability and buttons that queue each collection job; behind the Operations_View policy. |
| Symbol search | **Done** as a search box on every market page (suggestions from `Search`, Enter opens the best match). A keyboard command palette is still to do. |
| Navigation | **Done:** a "Market" group in the side menu; data health only for operators. |
| Jalali/RTL, dark mode | **Done for these pages.** Dates are Jalali under the fa culture (the template already gives fa the Persian calendar). Signed percentages are isolated as left-to-right runs so RTL does not reorder them. Charts stay left to right in every culture, as on TSETMC. |
| Tests | 5 bUnit tests in `src/Tests/Features/Market` (overview with data and empty, market watch with filter errors and a syntax error, symbol workspace with Codal links, screens and health). They pass in a scratch harness; inside `Boors.Tests` they need Docker because the assembly initializer starts Aspire. |
| Watchlists, command palette, sector page, treemap, synchronized crosshairs, Vazirmatn font, Persian digits, colour-blind preset | **Not started.** Next in this phase. |

**Chart library deviation.** §7.1 names Lightweight Charts and ECharts. The first pages use **BitChart** (Bit.BlazorUI.Extras, native SVG, theme tokens, no JavaScript chart runtime) for everything, including candlesticks built from floating bars plus error-bar wicks. That covers the overview and symbol needs today. Lightweight Charts stays the plan for the live intraday chart (P3) if BitChart's zoom/pan and redraw speed are not enough at that point, and ECharts for the treemap and calendar heatmap.

**BitChart behaviours found while checking the screenshots** (worth reporting upstream):
- With `IndexAxis = Y` the `"x"` scale stays the category scale and is drawn vertically, and `"y"` stays the value scale. Chart.js swaps them. Configuring `"x"` as linear, as Chart.js expects, leaves the chart empty.
- With an RTL page direction the SVG axis labels inherit `direction: rtl`, their anchors flip, and the y-axis labels overlap the plot. The pages force `direction: ltr` on the chart SVG.
- Animations run in C# and do not reliably finish under bUnit, so render tests must not assert on bar geometry.

Live sample (2026-10-17): TEDPIX 8,265,157 (−1.91%); in dollar terms −3.37%; equities 30.8 hemat traded; 146 up / 604 down; 71 buy queues (2.19 hemat) and 143 sell queues (1.59 hemat); individual net flow −6.8 hemat. فولاد: +114% over one year in rials, −11.8% in free-market dollars. Tests: 115 market-data tests passing, plus the 5 page tests.

### 9.4 Data findings from Phase 1 (live, 2026-10-17)

These change how the data must be handled; each is encoded in the code and tests.

1. **TSETMC's published adjustment list is incomplete and dated wrongly.** For فولاد, `GetPriceAdjustList` has 23 events but the bars imply 30. Most capital increases since 2020 are missing (2020-01-12, 2021-05-24, 2022-08-09, 2023-03-19, 2024-02-27, 2025-03-12, 2026-03-30, all matching `GetInstrumentShareChange`). Since 2020 it also dates events by meeting date rather than the day the new base price applied (e.g. listed 2026-08-03, effective 2026-08-05). **Use** `priceYesterday`, which is the exchange's adjusted base price: daily return = `Close / Yesterday − 1`, and implied factor = `Yesterday / previous Close`. Both sources are stored (`price_adjustment.source`).
2. **Most corporate actions take effect on days with no trades** (the symbol is still halted after its general meeting), so no trade filter may be applied to implied adjustments.
3. **TAL/block sibling listings** (e.g. `آباد3`, ISIN ending `0003`) never trade on their own and roll their reference price daily. They produced thousands of false "adjustments" until they were linked to their parent (`instrument.parent_ins_code`; inferred rule: stock ISINs ending in `0001` are primary) and excluded from history collection. 275 found.
4. **The market watch has two response variants for the same request**, apparently per CDN cache period. One is compact and honours `paperTypes` (887 rows for shares); the other is extended, ignores `paperTypes` (3,299 rows) and adds `yVal`, `flow` and `cGrValCot`. Consequences: always filter by class client-side, use `yVal` when present, and treat those fields as optional in contract checks.
5. **Codal rate-limits bursts with HTTP 429.** The Codal client now defaults to one request every 2 seconds.
6. **`GetClientTypeAll`** gives same-day individual/institutional volumes and counts for ~2,200 instruments in one call, but no rial values. EOD estimates the values from the day's average price (`values_estimated = true`), and history backfill replaces them with official values.
7. **`GetInstrumentState/{ins}/{date}`** returns an object on dates with a state record and `[]` otherwise (unknown, not "permitted").
8. **fipiran's fund list maps 516 funds to InsCode and fund type** in one call. That re-classified 287 ETFs into their rules classes (fixed income, leveraged, gold, etc.).
9. **Supervisor messages** often name a numbered sibling (`فولاد1`), and some symbols contain a space (`پی پاد`). Linking falls back to the symbol without trailing digits.

Original task list:

1. `dotnet new install Bit.Boilerplate@10.6.2`; create the solution; add the projects from §5.2; Aspire with Postgres 18 + Timescale, MinIO, Redis; add the ParsPack NuGet mirror as a secondary source.
2. Implement `TsetmcClient` (headers, rate limiter, Polly pipelines, raw cache) and a CLI: `boors fetch instruments`, `boors fetch history <ins>`, `boors fetch marketwatch`.
3. Load `GetStaticData` (sectors, paper types) and verify the `GetMarketWatch` compact-key mapping against `GetClosingPriceInfo` for 10 symbols; record results in the data spec.
4. Write `rules.yaml` v1 with instrument classes; implement the `day_status`/`halt_reason` classifier; seed the closure calendar.
5. Golden-file tests from saved responses for فولاد, فملی, خودرو, one ETF, one option, one sukuk, one rights ticker.
6. Fetch `search.codal.ir/api/search/v1/categories` and store the taxonomy; parse one ن-۳۰ `datasource` JSON end to end.
7. Deploy the collector to both nodes; verify reachability from each (including TSETMC/Codal from the EU VPS, which is still unproven) and log results on the data-health page.

---

## 10. Risks and mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| TSETMC endpoints change or break | High | High | Raw cache, contract tests, fallbacks (webgw, old.tsetmc Excel, BrsApi), parser isolation |
| Foreign IPs blocked from TSETMC/Codal | Medium | Medium | Iran node is primary collector; test from the EU VPS in P0 |
| Domestic internet blackout | Medium | High | Iran node autonomous for weeks; desktop offline replica; Bale and SMS for alerts |
| Telegram Bot API unreachable from the Iran node | Certain | Medium | Telegram bot abroad; Iran → abroad relay |
| FCM/web push unreliable in Iran | High | Low | Bale, SMS, in-app; push only for users abroad |
| LLM vendor terms exclude Iran | Certain | Medium | AvalAI gateway from Iran, vendor APIs from abroad, local Qwen3/Gemma-3 fallback |
| Rate limiting or IP ban | Medium | Medium | ≤2 req/s, incremental market watch, server-side fan-out, backoff |
| Rules change (limits widened, T+1, new halts) | High | Medium | Dated rules table per instrument class; inference checks against `psGelStaMax/Min` |
| Source response variants and rate limits (market-watch variants, Codal 429) | Certain | Medium | Client-side filtering, known-optional contract fields, per-source token buckets (Codal 0.5 req/s), retries with backoff, circuit breakers |
| DuckDB single-writer limit | Medium | Low | Jobs serialized per process; one store file per node; rebuild from the raw cache; revisit for multi-user serving in P9 |
| Bad data (phantom rows, adjusted-price leakage, incomplete tape, inferred compact keys) | Certain | High | `day_status`, PIT adjustment, official totals from closing-price fields, P0 key verification |
| Signals don't survive frictions | High | Medium (that is the point) | Lab reports honestly; only validated signals get promoted |
| Blazor Hot Reload instability; no MAUI Linux | Medium | Low | Accept; Windows + Android first |
| bit platform bus factor (1.4k stars, small team) | Low–Medium | Medium | MIT; fork option; keep component usage behind thin wrappers |
| Data-protection law arrives mid-project | Medium | Medium | Consent and deletion designed now |
| Legal exposure going public | Medium | High | Feature flags, licence gating, no AI recommendations, legal review before launch |
| Package/registry access from Iran | High | Medium | CI abroad, self-contained images, ParsPack mirrors |
| Scope creep | High | High | Phase exit criteria; P5+ only if earlier phases are in daily use |

---

## 11. Open decisions

Closed in v2.0: **(3)** charting → Lightweight Charts 5.2.1; **(4)** render mode → Blazor WebAssembly (Auto only if first paint proves a problem; multithreaded WASM arrives with .NET 12).

Still open (settle during P0–P1):
1. **Iran-node provider:** Liara (simplest managed stack, ~4.1 M T/month per 4 GB app or DB) vs Hamravesh Darkube (per-resource, Germany region available for the abroad node billed in Toman, with the sanctioned-counterparty caveat) vs a home server. Get the international-egress guarantee in writing.
2. **Third fallback feed:** subscribe to BrsApi (or another paid feed) beyond the free tier?
3. **Broker statement formats** to support first for portfolio import (Mofid, Agah, Farabi assumed).
4. ~~**Scheduler**~~ — closed in P0: the template's Hangfire (EF Core storage on Postgres) for scheduled jobs, `BackgroundService` for pollers.
5. **Abroad-node region:** EU VPS vs Hamravesh Germany, pending the TSETMC/Codal reachability test from abroad.
