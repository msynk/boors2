# Data Sources and APIs for Iranian Capital Market Data (TSE, IFB, IME, IRENEX), status as of Oct 2026

Method note: besides web research, I tested endpoints live with curl on 2026-10-17 from a **US IP address** (the result of `ifconfig.io/country_code` was `US`). Where a finding says "live test 2026-10-17", the cited URL is the exact endpoint I called, and the observation is mine. Iranian news sites (mehrnews.com, tahlilbazaar.com) and ime.co.ir timed out from the same IP during the session, while tsetmc, codal, tgju, bonbast and brsapi answered.

## 1. TSETMC (tsetmc.com / old.tsetmc.com / cdn.tsetmc.com): endpoints, coverage, restrictions, breakage, official service

### Takeaway
The free, unofficial JSON API at `cdn.tsetmc.com/api/...` is **working in Oct 2026 and reachable from a non-Iranian (US) IP** as long as you send a browser User-Agent. It covers daily OHLCV history, intraday trades, best-limits order book (current and historical), historical real/legal (haghighi/hoghoughi) flows, shareholders, capital changes, price-adjustment events, indices, ETF NAV, Codal letters and supervisor messages. Some legacy `old.tsetmc.com/tsev2` endpoints still work, including the Excel market watch and the text export. A second, official-infrastructure gateway, `webgw.tse.ir`, adds a full market watch with ETF NAV, options and bonds YTM. I found no public, documented, paid official TSETMC web service with published terms.

### Cited Findings
**Endpoint catalogue (community-maintained, reverse-engineered, no SLA)**
- BabakEslami/tse-market-data documents about 93 endpoints: 63 on `cdn.tsetmc.com` and 30 on `webgw.tse.ir`. The authors say they are unofficial, found by observing network traffic, and "may change at any moment without notice". The master reference v4.1 is dated 1405/04/24 (2026-07-15) and was last fully tested in Tir 1405 (Jun–Jul 2026). — [BabakEslami/tse-market-data README](https://github.com/BabakEslami/tse-market-data/tree/main); [ENDPOINTS.md](https://raw.githubusercontent.com/BabakEslami/tse-market-data/main/ENDPOINTS.md)
- Key cdn.tsetmc.com endpoints. All are keyed by the numeric `InsCode`; dates use the Gregorian `DEven` format YYYYMMDD. — [ENDPOINTS.md](https://raw.githubusercontent.com/BabakEslami/tse-market-data/main/ENDPOINTS.md)
  - Daily history: `/api/ClosingPrice/GetClosingPriceDailyList/{InsCode}/{Top}`. `Top=0` returns the full history. A CSV variant exists: `GetClosingPriceDailyListCSV`. Others are `GetChartData/{InsCode}/D`, `GetClosingPriceDaily/{InsCode}/{DEven}`, `GetInstrumentCalendar/{InsCode}`, and `GetInstrmentsHistoryInDay/{DEven}` (all instruments on one date, about 1,923 rows).
  - Intraday: `/api/Trade/GetTrade/{InsCode}` (last day), `/api/Trade/GetTradeHistory/{InsCode}/{DEven}/false`, `GetTradeIntraDay`, `GetTradeVolume`. Also `/api/ClosingPrice/GetClosingPriceHistory/{InsCode}/{DEven}` (intraday snapshots).
  - Order book: `/api/BestLimits/{InsCode}` (current 5 levels) and `/api/BestLimits/{InsCode}/{DEven}` (historical). The historical one is a **delta feed** that needs stateful carry-forward reconstruction.
  - Real/legal flows: `/api/ClientType/GetClientType/{InsCode}/1/0`, `/api/ClientType/GetClientTypeAll` (all symbols, last day), `/api/ClientType/GetClientTypeHistory/{InsCode}/{DEven}`.
  - Instrument: `/api/Instrument/GetInstrumentSearch/{query}`, `GetInstrumentInfo` (EPS, sector, thresholds), `GetInstrumentIdentity`, `GetInstrumentHistory/{InsCode}/{DEven}`, `GetInstrumentShareChange/{InsCode}` (capital increases).
  - Adjustments: `/api/ClosingPrice/GetPriceAdjustList/{InsCode}` and `GetPriceAdjustByFlow/{Flow}/{Top}`.
  - Indices: `/api/Index/GetIndexB1LastAll/SelectedIndexes/1` (TSE, 7 indices) and `/2` (IFB), `/All/{1|2}` (including industry indices), `GetInstEffect`, `/api/ClosingPrice/GetIndexCompany/{indexInsCode}`. TEDPIX `insCode` is 32097828799138957.
  - Market data: `/api/MarketData/GetInstrumentState/{InsCode}/{DEven}`, `GetInstrumentStatistic`, `GetMarketOverview/{Flow}`, `GetSectorsSummary`, `GetStaticThreshold`, `GetInstValueAllInstAllParam` (about 24 MB).
  - Funds: `/api/Fund/GetETFByInsCode/{InsCode}` (issue/redemption NAV), `GetFundInDetail/{0|1}`.
  - Shareholders: `/api/Shareholder/{InsCode}/{DEven}` and others.
  - Codal: `/api/Codal/GetPreparedData/{Top}`, `GetPreparedDataByInsCode/{Top}/{InsCode}`.
  - Supervisor messages: `/api/Msg/GetMsgByInsCode/{InsCode}`.
  - Energy exchange: `/api/Energy/GetAuctionById/{AuctionId}`.
  - Static data: `/api/StaticData/GetStaticData`.
- Known broken or empty: `/api/ClosingPrice/GetMarketWatch` returns empty. The working replacement is the legacy Excel `https://old.tsetmc.com/tsev2/excel/MarketWatchPlus.aspx?d=0`, which has about 3,133 symbols in one sheet but no InsCode, ISIN or sector columns. — [ENDPOINTS.md](https://raw.githubusercontent.com/BabakEslami/tse-market-data/main/ENDPOINTS.md)
- Quality warning: in the authors' samples, `GetTradeHistory` came out about 36–38% short of official trade statistics. For daily totals, use the `ClosingPrice` fields `qTotTran5J` (volume), `qTotCap` (value) and `zTotTran` (count). — [ENDPOINTS.md](https://raw.githubusercontent.com/BabakEslami/tse-market-data/main/ENDPOINTS.md)
- A `User-Agent` header is **mandatory** on cdn.tsetmc.com; without it the server returns 403. Browser-side calls hit CORS/403, so you need a backend proxy. The authors' monitor script also sends `Referer`/`Origin: https://www.tsetmc.com` and retries on 429/502/503/504 with exponential backoff (0.6 s base). — [ENDPOINTS.md](https://raw.githubusercontent.com/BabakEslami/tse-market-data/main/ENDPOINTS.md)
- To detect suspended or halted symbols, check `cEtaval`/`cEtavalTitle` in `instrumentState`. `"IS"` / «ممنوع-متوقف» means halted, and empty history for that day is then expected. — [ENDPOINTS.md](https://raw.githubusercontent.com/BabakEslami/tse-market-data/main/ENDPOINTS.md)
- A new "Trading At Last" session started on 20 Aban 1404 (≈ 11 Nov 2025): 12:45–13:00 at the fixed 12:30 closing price, main-board first-market symbols only. Those symbols carry a "4" suffix, e.g. «بترانس۴». It is exposed via webgw `MarketWatchTALMarket`. — [ENDPOINTS.md](https://raw.githubusercontent.com/BabakEslami/tse-market-data/main/ENDPOINTS.md)

**Live tests 2026-10-17 from a US IP, all HTTP 200 with real data:**
- `GetClosingPriceDailyList` for Foolad (InsCode 46348559193224090) returned rows through dEven 20261014. Fields: `priceFirst, priceMax, priceMin, pClosing, pDrCotVal (last), priceYesterday, zTotTran, qTotTran5J, qTotCap, hEven`. — [live test](https://cdn.tsetmc.com/api/ClosingPrice/GetClosingPriceDailyList/46348559193224090/3)
- Full history for Fameli (35425587644337450) with `Top=0` returned 4,675 rows from 2007-02-04 to 2026-10-14. — [live test](https://cdn.tsetmc.com/api/ClosingPrice/GetClosingPriceDailyList/35425587644337450/0)
- `GetClientTypeHistory` returned buy/sell volume, value and count split into individual (`_I_`) and legal (`_N_`). — [live test](https://cdn.tsetmc.com/api/ClientType/GetClientTypeHistory/46348559193224090/20261014)
- Other endpoints that returned data:
  - `GetInstrumentSearch` (URL-encoded Persian query) returned insCode, lVal18AFC, lVal30, flow and cgrValCot. — [live test](https://cdn.tsetmc.com/api/Instrument/GetInstrumentSearch/%D9%81%D9%88%D9%84%D8%A7%D8%AF)
  - `BestLimits` (current 5 levels). — [live test](https://cdn.tsetmc.com/api/BestLimits/46348559193224090)
  - Shareholders (e.g. IMIDRO 16.709%). — [live test](https://cdn.tsetmc.com/api/Shareholder/46348559193224090/20261014)
  - `GetTradeHistory`. — [live test](https://cdn.tsetmc.com/api/Trade/GetTradeHistory/46348559193224090/20261014/false)
- Two endpoints that the reference flagged as empty **did return data in my test**:
  - `GetPriceAdjustList` returned adjustment events with `pClosing` vs `pClosingNotAdjusted`, e.g. 2026-08-03: 2549 vs 2604. — [live test](https://cdn.tsetmc.com/api/ClosingPrice/GetPriceAdjustList/46348559193224090)
  - `GetInstrumentShareChange` returned capital increases, e.g. 2026-03-30: 1.5T → 1.935T shares. — [live test](https://cdn.tsetmc.com/api/Instrument/GetInstrumentShareChange/46348559193224090)
  - This contradicts the ⚠️ "returns empty" flags in [ENDPOINTS.md](https://raw.githubusercontent.com/BabakEslami/tse-market-data/main/ENDPOINTS.md).
- Index endpoints worked:
  - `GetIndexB2History/32097828799138957` returned TEDPIX daily history from 2008-12-04 (4,306 rows). — [live test](https://cdn.tsetmc.com/api/Index/GetIndexB2History/32097828799138957)
  - `GetIndexB1LastAll/SelectedIndexes/1` returned live TEDPIX at 8,265,163 (−1.91%) on 2026-10-17. — [live test](https://cdn.tsetmc.com/api/Index/GetIndexB1LastAll/SelectedIndexes/1)
  - `GetClosingPriceDailyList` with an index InsCode returns an empty list, so use `GetIndexB2History` for indices. — [live test](https://cdn.tsetmc.com/api/ClosingPrice/GetClosingPriceDailyList/32097828799138957/2)
- Legacy endpoints still alive:
  - `old.tsetmc.com/tsev2/data/Export-txt.aspx?t=i&a=1&b=0&i={InsCode}` returns a MetaStock-style CSV (`<TICKER>,<DTYYYYMMDD>,<FIRST>,<HIGH>,<LOW>,<CLOSE>,<VALUE>,<VOL>,<OPENINT>,<PER>,<OPEN>,<LAST>`). — [live test](https://old.tsetmc.com/tsev2/data/Export-txt.aspx?t=i&a=1&b=0&i=46348559193224090)
  - `MarketWatchPlus.aspx?d=0` returned an xlsx of 376,887 bytes. The first try timed out at 30 s; it succeeded with a 90 s timeout. — [live test](https://old.tsetmc.com/tsev2/excel/MarketWatchPlus.aspx?d=0)
  - `instinfofast.aspx` returned a 911-byte gzip/binary-looking body to plain curl. — [live test](https://old.tsetmc.com/tsev2/data/instinfofast.aspx?i=46348559193224090&c=57)
- `webgw.tse.ir` `MarketSummary` **answered from the US IP** (TEDPIX 8,265,163.3; market value; trade count 869,513). This contradicts the README's claim that webgw is "only from Iranian IP". — [live test](https://webgw.tse.ir/InstrumentProvider/api/v1/Instrument/MarketSummary/fa); [README claim](https://github.com/BabakEslami/tse-market-data/tree/main)
- webgw.tse.ir is keyed by **full ISIN**, not InsCode. — [ENDPOINTS.md](https://raw.githubusercontent.com/BabakEslami/tse-market-data/main/ENDPOINTS.md)
  - `MarketWatch/MarketWatchCash|Etf|Option|Debt|Future|TradeOption|TALMarket`. The ETF watch has `navValue`; the debt watch has YTM fields.
  - `Instrument/LiveInstrumentByIdQuery` (level-1 book), `InstrumentClientType` (live only, no history).
  - `History/Archive` (full daily history including **daily market value**).
  - `Instrument/CompanyState` (suspension reasons text).
  - A TradingView UDF feed: `/TradingView/history?symbol={Persian name}&resolution=1D`, daily only.
  - `MarketMap` heatmap with EPS/PE.

**Libraries: status and what broke**
- `tsetmc` (5j9) is the most actively maintained Python wrapper of the new endpoints. It is async, built on Polars, needs Python ≥3.13, latest release 5.1.0 on 2026-08-11 on PyPI. — [PyPI JSON](https://pypi.org/pypi/tsetmc/json); [5j9/tsetmc](https://github.com/5j9/tsetmc)
- `pytse-client` (Glyphack, 315 stars) last released 0.19.1 on 2025-03-13 and last pushed 2025-03-15, with 42 open issues. — [GitHub](https://github.com/Glyphack/pytse-client); [PyPI JSON](https://pypi.org/pypi/pytse-client/json)
  - Issue #270 (2025-08-01): `SYMBOLS_LIST_URL` gives TooManyRedirects, so symbols such as «سیسکو» come back as "Symbol not found". — [pytse-client issues](https://github.com/Glyphack/pytse-client/issues)
  - Issue #274 (2025-12): P/E fetch errors for symbols such as «مهرگان» and «سرچشمه». — [pytse-client issues](https://github.com/Glyphack/pytse-client/issues)
  - Issue #271: historical shareholder changes need one request per day, which is very slow. — [pytse-client issues](https://github.com/Glyphack/pytse-client/issues)
- `finpy-tse` (ARahimiQuant, 100 stars) last released 1.2.10 on 2024-04-24 with no pushes since; it is effectively unmaintained. — [PyPI JSON](https://pypi.org/pypi/finpy-tse/json); [GitHub API repo](https://api.github.com/repos/ARahimiQuant/finpy-tse)
  - Issue #23 (2025-12-30): `Get_RI_History` (real/legal history) errors. — [finpy-tse issues](https://github.com/ARahimiQuant/finpy-tse/issues)
  - PR #22 (2025-12-18): **old.tsetmc.com uses a self-signed certificate**, causing SSLError; the fix disables verification. — [finpy-tse issues](https://github.com/ARahimiQuant/finpy-tse/issues)
  - Issue #21: trade-count mismatch (3,788 vs 3,796 for «فارس» on 1404/08/20). Per a maintainer comment (2026-01-07), the site reads the count from the history tab. — [finpy-tse issue #21](https://github.com/ARahimiQuant/finpy-tse/issues/21)
- `tsetmc-api` (mahs4d) last released 5.3.0 in June 2023 and is stale. `tehran-stocks` last released 2.0.0 in Aug 2023. — [PyPI JSON](https://pypi.org/pypi/tsetmc-api/json); [PyPI JSON](https://pypi.org/pypi/tehran-stocks/json)
- Node/JS: `tse-client` (m-ahmadi) works in the browser, in Node and as a CLI. — [tse-client](https://github.com/m-ahmadi/tse-client)
- Other community references:
  - m-ahmadi/exref `tse/urls.txt` lists routes such as `GetClosingPriceDaily/:InsCode/:DEven` and `GetClosingPriceDailyAllInst`. — [exref urls.txt](https://github.com/m-ahmadi/exref/blob/master/tse/urls.txt)
  - `codal-tsetmc` (PyPI 3.1.1, 2025-09-17) combines Codal and TSETMC data. — [PyPI JSON](https://pypi.org/pypi/codal-tsetmc/json)

**Official / paid service**
- Tehran Securities Exchange Technology Management Co. (TSETMC) runs the trading system and index calculation and publishes real-time trade data via tsetmc.com/.ir. One TSETMC page references payment IDs for the company's paid services. However, **no public pricing, terms or application process for a paid data feed / web service was found**. — [Mofid learning](https://learning.emofid.com/tehran-securities-exchange-technology-management-co/); [tsetmc.com](http://www.tsetmc.com/Loader.aspx?Partree=11)

### Inferences
- For a personal app in late 2026, the practical primary feed is cdn.tsetmc.com via a small backend with a browser UA and Referer, caching and backoff. Use webgw.tse.ir as a complement for full market watch, ETF NAV, options and bonds, and market value. Expect undocumented breaking changes: the 2023 site redesign that moved data to `cdn.tsetmc.com/api` is why older libraries such as tsetmc-api and finpy-tse decayed, and old.tsetmc.com legacy routes are being degraded (self-signed certificate, redirect loops).
- Geo-blocking does **not** currently apply to cdn.tsetmc.com, webgw.tse.ir or search.codal.ir from a US IP. The larger risk is the reverse: Iranian-side internet blackouts (section 6) cutting off the servers or the user, and some Iranian sites (ime.co.ir, news sites) being intermittently unreachable from abroad.
- Rate limits are undocumented. Nothing in the sources gives a numeric limit, so be conservative (e.g. ≤1–2 req/s with retries) and cache aggressively.

### Gaps
- No primary source found for an official paid TSETMC data feed (pricing, contract, eligibility) or for the rate-limit policy.
- I did not verify the historical claim that TSETMC blocked non-Iranian IPs in earlier years (e.g. 2022–2023); my searches turned up nothing on it.
- I did not verify end-of-day vs real-time freshness of BestLimits during trading hours (my test ran on 2026-10-17, which was a trading day, but I did not time it).

## 2. Codal.ir (disclosure system)

### Takeaway
`search.codal.ir/api/search/v2/q` is open JSON. It works from a US IP without authentication and returns paginated letter metadata with links to HTML, PDF, Excel, XBRL and attachments. Most financial statements and monthly activity reports are HTML-rendered and often have an Excel export. XBRL flags were rare in my sample. Parsing statement tables is the hard part.

### Cited Findings
- Live test: an unfiltered query returned `{"Total":627608,"Page":31381,"Letters":[...]}`. Each letter has these fields:
  - `TracingNo, Symbol, CompanyName, Title, LetterCode` (e.g. «ن-۲۱»)
  - Jalali `SentDateTime/PublishDateTime` in Persian digits
  - `HasHtml/HasExcel/HasPdf/HasXbrl/HasAttachment`
  - `Url` (e.g. `/Reports/Decision.aspx?LetterSerial=...`), `PdfUrl`, `ExcelUrl`, `XbrlUrl`, `AttachmentUrl`
  - `SuperVision.UnderSupervision`
  - [live test](https://search.codal.ir/api/search/v2/q?&Audited=true&AuditorRef=-1&Category=-1&Childs=true&CompanyState=-1&CompanyType=-1&Consolidatable=true&IsNotAudited=false&Length=-1&LetterType=-1&Mains=true&NotAudited=true&NotConsolidatable=true&PageNumber=1&Publisher=false&TracingNo=-1&search=false)
- Live test for symbol «فولاد» (`search=true&Symbol=فولاد`): Total 1,471 letters. Of the first 20: 18 had HTML, 18 had PDF, 9 had Excel, 0 had XBRL. — [live test](https://search.codal.ir/api/search/v2/q?LetterType=-1&PageNumber=1&search=true&Symbol=%D9%81%D9%88%D9%84%D8%A7%D8%AF)
- Report types seen in live results:
  - "شفاف سازی در خصوص شایعه" (rumor clarification, code ن-۲۱)
  - State-company year-end financial statements with auditor report, FY ending 1404/12/29
  - Monthly activity report "گزارش فعالیت ماهانه دوره 1 ماهه منتهی به 1405/06/31" (Foolad, published 2026-09-29)
  - [codal live test](https://search.codal.ir/api/search/v2/q?&Audited=true&AuditorRef=-1&Category=-1&Childs=true&CompanyState=-1&CompanyType=-1&Consolidatable=true&IsNotAudited=false&Length=-1&LetterType=-1&Mains=true&NotAudited=true&NotConsolidatable=true&PageNumber=1&Publisher=false&TracingNo=-1&search=false); [cdn Codal live test](https://cdn.tsetmc.com/api/Codal/GetPreparedDataByInsCode/3/46348559193224090)
- The TSETMC mirror `/api/Codal/GetPreparedDataByInsCode/{Top}/{InsCode}` returns the same letters with Gregorian timestamps, `publishDateTime_DEven` and `tracingNo`, plus `hasHtmlReport/hasExcelReport/hasPDFReport/hasXMLReport` flags and attachment names. This is a convenient way to join Codal to TSETMC InsCodes. — [live test](https://cdn.tsetmc.com/api/Codal/GetPreparedDataByInsCode/3/46348559193224090); [ENDPOINTS.md](https://raw.githubusercontent.com/BabakEslami/tse-market-data/main/ENDPOINTS.md)
- Library: `codal-tsetmc` 3.1.1 (2025-09-17) is a Python package bridging Codal and TSETMC. — [PyPI](https://pypi.org/pypi/codal-tsetmc/json); [docs](https://mohsenebrahimyir.github.io/codal-tsetmc/)
- BrsApi.ir sells or offers a "Codal API" product (listed in its menu). — [BrsApi.ir](https://brsapi.ir/)

### Inferences
- Use the search API for discovery and incremental polling by TracingNo/date. Use the Excel export, where `HasExcel`, for monthly sales and production and for statements. Fall back to HTML-table scraping of `Decision.aspx` pages; the HTML is server-rendered.
- XBRL appears to be rare in practice in 2026 (0 of 20 in my sample), so do not plan around XBRL.
- Dates come in Jalali with Persian digits, so normalize them, e.g. with `jdatetime` 6.1.1 (2026-10-12) or `persiantools` 6.2.0 (2026-08-05). — [jdatetime PyPI](https://pypi.org/pypi/jdatetime/json); [persiantools PyPI](https://pypi.org/pypi/persiantools/json)

### Gaps
- I did not find documentation of the `LetterType` / `Category` code values (e.g. which code means interim statements, monthly activity, board decisions, DPS/AGM, or capital increase). My LetterType test values 6 and 58 were not decoded.
- I did not test the Codal rate limit or anti-bot behavior on `Decision.aspx` or Excel downloads.

## 3. Commercial and third-party providers

### Takeaway
**BrsApi.ir** is the only third-party provider I found with public docs. It offers free-key JSON APIs scraped from TSETMC (all symbols, history, real/legal flows, order book, shareholders, Codal) plus IME products, with a free tier of 1,000 requests/day on at least some endpoints. Mabna (Rahavard365) has an authenticated API but no public developer docs or pricing. Others (fipiran, sahamyab, Tadbir, Rayan) offer no public developer APIs that I could confirm.

### Cited Findings
- **BrsApi.ir** describes its data as "collected from tsetmc". — [BrsApi.ir home](https://brsapi.ir/)
  - Endpoint pattern: `https://Api.BrsApi.ir/Tsetmc/AllSymbols.php?key=...&type=N`, where type 1 = stocks+ETFs+rights, 2 = IME symbols on tsetmc, 3 = futures, 4 = debt, 5 = housing facilities.
  - Fields include l18, l30, isin, id (InsCode), sector, z (shares), eps, pe, price thresholds, OHLC, trade count/volume/value, real/legal buy/sell counts and volumes, and 5-level book (zd/qd/pd, zo/qo/po).
  - It publishes a public demo key `FreeSV0E1LSgB9RDjuf0QorSLViX8pPG` and advises setting a browser User-Agent, because Python's default UA is blocked by its firewall.
- BrsApi history API: `History.php?key=...&type=0&l18={symbol}` returns daily prices; `type=1` returns daily real/legal flows. Dates are Jalali (e.g. 1403-08-08). — [BrsApi history](https://brsapi.ir/bourse-api-history-webservice/)
- BrsApi menus also list:
  - Bourse: Index, Symbol, ETF NAV, Options, Transactions (ریزمعاملات), Candlestick, Shareholders, Codal APIs.
  - IME: futures, options, commodity deposit certificates, commodity funds, physical-market trade statistics.
  - [BrsApi.ir](https://brsapi.ir/); [BrsApi bourse category](https://brsapi.ir/category/api-bourse/)
  - The IME option-market API is described as delivering official, real-time IME data in JSON with an API key. — [BrsApi IME option](https://brsapi.ir/ime-api-option-webservice/); [IME physical](https://brsapi.ir/ime-api-physical-webservice/)
- BrsApi pricing statements are inconsistent: the main site says "completely free", while the index page says free up to 1,000 requests/day. Paid tiers are described only as "economical", with no figures found. — [BrsApi index API](https://brsapi.ir/bourse-api-index-webservice/); [BrsApi home](https://brsapi.ir/)
- An APIs-made-in-Iran GitHub issue describes BrsApi as offering a free API for gold, currency and crypto prices with no key required. — [APIs-made-in-Iran #120](https://github.com/Hameds/APIs-made-in-Iran/issues/120)
- A community pipeline report says only cdn.tsetmc.com and BrsApi.ir were reachable from GitHub Actions runners (non-Iranian cloud). — [ENDPOINTS.md](https://raw.githubusercontent.com/BabakEslami/tse-market-data/main/ENDPOINTS.md)
- **Rahavard365** is a product of Mabna Financial Data Processing Co., licensed by SEO since 1389 (2010). Its "data transfer" feature lets users extract market data automatically. — [Wallex blog on Rahavard365](https://wallex.ir/blog/rahavard365/)
  - Live test: `rahavard365.com/api/v2/asset/1/chart/price` returned **HTTP 401**, so an internal API exists but needs authentication. — [live test](https://rahavard365.com/api/v2/asset/1/chart/price?period=1y)
- Mabna Data (mabnadata.com) API: my searches found no public documentation or pricing. — [search returned nothing relevant; see gap]
- fipiran.com responded with HTTP 200 (alive). I found no public API documentation. — [live test](https://www.fipiran.com/)

### Inferences
- For a personal app, BrsApi is the lowest-friction "managed" option, especially for IME data and a single-call all-symbols snapshot. It is a scraper of TSETMC, though, so it inherits TSETMC's breakages and adds a third-party dependency and terms risk. Direct cdn.tsetmc.com is free and comparably complete.
- Mabna/Rahavard, Tadbir Pardaz and Rayan Hamafza are B2B vendors to brokers and funds. API access is probably by contract only; no public developer program was found.

### Gaps
- No pricing found for Mabna Data API, Rahavard365 premium data export, Tablokhani, Kaladas, sahamyab or fipiran. Searches returned nothing relevant.
- I could not find BrsApi's terms of service, paid-plan prices or its rate limits beyond 1,000/day.

## 4. Broker platforms and algorithmic trading for individuals

### Takeaway
No Iranian broker publishes an open, documented retail trading API. Algorithmic trading is permitted only through SEO-licensed providers and brokers that meet an 8-condition framework (2021). Reports in 2025 say about 18 brokers are licensed to offer algorithmic tools to retail and institutional clients. Retail "bots" that drive Exir/Agah/Mofid web UIs are unofficial.

### Cited Findings
- 1399 (Sept 2020): SEO banned algorithmic trading and online order-splitting for all individuals and legal entities "until further notice". — [IRIB News](https://www.irib-news.ir/fa/news/2831677/%D9%85%D8%B9%D8%A7%D9%85%D9%84%D8%A7%D8%AA-%D8%A7%D9%84%DA%AF%D9%88%D8%B1%DB%8C%D8%AA%D9%85%DB%8C-%D8%AF%D8%B1-%D8%A8%D8%A7%D8%B2%D8%A7%D8%B1-%D8%B3%D9%87%D8%A7%D9%85-%D9%85%D9%85%D9%86%D9%88%D8%B9-%D8%B4%D8%AF)
- 30 Farvardin 1400 (Apr 2021): SEO allowed algorithmic trading services **by SEO-licensed institutions** subject to 8 conditions. — [Tejarat News, 1400-01-30](https://tejaratnews.com/%D9%85%D8%B9%D8%A7%D9%85%D9%84%D8%A7%D8%AA-%D8%A7%D9%84%DA%AF%D9%88%D8%B1%DB%8C%D8%AA%D9%85%DB%8C-%D8%AF%D8%B1-%D8%A8%D9%88%D8%B1%D8%B3)
  - Comply with the algo-trading requirements approved 7 Bahman 1398.
  - Provide monitoring infrastructure to SEO, TSE and IFB.
  - Use no rule-violating algorithms.
  - Obtain TSE and IFB operational approvals.
  - Obtain TSETMC system-requirements approval.
  - Obtain capital-market security center approval.
  - Submit periodic logs.
- The SEO online-trading directive says services such as algorithmic trading over online channels are subject to SEO-issued requirements. — [novinlaw.ir directive text](https://novinlaw.ir/rules/legals/%D9%82%D9%88%D8%A7%D9%86%DB%8C%D9%86-%D9%88-%D9%85%D9%82%D8%B1%D8%B1%D8%A7%D8%AA/show/679/%D8%AF%D8%B3%D8%AA%D9%88%D8%B1%D8%A7%D9%84%D8%B9%D9%85%D9%84-%D8%A7%D8%AC%D8%B1%D8%A7%D9%8A%DB%8C-%D9%85%D8%B9%D8%A7%D9%85%D9%84%D8%A7%D8%AA-%D8%A8%D8%B1%D8%AE%D8%B7)
- A report lists 18 brokers licensed to offer algorithmic tools to individual and legal clients. Per the search engine's relative date it is roughly from late 2025; I could not open the page from my IP to confirm the date. — [Tahlil Bazaar](https://www.tahlilbazaar.com/news/183412/%D9%81%D9%87%D8%B1%D8%B3%D8%AA-%DB%B1%DB%B8-%DA%A9%D8%A7%D8%B1%DA%AF%D8%B2%D8%A7%D8%B1%DB%8C-%D8%A8%D8%B1%D8%A7%DB%8C-%D8%AF%D8%B3%D8%AA%D8%B1%D8%B3%DB%8C-%D9%85%D8%B9%D8%A7%D9%85%D9%84%D9%87-%DA%AF%D8%B1%D8%A7%D9%86-%D8%AD%D9%82%DB%8C%D9%82%DB%8C-%D8%A8%D9%87-%D9%85%D8%B9%D8%A7%D9%85%D9%84%D8%A7%D8%AA-%D8%A7%D9%84%DA%AF%D9%88%D8%B1%DB%8C%D8%AA%D9%85%DB%8C)
- A Mehr report says the SEO chief directed that algorithmic trading be opened to individual investors too, and that it be stopped at brokers lacking oversight tools (date not confirmed). — [Mehr News](https://www.mehrnews.com/news/5320219/%D8%AF%D8%B3%D8%AA%D8%B1%D8%B3%DB%8C-%D8%A8%D9%87-%D9%85%D8%B9%D8%A7%D9%85%D9%84%D8%A7%D8%AA-%D8%A7%D9%84%DA%AF%D9%88%D8%B1%DB%8C%D8%AA%D9%85%DB%8C-%D8%A8%D8%B1%D8%A7%DB%8C-%D8%B3%D8%B1%D9%85%D8%A7%DB%8C%D9%87-%DA%AF%D8%B0%D8%A7%D8%B1%D8%A7%D9%86-%D8%AD%D9%82%DB%8C%D9%82%DB%8C-%D9%87%D9%85-%D9%81%D8%B1%D8%A7%D9%87%D9%85)
- An example broker offering: Kian offers algorithmic trading via a third-party vendor platform. Applicants are screened on portfolio and eligibility, and the page references SEO rules dated Esfand 1401. — [kian.trade](https://kian.trade/services/algorithmic-trading)
- The retail tools found are unofficial "سرخطی" queue-sniping bots: browser extensions or apps that automate the Mofid/Agah/Exir web UIs, plus GitHub projects posting to Exir order endpoints. None is an official broker API. — [Firefox add-on](https://addons.mozilla.org/en-US/firefox/addon/sarkhati/); [GitHub mohammad1384p/stock](https://github.com/mohammad1384p/stock); [datisnetwork Exir robot](https://www.datisnetwork.com/shop/exir-robot)

### Inferences
- A personal **analysis** app (read-only market data) has no regulatory issue. Automated **order placement** by a retail user through scraped broker UIs is likely outside the licensed framework and could breach broker terms. Officially it should go through a licensed broker's algo product.

### Gaps
- The current (1403–1405) SEO algo-trading directive text and the up-to-date list of licensed brokers were not retrieved.
- Whether Mofid (Easy Trader), Agah (Online Plus) or Tadbir/Rayan OMS expose documented retail APIs was not confirmed; no evidence found that they do.

## 5. Macro and correlation data (FX, gold, CPI, commodities, IME/IRENEX)

### Takeaway
The free-market USD/IRR rate and gold/coin prices are easiest from tgju.org's JSON endpoint, which works from abroad; bonbast is another option but uses a tokenized POST. CPI is published by both the Statistical Centre of Iran (SCI) and the CBI, with methodological differences. IME commodity data is available via ime.co.ir (flaky from abroad), via BrsApi's IME APIs, and partly via TSETMC.

### Cited Findings
- tgju live test: `api.tgju.org/v1/market/indicator/summary-table-data/price_dollar_rl` returned 3,978 daily rows. Each row has open/low/high/close, change, Gregorian and Jalali date. The latest was 2026/10/15 (1405/07/23): **USD close 2,674,050 rial**. Cells contain HTML spans that need cleaning. — [live test](https://api.tgju.org/v1/market/indicator/summary-table-data/price_dollar_rl)
- bonbast live test: the homepage embeds a rotating `param` token (e.g. `"…,2026-10-17-09-17-02"`) used by a POST to `/json`, so scraping needs a two-step fetch. — [live test](https://www.bonbast.com/)
- alanchand.com returned 301, i.e. alive. — [live test](https://www.alanchand.com/)
- Inflation: SCI is still publishing. It reported 89.8% point-to-point for September 2026, and for the period ending 21 June 2026, 62.0% annual and 88.6% y/y. — [IranWire](https://iranwire.com/en/news/158322-irans-inflation-reaches-898-in-september-statistical-center/); [Euronews](https://euronews.com/business/2026/06/30/irans-inflation-spiral-deepens-as-rial-slides-and-tensions-rise)
- CBI publishes a separate series: 84.4% y/y in August 2026. CBI's index is urban-only, while SCI includes rural households, hence the gaps. — [IranWire](https://iranwire.com/en/news/156819-irans-inflation-crisis-deepens-as-year-on-year-rate-hits-844-in-august/); [Wikipedia: Inflation in Iran](https://en.wikipedia.org/wiki/Inflation_in_Iran)
- From the US IP, amar.org.ir returned 301 and cbi.ir timed out. — [live test](https://www.amar.org.ir/); [live test](https://www.cbi.ir/)
- IME/IRENEX:
  - ime.co.ir returned 200 on the first probe and timed out on a later one. irenex.ir timed out. — [live test](https://www.ime.co.ir/); [live test](https://www.irenex.ir/)
  - TSETMC exposes `/api/Energy/GetAuctionById/{AuctionId}` for energy-exchange auctions. — [ENDPOINTS.md](https://raw.githubusercontent.com/BabakEslami/tse-market-data/main/ENDPOINTS.md)
  - BrsApi `AllSymbols type=2` covers IME symbols listed on tsetmc. — [BrsApi](https://brsapi.ir/)
  - BrsApi also has IME physical-market, futures, options, certificates and commodity-fund APIs. — [BrsApi IME physical](https://brsapi.ir/ime-api-physical-webservice/); [BrsApi IME futures](https://brsapi.ir/ime-api-futures-webservice/)
- BrsApi also lists a "free commodity API" for global commodities. — [BrsApi](https://brsapi.ir/)

### Inferences
- For correlation work, use tgju for daily USD, EUR, gold, coin and gold-ounce series, and SCI/CBI monthly CPI with the source labeled. For global commodity drivers of TSE heavyweights (steel, copper, urea/methanol, oil), international free sources such as World Bank or FRED are likely easier than Iranian ones (not researched here).
- IME data scraping from abroad is unreliable. Run collectors inside Iran or use BrsApi.

### Gaps
- No documented public endpoint for IME's own physical-market statistics was confirmed; my ASMX/endpoint search returned nothing.
- I did not verify SCI's or CBI's machine-readable formats (Excel/PDF) or download URLs.

## 6. Data-quality issues and 2025–2026 disruptions

### Takeaway
The biggest gotchas:
- **Phantom rows during closures:** TSETMC history contains rows with zero volume and carried-forward prices for days the market was shut.
- **Unadjusted prices:** history is unadjusted by default; adjustment events are available separately.
- **Capital increases and Trading-At-Last "4"-suffix symbols** complicate continuity.
- **Mixed calendars:** Jalali vs Gregorian dates and Persian digits.

### Cited Findings
- **Market closures:**
  - The Israel–Iran war began 13 June 2025. TSE was shut for about nine days and reopened Saturday 28 June 2025, when the index fell 2.1% to 2,922,101 with a ~350 trillion-rial sell queue. Fixed-income funds and the IME/IRENEX physical markets reportedly kept operating. — [Eghtesad Online](https://www.eghtesadonline.com/en/news/2001501/tehran-stock-exchange-reopens-after-nine-days-on-saturday); [Iran International](https://v1.iranintl.com/en/202506283565)
  - After the 28 Feb 2026 US/Israel attack, TSE closed for **80 days**, its longest ever, and reopened **19 May 2026** under selling limits and extended hours. About 42 firms in steel, petrochemicals and energy remained closed, with reported shares of market value varying around 36%. A ceasefire took effect on 8 April 2026. — [Al Arabiya](https://english.alarabiya.net/News/middle-east/2026/05/19/iran-stock-exchange-reopens-after-war-forced-longestever-shutdown); [Iran International](https://www.iranintl.com/en/202605208411); [Xinhua](https://english.news.cn/20260519/7e398f61cd9b41a9bbf328466a153055/c.html); [Crypto Briefing](https://cryptobriefing.com/tehran-stock-exchange-reopens-trading-limits/)
- **How closures appear in the data (live test):** `GetClosingPriceDailyList` for Fameli and `GetIndexB2History` for TEDPIX contain rows for **every weekday of both closures** with flat prices and `zTotTran=0`, `qTotTran5J=0`, and `hEven` around 06:10.
  - 15–25 June 2025.
  - 28 Feb–18 May 2026, with TEDPIX flat at 3,713,955.9.
  - Real trading resumed 28 June 2025 and 19 May 2026.
  - [live test Fameli](https://cdn.tsetmc.com/api/ClosingPrice/GetClosingPriceDailyList/35425587644337450/0); [live test TEDPIX](https://cdn.tsetmc.com/api/Index/GetIndexB2History/32097828799138957)
- **Internet blackouts:**
  - There was a 2025 blackout around the June war; a ntc.party thread on a network shutdown from 2025-06-18 says few IPs (e.g. api.github.com) stayed reachable. — [Wikipedia 2025 blackout](https://en.wikipedia.org/wiki/2025_Internet_blackout_in_Iran); [ntc.party](https://ntc.party/t/network-shutdown-in-iran-since-2025-06-18/17068)
  - In 2026 there was a near-total blackout from 8 Jan 2026 during protests, relaxed 28 Jan with severe restrictions through 26 May. — [Wikipedia 2026 blackout](https://en.wikipedia.org/wiki/2026_Internet_blackout_in_Iran); [CNN](https://www.cnn.com/2026/01/30/middleeast/iran-internet-blackout-censorship-intl); [Freedom Online Coalition](https://www.canada.ca/en/global-affairs/news/2026/02/freedom-online-coalition-joint-statement-on-internet-shutdowns-in-the-islamic-republic-of-iran.html)
  - This was followed by an 88-day global shutdown at the start of the war. — [Al Jazeera 2026-10-14](https://www.aljazeera.com/news/2026/10/14/iran-targets-starlink-as-tool-of-war-with-hints-at-internet-restrictions)
  - A whitelist / "tiered internet" model is being built, with global access for approved groups (traders, researchers). — [Rest of World](https://restofworld.org/2026/iran-blackout-tiered-internet/); [Al Jazeera](https://www.aljazeera.com/news/2026/10/14/iran-targets-starlink-as-tool-of-war-with-hints-at-internet-restrictions)
- **Adjustments:**
  - `GetPriceAdjustList` gives per-event adjusted vs unadjusted closes, e.g. Foolad: 2026-08-03 2549/2604 and 2025-08-04 2580/2860. — [live test](https://cdn.tsetmc.com/api/ClosingPrice/GetPriceAdjustList/46348559193224090)
  - `GetInstrumentShareChange` gives share-count changes, e.g. Foolad 1.5T→1.935T on 2026-03-30. — [live test](https://cdn.tsetmc.com/api/Instrument/GetInstrumentShareChange/46348559193224090)
  - finpy-tse added an "adj low" field in 2025 (PR #20), which shows adjusted series must be computed client-side. — [finpy-tse issues](https://github.com/ARahimiQuant/finpy-tse/issues)
- **Count and volume discrepancies:** trade counts from library or API fields can differ slightly from what the site displays (finpy-tse #21). `GetTradeHistory` under-reports by about 36–38% in samples, so use ClosingPrice daily totals. — [finpy-tse #21](https://github.com/ARahimiQuant/finpy-tse/issues/21); [ENDPOINTS.md](https://raw.githubusercontent.com/BabakEslami/tse-market-data/main/ENDPOINTS.md)
- **Suspended symbols:** empty or 500 responses on historical endpoints are expected on halt days, so check `cEtaval`. — [ENDPOINTS.md](https://raw.githubusercontent.com/BabakEslami/tse-market-data/main/ENDPOINTS.md)
- **Symbol identity:** each instrument has `insCode` plus `insCode2/3/4` aliases in search results. Pytse-client issue #270 shows symbol-list lookups failing for some symbols. Trading-At-Last creates "…۴" sibling symbols. — [live test search](https://cdn.tsetmc.com/api/Instrument/GetInstrumentSearch/%D9%81%D9%88%D9%84%D8%A7%D8%AF); [pytse-client issues](https://github.com/Glyphack/pytse-client/issues); [ENDPOINTS.md](https://raw.githubusercontent.com/BabakEslami/tse-market-data/main/ENDPOINTS.md)
- **Calendars:** TSETMC cdn uses Gregorian `dEven` integers. Codal search returns Jalali strings in Persian digits. BrsApi and tgju return Jalali (tgju returns both). — [codal live test](https://search.codal.ir/api/search/v2/q?&Audited=true&AuditorRef=-1&Category=-1&Childs=true&CompanyState=-1&CompanyType=-1&Consolidatable=true&IsNotAudited=false&Length=-1&LetterType=-1&Mains=true&NotAudited=true&NotConsolidatable=true&PageNumber=1&Publisher=false&TracingNo=-1&search=false); [BrsApi history](https://brsapi.ir/bourse-api-history-webservice/); [tgju live test](https://api.tgju.org/v1/market/indicator/summary-table-data/price_dollar_rl)

### Inferences
- An ingestion pipeline should:
  - drop or flag rows with `zTotTran==0` before computing returns or volatility (otherwise closures look like zero-volatility periods followed by gap moves);
  - build adjusted series from `GetPriceAdjustList` ratios (pClosing/pClosingNotAdjusted, applied backward) or the standard TSETMC adjustment method;
  - store InsCode as the primary key, with ISIN for webgw joins;
  - keep Gregorian dates internally and convert to Jalali for display.
- Plan for multi-week outages of Iranian-hosted sources during crises, both blackouts and market closures. Caching full history locally is essential, and hosting the collector abroad currently works for cdn.tsetmc.com, Codal and tgju.

### Gaps
- I did not check whether the "about 42 firms stayed closed" after 19 May 2026 show the same phantom zero-volume rows until each one reopened; likely, but not tested.
- I did not test whether cdn.tsetmc.com stayed reachable from abroad *during* the 2026 blackouts. Wartime availability of TSETMC/Codal from outside Iran is unknown.
