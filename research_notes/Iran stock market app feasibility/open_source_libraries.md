# Open-source libraries and tools for Iranian stock market (TSE/IFB) data and analysis: maintenance status 2025-2026

Research date: 2026-10-17. How the data was collected: the GitHub REST API (stars, `pushed_at`, license, open issues, recent issues and commits), the PyPI JSON API (release history), the npm, NuGet, CRAN and Hugging Face registries, and the Kaggle API. Each package's latest published wheel or tarball was downloaded and grepped to list the hosts and endpoints it calls. I also ran live HTTP probes against TSETMC endpoints on 2026-10-17.

Caveat on GitHub `pushed_at`: this field changes whenever any branch is pushed, including Dependabot or Renovate branches. Where it differed, I checked the last real commit on the default branch and report that date instead.

## Q0. Context: state of the TSETMC API itself (needed to judge whether a library "works")

### Takeaway
The live data is still served by two backends: the JSON REST API at `cdn.tsetmc.com/api/...` and the legacy `old.tsetmc.com/tsev2/data/*.aspx` text endpoints. Both answered on 2026-10-17. However, `cdn.tsetmc.com` returns 403 unless the request sends a browser-like User-Agent, and some legacy `Loader.aspx` pages now redirect to `Site.aspx`. Those redirects are the root cause of several library breakages.

### Cited Findings
- Direct probe, 2026-10-17:
  - `https://cdn.tsetmc.com/api/ClosingPrice/GetClosingPriceInfo/46348559193224090` returned **HTTP 403 with curl's default User-Agent** and **HTTP 200 with a Chrome User-Agent**. The JSON included `"finalLastDate":20261017`.
  - With a browser User-Agent, these also returned live JSON: `cdn.tsetmc.com/api/ClientType/GetClientTypeHistory/{insCode}` (latest `recDate` 20261014), `/api/ClosingPrice/GetClosingPriceDailyList/{insCode}/0`, `/api/ClosingPrice/GetMarketWatch?...` (about 560 KB) and `/api/Instrument/GetInstrumentSearch/{name}`.
  - Source: [cdn.tsetmc.com API](https://cdn.tsetmc.com/api/ClosingPrice/GetClosingPriceInfo/46348559193224090)
- Direct probe, 2026-10-17:
  - These legacy endpoints still return data (gzip-compressed): `old.tsetmc.com/tsev2/data/InstTradeHistory.aspx?i=...&Top=3&A=0` (rows dated 20261014), `old.tsetmc.com/tsev2/data/clienttype.aspx?i=...` and `old.tsetmc.com/tsev2/data/MarketWatchInit.aspx?h=0&r=0` (timestamp `05/07/25 12:45:38`, i.e. Jalali 1405/07/25 = 2026-10-17).
  - Plain `http://old.tsetmc.com` 303-redirects to https.
  - `old.tsetmc.com/Loader.aspx?ParTree=111C1417` (the classic all-symbols list page) now 302-redirects to `/Site.aspx` instead of returning the list.
  - `old.tsetmc.com/tsev2/data/MarketWatchPlus.aspx` returned 404.
  - Source: [old.tsetmc.com](https://old.tsetmc.com/tsev2/data/InstTradeHistory.aspx?i=46348559193224090&Top=3&A=0)
- Codal's search API `https://search.codal.ir/api/search/v2/q?&PageNumber=1` returned HTTP 200 on 2026-10-17. — [search.codal.ir](https://search.codal.ir/api/search/v2/q?&PageNumber=1)
- A community repo ("BabakEslami/tse-market-data", MIT, 11 stars, last push 2026-07-15) documents cdn.tsetmc.com endpoints. Its description (in Persian) says it is a full reference of Iran capital-market API endpoints and mentions 56 endpoints. — [GitHub](https://github.com/BabakEslami/tse-market-data)
- TSETMC also offers an official, registration-based web service. Its help pages (`tsetmc.com/StaticContent/WebServiceHelp`, `WS-ClientType`, `WS-BestLimitsAllIns`, `WS-InstrumentsState`, etc.) are referenced in the source code of 5j9/tsetmc and m-ahmadi/tse-client. — [5j9/tsetmc on PyPI](https://pypi.org/project/tsetmc/); [tse-client on npm](https://www.npmjs.com/package/tse-client)

### Inferences
- Any library that calls cdn.tsetmc.com without a browser User-Agent will fail with 403. Libraries that depend on `Loader.aspx` symbol-list pages are broken for symbol discovery. A production app should own a thin client that targets `cdn.tsetmc.com/api/*`, sends a browser User-Agent, retries, and uses caching, rather than depend on an unmaintained wrapper.
- These probes ran from a non-Iranian network. TSETMC is reachable from abroad, but rate limits and geo-filtering may differ from inside Iran.

### Gaps
- I could not find an official, publicly documented rate limit or terms of use for cdn.tsetmc.com.
- The official TSETMC web service (paid or registered access) was not evaluated: its pricing and access process are not visible in open sources.

---

## Q1. pytse-client (Glyphack)

### Takeaway
pytse-client is the best-known Python TSETMC wrapper but is effectively in low-maintenance or semi-abandoned mode. Its last release was 0.19.1 on 2025-03-13 and its last commit was on 2025-03-15. Several 2025-2026 bug reports are open with no fix, including the symbol-list `TooManyRedirects` error. It still mixes legacy `old.tsetmc.com` and `Loader.aspx` URLs with some `cdn.tsetmc.com` endpoints.

### Cited Findings
- **Basics:** Python, GPL-3.0, 315 stars, 42 open issues and PRs, not archived, last push 2025-03-15. — [GitHub Glyphack/pytse-client](https://github.com/Glyphack/pytse-client)
- **PyPI releases:** latest 0.19.1 (0.19.0 and 0.19.1 both on 2025-03-13). The previous release was 0.18.0 on 2023-12-28, and the first was 0.1.0 on 2020-04-21. — [PyPI pytse-client](https://pypi.org/project/pytse-client/)
- **Last commits:** 2025-03-14/15 (README updates; merge of PR #268). — [commits](https://github.com/Glyphack/pytse-client/commits)
- **Features (from the 0.19.1 wheel):**
  - `download` (historical prices, with `adjust_price`)
  - `download_client_types_records` (individual vs. institutional, "حقیقی/حقوقی")
  - `get_asks_and_bids` and an `orderbook` module
  - `download_financial_indexes` and `get_financial_index`
  - `historical_intraday` and `ticker_statisticals` modules
  - `symbols_information` and `all_symbols`
  - a `proxy` module
  - Source: [PyPI wheel 0.19.1](https://pypi.org/project/pytse-client/)
- **Ticker module:** ticker info includes shareholders. The README describes an order-book function that returns only order-book changes and a function for intraday or tick data. — [GitHub README](https://github.com/Glyphack/pytse-client)
- **Endpoints in the wheel:**
  - legacy: `old.tsetmc.com/tsev2/data` (9 refs), `old.tsetmc.com/Loader.aspx` (5), `www.tsetmc.com/Loader.aspx` (4), `service.tsetmc.com/tsev2/data`
  - new: `cdn.tsetmc.com/api/Instrument`, `/api/Trade`, `/api/Shareholder`, `/api/BestLimits`
  - Source: [PyPI pytse-client](https://pypi.org/project/pytse-client/)
- **Open bugs and themes, 2025-2026:**
  - #270 "SYMBOLS_LIST_URL: TooManyRedirects" (2025-08-01, open). This matches my probe finding that `Loader.aspx` symbol pages now redirect. — [issue #270](https://github.com/Glyphack/pytse-client/issues/270)
  - #273 "دریافت اطلاعات نادرست از سایت بورس" (incorrect data received from the exchange site; 2025-11-26, open). — [issue #273](https://github.com/Glyphack/pytse-client/issues/273)
  - #267 "دریافت دامنه نوسان" (request to fetch the daily price limit; 2025-03-13, open). — [issue #267](https://github.com/Glyphack/pytse-client/issues/267)
  - #271 (history of percentage-holder shareholder changes, open) and #272 (first trading day of all symbols, open). — [issues](https://github.com/Glyphack/pytse-client/issues)
  - #274 (P/E retrieval for some symbols, closed 2025-12).
  - PR #277 "Fix: update share's indexes" (2026-08-26) is open and unmerged. — [PR #277](https://github.com/Glyphack/pytse-client/pull/277)
  - PR #269 "tse local data" (2025-03-15) is open and unmerged.
  - The PR "fix KeyError: 'NOT_VALID_KEY'" (#265) was merged in March 2025.
- **Dependency hygiene:** the security bumps for certifi and urllib3 (#263/#264) were opened by Dependabot in 2025-03; #264 is still open. — [PR list](https://github.com/Glyphack/pytse-client/pulls)

### Inferences
- **Status: stale or semi-abandoned.** There have been no commits for about 19 months while community fix PRs pile up. It is usable for one-off research with patches, but not as a dependency for a product.
- The GPL-3.0 license is a constraint for closed-source commercial apps that would distribute the code (for example a desktop or mobile client).

### Gaps
- I did not run pytse-client end-to-end, so I cannot say which functions currently work apart from the documented redirect failure.

---

## Q2. finpy-tse (actual author: Abbas Rahimi, GitHub "ARahimiQuant", not ghodsizadeh)

### Takeaway
finpy-tse is the most feature-rich "data-science style" TSE library: Jalali date ranges, price and individual/institutional (RI) history, indices, market watch, intraday trades and order book, queue history, shareholders, and the USD/IRR rate. However, its last release was 1.2.10 on 2024-04-24, and the author has not merged the 2025 fix PRs, including one that works around an `old.tsetmc.com` SSL certificate problem.

### Cited Findings
- **Correct attribution and basics:** the repo is `ARahimiQuant/finpy-tse`, BSD-3-Clause, Python, 100 stars, 2 open items, last push 2024-04-24. — [GitHub ARahimiQuant/finpy-tse](https://github.com/ARahimiQuant/finpy-tse)
- **PyPI releases:** 1.2.10 (2024-04-24) is the latest. 1.2.9 came out 2024-04-23, 1.2.6 on 2023-05-27, and the first release (1.0.0) on 2022-01-29; 12 releases in total. — [PyPI finpy-tse](https://pypi.org/project/finpy-tse/)
- **Last commit:** 2024-04-24, "Update __init__.py for FanAva search issue". — [commits](https://github.com/ARahimiQuant/finpy-tse/commits)
- **Public functions in 1.2.10:**
  - `Get_Price_History`, `Get_RI_History`, `Get_MarketWatch`
  - `Get_IntradayTrades_History`, `Get_IntradayOB_History`, `Get_Queue_History`
  - `Get_ShareHoldersInfo`, `Get_USD_RIAL`
  - `Get_CWI_History`, `Get_EWI_History`, `Get_CWPI_History`, `Get_EWPI_History`, `Get_FFI_History`, `Get_SectorIndex_History`, `Get_INDI_History`
  - `Build_Market_StockList`, `Build_PricePanel`
  - `Get_ACT`, `Get_LCI`, `Get_MKT`
  - Source: [PyPI wheel](https://pypi.org/project/finpy-tse/)
- **Endpoints:** the library is still heavily legacy-dependent:
  - legacy: `old.tsetmc.com/tsev2/data` (13 refs), `old.tsetmc.com/tsev2/chart` (10), `old.tsetmc.com/Loader.aspx` (7)
  - new: `cdn.tsetmc.com/api/Index` (11), `/api/MarketData`, `/api/ClientType`, `/api/BestLimits`, `/api/StaticData`
  - USD rate: tgju.org (`tgju.org/profile/price_dollar_rl`, `platform.tgju.org/fa/tvdata`)
  - IFB: `ifb.ir/StockQoute.aspx`
  - Source: [PyPI finpy-tse](https://pypi.org/project/finpy-tse/)
- **Issue themes:**
  - #23 "مشکلات کتابخانه‌ی Finpy-tse" (problems with the library; 2025-12-30)
  - #21 incorrect data from the exchange site (2025-11-26)
  - #18 unrealistic intraday trade data (2024-12-21)
  - #14 and #16 `Get_RI_History` errors (2024-07 and 2024-11)
  - #13 `Get_Price_History` error (2024-05)
  - all of the above were closed
  - Source: [issues](https://github.com/ARahimiQuant/finpy-tse/issues?q=is%3Aissue)
- **Open, unmerged PRs:**
  - #22 "Bypass SSL verification for old.tsetmc.com due to self-signed certificate issue" (2025-12-18). — [PR #22](https://github.com/ARahimiQuant/finpy-tse/pull/22)
  - #20 "Update __init__.py" (2025-09-12). — [PR #20](https://github.com/ARahimiQuant/finpy-tse/pull/20)
- An example-notebook companion repo exists (19 stars, last push 2023-01-12). — [GitHub finpy-tse-example-notebook](https://github.com/ARahimiQuant/finpy-tse-example-notebook)

### Inferences
- **Status: unmaintained since April 2024** (about 2.5 years without a release). Known 2025 breakages (the SSL problem on old.tsetmc.com) have only community patches. It is still a valuable reference for which endpoint gives which dataset (RI history, queues, intraday order book).

### Gaps
- I did not confirm whether `Get_USD_RIAL` (tgju scraping) still works in 2026.

---

## Q3. Other Python TSETMC/Codal libraries

### Takeaway
The ecosystem has moved. The old "big three" (tehran-stocks, pytse-client, finpy-tse) are stale. The actively maintained Python options as of Sep-Oct 2026 are newer, smaller projects: **oxtapus** (Polars-native, cdn API, MIT), **algotik-tse** (broad coverage, GPL-3.0), **5j9/tsetmc** (async, GPL-3.0, requires Python ≥3.14) and **fima** (new, MIT). For Codal: **codal-tsetmc** (MIT, last release 2025-09) and **codalpy** (last release 2025-09).

### Cited Findings

**tehran-stocks (ghodsizadeh)**
- Python and Jupyter, MIT, 466 stars (the most-starred TSE repo), 29 open items. — [GitHub](https://github.com/ghodsizadeh/tehran-stocks)
- PyPI 2.0.0 (2023-08-06) is the latest; the first release (0.0.1) was 2019-07-05. — [PyPI tehran-stocks](https://pypi.org/project/tehran-stocks/)
- The last real commit was 2023-10-19 (a merged Dependabot bump). The 2026-04-21 `pushed_at` is only a Dependabot lxml PR (#104). — [commits](https://github.com/ghodsizadeh/tehran-stocks/commits)
- Open issues:
  - #93 "ts-api is down" (2023-03)
  - #100 "Stock prices cannot be fetched!" (2024-02)
  - #101 DataFrame loading error (2024-06)
  - #102 history of "number of shares" (2025-03)
  - #103 "library fails to install" (2025-12-30)
  - Source: [issues](https://github.com/ghodsizadeh/tehran-stocks/issues)
- The 2.0.0 wheel references only `old.tsetmc.com/Loader.aspx`. — [PyPI](https://pypi.org/project/tehran-stocks/)
- It offers a CLI, csv/excel/Stata export and a local SQL database through SQLAlchemy, and works offline after the first sync. — [PyPI 1.0.8 page](https://pypi.org/project/tehran-stocks/1.0.8)
- **Status: abandoned.**

**tsetmc-api (mahs4d)**
- Python, MIT, 65 stars, 5 open issues, last push 2023-10-17. — [GitHub](https://github.com/mahs4d/tsetmc-api)
- PyPI 5.3.0 (2023-06-14) is the latest. — [PyPI tsetmc-api](https://pypi.org/project/tsetmc-api/)
- It uses both `old.tsetmc.com` and `cdn.tsetmc.com/api/{ClosingPrice,ClientType,Shareholder,Trade}`.
- **Status: abandoned since 2023.**
- The same author's `mahs4d/tsetmc-webservice` (last push 2021-10-30) and `mahs4d/tsetmc-filters` (JavaScript, 2023-01) are also dormant. — [GitHub search](https://github.com/mahs4d?tab=repositories)

**tsetmc (5j9)**
- Async Python, GPL-3.0, 36 stars, 1 open issue. — [GitHub 5j9/tsetmc](https://github.com/5j9/tsetmc)
- PyPI 5.1.0 was released 2026-08-11 (5.0.0 on 2026-07-23); 118 releases since 2020-11-06. It **requires Python ≥3.14**. — [PyPI tsetmc](https://pypi.org/project/tsetmc/)
- It references the official TSETMC web-service help pages and `old.tsetmc.com`, `members.tsetmc.com` and `www.tsetmc.com/tsev2/data`.
- Recent issues: "Add support for Gold Fund Options" (closed 2026-07-04) and "Fix failing tests" (closed 2025-11). #4 "Prepare for the new market watch" (2024-01) is still open. — [issues](https://github.com/5j9/tsetmc/issues)
- **Status: actively maintained.**

**oxtapus (yghaderi)**
- MIT, 29 stars. Described as "A typed, Polars-native Python SDK for TSETMC, TGJU, and Iranian financial market data". Requires Python >=3.11,<3.16. — [PyPI oxtapus](https://pypi.org/project/oxtapus/)
- Latest release 1.3.0 (2026-10-13); 34 releases since 0.2.2 (2023-09-07). — [PyPI](https://pypi.org/project/oxtapus/)
- Last commits on 2026-10-13: "upgrade the DataFrame engine to Polars 2", CI release publishing. — [GitHub](https://github.com/yghaderi/oxtapus)
- The wheel uses `https://cdn.tsetmc.com` (14 refs) and `api.tgju.org`. It has sync and async clients, a CLI, storage and observability modules.
- Closed issues show coverage of:
  - options market watch and depth-5 order book for options (#8)
  - individual/institutional (RI) data in market watch (#2)
  - mismatch in adjusted historical data (#11, closed 2025-11)
  - IFB options (#5)
  - Source: [issues](https://github.com/yghaderi/oxtapus/issues?q=is%3Aissue)
- The README (in Persian) describes data from tsetmc, tgju and rahavard365. — [GitHub](https://github.com/yghaderi/oxtapus)
- **Status: actively maintained** (the best-engineered of the maintained set, judging by the package structure).

**algotik-tse (mohsenalipour)**
- GPL-3.0 on PyPI (GitHub reports NOASSERTION), 20 stars. — [GitHub](https://github.com/mohsenalipour/algotik_tse)
- PyPI 1.8.0 (2026-09-12); 35 releases since 2023-10-04. Summary: "Iran capital-market data and analytics for TSETMC, debt, gold, FX, energy and commodities". — [PyPI algotik-tse](https://pypi.org/project/algotik-tse/)
- Commits on 2026-09-12 added debt instruments, a TGJU catalog, energy and commodity markets, and shareholder history and ownership analytics. — [commits](https://github.com/mohsenalipour/algotik_tse/commits)
- Endpoints:
  - TSETMC new API: `cdn.tsetmc.com/api/{Instrument,Index,ClosingPrice,MarketData,Energy,Shareholder,Trade}`
  - legacy: some `old.tsetmc.com`
  - other: `api.tgju.org/v1/market`, `ifb.ir/ytm.aspx`
- A third-party "agent skill" wraps algotik-tse 1.8.0 for LLM agents. — [GitHub miladaghdak/algotik-tse-skill](https://github.com/miladaghdak/algotik-tse-skill)
- **Status: active**, but release bursts are concentrated in a few days.

**fima (hemoboghosian)**
- MIT, 0 stars, 6 open issues. — [GitHub](https://github.com/hemoboghosian/fima)
- PyPI 0.7.0 (2026-10-14); 11 releases since 0.1.0 (2025-05-06). — [PyPI fima](https://pypi.org/project/fima/)
- The README states that the project is under development and pulls data from public online services, so a change or outage at the source affects its outputs. Requires Python 3.10+. — [PyPI fima](https://pypi.org/project/fima/)
- **Status: new and active, but immature.**

**Other Python libraries**

| Package | Facts | Status | Source |
|---|---|---|---|
| PyTse (miladj) | 45 stars, MIT, last push 2022-12-09; PyPI `pytse` 1.6.2 on 2021-06-09 | Abandoned | [GitHub](https://github.com/miladj/PyTse); [PyPI](https://pypi.org/project/pytse/) |
| pytse-filter (farhad-mohammadi) | MIT, 13 stars; PyPI 1.45 (2024-05-16); filters symbols; uses old.tsetmc.com | Dormant | [PyPI](https://pypi.org/project/pytse-filter/) |
| tse-index (alised) | GPL-3.0, 11 stars; PyPI 0.0.4 on 2021-10-20; repo push 2025-03-16 | Dormant | [GitHub](https://github.com/alised/tse-index) |
| tfinance (sdqri) | GPL-3.0, 9 stars; PyPI 0.1.4 on 2023-02-09 | Abandoned | [PyPI](https://pypi.org/project/tfinance/) |
| tse-option (sm-sokout) | Option pricing for TSE/IFB; MIT, 30 stars; PyPI 0.1.3.0 on 2025-01-23; repo push 2026-10-08 | Low-activity | [PyPI](https://pypi.org/project/tse-option/); [GitHub](https://github.com/sm-sokout/tse-option) |
| strix-stock (straxico) | Personal dashboard for Iranian market data, Python; 32 stars, 44 open issues, no license, last push 2026-02-10 | No license (cannot legally reuse) | [GitHub](https://github.com/straxico/strix-stock) |
| yfinance-ir (Fazel94) | "yfinance-shaped market data for the Iranian markets: TSETMC, Codal, TGJU and CPI"; MIT, 2 stars, push 2026-10-11; not on PyPI | Very new | [GitHub](https://github.com/Fazel94/yfinance-ir) |
| behnamhatami/stock | Tehran stock market crawler and analyzer; GPL-3.0, 14 stars, push 2026-01-06 | n/a | [GitHub](https://github.com/behnamhatami/stock) |
| MCP servers | `AMohammadzadeh/mcp-tse` (MIT, 4 stars, 2025-07-11); `a-saeedia/bource-mcp` (JavaScript, MIT, read-only TSETMC bridge, 2026-09-16); `mohammad-k13/Codal-MCP` (TypeScript, 2026-07-26) | Early-stage | [mcp-tse](https://github.com/AMohammadzadeh/mcp-tse); [bource-mcp](https://github.com/a-saeedia/bource-mcp); [Codal-MCP](https://github.com/mohammad-k13/Codal-MCP) |

**Codal libraries**

| Package | Facts | Status | Source |
|---|---|---|---|
| codal-tsetmc (mohsenebrahimyir) | MIT, 16 stars, push 2025-09-17; PyPI 3.1.1 (2025-09-17), 27 releases since 2024-06-06; "Data Downloader for Codal and Tehran stock market"; uses `search.codal.ir/api/search` and cdn.tsetmc.com `Instrument`/`Index`/`StaticData` plus some old.tsetmc.com | Maintained until Sep 2025, about a year idle | [GitHub](https://github.com/mohsenebrahimyir/codal-tsetmc); [PyPI](https://pypi.org/project/codal-tsetmc/) |
| codalpy | PyPI 0.4.5 (2025-09-22), 15 releases since 2024-08-14, Python ≥3.12; scrapes `www.codal.ir/Reports/...` (about 40 refs) and `search.codal.ir`; PyPI summary is a placeholder ("Add your description here") and no repo link | Low documentation quality | [PyPI codalpy](https://pypi.org/project/codalpy/) |
| Other Codal scrapers | Aghpour/Codal_Scraping (monthly reports, 16 stars, 2022, no license); mohapak/codal_scraper (board-of-directors scraper, MIT, 2026-10-11); shayanAbbasi1995/codal-financial-scraper (Selenium-based, 2026-03); imahdimir/u-d-all-Codal-letters (2023) | Mostly small personal projects | [GitHub search "codal.ir"](https://github.com/search?q=codal.ir&type=repositories) |

There is no PyPI package named `codal`, and no package named `codal_scraper` on PyPI (PyPI JSON lookup returned no project). — [PyPI](https://pypi.org/project/codal/)

**Jalali date and Persian text utilities**

| Package | Facts | Source |
|---|---|---|
| jdatetime | PyPI 6.1.1 (2026-10-12), 49 releases since 2011; Python ≥3.9 | [PyPI jdatetime](https://pypi.org/project/jdatetime/) |
| persiantools | PyPI 6.2.0 (2026-08-05); MIT, 226 stars, push 2026-10-07; Jalali dates, Persian digits, character utilities | [PyPI](https://pypi.org/project/persiantools/); [GitHub](https://github.com/majiidd/persiantools) |
| hazm (Persian NLP) | PyPI 0.12.1 (2026-04-01); MIT, 1,433 stars; Python >=3.12,<3.14 | [PyPI hazm](https://pypi.org/project/hazm/); [GitHub](https://github.com/roshan-research/hazm) |
| persian-tools (Python) | 0.0.11 on 2023-03-19, stale | [PyPI](https://pypi.org/project/persian-tools/) |

### Inferences
- Among maintained Python libraries, **oxtapus (MIT)** is the most product-friendly: permissive license, typed, uses cdn.tsetmc.com, active in Oct 2026. **algotik-tse** and **5j9/tsetmc** are active but GPL-3.0. The Python ≥3.14 requirement of 5j9/tsetmc is also a deployment constraint.
- Bus factor: every maintained library has a single maintainer and fewer than 40 stars. A product team should expect to fork or own the data layer.
- Codal tooling is thin. No widely adopted, well-documented library parses Codal XBRL or financial statements into a structured form. Most projects are scrapers of HTML report pages.

### Gaps
- I could not verify the claimed endpoint count (56) in BabakEslami/tse-market-data beyond its description.
- Projects such as "TSETMC_Data", "tse-rest", "iran-stock" (Python) and "tsetmc_scraper" (as a named package) had no notable PyPI or GitHub presence in my searches. Only small repositories exist under similar names (e.g. Aghpour/tsetmc_scraping, 7 stars, 2023).

---

## Q4. Non-Python libraries (JS/Node, .NET, Go, R, PHP, Java)

### Takeaway
JavaScript has the only notable non-Python clients: m-ahmadi/tse-client and shahradelahi/tsetmc-client. Both are now dormant (last releases 2025-04 and 2024-03). .NET, Go, R and PHP options are abandoned or trivial. **There is no R package for TSE on CRAN.**

### Cited Findings
- **npm `tse-client` (m-ahmadi):**
  - MIT, 41 stars, version 2.27.6 published 2025-04-11 (created 2019-09-07); works in the browser and in Node, and has a CLI.
  - Mostly calls `service.tsetmc.com/tsev2/data` and the TseClient static-content pages on `cdn.tsetmc.com`.
  - Source: [npm tse-client](https://www.npmjs.com/package/tse-client); [GitHub](https://github.com/m-ahmadi/tse-client)
- **npm `tsetmc-client` (shahradelahi):**
  - TypeScript, MIT, 165 stars; version 1.1.2 published 2024-03-18. The last real commit was 2024-03-18; the 2026 repo activity is Renovate bot PRs only, including an unmerged "[SECURITY]" axios update (#24) and a moment.js security update (#30, 2026-10-11).
  - Uses `old.tsetmc.com/tsev2/data` (12 refs) plus `cdn.tsetmc.com/api/{ClosingPrice,Trade,StaticData,Shareholder,Msg,MarketData}`.
  - Source: [npm tsetmc-client](https://www.npmjs.com/package/tsetmc-client); [GitHub](https://github.com/shahradelahi/tsetmc-client)
- **Other npm packages:**
  - `tsetmc-api` (aminroosta) last published 2020-06-11. — [npm](https://www.npmjs.com/package/tsetmc-api)
  - `iran-stock` (ferrriii) last published 2020-07-16 and uses `tsetmc.com/tsev2/data`. — [npm](https://www.npmjs.com/package/iran-stock)
  - npm `tse` is an unrelated Express scaffolding package; there is no npm package named `tsetmc`. — [npm tse](https://www.npmjs.com/package/tse)
- **JS Persian and Jalali utilities (maintained):**
  - `@persian-tools/persian-tools` 4.0.4 (2025-09-30). — [npm](https://www.npmjs.com/package/@persian-tools/persian-tools)
  - `jalaali-js` 2.0.1 (2026-08-09). — [npm](https://www.npmjs.com/package/jalaali-js)
  - `moment-jalaali` 0.10.5 (2026-08-09). — [npm](https://www.npmjs.com/package/moment-jalaali)
- **.NET / C#:**
  - The NuGet package `TSETMC` ("TSETMC Api Wrapper", by MRDaghestani) has versions 1.0.0 to 1.0.3, all published 2017-10-16, with about 6.1k total downloads. — [NuGet TSETMC](https://www.nuget.org/packages/TSETMC)
  - GitHub `AFP33/TSE` ("Tehran Securities Exchange Library", C#, GPL-2.0, 8 stars, last push 2023-04-09). — [GitHub](https://github.com/AFP33/TSE)
  - `tayyebi/TehranStocks` (C#, archived). — [GitHub](https://github.com/tayyebi/TehranStocks)
  - `mjavad-tech/TSETMC-DataAnalysis` (WPF desktop app for options, 2025-05-08). — [GitHub](https://github.com/mjavad-tech/TSETMC-DataAnalysis)
- **Go:**
  - `empire/go-tse` (MIT, 2 stars, last push 2021-04-06). — [GitHub](https://github.com/empire/go-tse)
  - `thisismz/tsetmc-gopher` (GPL-3.0, 2020). — [GitHub](https://github.com/thisismz/tsetmc-gopher)
  - `amirHravan/tsetmc-exporter` (Prometheus exporter, 0 stars, 2026-08-23). — [GitHub](https://github.com/amirHravan/tsetmc-exporter)
- **R:**
  - A CRAN catalog scan for "tehran/iran/tsetmc/persian/jalali/farsi/shamsi" found no TSE data package.
  - Only Jalali-date packages exist: `jalcal` 0.3.0, `RJalaliDate` 0.1.0, `shide` 0.3.0.
  - A GitHub search for `tsetmc language:R` returned zero repositories.
  - Source: [CRAN DB](https://crandb.r-pkg.org/-/desc); [GitHub search](https://github.com/search?q=tsetmc+language%3AR&type=repositories)
- **C++, PHP and Java:**
  - `NuLL3rr0r/tse-rtsq` (C++, 2016). — [GitHub](https://github.com/NuLL3rr0r/tse-rtsq)
  - `ahmad30903090/tse-client-php` (GPL-3.0, 2023). — [GitHub](https://github.com/ahmad30903090/tse-client-php)
  - `saeedeyvazy/iran-stock-market-tse-crawler` (Java, 2026-10-12, no license). — [GitHub](https://github.com/saeedeyvazy/iran-stock-market-tse-crawler)

### Inferences
- A .NET, Go or JS/TS app would need its own TSETMC client. shahradelahi/tsetmc-client (MIT) is a reasonable code reference for the cdn.tsetmc.com endpoints, but it should not be used as a dependency: its dependencies have pending security updates and it relies on old.tsetmc.com.

### Gaps
- No R or Julia TSE package was found at all. This is a real absence, not a search failure: both the CRAN scan and the GitHub search came back empty.

---

## Q5. Backtesting frameworks adapted to Iran market rules

### Takeaway
I found **no open-source, maintained backtesting framework that encodes TSE/IFB rules**: daily price limits (دامنه نوسان), T+2 settlement, base volume (حجم مبنا), minimum order value, the separate buy and sell fee schedule, and queue dynamics. Developers combine a data library with a generic engine such as backtrader, vectorbt or backtesting.py, and code these rules themselves.

### Cited Findings
- A GitHub search for "backtest tehran" returned only two zero-star 2026 repos: `HosseinSartip/AI-Algo_TSE` ("AI-Algorithmic trading platform for the Tehran Stock Exchange", MIT, 2026-09-13) and a volatility-forecasting thesis repo. — [GitHub search](https://github.com/search?q=backtest+tehran&type=repositories); [AI-Algo_TSE](https://github.com/HosseinSartip/AI-Algo_TSE)
- A Persian web search for TSE backtest libraries handling price limits and fees surfaced only data-access libraries (tehran-stocks, pytse-client, fima). No backtesting layer was found. — [Faradars blog](https://blog.faradars.org/دریافت-داده-های-بورس-تهران/); [PyPI fima](https://pypi.org/project/fima/)
- The daily price limit is not even exposed by pytse-client: open feature request #267 "دریافت دامنه نوسان" asks for it. — [pytse-client #267](https://github.com/Glyphack/pytse-client/issues/267)
- Related partial tools:
  - finpy-tse `Get_Queue_History` (buy and sell queue history, useful for modeling locked-limit days). — [PyPI finpy-tse](https://pypi.org/project/finpy-tse/)
  - `MahdiHooshmand/tsetmc-iran-bourse-auto-trader-optimization` (2024-08, 0 stars). — [GitHub](https://github.com/MahdiHooshmand/tsetmc-iran-bourse-auto-trader-optimization)
  - `MirbahaMoein/Iran-Bourse-Queue-Scraper` (queue-position monitoring, 2022). — [GitHub](https://github.com/MirbahaMoein/Iran-Bourse-Queue-Scraper)
  - `amirali110/Sarkhati-Bourse` (C++, fast order sending for the "sarkhati" opening race, 2022). — [GitHub](https://github.com/amirali110/Sarkhati-Bourse)

### Inferences
- An Iran-aware backtester would be a differentiating component. The data needed for it, including the daily min/max allowed price fields and the `thirtyDayClos`-style fields, is available from `cdn.tsetmc.com/api/ClosingPrice/*`, as seen in my probe.

### Gaps
- I could not verify the current fee and tax percentages or the T+ settlement rules from these sources; that belongs to a market-rules research track.
- Closed-source Iranian platforms (e.g., broker "algo" platforms) may have backtesters, but they are out of scope for open source.

---

## Q6. Open-source dashboards, screeners, and TSETMC filter scripts

### Takeaway
TSETMC's market watch has a built-in, JavaScript-like filter language (فیلترنویسی). The open-source material is small GitHub collections of filter scripts plus a few Streamlit or Telegram screeners, all hobby-scale. "Filterkhaneh" and "Sahmeto" appear to be commercial or closed websites: no source repositories were found for them.

### Cited Findings
- The filter-writing feature is built into the TSETMC website (run by the Tehran Stock Exchange Technology Management Co.). You open market watch, adjust its settings and choose the filter icon. — [Emofid learning: TSETMC](https://learning.emofid.com/trading-with-tsetmc/); [Emofid: آشنایی با فیلتر نویسی](https://learning.emofid.com/?p=2643)
- Filter writing means writing logical code for the TSETMC market-watch system so that only symbols meeting the conditions are displayed. Example: quarterly net-profit growth of 30% or more and P/E ttm below 7. — [Enigma blog screener](https://enigma.ir/blog/screener/)
- Courses on filter writing exist, for example on Maktabkhooneh. — [Maktabkhooneh](https://maktabkhooneh.org/learn/filter-writing/)
- GitHub filter collections (all small):
  - `farhad-mohammadi/Indicator-filters-for-tsetmc-site` (JavaScript, 8 stars, 2022, no license)
  - `ironcladgeek/TSE-Filters` (2019)
  - `mahs4d/tsetmc-filters` (MIT, 2023)
  - `saeed-mcu/filterNevisi_tsetmc` (2020)
  - `abebr/tsetmc-stock-filters` (MIT, 2026-10-01, Persian docs)
  - `eekhosravi/Tsetmc-Smart-Filter` (2026-09-30)
  - `Ghoreish/tsetmc` (2026-10-07)
  - Source: [GitHub search "tsetmc filter"](https://github.com/search?q=tsetmc+filter&type=repositories)
- Dashboards and screeners:
  - `straxico/strix-stock` (32 stars, no license, 2026-02). — [GitHub](https://github.com/straxico/strix-stock)
  - `alireza79y/TSE-option-dashboard` (Streamlit, 2025-12-31). — [GitHub](https://github.com/alireza79y/TSE-option-dashboard)
  - `Farhad-Shabani/ScreeningMarketData_TSETMC` (2021). — [GitHub](https://github.com/Farhad-Shabani/ScreeningMarketData_TSETMC)
  - `hghhgh/IranBourseAnalyser` (GPL-3.0, 32 stars, 2022). — [GitHub](https://github.com/hghhgh/IranBourseAnalyser)
  - `Ali-Marandi/finsight-pro` (MIT, 110 stars, 2026-08-26), a general AI financial-analysis desktop app that surfaced in the TSETMC search. — [GitHub](https://github.com/Ali-Marandi/finsight-pro)
  - Many zero-star 2026 "AI bourse analyzer" repos exist (e.g., `behzadbakrani61-eng/bourse-radar`, `AmirRezaVR/BourseRadar`), indicating a recent wave of AI-generated hobby projects. — [GitHub search "bourse iran"](https://github.com/search?q=bourse+iran&type=repositories)
- Rahavard365: `kamyargerami/Rahavard365-Market-Map-Reader` (2022); oxtapus lists rahavard365 as a source. — [GitHub](https://github.com/kamyargerami/Rahavard365-Market-Map-Reader)

### Inferences
- A product could embed the TSETMC filter semantics (the same variable names as market watch) to attract existing "filter writers". Doing so requires re-implementing the variables, since no open-source interpreter for the TSETMC filter language was found.

### Gaps
- I found no reliable information on "Filterkhaneh" or "Sahmeto". The Persian web search returned only filter-writing tutorials and no source code or site details. I could not confirm whether they are open source; they are most likely closed commercial services.

---

## Q7. Persian financial NLP resources (Codal disclosures, news, Sahamyab)

### Takeaway
General Persian NLP is mature enough to build on: hazm, ParsBERT, and Persian-tuned open LLMs such as Dorna (Llama-3/3.1 based), PersianMind and Maral. **Finance-specific Persian resources are scarce.** There are no public, maintained Sahamyab or Codal sentiment datasets on Hugging Face; only small scrapers, a few old research repos, and a couple of 2026 hobby fine-tunes exist.

### Cited Findings
- **Toolkits and encoders:**
  - hazm: MIT, 1,433 stars, PyPI 0.12.1 on 2026-04-01. — [GitHub roshan-research/hazm](https://github.com/roshan-research/hazm)
  - ParsBERT (hooshvare): Apache-2.0, 431 stars, repo last push 2023-06-12. The model `HooshvareLab/bert-fa-base-uncased` was last modified 2021-05-18 and had about 7.9k recent downloads. — [GitHub hooshvare/parsbert](https://github.com/hooshvare/parsbert); [HF model](https://huggingface.co/HooshvareLab/bert-fa-base-uncased)
  - The ParsBERT paper reports gains over multilingual BERT on sentiment analysis, text classification and NER. — [Papers with Code](https://paperswithcode.com/paper/parsbert-transformer-based-model-for-persian)
  - `hooshvare/pn-summary`, a Persian news summarization dataset (MIT, 54 stars). — [GitHub](https://github.com/hooshvare/pn-summary)
- **Stock-specific sentiment research:**
  - A Persian sentiment review lists a 2021 paper, "ParsBERT Post-Training for Sentiment Analysis of Tweets Concerning Stock Market" (3-class, F-score about 81.83%). Its dataset source was not confirmed. — [arXiv 2212.06041](https://arxiv.org/pdf/2212.06041)
  - The same review describes a TSE study that collected Sahamyab.com stocktwits for about 3 months and built a domain-specific Persian stock lexicon, because general Persian lexicons were impractical. — [arXiv 2212.06041](https://arxiv.org/pdf/2212.06041)
  - `mhkhodashahi/persian-sentiment-data`, a news dataset with sentiment tags and annotated Iranian stock names (2019). — [GitHub](https://github.com/mhkhodashahi/persian-sentiment-data)
  - `mirzanahal/Persian-Stock-Prediction` (Telegram-channel news sentiment, 2022). — [GitHub](https://github.com/mirzanahal/Persian-Stock-Prediction)
  - `behzadbb/Sentiment-Analysis-for-Stock-Prediction-by-Deep-Learning` (MIT, 2020). — [GitHub](https://github.com/behzadbb/Sentiment-Analysis-for-Stock-Prediction-by-Deep-Learning)
  - `imRezaAlie/Dataset-TSE-chat-in-Telegram-group` (2020). — [GitHub](https://github.com/imRezaAlie/Dataset-TSE-chat-in-Telegram-group)
- **Sahamyab scrapers:**
  - `alireza-heidarii/Lightstreamer-Python-Client-To-Fetch-Sahamyab-Tweets` (12 stars, 2023). — [GitHub](https://github.com/alireza-heidarii/Lightstreamer-Python-Client-To-Fetch-Sahamyab-Tweets)
  - `e-Hajinezhad/sahamyab-sentiment-pipeline` (end-to-end pipeline using Sahamyab's API, 2026-08-16). — [GitHub](https://github.com/e-Hajinezhad/sahamyab-sentiment-pipeline)
  - `Silverbrain/SahamyabScraper` (MIT, 2025-07). — [GitHub](https://github.com/Silverbrain/SahamyabScraper)
- **Hugging Face search (2026-10-17):**
  - Zero datasets for "sahamyab", "tsetmc", "persian finance" or "farsi stock".
  - `alireza-fallah/bourse-assistant-sft` and `bourse-assistant-rl` datasets (2026-09/10, also mirrored on Kaggle as Apache 2.0), plus a `gemma3-4b-adapter-bourse-assistant` adapter.
  - `mrjodi/llama3-financial-persian` model (49 downloads).
  - Source: [HF bourse-assistant-sft](https://huggingface.co/datasets/alireza-fallah/bourse-assistant-sft); [Kaggle afallah/bourse-assistant-sft](https://www.kaggle.com/datasets/afallah/bourse-assistant-sft)
- **General Persian sentiment datasets:**
  - `SeyedAli/Persian-Text-Sentiment` and `sepidmnorozy/Persian_sentiment` on Hugging Face. — [HF](https://huggingface.co/datasets/SeyedAli/Persian-Text-Sentiment)
  - Exa-PSD (12k Persian tweets, 3 labels, ParsBERT/RoBERTa reaching 79.87 macro-F1; not finance-specific). — [arXiv 2602.20892](https://arxiv.org/pdf/2602.20892)
- **Persian LLMs:**
  - PersianMind extends LLaMA-2's vocabulary, was trained on about 2B Persian tokens, and is reported to be comparable to GPT-3.5-turbo in Persian. — [arXiv 2401.06466](https://arxiv.org/html/2401.06466v1)
  - Dorna (PartAI) is built on Llama 3 and 3.1 Instruct. `PartAI/Dorna2-Llama3.1-8B-Instruct` was last modified 2025-03-08 under the Llama 3.1 license. — [Featherless Dorna2](https://featherless.ai/models/PartAI/Dorna2-Llama3.1-8B-Instruct); [HF](https://huggingface.co/PartAI/Dorna2-Llama3.1-8B-Instruct)
  - Maral fine-tunes Mistral-7B on machine-translated Alpaca. — [arXiv 2510.12807](https://arxiv.org/html/2510.12807v1)
  - On MELAC (2025), the Dorna and Llama-family models generally score above PersianMind-v1.0 and Maral-7B-alpha-1. — [arXiv 2508.00673](https://arxiv.org/pdf/2508.00673)
  - On PerHalluEval, Dorna placed second on the Persian-specific split, behind Claude 3.7 Sonnet. — [arXiv 2509.21104](https://arxiv.org/pdf/2509.21104)
  - FarsEval-PKBETS (4,000 questions) reported average accuracy below 50% for Llama3-70B, PersianMind and Dorna. — [themoonlight review](https://themoonlight.io/review/farseval-pkbets-a-new-diverse-benchmark-for-evaluating-persian-large-language-models)
- **Kaggle TSE datasets (mostly stale):**
  - `mehradaria/tehran-stock-exchange` (ODC-By, 2023-01-26)
  - `adivireza/tse-cmi` (CC0, 2023-02-18)
  - `paymanfara/tse-stocks-data` (CC0, 2021)
  - `alireza151/...indexes-2017-to-2021` (2023)
  - `themjns/ins-list-of-iranian-market-symbols-all-segments` (CC BY 4.0, 2024-09-02)
  - `themjns/monthly-major-shareholders-in-irans-stock-market` (CC BY 4.0, 2024-08-30)
  - `mobinranjbar/tehran-stock-exchange-shareholders-tiny-version` (CC BY-NC, 2024-01)
  - Source: [Kaggle search](https://www.kaggle.com/datasets?search=tehran+stock)

### Inferences
- For Codal-disclosure analysis, the realistic stack is: a Codal scraper (codal-tsetmc, codalpy or your own, against `search.codal.ir`), then hazm normalization, then either a frontier multilingual LLM or a Persian-tuned open model (Dorna2 8B) for extraction and summarization. Expect to build and label your own finance sentiment set, because no public Sahamyab or Codal labeled corpus is maintained.
- Benchmarks show that even Persian-tuned 8B models stay below 50% on knowledge-heavy Persian tests. Frontier closed models are likely stronger for nuanced financial text, but that comparison was not directly measured for finance.

### Gaps
- I could not retrieve the full text of the 2021 "ParsBERT post-training … stock market tweets" paper, so its dataset availability and license are unknown.
- I found no evaluation of Persian LLMs specifically on financial or Codal text.
- No evidence was found for an open-source Persian LLM called "Hormoz".
