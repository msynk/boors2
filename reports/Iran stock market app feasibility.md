# Build the backtester Iran's market lacks

A personal app that collects Tehran Stock Exchange (TSE) and Iran Fara Bourse (IFB) data, cleans it and produces decision-support statistics is fully achievable in October 2026, at near-zero data cost. The free but unofficial JSON API at `cdn.tsetmc.com` still serves full daily history, individual/institutional (haghighi/hoghoughi) flows, order-book snapshots, corporate-action adjustments and Codal filings, and it answered from a US IP on 2026-10-17 when sent a browser User-Agent. No official, priced data feed with published terms could be found, and every maintained wrapper library has a single maintainer. The hard part is not getting data but making it honest. Price limits have been ±3% (not the "normal" ±5%) since before June 2025. The closing-price formula changed on 22 Dec 2025, the market shut for 9 days in June 2025 and for a record 80 days in Feb–May 2026, and TSETMC fills those closures with phantom zero-volume rows. Any backtest that ignores this is wrong. The academic evidence supports momentum, 52-week-high and limit-related effects plus one study of per-account individual flow; it does not support the LSTM price forecasts and "smart money" signals sold to retail. Competitors already sell charting, screeners and board-reading (تابلوخوانی), but none sells a point-in-time, queue-aware backtester or a statistically validated flow signal, and that gap is what the user should build. Personal use is legal. Publishing buy/sell signals to others requires a Securities and Exchange Organization (SEO, سازمان بورس) licence, and no broker offers individuals a direct trading API, so the app should stop at analysis and alerts, with trades placed manually.

## Free data flows in 2026, but only through unofficial, breakable pipes

### TSETMC's cdn API is the backbone, and it works from abroad with a browser User-Agent

The de facto primary source is `cdn.tsetmc.com/api/...`, the JSON backend behind the redesigned TSETMC site. A community reference (BabakEslami/tse-market-data, master reference v4.1 dated 15 Jul 2026) catalogues **about 93 endpoints: 63 on cdn.tsetmc.com and 30 on webgw.tse.ir**. Its authors describe them as reverse-engineered from network traffic and liable to "change at any moment without notice" ([ENDPOINTS.md](https://raw.githubusercontent.com/BabakEslami/tse-market-data/main/ENDPOINTS.md); [README](https://github.com/BabakEslami/tse-market-data/tree/main)). All endpoints are keyed by the numeric `InsCode` and use Gregorian `DEven` integers (YYYYMMDD).

Live tests on 2026-10-17 confirmed the endpoints a study app needs. `GetClosingPriceDailyList/{InsCode}/0` returned Fameli's **full history of 4,675 rows from 2007-02-04 to 2026-10-14** ([live test](https://cdn.tsetmc.com/api/ClosingPrice/GetClosingPriceDailyList/35425587644337450/0)). `GetClientTypeHistory` returned individual (`_I_`) and institutional (`_N_`) buy/sell volume, value and count ([live test](https://cdn.tsetmc.com/api/ClientType/GetClientTypeHistory/46348559193224090/20261014)). `GetPriceAdjustList` returned adjusted vs unadjusted closes per event, e.g. Foolad on 2026-08-03 at 2549 vs 2604 ([live test](https://cdn.tsetmc.com/api/ClosingPrice/GetPriceAdjustList/46348559193224090)), and `GetInstrumentShareChange` returned capital increases, e.g. 1.5T → 1.935T shares on 2026-03-30 ([live test](https://cdn.tsetmc.com/api/Instrument/GetInstrumentShareChange/46348559193224090)). For the index, `GetIndexB2History` gave TEDPIX daily history from 2008-12-04 ([live test](https://cdn.tsetmc.com/api/Index/GetIndexB2History/32097828799138957)). Shareholders, the current 5-level `BestLimits` book, intraday trades, the Codal letter mirror and supervisor messages also responded.

The API's own catalogue adds several things a pipeline must know:

- Historical `BestLimits/{InsCode}/{DEven}` is a **delta feed that needs stateful reconstruction**.
- `GetTradeHistory` came out **about 36–38% short of official trade statistics** in the authors' samples, so daily totals must come from the `ClosingPrice` fields `qTotTran5J`, `qTotCap` and `zTotTran`.
- Halted symbols are identified by `cEtaval = "IS"` («ممنوع-متوقف»).

All three come from [ENDPOINTS.md](https://raw.githubusercontent.com/BabakEslami/tse-market-data/main/ENDPOINTS.md).

Access has two hard requirements. **Without a browser User-Agent, cdn.tsetmc.com returns 403**, and the same URL returns 200 with a Chrome UA ([probe](https://cdn.tsetmc.com/api/ClosingPrice/GetClosingPriceInfo/46348559193224090)). Browser-side calls also hit CORS, so a backend proxy is mandatory; the community monitor additionally sends `Referer`/`Origin: https://www.tsetmc.com` and backs off on 429/5xx ([ENDPOINTS.md](https://raw.githubusercontent.com/BabakEslami/tse-market-data/main/ENDPOINTS.md)). No numeric rate limit is documented anywhere, so a conservative ≤1–2 requests per second with caching is the prudent default.

### Secondary sources fill the gaps the cdn API leaves

The second official-infrastructure gateway is `webgw.tse.ir`, keyed by full ISIN rather than InsCode. It adds full market watches for cash, ETF (with `navValue`), options, debt (with YTM) and futures, plus daily market value via `History/Archive` and suspension reasons via `CompanyState`. Its `MarketSummary` endpoint answered from a US IP ([live test](https://webgw.tse.ir/InstrumentProvider/api/v1/Instrument/MarketSummary/fa)).

For disclosures, Codal's `search.codal.ir/api/search/v2/q` is open JSON with no authentication. It held **627,608 letters** in total ([live test](https://search.codal.ir/api/search/v2/q?&Audited=true&AuditorRef=-1&Category=-1&Childs=true&CompanyState=-1&CompanyType=-1&Consolidatable=true&IsNotAudited=false&Length=-1&LetterType=-1&Mains=true&NotAudited=true&NotConsolidatable=true&PageNumber=1&Publisher=false&TracingNo=-1&search=false)). Of Foolad's 1,471 letters, a 20-letter sample had 18 with HTML, 9 with Excel and **0 with XBRL**, so planning around XBRL is pointless ([live test](https://search.codal.ir/api/search/v2/q?LetterType=-1&PageNumber=1&search=true&Symbol=%D9%81%D9%88%D9%84%D8%A7%D8%AF)). The TSETMC mirror `GetPreparedDataByInsCode` already joins letters to InsCodes with Gregorian timestamps ([live test](https://cdn.tsetmc.com/api/Codal/GetPreparedDataByInsCode/3/46348559193224090)). The Codal `LetterType`/`Category` codes are undocumented, and decoding them is an early engineering task.

For macro series, tgju's JSON endpoint returned **3,978 daily rows of the free-market dollar**, closing at **2,674,050 rial on 2026-10-15**. The cells contain HTML spans that must be stripped ([live test](https://api.tgju.org/v1/market/indicator/summary-table-data/price_dollar_rl)). Bonbast needs a two-step tokenised POST ([live test](https://www.bonbast.com/)). CPI comes in two series: the Statistical Centre's (SCI) reported **89.8% point-to-point for September 2026** ([IranWire](https://iranwire.com/en/news/158322-irans-inflation-reaches-898-in-september-statistical-center/)), and the CBI's urban-only index showed 84.4% y/y in August ([IranWire](https://iranwire.com/en/news/156819-irans-inflation-crisis-deepens-as-year-on-year-rate-hits-844-in-august/)). Label which series you use.

IME and IRENEX sites timed out intermittently from abroad ([live test](https://www.ime.co.ir/)). For commodity-exchange data, **BrsApi.ir** is the practical managed option. It scrapes TSETMC, offers IME APIs, publishes a demo key and **1,000 free requests/day** on at least some endpoints, and says Python's default UA is blocked by its firewall ([BrsApi](https://brsapi.ir/); [BrsApi index API](https://brsapi.ir/bourse-api-index-webservice/)). It publishes no terms of service or paid prices. Mabna/Rahavard365's API exists but returned 401 to an unauthenticated call ([live test](https://rahavard365.com/api/v2/asset/1/chart/price?period=1y)), and no public developer program was found for it. TSETMC itself references paid services and registration-based web-service help pages, but **no pricing, terms or application process is public** ([Mofid learning](https://learning.emofid.com/tehran-securities-exchange-technology-management-co/); [5j9/tsetmc](https://pypi.org/project/tsetmc/)).

### The library ecosystem turned over: the famous wrappers are stale, and small new ones are active

The best-known packages all decayed after TSETMC's redesign moved data to `cdn.tsetmc.com/api` and legacy `Loader.aspx` symbol pages began redirecting:

- **pytse-client** (315 stars) has not shipped since 0.19.1 on 2025-03-13. Issue #270 (`TooManyRedirects` on the symbol list) is still open ([GitHub](https://github.com/Glyphack/pytse-client); [issue #270](https://github.com/Glyphack/pytse-client/issues/270)).
- **finpy-tse** last shipped 1.2.10 in April 2024. A 2025 community PR that bypasses old.tsetmc.com's self-signed certificate sits unmerged ([PR #22](https://github.com/ARahimiQuant/finpy-tse/pull/22)).
- **tehran-stocks** (466 stars) last shipped in 2023 and calls only `Loader.aspx` ([PyPI](https://pypi.org/project/tehran-stocks/)).

The active packages in autumn 2026 are smaller:

- **oxtapus**: MIT, Polars-native, sync and async clients, calls cdn.tsetmc.com and tgju, release 1.3.0 on 2026-10-13, Python ≥3.11,<3.16 ([PyPI](https://pypi.org/project/oxtapus/)).
- **algotik-tse**: GPL-3.0, 1.8.0 on 2026-09-12, adds debt, energy, commodities and TGJU ([PyPI](https://pypi.org/project/algotik-tse/)).
- **5j9/tsetmc**: GPL-3.0, async, 5.1.0 on 2026-08-11 ([PyPI](https://pypi.org/project/tsetmc/)).
- **fima**: MIT, immature ([PyPI](https://pypi.org/project/fima/)).

For Codal, the options are **codal-tsetmc** (MIT, idle since Sep 2025) and **codalpy** (poorly documented) ([PyPI codal-tsetmc](https://pypi.org/project/codal-tsetmc/); [PyPI codalpy](https://pypi.org/project/codalpy/)). **No R package exists on CRAN**, and the JavaScript clients have been dormant since 2024–25 ([CRAN DB](https://crandb.r-pkg.org/-/desc); [npm tsetmc-client](https://www.npmjs.com/package/tsetmc-client)). Every maintained library has one maintainer and fewer than 40 stars. The sound strategy is therefore to **own a thin client and use oxtapus (MIT) as a reference or optional dependency**, not to build on GPL or abandoned wrappers.

### Where the notes disagree, and how this report resolves it

| Conflict | One source says | Another says | Resolution |
|---|---|---|---|
| 5j9/tsetmc Python requirement | ≥3.13 ([data-sources note](https://pypi.org/pypi/tsetmc/json)) | ≥3.14 ([libraries note](https://pypi.org/project/tsetmc/)) | PyPI metadata re-checked while writing (2026-10-17): 5.1.0 declares `requires_python >=3.14` ([PyPI JSON](https://pypi.org/pypi/tsetmc/json)). The ≥3.13 figure is out of date. hazm requires Python <3.14 ([PyPI](https://pypi.org/project/hazm/)), so the two cannot share one environment. |
| Is the TSETMC JSON API reachable from abroad? | `cdnapi.tsetmc.com` refused connections; codal.ir and seo.ir timed out (single test, rules note) | `cdn.tsetmc.com` and `search.codal.ir` returned 200 with a browser UA from a US IP (two independent probes) | These are **different hostnames**: `cdn.tsetmc.com` is the working API host, while `cdnapi.` appears to be a stale or wrong host. The codal.ir timeout concerned the main site, not `search.codal.ir`. Foreign-IP blocks have occurred before, including after the June 2025 war ([Boursepress](https://boursepress.ir/news/225995/); [Sena](https://www.sena.ir/news/74069/)), so treat abroad access as **working now but revocable**. |
| webgw.tse.ir | "Iranian IP only" ([README](https://github.com/BabakEslami/tse-market-data/tree/main)) | Answered from a US IP ([live test](https://webgw.tse.ir/InstrumentProvider/api/v1/Instrument/MarketSummary/fa)) | Reachable in Oct 2026; the README is stale or describes intermittent filtering. |
| `GetPriceAdjustList`, `GetInstrumentShareChange` | Flagged as returning empty ([ENDPOINTS.md](https://raw.githubusercontent.com/BabakEslami/tse-market-data/main/ENDPOINTS.md)) | Returned real events in live tests | They work. Validate on several symbols before relying on them. |
| `GetMarketWatch` | Returns empty; use the legacy Excel export instead | Returned about 560 KB when called with query parameters ([probe](https://cdn.tsetmc.com/api/ClosingPrice/GetClosingPriceInfo/46348559193224090)) | Probably depends on parameters. Keep `old.tsetmc.com/tsev2/excel/MarketWatchPlus.aspx?d=0` (xlsx, ~3,133 symbols, needs a 90 s timeout) as a fallback ([live test](https://old.tsetmc.com/tsev2/excel/MarketWatchPlus.aspx?d=0)). The `/tsev2/data/MarketWatchPlus.aspx` path that returned 404 is a different URL. |
| Endpoint count | 56 (repo description) | 93 (ENDPOINTS.md v4.1) | Different document versions; v4.1 is the newer one. |
| Trading-At-Last (TAL) symbol suffix and scope | "۴" suffix, first-market main board only, from Nov 2025 ([ENDPOINTS.md](https://raw.githubusercontent.com/BabakEslami/tse-market-data/main/ENDPOINTS.md)) | From 6 Jun 2026 the suffix is "۳", covering first- and second-board shares ([Nabz-e Bourse](https://nabzebourse.com/fa/news/130871/)) | The newer TSE notice wins. Map **both** suffixes to the parent InsCode. |
| Number of licensed algo brokers | 18 | 19, plus 5 under audit (same Tahlil Bazaar article) | The page date is uncertain (c. 2021 vs late 2025), so treat the list as historical ([Tahlil Bazaar](https://www.tahlilbazaar.com/news/183412/)). |
| Sahmeto | No information found (libraries note) | An AI assistant built on Codal citations ([sahmeto.com](https://sahmeto.com/)) | It exists, is closed-source and commercial. |

## Eighteen months of rule changes break any naive analysis

### The rules a backtester must encode

As of October 2026 the **daily price limit (دامنه نوسان) is ±3%** for shares and rights on TSE and the IFB main markets. The IFB base market (بازار پایه) uses ±3/±2/±1% for yellow/orange/red boards. Limits are computed on a base price, normally the previous close ([Charisma Learning, updated Shahrivar 1405](https://learning.charisma.ir/beginning-investing/%D8%AF%D8%A7%D9%85%D9%86%D9%87-%D9%86%D9%88%D8%B3%D8%A7%D9%86-%DA%86%DB%8C%D8%B3%D8%AA/)). This is a crisis setting: "normally, Iran's daily price fluctuation limit is five percent" ([Iran International](https://www.iranintl.com/en/202506283565)). The SEO chief said in May 2026 that widening the limit was postponed because "now is not the time to change a regulation" ([Hamshahri Online](https://www.hamshahrionline.ir/news/1037271/)). The exact date the ±5% → ±3% switch happened was not found. The backtester therefore needs a **dated rules table**, not constants, and the switch date has to be inferred from the data by checking max/min allowed prices against the previous close.

Four other rules belong in the engine:

- **Base volume (حجم مبنا) is set to 1 share from 22 Dec 2025.** This effectively abolished it, along with the "trading knot" rules ([Nabz-e Bourse](https://nabzebourse.com/fa/news/120131/); [Digiato](https://digiato.com/business/base-volume-removed-major-market-change-two-decades)). Before that date, a low-volume day's close was only partly moved toward the VWAP by the formula `prev_close + (VWAP − prev_close) × volume/base_volume` ([Khane Sarmaye](https://khanesarmaye.com/basis-volume/)). **Closing prices before and after 22 Dec 2025 are not strictly comparable**, and a pre-2025 "close" on a thin day is a partly synthetic number.
- **Settlement is T+2.** A T+1 proposal from Oct 2025 was consultation only ([Taadol](https://www.taadolnewspaper.ir/fa/news/362447/); [Pasargad academy](https://academy.ipasargad.ir/transaction-settlement/)).
- **Trading hours are 09:00–12:30, Saturday–Wednesday.** The closing-price stage runs 12:45–13:00 from 6 Jun 2026 ([Nabz-e Bourse](https://nabzebourse.com/fa/news/130871/)).
- **Round-trip costs are about 1.25%**: 0.3712% to buy and 0.88% to sell, including the 0.5% transfer tax on gross sale value. There is no separate capital-gains tax ([Rade Bourse](https://www.rade.ir/16808-%DA%A9%D8%A7%D8%B1%D9%85%D8%B2%D8%AF-%D9%85%D8%B9%D8%A7%D9%85%D9%84%D8%A7%D8%AA-%D8%A8%D9%88%D8%B1%D8%B3/)). The fee figures come from a single broker-education page, so keep them configurable.

Two rules break naive fill models. Symbols reopen **without any price limit** on their first day after an AGM (مجمع) or a "type-A" material disclosure halt; 249 symbols did so on 19 May 2026 ([Jamaran](https://www.jamaran.news/%D8%A8%D8%AE%D8%B4-%D8%A7%D9%82%D8%AA%D8%B5%D8%A7%D8%AF-13/1709640)). And at ±3%, one-sided demand stacks into **buy and sell queues (صف خرید/فروش)** that can persist for days, so fills at the limit are rationed ([Charisma Learning](https://learning.charisma.ir/beginning-investing/%D8%AF%D8%A7%D9%85%D9%86%D9%87-%D9%86%D9%88%D8%B3%D8%A7%D9%86-%DA%86%DB%8C%D8%B3%D8%AA/)). Sell-side order splitting by online clients has been banned since 22 Sep 2020 ([Boursepress](https://boursepress.ir/news/158294)).

### Two wars, two blackouts and a debased rial reshape every statistic

The June 2025 Israel–Iran war closed TSE for about nine days. It reopened on 28 Jun 2025 with **over 99% of stocks falling** and a 350-trillion-rial sell queue ([Iran International](https://www.iranintl.com/en/202506283565)). After US–Israeli strikes on 28 Feb 2026, equities **closed for 80 days, the longest closure on record**, and reopened on 19 May 2026 under ±3% limits. At the reopening, **42 symbols (~36% of market cap) remained suspended**, including Mobarakeh and Khuzestan steel and the Fajr and Mobin petrochemicals ([Al Jazeera](https://www.aljazeera.com/economy/2026/5/20/controlled-reopening-ends-irans-lengthy-stock-market-shutdown); [Al Arabiya](https://english.alarabiya.net/News/middle-east/2026/05/19/iran-stock-exchange-reopens-after-war-forced-longestever-shutdown)). The reopening came with administrative sell caps on institutions and major shareholders, and margin calls were suspended ([Iran International](https://www.iranintl.com/en/202605198001)). Companies were barred on security grounds from fully disclosing war damage ([Al Jazeera](https://www.aljazeera.com/economy/2026/5/20/controlled-reopening-ends-irans-lengthy-stock-market-shutdown)), which weakens fundamentals for exactly the heaviest index constituents.

In the data, these closures show up as **phantom rows**. TSETMC history has a row for every weekday of both closures with flat prices and `zTotTran = 0`. TEDPIX sits at 3,713,955.9 from 28 Feb to 18 May 2026 ([live test TEDPIX](https://cdn.tsetmc.com/api/Index/GetIndexB2History/32097828799138957); [live test Fameli](https://cdn.tsetmc.com/api/ClosingPrice/GetClosingPriceDailyList/35425587644337450/0)). Computing volatility or returns on them makes an 80-day shutdown look like a calm period followed by a gap, so these rows must be flagged before any statistics are computed.

Nominal returns mislead in this market. TEDPIX reached a record **8,505,588 on 13 Oct 2026** ([Asriran](https://www.asriran.com/fa/news/1198533/)), roughly 90% above end-2025 ([GuruFocus](https://www.gurufocus.com/news/9103205/irans-tedpix-index-soars-908-amid-economic-turbulence)), and stood at 8,265,163 (−1.91%) on 17 Oct ([live test](https://cdn.tsetmc.com/api/Index/GetIndexB1LastAll/SelectedIndexes/1)). Over the same months the free-market dollar went to ~2.7m rial ([Iran International](https://www.iranintl.com/en/202610076990)). One tracker found **+23.02% in rials but −0.43% in dollars** between 8 Jul and 2 Sep 2026 ([tehranindex.com](https://tehranindex.com/insights/iran-stock-returns-in-dollars)).

The policy backdrop is severe: a US naval blockade with oil exports "effectively stopped", and an IMF forecast of a 7.6% GDP contraction in 2026 ([JPost](https://www.jpost.com/middle-east/iran-news/article-911670)). Authorities also treat the index as a political signal and shape reopening rules "to manage the behavior of the index" ([Iran International](https://www.iranintl.com/en/202605198001)). The app must therefore report USD- and CPI-deflated returns next to rial returns, and treat administered prices as a regime variable. Examples are refinery feedstock formulas and car price lists ([Boursepress](https://boursepress.ir/news/158294)), and the Market Stabilization Fund (صندوق تثبیت) ([Taadol](https://www.taadolnewspaper.ir/fa/news/313764/)).

Infrastructure is the other risk. A near-total internet blackout began 8 Jan 2026, and a wartime shutdown cut connectivity to about 1% through March and lasted 88 days. Iran is now building a tiered, whitelisted "Barracks Internet" ([Wikipedia: 2026 blackout](https://en.wikipedia.org/wiki/2026_Internet_blackout_in_Iran); [Rest of World](https://restofworld.org/2026/iran-blackout-tiered-internet/); [Al Jazeera](https://www.aljazeera.com/news/2026/10/14/iran-targets-starlink-as-tool-of-war-with-hints-at-internet-restrictions)). Nobody tested whether cdn.tsetmc.com stayed reachable from abroad *during* those blackouts. If the user lives in Iran, a collector hosted abroad can be cut off from its owner; if the collector is hosted in Iran, it can lose foreign dependencies such as PyPI, GitHub and LLM APIs. Either way, **a complete local cache is a hard requirement**.

## Personal analysis is legal; public signals and bot trading are not

Under Iran's Securities Market Act (22 Nov 2005), providing investment advice, signals or portfolio management to others without an SEO licence is a crime under **Article 49(1)**. An SEO letter of 30 Apr 2025 explicitly extended this to apps, online platforms and app stores ([YJC](https://www.yjc.ir/fa/news/8944654/)). The SEO has previously filed criminal complaints against Telegram signal channels and worked with the cyber police to identify their operators ([Asriran](https://www.asriran.com/fa/news/752103/)). The licensed route for online advice is the **"type-2 investment adviser" (مشاور سرمایه‌گذاری نوع دوم)** licence: minimum capital of 50m toman, no office requirement, a single qualified person can hold it, and approval takes about 2–3 months in principle ([IRIB News](https://www.irib-news.ir/fa/news/3236944/)). Commercially redistributing data plausibly falls under the "financial information processing company" licence ([Sena](https://www.sena.ir/news/4140/)). No explicit rule on reselling TSETMC data was found, so that point is an inference.

Trading automation is narrower still. The SEO banned algorithmic trading for all clients in Sep 2020 ([IRIB News](https://www.irib-news.ir/fa/news/2831677/)). It re-permitted algo services in Apr 2021 only through licensed institutions that pass eight conditions: TSE, IFB, TSETMC and security-centre approvals, supervisory access and log submission ([Tejarat News](https://tejaratnews.com/%D9%85%D8%B9%D8%A7%D9%85%D9%84%D8%A7%D8%AA-%D8%A7%D9%84%DA%AF%D9%88%D8%B1%DB%8C%D8%AA%D9%85%DB%8C-%D8%AF%D8%B1-%D8%A8%D9%88%D8%B1%D8%B3); [Mehr News](https://www.mehrnews.com/news/5192979/)). Brokers such as Kian offer algo trading through vendor platforms with eligibility screening ([kian.trade](https://kian.trade/services/algorithmic-trading)). **No broker publishes an open retail trading API.** The "sarkhati" (سرخطی) queue-sniping extensions that drive Mofid, Agah and Exir web UIs are unofficial ([Firefox add-on](https://addons.mozilla.org/en-US/firefox/addon/sarkhati/); [GitHub example](https://github.com/mohammad1384p/stock)). Automating orders through a broker's retail interface is most likely outside the approved framework and against broker terms.

> **Legal note (not legal advice).** A tool the user runs for themselves, reading public data and informing manual trades, needs no SEO licence. Sharing its rankings or buy/sell calls with others, through an app, a channel or a website, requires a type-2 adviser licence or a partnership with a licensed entity, and Iranian app stores are explicitly targeted. Automated order placement is only compliant through a broker's SEO-approved algo product. The current 1404–1405 text of the algo directive and any rule on data redistribution were not retrieved, and neither was whether non-residents can operate a Sejam/brokerage account from abroad. Confirm these with a broker or lawyer before going public.

## Evidence favors momentum and filings, not LSTM forecasts

### What the academic record supports

Iranian research on TSE anomalies is extensive, though mostly on 2000s–2010s samples, and the research team saw abstracts rather than full texts, so effect sizes are unverified. Four patterns recur:

- **Price limits delay price discovery** rather than curb overreaction ([magiran](https://www.magiran.com/paper/1187556/?lang=en)), and prices accelerate toward the limit (the "magnet effect") ([IAU](https://sanad.iau.ir/en/Article/843889)).
- **Momentum works in several samples.** Adding it to Fama-French raised adjusted R² from 0.50 to 0.56 ([JAK](https://jak.uk.ac.ir/article_519.html?lang=en)). **52-week-high momentum was significantly profitable in 2004–2008**, even after FF3 adjustment ([HRMARS](https://hrmars.com/papers_submitted/10309/investigating-the-effect-of-momentum-strategies-on-investment-success-in-the-iran-stock-market.pdf)), and residual momentum is linked to under-reaction ([Ensani](https://www.ensani.ir/fa/article/download/644544)). One study found the result depends on method ([JFMP](https://jfmp.sbu.ac.ir/article_95544.html?lang=en)).
- **Herding intensifies in extreme and falling markets**, according to weekly 2016–2024 data ([Ensani](https://ensani.ir/fa/article/592892/macro-herding-behavior-and-its-implications-in-tehran-stock-exchange-an-analysis-of-extreme-market-conditions)). Industry herding coincided with higher 1–2-month momentum ([UT](https://acctgrev.ut.ac.ir/article_19113.html?lang=en)).
- **Seasonality**: excess returns in Farvardin and weakness in Esfand and Mehr ([JFR](https://jfr.ut.ac.ir/article_23830.html?lang=en)). Weakness in months 6 and 12 has been attributed to brokers settling credit at fiscal year-end ([Ensani](https://www.ensani.ir/fa/article/423878/stock-market-returns-before-and-after-brokerage-firms-fiscal-year-end-the-case-of-tehran-stock-exchange)).

The retail favourite is the weakest-tested. "Real money inflow" (ورود پول حقیقی) and per-capita buying are reported in daily commentary ([Vananews](https://vananews.com/fa/news/668066/)) and sold as signals. Rigorous evidence rests mainly on **one Sharif thesis (Moradi 2020)**: net trade per account predicted short-term returns, and buying stocks with high net individual trade per account earned positive excess returns over 1–3 months ([Sharif repository](https://repository.sharif.edu/resource/484562/-/&from=search&&query=haghpanah--n&field=authorOther&count=20&execute=true)). Order-flow imbalance explains *same-period* returns for the 30 largest stocks ([JFMP](https://jfmp.sbu.ac.ir/article_103073.html?lang=en)), which is not the same as predicting them. Evidence that TSE hedges inflation is mixed to negative: it was not a reliable hedge in about 2005 ([QJMA](https://qjma.atu.ac.ir/article_4159.html)), and gold hedged TSE risk without being a safe haven ([Ensani](https://www.ensani.ir/fa/tag/313606/Safe+haven)). **No study tested whether Codal monthly activity reports (گزارش فعالیت ماهانه) predict returns.** That is an open question the app can answer with its own data, since post-earnings drift exists on TSE ([JFMP](https://jfmp.sbu.ac.ir/article_103147_en.html)) and a monthly-announcement analog exists in the US ([RePEc](https://ideas.repec.org/a/bla/jbfnac/v46y2019i9-10p1123-1143.html)).

### Machine learning and LLMs

Iranian ML papers consistently report that hybrid models such as CNN-LSTM beat plain LSTM on RMSE/MAE for the total index ([Ensani](https://www.ensani.ir/fa/article/581418/)), and that features matter more than architecture ([IJES](https://ijes.shirazu.ac.ir/article_6213.html)). **None reports out-of-sample trading results net of costs, queues and limits.** A low error on next-day index levels is not a tradable edge when the closing price may be locked in a queue. Back-adjusted prices also leak future corporate actions into features, and delisted symbols drop out of the universe and create survivorship bias. The defensible form of ML here is **cross-sectional ranking** (for example, gradient boosting on momentum, flow and valuation features), validated walk-forward and scored net of 1.25% costs with queue-untradable days excluded. That is an engineering inference, not a published result.

For language models, the best-grounded use is summarising Codal disclosures. Code parses the numbers out of the tables, and the LLM explains the changes, which limits hallucinated figures. Benchmarks are sobering:

- GPT-4 was strongest on Persian reasoning but often trailed fine-tuned task models ([arXiv 2404.02403](https://arxiv.org/abs/2404.02403)).
- Llama3-70B, PersianMind and Dorna stayed **below 50% average accuracy on FarsEval-PKBETS** ([review](https://themoonlight.io/review/farseval-pkbets-a-new-diverse-benchmark-for-evaluating-persian-large-language-models)).
- **No benchmark of LLMs on Persian financial text exists.**

For sentiment, Sahamyab comment volume helped predict prices, but general Persian lexicons failed on stock text ([arXiv 1909.03792](https://arxiv.org/pdf/1909.03792.pdf)). Hugging Face has **zero datasets** for Sahamyab, TSETMC or Persian finance, so a sentiment model requires the user's own labelling ([HF search result](https://huggingface.co/datasets/alireza-fallah/bourse-assistant-sft)). Telegram and Sahamyab chatter is manipulation-prone and is best used as an attention or contrarian gauge. Whether foreign LLM APIs can be accessed and paid for from inside Iran was not verified. If they cannot, the open Dorna2-Llama3.1-8B ([HF](https://huggingface.co/PartAI/Dorna2-Llama3.1-8B-Instruct)) is the local fallback.

For portfolio work, limits and queues make daily returns truncated and autocorrelated. Weekly or monthly returns with shrinkage covariance (Ledoit-Wolf) and inverse-volatility or risk-parity weights are more robust than plain Markowitz. The obvious hurdle is fixed-income funds (صندوق درآمد ثابت), which mostly returned **32–36% over a recent year** ([Nabz-e Bourse](https://nabzebourse.com/fa/news/132513/)). That is still deeply negative in real terms against ~90% inflation, which explains why money rotates into equities and gold funds ([Jamaran](https://www.jamaran.news/%D8%A8%D8%AE%D8%B4-%D8%A7%D9%82%D8%AA%D8%B5%D8%A7%D8%AF-13/1709640)).

## Competitors sell charts and board-reading; nobody sells honest backtests

The Iranian market already has a crowded tool layer ([Khane Sarmaye overview](https://khanesarmaye.com/exchange-software/); [Kanoon-e Bourse](https://kanoonebourse.com/stock-market-sites/)):

| Need | Who already provides it | Verdict |
|---|---|---|
| Charting and technical analysis, advanced filter-writing (فیلترنویسی) | Rahavard 365 (Mabna; partly free, subscription for advanced features) ([Wallex](https://wallex.ir/blog/rahavard365/)) | **Buy** |
| Free custom screens on price, volume and haghighi/hoghoughi | TSETMC market-watch filters ([Emofid](https://learning.emofid.com/trading-with-tsetmc/)) | **Use free** |
| Fundamentals, ratios, peer comparison | BourseView (7-day web trial, then paid; Mofid-linked) ([Emofid](https://learning.emofid.com/get-a-subscription-bourseview/)) | **Buy** |
| Board reading, "hot money" (پول داغ), tracking big players | Tablokhani (board-reading section **300,000 toman**, period unstated) ([tablokhani.com](https://tablokhani.com/)) | Optional |
| AI Q&A over Codal, analyst ranking, Telegram aggregation | Sahmeto ([sahmeto.com](https://sahmeto.com/)) | Optional |
| Social comments and sentiment | Sahamyab | Use as raw input only |
| Point-in-time, queue-aware backtests; statistical validation of flow signals; Codal monthly-sales tracking with alerts; USD/inflation-hurdle reporting | **No one found** | **Build** |

Most published prices are missing, and whether TradingView carries TSE/IFB symbols was not confirmed. The pattern is clear even so: competitors sell signals and views, but none publishes evidence that its signals survive the Iran-specific frictions. Open-source backtesting has the same gap. A GitHub search found **no maintained backtester encoding TSE price limits, T+2, fees or queues** ([GitHub search](https://github.com/search?q=backtest+tehran&type=repositories)), and pytse-client cannot even return the daily limit ([issue #267](https://github.com/Glyphack/pytse-client/issues/267)).

## Recommended architecture: a thin collector, a point-in-time store, an Iran-aware engine

### Stack overview

| Layer | Recommendation | Why |
|---|---|---|
| Language/runtime | Python 3.12 or 3.13 (not 3.14, so that hazm works) | hazm needs <3.14 ([PyPI](https://pypi.org/project/hazm/)); oxtapus supports 3.11–3.15 ([PyPI](https://pypi.org/project/oxtapus/)). Avoid 5j9/tsetmc (≥3.14, GPL) as a core dependency. |
| HTTP client | Own thin `httpx` client (async), with oxtapus as reference or fallback | Chrome User-Agent plus `Referer`/`Origin: https://www.tsetmc.com`; ≤1–2 req/s per host; exponential backoff on 403/429/5xx; 90 s timeout for the Excel exports; certificate pinning or `verify=False` only for `old.tsetmc.com` (self-signed cert, [PR #22](https://github.com/ARahimiQuant/finpy-tse/pull/22)) |
| Raw cache | Write-once store of every response (gzip JSON keyed by URL and fetch date) | Lets you re-parse after schema fixes, survives blackouts and API changes, and gives an audit trail |
| Analytical store | Parquet partitioned by table and date, queried with DuckDB; Polars for transforms | Single-user, zero-ops, fast. Move to PostgreSQL/TimescaleDB only if multi-user. |
| Scheduler | cron/systemd timer or APScheduler. Daily EOD job after ~13:30 Tehran time, re-run in the evening for late fields; Codal poll every 15–30 min | Daily data is final after the TAL session |
| Dates and text | Store Gregorian `DEven` ints and UTC timestamps; convert to Jalali only at display with `jdatetime`/`persiantools` ([PyPI](https://pypi.org/project/persiantools/)); normalise Persian digits and Arabic/Persian letter variants before joining on symbol names | Codal returns Jalali strings with Persian digits; tgju returns both calendars |
| Backtester | Custom daily event-driven engine in Polars, driven by a dated rules table; vectorbt or backtesting.py only for quick prototypes | No generic engine handles limits, queues or closures correctly |
| UI | Streamlit for the personal phase; FastAPI + a web frontend (with `jalaali-js`) only if it becomes multi-user | Fastest route to dashboards |
| LLM (optional) | Frontier API if reachable, otherwise local Dorna2-8B; LLM sees parsed numbers only | Limits hallucination; access from Iran unverified |
| Hosting | VPS abroad works today for cdn.tsetmc.com, Codal and tgju; add an Iran-side collector or BrsApi for IME; keep a full local copy on the user's machine | Abroad access is revocable, and so is domestic internet |

### Data model and ingestion rules

The instrument master is keyed on **InsCode**, with ISIN as a secondary key for webgw joins. It should keep a symbol-name history, the `insCode2/3/4` aliases, and the TAL siblings (suffix ۳ or ۴) mapped back to the parent ([live search](https://cdn.tsetmc.com/api/Instrument/GetInstrumentSearch/%D9%81%D9%88%D9%84%D8%A7%D8%AF)). Delisted and renamed symbols stay in the universe to avoid survivorship bias.

The core fact tables are `daily_raw`, built from `ClosingPrice` fields with daily totals from `qTotTran5J/qTotCap/zTotTran` rather than trade history, plus `client_type_daily`, `corporate_events` (adjustment list plus share changes), `instrument_state` (`cEtaval`), `index_daily`, `codal_letters`, `macro_daily` (tgju USD, gold coin, gold ounce) and `macro_monthly` (SCI and CBI CPI, labelled by source).

Each row in `daily_raw` gets a `day_status` flag. A day is **`market_closed`** when the whole market shows `zTotTran = 0` and TEDPIX is flat; this covers 15–25 Jun 2025 and 28 Feb–18 May 2026, and the set should be maintained as an explicit calendar. A day is **`symbol_halted`** when `cEtaval = "IS"` or the symbol sits on the post-May-2026 suspension list. A day is **`no_trade`** when the symbol is open but has zero trades. Otherwise the day is **`traded`**. Returns, volatility, correlations and money-flow ratios are computed **on traded days only**. A non-traded day gets a null return, never zero, and the first traded day after a gap carries the whole move and is tagged as a reopening.

### Point-in-time price adjustment

Store **unadjusted prices permanently**. Never overwrite them with TSETMC's back-adjusted series. Turn each `GetPriceAdjustList` event into a factor (adjusted ÷ unadjusted close, e.g. 2549/2604 ≈ 0.979 for Foolad on 2026-08-03) ([live test](https://cdn.tsetmc.com/api/ClosingPrice/GetPriceAdjustList/46348559193224090)), and record the date the event became known.

When a strategy evaluates day *t*, build the adjusted series **as of t**, using only events with an effective date ≤ *t*. Total returns come from raw closes plus the event factor on the ex-date. Limit checks and fill prices always use **raw prices**, because the exchange computes the limit on the (adjusted) base price for that day.

Validate the factor method against oxtapus's adjusted output, since a mismatch in adjusted history was a reported bug there ([oxtapus issues](https://github.com/yghaderi/oxtapus/issues?q=is%3Aissue)). Revaluation capital increases are frequent and large, e.g. 236% and 592% ([Boursepress](https://boursepress.ir/news/158294)), so an unadjusted series is useless for return statistics.

### Backtester rules

The engine reads a dated rules table, so a policy change becomes a data edit rather than a code change:

```yaml
# rules.yaml — each block applies from its start date until superseded
- from: 2025-01-01          # exact ±5%→±3% switch date unknown; infer from max/min allowed prices
  price_limit: {tse: 0.03, ifb_main: 0.03, ifb_base: {yellow: 0.03, orange: 0.02, red: 0.01}}
  close_method: base_volume_weighted
- from: 2025-12-22
  close_method: vwap        # base volume = 1 share
- from: 2026-06-06
  session: {continuous: "09:00-12:30", tal: "12:45-13:00", tal_suffix: "۳"}
costs: {buy: 0.003712, sell: 0.0088}   # ≈1.25% round trip; 0.5% sell tax inside sell
settlement_days: 2
no_limit_days: [first_day_after_AGM, first_day_after_typeA_halt]
```

Signals form on day *t* information, and orders fill at day *t+1*. Use the open or VWAP for fills, never the same-day close.

Two days are **queue-untradable**. A buy is unfillable when the day was locked at the upper limit (`priceMin == priceMax == upper limit`). A sell is unfillable on the mirror-image locked-down day. If the price touched the limit but traded across a range, apply a configurable fill fraction (for example 0–30%) and report results under several fractions. Where historical `BestLimits` deltas have been reconstructed, use the actual queue volume instead.

Positions are frozen through `market_closed` and `symbol_halted` days and marked at the reopening print. Reopening days without a limit allow any price. Proceeds become reusable after T+2 by default. How brokers treat buying power from unsettled sales was not verified, so keep it as a switch.

Every report should show rial, USD-deflated (tgju free rate) and CPI-deflated returns, plus excess return over a fixed-income-fund hurdle. The regime splits are pre-June-2025, the ±3%/base-volume era, the post-22-Dec-2025 VWAP-close era and post-May-2026.

## Phased roadmap from data spike to research platform

| Phase | Duration (one developer, part-time) | Deliverables | Exit criterion |
|---|---|---|---|
| 0. Data spike | 1–2 weeks | Thin TSETMC client; instrument master from `GetInstrmentsHistoryInDay` and search; full daily history, client-type history, adjustment and share-change events for all symbols; TEDPIX; tgju USD and gold; raw cache | A full rebuild runs unattended, and the closure rows are detected |
| 1. Clean dataset + market dashboard (MVP) | 3–6 weeks | `day_status` flags; point-in-time adjusted series; Jalali display; dashboards for breadth, sector heatmap, individual net flow by symbol and sector, queue counts, TEDPIX in rial/USD/CPI terms, fixed-income hurdle; watchlist alerts (Telegram bot to yourself) | Statistics match TSETMC on spot checks; no phantom-row artefacts |
| 2. Iran-aware backtester + factor study | 1–3 months | Rules-table engine; momentum, 52-week-high, residual momentum; per-account individual net flow (replicating Moradi 2020); calendar effects; walk-forward results net of costs, under several queue-fill assumptions, split by regime | Each signal has an honest net-of-friction verdict, including "doesn't work" |
| 3. Codal pipeline | 1–2 months | Letter poller; decode LetterType codes; parse monthly activity reports (Excel first, then HTML tables) into MoM/YoY sales and production; earnings-release events; alerts; optional LLM summaries built on parsed numbers | Sales series for the top ~100 companies; event study of monthly-report surprises |
| 4. Portfolio and ML layer | 2–3 months | Weekly/monthly returns, Ledoit-Wolf shrinkage, inverse-vol and risk-parity sizing; gradient-boosting cross-sectional ranker with purged walk-forward validation; position-level risk report | The ranker beats a simple momentum baseline after costs out of sample, or gets dropped |
| 5. Advanced (optional) | Open-ended | Intraday and order-book reconstruction from BestLimits deltas for queue modelling; ETF NAV premium/discount from webgw; options and debt YTM; IME data via BrsApi; sentiment from self-labelled Sahamyab data | Pursue only if Phase 2–4 results justify it |

Going public means getting a type-2 adviser licence or partnering with a licensed entity before release. Without one, the app remains a personal tool, or a data-and-education product with no buy/sell calls, subject to legal confirmation.

## Achievable, risky, or out of reach

| Capability | Status | Reason |
|---|---|---|
| Full daily history, individual/institutional flows, corporate actions, indices for all TSE/IFB symbols | **Achievable now** | Free cdn API, verified live from abroad on 2026-10-17 |
| Clean, closure-aware, point-in-time adjusted dataset | **Achievable** with care | All inputs exist; the work is in flags and adjustment logic |
| Market-study dashboards in rial, USD and CPI terms | **Achievable** | tgju and SCI/CBI data available |
| Honest backtests of momentum, flow and calendar signals | **Achievable**, the core value | No competitor or library does it |
| Codal monthly sales tracking | **Achievable, labour-intensive** | Excel/HTML only, undocumented codes, little XBRL |
| Real-time intraday trading signals | **Risky** | Undocumented rate limits, intraday trade history ~36–38% short, delta-feed order books |
| Uninterrupted data during crises | **Risky** | 80-day closure, 88-day blackout, past foreign-IP blocks |
| Long-run stability of the data pipe | **Risky** | Unofficial endpoints with no SLA; single-maintainer libraries |
| Accurate fill simulation on limit days | **Partially achievable** | Queue position is unknowable from EOD data; use sensitivity ranges |
| LLM summaries of filings | **Achievable with guardrails**; access from Iran unverified | Numbers must come from parsers |
| LSTM-style price forecasting as a trading edge | **Not supported by evidence** | No net-of-friction results in the literature |
| Fundamentals for war-hit large caps | **Degraded** | War-damage disclosure suppressed; 42 large caps suspended in May 2026 |
| Automated order execution from a personal script | **Not achievable legally** | Only SEO-approved broker algo platforms; no retail API |
| Public signal or recommendation app without a licence | **Not legal** | Securities Market Act Art. 49(1); April 2025 SEO letter |
| Official, contracted TSETMC data feed | **Unknown** | Paid services are referenced but no public terms exist |

## Conclusion

The user's bottleneck is not data access, which is easier in 2026 than most Iranian investors assume. It is the gap between what the data says and what could actually have been traded. Iran's ±3% limits, queues, VWAP-close switch, 80-day closure and rial collapse each turn a naive statistic into a misleading one. Commercial tools profit from that ambiguity, selling flow signals and AI price forecasts without net-of-friction proof. The highest-value thing the user can build is a skeptic: a point-in-time, rules-versioned backtester that reports every signal in rial, dollar and inflation-adjusted terms against the fixed-income hurdle. Two untested ideas from the literature, per-account flow and Codal monthly-report surprises, are the most promising candidates to put through it.

Two design choices follow from the fragility found here. First, treat the data layer as owned infrastructure, with a raw cache, a thin client, dated rules and a full local copy, because both the endpoints and the country's internet can disappear for months. Second, keep the app strictly personal and analytical until a licence is in hand. That avoids legal exposure, and it also keeps the user honest about whether any signal deserves to be shared.
