# UX & Feature Benchmark: Global vs Iranian Market-Analysis / Trading-Analytics Apps (state as of Oct 2026; 2025–2026 focus)

Research date: 2026-10-17. Dates on each finding are the source's publication/observation date where known. Prices in USD are as reported by the cited (often affiliate) review sites; Toman prices were largely NOT found on public pages (see Gaps).

## Key Question 1 — Global leaders (2025–2026): features, dominant UI patterns, AI features, pricing, praise/complaints

### Takeaway
The global 2025–26 bar is: a dense dark multi-chart workspace (TradingView Supercharts, Quantower/Bloomberg-style dockable, colour-linked panels), a natural-language layer over screeners/charts/filings (TradingView AI Screener + Chart Copilot + AI filing summaries; Perplexity Finance's cited, conversational research with heatmaps and brokerage linking), and a tiered subscription where the most-repeated complaints are paywall creep, data delays on cheap tiers, slow alerts, and billing/refund friction.

### Cited Findings

**TradingView (reference product for charting/screening UX)**
- AI Screener (public beta, article dated 17 Aug 2026): user types a natural-language request (AI icon in upper-left of filter panel or Shift+A); the AI maps it to standard screener filters, picks a column set, places missing columns on the left, sorts by most relevant metric, and an "Explanation" panel shows why each filter was applied; available on all paid plans; a remaining-request balance is shown above the input and resets monthly; running an AI request replaces manual filters; Stock Screener only, mobile not yet supported, any language accepted but English works best — [FX News Group](https://fxnewsgroup.com/forex-news/platforms/tradingview-introduces-ai-screener/)
- Late Mar / early Apr 2026: three AI features in one week — AI document summaries (per-filing "quick insights" on the Documents tab, full summary on open, jump-to-original to verify; 10-K/10-Q/8-K/Form 4/13D/S-1/earnings transcripts from June 2024 onward), AI Corporate News (summaries within minutes of SEC/regulatory filings, seven event types incl. earnings, M&A, management changes, insider trades >$500k, IPO filings, activist changes, buybacks; filterable by watchlist/event type/provider with push notifications), and AI Chart Copilot (third-party Chrome side-panel extension "TradingView Remix", public beta 2 Apr 2026, reads symbol/timeframe/indicators/visible range, 15 free requests/day, Chromium only; reviewers call its Pine generation unreliable and fundamentals basic) — [TradersPost blog](https://blog.traderspost.io/article/tradingview-ai-features-chart-copilot-documents-news)
- Sep–Oct 2026 blog headlines: nested drawing groups (15 Oct), market breadth tools (1 Oct), earnings calls playable in-app (8 Oct), options volume profile indicator (25 Sep), "AI Copilot" introduced (28 Sep), commercial Pine scripts move to a TradingView Marketplace (30 Sep), Portfolios support reward transactions/account income (13 Oct), Perplexity Computer integrates Lightweight Charts (6 Oct) — [TradingView Blog](https://www.tradingview.com/blog/en/)
- Supercharts layout update: 12 new grid options (10 for 4- and 5-chart layouts, two 7-chart, one each for 6-, 9-, 12-chart) — undated — [FX News Group](https://fxnewsgroup.com/forex-news/platforms/tradingview-enhances-supercharts/)
- TradersPost's price context: TrendSpider from ~$52/mo (annual), Trade Ideas Holly AI Premium ~$178/mo (annual) — [TradersPost blog](https://blog.traderspost.io/article/tradingview-ai-features-chart-copilot-documents-news)
- Complaints (2025–26): most repeated on Trustpilot are unexpected charges/renewals, hard refunds and features moved behind paywalls; a Sep 2025 1-star review says the basic plan went from real-time to a 10-minute delay; alerts/indicators limited on free tier and one 2026 user reports a 45-second wait for an alert to register; mobile app "restrictive for heavy charting"; occasional web↔mobile sync delays; watchlist capped at 30 pairs for free; FRED data removed. Essential plan cited at $12.95/mo annual with 20 alerts; plans range $12.95–$199.95/mo billed yearly — [Strike.money review](https://www.strike.money/reviews/tradingview); [Trustpilot](https://www.trustpilot.com/review/tradingview.com?page=9); [G2](https://www.g2.com/products/tradingview/reviews_and_filters)

**Koyfin (dashboards + fundamentals for the retail/advisor tier)**
- Five plans: Free; Plus $39/mo; Premium $79/mo; Advisor $209 and $299/mo (another source: $0–$349). Free tier: 2Y financials, 1Y estimates, advanced charting, market & macro dashboards. Data powered by Capital IQ; >500,000 users. Main con: onboarding "can be smoother" for people new to financial data tools — [ToolRadar](https://toolradar.com/tools/koyfin/pricing); [The CFO Club](https://thecfoclub.com/tools/koyfin-review/); [daytradingz](https://daytradingz.com/koyfin-review); [Capterra](https://www.capterra.com/p/207294/Koyfin/)

**Bloomberg Terminal (what pros value)**
- The whole system is discoverable from the command line, which acts both as command entry and search bar; function mnemonics or tickers can be typed directly — [Bloomberg help PDF](https://datacenter.safe-frankfurt.de/documents/Bloomberg_help_page.pdf)
- Keyboard designed for traders with no prior computer experience; colour-coded keys — [Wikipedia](https://en.wikipedia.org/wiki/Bloomberg_Terminal)
- Launchpad = user-specific arrangement of connected Bloomberg applications laid out across a multi-display desktop; Bloomberg's UX team said UI-framework changes had to roll out gradually because "any abrupt change to the user interface can dramatically disrupt their workflows" (2017 post; page returned 403 on fetch so details are from search snippet) — [Bloomberg UX blog](https://www.bloomberg.com/ux/2017/11/10/relaunching-launchpad-disguising-ux-revolution-within-evolution/)
- 2026 opinion piece: keyboard-first design is deliberate because "the distance between your hand and the execute button is measured in dollars" — [simplefunctions.dev](https://simplefunctions.dev/blog/why-best-trading-terminal-is-command-line)

**Quantower / thinkorswim (dockable workspaces, linked panels)**
- Quantower: unlimited workspaces across multiple screens, one-click switching; panels combined into Groups or Binds; panels linked by colour tag so changing symbol in one updates the set; Workspaces Manager can lock layouts — [Quantower](https://quantower.com/interface-features); [AMP Futures help](https://help.ampfutures.com/trading-platforms/quantower/general-settings/workspaces-binds-groups); [Quantower help](https://help.quantower.com/quantower/general-settings/standalone-panels)
- thinkorswim: workspaces with preset layouts (e.g., Futures, Active Trader) that users customise — [thinkorswim manual](https://toslc.thinkorswim.com/center/howToTos/thinkManual/Getting-Started/Workspaces)

**Robinhood Legend / Webull desktop**
- Legend: desktop for active traders at no extra cost, custom/preset layouts, real-time data; "sleek, widgetized interface and 90+ technical indicators"; stocks, options, futures, crypto (launched Oct 2024) — [Slashdot/Reuters](https://news.slashdot.org/story/24/10/16/237244/robinhood-launches-desktop-platform-adds-features-and-index-options-trading); [StockBrokers.com](https://www.stockbrokers.com/review/robinhood/fees-features); [Finder](https://www.finder.com/webull-vs-robinhood)
- Webull: five platforms (web, desktop, two mobile apps incl. Webull Lite, APIs); added Webull Premium with "Vega AI"; NerdWallet 2026: Webull geared to intermediate/advanced, Robinhood streamlined for newer investors — [NerdWallet](https://www.nerdwallet.com/investing/learn/webull-vs-robinhood); [Finder](https://www.finder.com/webull-vs-robinhood)

**Flow / fundamentals / backtesting niche leaders**
- Unusual Whales (pricing checked 9 Oct 2026): Retail Basic $50/mo ($42 yearly), Pro $75 ($63), Max $120 ($102); free tier with options flow delayed 2 days, news 15 min, alerts 30 min; features: live options flow, screener, 0DTE flow, net-flow charts, GEX by strike, flow alerts, historical downloads, congressional-trade tracker (most-cited differentiator); ease-of-use scored 6/10 by one review; "less suitable for beginners" — [FlashAlpha](https://flashalpha.com/articles/spotgamma-vs-unusual-whales-comparison); [daytradingtoolkit](https://daytradingtoolkit.com/reviews/unusual-whales-review); [OptionsTradingIQ](https://optionstradingiq.com/unusual-whales-review/)
- Simply Wall St: five-axis "Snowflake" (Value, Health, Dividend, Future, Past) built from ~30 data points; free = 1 Snowflake/day, Standard $10/mo, Premium $20/mo, Pro $40/mo (another source: from $10.95/mo; another $7.50/mo annual); end-of-day data only, auto-generated DCF "sometimes optimistic", "not suitable for active traders" (2026 reviews) — [StockUnlock](https://stockunlock.com/simply-wall-st-review.html); [Freenance](https://freenance.io/products/simply-wall-st-review-2026-stock-analysis-charts-fair-value/); [QuantRoutine](https://quantroutine.com/tools/simply-wall-st/)
- TrendSpider ranked #1 in a 2026 roundup for backtesting built on the same chart rules that drive scans and alerts; Stock Rover positioned long-term/income with broad broker integration but no trading from charts — [Gitnux](https://gitnux.org/best/stock-analysis-software/); [Traders Union](https://tradersunion.com/interesting-articles/stock-analysis-software/)

**AI-native research UX (Perplexity Finance)**
- Article 19 Mar 2026: conversational ticker/company research; market dashboard with S&P/NASDAQ/Dow futures and VIX, AI-generated Market Summary with cited news; Crypto tab leaderboard; Earnings hub with upcoming/past reports and live transcripts; market heatmap added 2025 for sector performance at a glance; brokerage account linking for portfolio Q&A; price alerts; automated research tasks; SEC filing analysis; "every data point is traceable back to its original source"; Free / Pro $20/mo / Max; data from SEC/EDGAR, FactSet, S&P Global, Morningstar, Coinbase, LSEG, Quartr, Polymarket, FMP, Crunchbase — [Sid Saladi Substack](https://sidsaladi.substack.com/p/perplexity-finance-101-the-complete)
- Hands-on review: "fast, structured stock analysis but lacks depth for serious research"; lacks institutional analytics, advanced charting, trading integration vs Bloomberg — [Techpoint Africa](https://techpoint.africa/guide/perplexity-finance-review/); [CO/AI](https://getcoai.com/news/perplexitys-new-finance-tool-makes-stock-research-a-breeze)

**Dominant UI patterns (design-industry sources)**
- 2026 dashboard review: trading interfaces are "denser than anything a usability guide would approve, almost always dark, and they refresh continuously"; a terminal must fit ~five live regions on one screen — [AdminLTE blog](https://adminlte.io/blog/trading-dashboard-design-examples/)
- Green/red for gain/loss is the standard; deviating "breaks user trust immediately" — [Lollypop Design, Jun 2026](https://lollypop.design/blog/2026/june/trading-app-design/)
- Lollypop (Jun 2026) recommendations: big high-contrast live price on top, muted secondary metrics; bottom nav with portfolio/watchlists/account; one-tap trade action; dark mode as a usability need; WCAG 2.1 AA 4.5:1; don't rely on red/green alone (add arrows/icons); timeframe selector beside chart; incremental rendering with explicit loading/stale-data states; alerts configurable in ≤2 taps; AI surfaced in context of a specific asset, not as a separate layer; paper trading with real data for onboarding; order confirmation via press-and-hold/swipe — [Lollypop Design](https://lollypop.design/blog/2026/june/trading-app-design/)
- Devexperts (Sep 2024): Lite/Pro mode toggle; data-driven default layouts per cohort; multi-device; "lead with strengths" (equity traders value user-defined screening over a generic heatmap); mistakes: too many registration steps, overly complex layouts, samey design, too much irrelevant data — [Devexperts](https://devexperts.com/blog/?p=8093)
- Capital.com 2026 redesign: clients wanted "more context, less noise"; added AI assistant to search markets, platform features and FAQs "without breaking focus" (a command-palette-like pattern) — [TradeInformer](https://tradeinformer.com/broker-news/capital-com-launches-newly-designed-app-with-ai-features)

### Inferences
- "AI" in 2026 leaders is not prediction; it is (a) natural-language → structured filters with an explanation panel, (b) document/news summarisation with jump-to-source, and (c) a chart-context copilot with daily request quotas. A new Iranian app should copy this shape (explainable, quota-metered, source-linked) rather than "AI signals".
- Multi-chart grids, colour-linked symbol groups, lockable workspaces and a keyboard command line/palette are the professional-desktop table stakes; mobile leaders instead use a 3-tab bottom nav, one big live price, and ≤2-tap alerts.
- Pricing complaints cluster on moving real-time data or alerts behind higher tiers after users are hooked; whatever is free at launch should stay free.

### Gaps
- No primary TradingView 2025 changelog was retrievable (blog page 1 covers only Sep–Oct 2026); 2025 Supercharts/alerts/mobile specifics are unconfirmed.
- No hands-on 2025–26 desktop review of Webull or Robinhood Legend was found; no detail on Finviz, Thinkorswim docking, Trade Ideas Holly UX, Portfolio123/Quantpedia backtesting UX, Fintel, or LSEG Workspace panel-linking.
- No current ChatGPT finance-feature source was found; the Bloomberg Launchpad article returned 403.
- No source explicitly on "command palette" adoption in trading apps.

## Key Question 2 — Data-visualisation best practice for finance dashboards (2025–2026), including RTL

### Takeaway
Sources converge on: candlesticks for intraday price, line/area for long-term portfolio, treemaps only for rough allocation (not precise comparison), heatmaps for pattern-not-value, avoid dual axes unless there is a clear correlation need, keep bar baselines at zero and mark zero on diverging bars, small multiples ≤12 panels with shared scales, redundant encoding for red/green, and for RTL: keep numbers LTR, right-align text/legends, decide chart-direction early.

### Cited Findings
- Candlesticks recommended for financial price data; log vs linear scale for long timeframes; annotate key events (earnings, M&A); treemaps suit portfolio allocation — [grimbard data-visualization skill](https://skills.sh/igbuend/grimbard/data-visualization)
- Nielsen Norman (second-hand): treemaps are poor for precise comparison because area is not preattentively processed; treemaps/bubbles are weak whenever the reader must judge values — [Querio](https://querio.ai/blogs/data-visualisation-best-practice)
- Red/green coding is hard to distinguish for ~8% of men; fixes: redundant encoding (icons), high-contrast palettes such as blue/orange/grey, texture/pattern — [DataCalculus](https://datacalculus.com/en/knowledge-hub/data-analytics/data-visualization/colorblind-friendly-visualization-design)
- Dual axes: generally discouraged because easily misinterpreted or manipulated — use separate charts; Datawrapper: good for comparing ups/downs of measures with different units but readers "draw the wrong insights"; an IEEE finance case study argues dual-axis time series are necessary for fine-grained correlation — [Learnsignal](https://www.learnsignal.com/blog/choosing-charts-data-visualisation-finance-reports-guide/); [Datawrapper](https://www.datawrapper.de/blog/dual-axis-charts-guide); [IEEE](https://ieeexplore.ieee.org/document/9373177)
- Small multiples: same x and y scales per panel, ≤12 panels; Highcharts calls them the safer alternative to dual axes — [ONS service manual](https://service-manual.ons.gov.uk/data-visualisation/chart-types/small-multiple-charts); [Highcharts](https://www.highcharts.com/blog/?p=31290)
- Diverging bars: use for positive vs negative from a baseline, clearly mark the zero line; keep bar axes at zero; heatmaps poor when exact values needed; avoid rainbow scales — [sheets.works chart guide](https://sheets.works/data-viz/guide)
- Mobile charts: line/area for long-term portfolio, candlestick for intraday; timeframe selector adjacent; explicit loading and stale-data states; incremental rendering — [Lollypop Design](https://lollypop.design/blog/2026/june/trading-app-design/)
- RTL: numerals, punctuation and brand names keep LTR flow inside RTL text — [Argos Multilingual](https://www.argosmultilingual.com/blog/planning-for-rtl-languages-how-layout-content-and-qa-fit-together); one guide says chart axes/data should start from the right, another says many Arabic speakers are at ease with LTR charts and recommends right-aligning labels/titles/legends; "decide early whether the chart's flow should mirror the text" — [Centus](https://centus.com/blog/right-to-left-languages-translation); [Ronas IT](https://ronasit.com/blog/right-to-left-design-guide/)
- AG Charts has an RTL option covering text in data elements and chart visual direction, detected from a `dir="rtl"` ancestor; Tabler uses CSS logical properties so one stylesheet works both ways — [AG Charts](https://ag-grid.com/charts/javascript/rtl.md); [Tabler](https://docs.tabler.io/ui/getting-started/rtl.md)
- Research on Arabic data visualisation found tools often lack proper RTL support, forcing manual mirroring/alignment fixes — [st-hakky](https://book.st-hakky.com/en/news/designing-within-line-arabic-context-pra)

### Inferences
- For a Persian app: keep time axes LTR (oldest→newest left→right, as TSETMC and all Iranian charting already do), right-align labels/legends/tooltips, render Persian digits only in text/tables (not inside chart axes unless tested), and choose a charting library with first-class `dir="rtl"` handling or isolate charts in an LTR island.
- Use treemap/heatmap for the "market map" overview (sector size × % change) but always pair with a sortable table for exact values; use diverging bars for haghighi/hoghooghi net flow (zero line marked) rather than dual-axis overlays on price.
- Offer a colour-blind palette toggle (blue/orange) and redundant arrows on every up/down cell; note that Iran's convention is green=up/red=down (same as Western, unlike East Asia).

### Gaps
- No finance-specific 2025–26 study on calendar heatmaps or on Persian-digit rendering in charts was found; RTL chart-direction advice is conflicting and from vendor blogs.

## Key Question 3 — Iranian apps (2025–2026): UX, features, charting, pricing, ratings, AI, Jalali/RTL handling, flows/queues, alerts

### Takeaway
The Iranian landscape splits into broker-bundled platforms (Mofid EasyTrader/EasyChart + BourseView; Agah AsaTrader; Farabixo) that are free with a brokerage account, standalone analysis tools (Rahavard 365 web+mobile, Rahavard Novin desktop) sold on subscription (amounts not publicly indexed), the public utility TSETMC (new site live since May 2023, old site kept at old.tsetmc.com), and a first LLM-style entrant (Sahmeto: Codal-document Q&A, free trial, pricing "not finalised", 1405). Market maps (tree/bubble/scatter), formula filters, haghighi/hoghooghi panels, queue views and price alerts are all present somewhere; no Iranian product was found offering a TradingView-grade multi-chart workspace or explainable AI screener.

### Cited Findings

**TSETMC (public reference data site)**
- New site: traffic shifted to it the week of 2 Khordad 1402 (~23 May 2023) after about a year of testing in quiet hours; a tab on the new site and old.tsetmc.com give access to the old UI; old site frozen, all new development on the new site; plugin developers using tsetmc.com data told to migrate; criticism that it launched during market hours; a direct-feedback messaging section promised — [Nabz-e Bourse, 1402/03/02](https://nabzebourse.com/fa/news/57325/سایت-قدیم-tsetmc-همچنان-در-دسترس-است)
- Layout: home dashboard (index status for bourse/farabourse, popular symbols, index movers, total market value); top tabs per market (TSE, Farabourse, Energy, Codal, funds, Commodity); left menu led by Market Watch (دیده‌بان بازار) with row/card/classic views, custom templates, column picker, sort by volume/price/value, full-screen, Excel/HTML export, formula filter (e.g., symbols with buy queues); symbol page: right column live trade stats, middle column price band/base volume/free float/52-week, left column haghighi vs hoghooghi buy/sell; tabs for shareholders, price history, ناظر messages, trade-by-trade, status history; TSE "supply and demand" tab shows top buy/sell queues — [Khane Sarmaye](https://khanesarmaye.com/tsetmc/)
- Historical outages: 1400 server failures acknowledged by TSETMC CEO Rouhollah Dehghan; a Donya-e-Eqtesad report of access disruption at the market open — [Nabz-e Bourse](https://nabzebourse.com/fa/news/34271/مشکل-سایت-tsetmc-در-حال-بررسی-است); [Donya-e-Eqtesad](https://donya-e-eqtesad.com/fa/tiny/news-4110305)
- Browser extensions "TSETMC PLUS" and "LiveTSE" add live-trade details and major-shareholder movement tracking on top of TSETMC — [extscope](https://extscope.org/extension/aaojgabhhamejdlcenggjjcimjgbljin); [extscope](https://extscope.org/extension/mfjfecdfglbbkhoconmkfmhofndbdnkj)

**Rahavard 365 (Mabna)**
- Browser-based, no install, data direct from TSE, technical + fundamental analysis + financial statements; priced "free / subscription" (no Toman figures); recommended as first choice for Iranian traders, especially beginners (article titled 2026) — [Khane Sarmaye](https://khanesarmaye.com/best-technical-analysis-tools)
- Technical & advanced analysis, smart filters, many variables, market map, web version; listed on both Google Play and App Store (article ~5 years old) — [Technolife blog](https://www.technolife.com/blog/بهترین-اپلیکیشن-ها-برای-فعالیت-در-بازا/)
- Rahavard Novin (desktop, Mabna) 3rd edition: market report pack, technical, fundamental, portfolio management; free registration tier — [Donya-e-Eqtesad](https://donya-e-eqtesad.com/fa/tiny/news-637489)

**Mofid (EasyTrader / EasyChart / Mofid Trader / BourseView)**
- EasyTrader: candlestick price view and a market map in three modes — tree, bubble, scatter; "designed to be attractive to professionals and fully usable by beginners" — [learning.emofid.com](https://learning.emofid.com/?p=1498); [learning.emofid.com](https://learning.emofid.com/orbis/)
- EasyChart inside EasyTrader: many indicators/drawings, saved templates, multi-symbol compare, corporate events (dividends, capital increases) on chart, adjusted/unadjusted, linear/log/percent scales, minute→monthly, order placement from chart; in-app alerts under "سایر → هشدار درون‌برنامه‌ای"; Mofid Trader = localised MetaTrader 5 with 21 timeframes, tiled multi-chart windows, market depth, Codal news, free for Mofid clients, service ending end-2023 (article 2 Jan 2021) — [learning.emofid.com](https://learning.emofid.com/?p=7195)
- Mofid Trader alternatives for non-Mofid users named as Rahavard 365 and BourseView — [learning.emofid.com](https://learning.emofid.com/?p=30613)
- BourseView: fundamental platform — ratios, industry data, stock screener by strategy, market map on fundamental metrics, fixed-income data, Codal announcements posted as published, investment-company portfolios — [learning.emofid.com](https://learning.emofid.com/?p=7195)
- BourseView subscription (article 26 Jan 2026): web plans "Standard" and "Professional"; price computed from plan × duration (1 month, 3 months, …) × users; 7-day free trial; mobile web-app free for Mofid clients; buy with cash or Pelekan loyalty points (earned by trading and referrals), with Pelekan discounts — [learning.emofid.com](https://learning.emofid.com/get-a-subscription-bourseview/)
- BourseView mobile "تابلوخوانی" filters include buy/sell queue threshold, highest daily swing, suspicious volume; web has "غربالگری پیشرفته" formula filtering — [learning.emofid.com](https://learning.emofid.com/filter-in-bourseview-mobile-app/)
- EasyTrader Android/iOS not on Google Play/App Store, installed via vendor link — [Technolife blog](https://www.technolife.com/blog/بهترین-اپلیکیشن-ها-برای-فعالیت-در-بازا/)

**Agah AsaTrader (Cafe Bazaar listing, fetched 17 Oct 2026)**
- Rating 4.0/5 from 30,709 votes; 3 million installs; v3.67.0.69; last update 4 Mehr 1405 (~26 Sep 2026); 20 MB; features: Sejam e-KYC, portfolio overview (market value, balance, net value, P/L, charts by bond/symbol/industry), custom watchlists, queue position and order status with time filters, buying/selling-power charts, candlestick charts, order book, quick order from watchlist, advanced order entry, indices/market overview, offline IPO orders, technical charts with indicators, multi-user management, dividend/capital-increase history — [Cafe Bazaar](https://cafebazaar.ir/app/com.agah.asatrader)
- Reviews: 1405/07/03 "for 20 days orders register exactly 5 seconds after the golden time every day… don't use for fast trading"; 1405/06/31 deposits/withdrawals "confuse more than guide"; 1405/06/28 "complete in every respect" — [Cafe Bazaar](https://cafebazaar.ir/app/com.agah.asatrader)

**Farabixo (Farabi) — article titled 1405**
- Two modes (simple / Advanced with full candlestick chart and order-from-chart); Market section: top gainers/losers, most traded, money flow by industry group, main and equal-weight index; Hot List grouped by short/medium/long horizon with candlestick patterns and "strong stocks"; price alert per watchlist symbol; live portfolio with average buy price; floating/multiple order forms; pre-orders outside hours; web + Android + iOS; "Bikso" credit for premium analyst signals; no platform fee mentioned; 1405 commissions ~0.464% buy / ~0.975% sell — [Khane Sarmaye](https://khanesarmaye.com/farabixo/)

**Sahmeto (AI entrant)**
- Uses AI to analyse financial, performance and price data of Bourse/Farabourse companies; three modes: document-grounded answers from Codal, quick report summaries, multi-symbol/period comparison (sales, revenue, EPS, margins, ratios); explicitly gives no buy/sell/hold recommendation; 3 free messages logged-out, unlimited when logged in; under licence of investment-advisory firm Farbin; copyright 1405 — [Sahmeto](https://sahmeto.com/)
- Pricing page: in trial phase, free, no card; pricing model "هنوز نهایی نشده", users to be notified before paid plans — [Sahmeto pricing](https://sahmeto.com/pricing)

**Other apps / tools**
- Cafe Bazaar "بورس ویو | آموزش بورس" (smr.apps.bours22) is an education app also showing Edalat shares and brokerage assets — not Mofid's BourseView — [Cafe Bazaar](https://cafebazaar.ir/app/smr.apps.bours22)
- Rayan Hamrah: automated technical analysis, all indices, broker integrations, Android only (old article) — [Technolife blog](https://www.technolife.com/blog/بهترین-اپلیکیشن-ها-برای-فعالیت-در-بازا/)
- TradingView now carries some high-volume Iranian symbols and the total index, coverage incomplete — [Khane Sarmaye](https://khanesarmaye.com/best-technical-analysis-tools)
- Filters are screening, "the start of analysis, not the end" (common Iranian educational framing) — [Khane Sarmaye](https://khanesarmaye.com/oscillation-filters/)

**AI in Iranian market (context)**
- Digiato (Mehr 1404) discusses AI changing investing in Iran but names no product; Donya-e-Eqtesad analyst: robo-advice is harder in Iran, e.g., a robot buy signal fails because of queue constraints — [Digiato](https://digiato.com/iran-technology-news/how-ai-transform-the-future-of-investing-in-stock-market); [Donya-e-Eqtesad](https://donya-e-eqtesad.com/fa/tiny/news-3667326)

### Inferences
- Market map (tree/bubble/scatter), formula/"فیلترنویسی" screeners, haghighi/hoghouqi columns, queue depth/position and simple price alerts are already table stakes in Iran; charting is mostly single-chart with templates, and MT5-grade multi-window charting left the market when Mofid Trader ended (2023).
- Jalali dates are universal and RTL layouts are native in all Iranian products (TSETMC columns run right→left with left-column for haghighi/hoghoughi); no source documents dark mode, so it is likely a differentiator opportunity.
- The only AI product found (Sahmeto) is document-QA over Codal with an advisory licence and explicit no-recommendation stance — the regulatory-safe shape for AI in Iran.
- Pricing is opaque: Iranian vendors compute price in-app (plan × duration × seats) and use loyalty points (Pelekan) and brokerage bundling rather than public price lists.

### Gaps
- No Toman price for Rahavard 365, BourseView, Tablokhani, Sahamyab, Filterkhaneh, Didebane Bazar or Rahavard Novin was indexed anywhere; the Rahavard site (rahavard365.com) served only a title and its /pricing URL has a certificate mismatch; Cafe Bazaar pages for Rahavard 365 and the Mofid app returned 404 for guessed package IDs.
- No source confirms which Iranian apps embed TradingView's charting library, which offer dark mode, or how alerts are delivered (push vs SMS).
- Sahamyab, Tablokhani.com, "Novaran Amin Tahlil", "Tahlilgaran" and Exir-based platforms: no 2025–26 source found.
- Myket ratings/reviews not retrievable.

## Key Question 4 — Iranian user complaints, rankings and demands (2025–2026)

### Takeaway
No neutral 2025–26 ranking of Iranian bourse apps exists in indexed sources; the recurring complaint themes (from app-store reviews and press) are order-timing lag at the "golden" pre-open second, platform outages at open/close, confusing cash in/out flows, data lag, and poor vendor communication during outages — plus a structural distrust of "signal" services.

### Cited Findings
- AsaTrader review 1405/07/03: orders registering 5 s after the golden time for 20 days; 1405/06/31: deposit/withdrawal flow confusing — [Cafe Bazaar](https://cafebazaar.ir/app/com.agah.asatrader)
- Farabi, Agah and Mofid all suffered pre-open outages on the same day, resolved near 09:00, with no explanatory message to users; Agah outage (Tir 1399) ~12 h with users reporting cutoffs "half an hour before market and one hour after, always" and "not accountable to the customer" — [Nabz-e Bourse](https://nabzebourse.com/fa/news/15363/در-پیش-گشایش-پرتفوی-کاربران-کارگزاری-فارابی-پرید-|-صدای-سهامدار); [Tejarat News](https://tejaratnews.com/قطع-سامانه-معاملاتی-کارگزاری-آگاه)
- Nabz-e Bourse warns that divergent signal services can mislead analysts — [Nabz-e Bourse](https://nabzebourse.com/fa/news/66242/)
- A Cafe Bazaar user of a signal app reports it "sticks on a black screen" — [Cafe Bazaar](https://cafebazaar.ir/app/com.example.zarrincoinsignal)
- Criteria Iranian educators use to judge tools: full TSE+Farabourse coverage, real-time or low-lag data, trend-line and Fibonacci drawing — [Khane Sarmaye](https://khanesarmaye.com/best-technical-analysis-tools)
- 2025 VPN/filtering context: "free internet" packages 80–120k Toman (15–20 GB) to 150–200k Toman (50 GB) around Farvardin 1404 — a cost many users already bear for foreign tools — [Taadol](https://www.taadolnewspaper.ir/fa/news/382779/)

### Inferences
- Reliability at 08:30–09:00 and 12:30 (open/close) is the single most emotionally loaded issue; a status banner, offline/degraded mode and honest incident messaging would be differentiators.
- Users accept paying tens-of-thousands of Toman monthly for connectivity, suggesting a price ceiling for an analysis app in the low hundreds of thousands of Toman/month — but this is an inference, not a sourced figure.

### Gaps
- No 2025–26 (1404–1405) ranking or survey of Iranian bourse apps; no Myket review data; no sourced data on crash behaviour during 2025–26 internet blackouts.

## Key Question 5 — Synthesised benchmark: table-stakes, differentiators, mistakes to avoid

### Takeaway
Table stakes = what both TradingView-class tools and the better Iranian broker apps already ship; differentiators = the 2026 AI/UX layer nobody in Iran has yet shipped; mistakes = the complaint clusters above.

### Cited Findings
- Table stakes (present in global leaders and in at least one Iranian product): market watch with custom columns/templates/export and formula filters ([Khane Sarmaye/TSETMC](https://khanesarmaye.com/tsetmc/)); market map tree/bubble/scatter ([Mofid](https://learning.emofid.com/orbis/)); haghighi/hoghoughi panel and queue views ([Khane Sarmaye/TSETMC](https://khanesarmaye.com/tsetmc/), [Cafe Bazaar/AsaTrader](https://cafebazaar.ir/app/com.agah.asatrader)); candlestick with indicators, templates, adjusted/unadjusted, log/linear, corporate events on chart ([Mofid](https://learning.emofid.com/?p=7195)); price alerts ≤2 taps ([Lollypop](https://lollypop.design/blog/2026/june/trading-app-design/), [Farabixo](https://khanesarmaye.com/farabixo/)); portfolio with avg cost and P/L ([Farabixo](https://khanesarmaye.com/farabixo/)); dark dense desktop UI with green/red convention ([AdminLTE](https://adminlte.io/blog/trading-dashboard-design-examples/)); free trial + tiered plans ([BourseView](https://learning.emofid.com/get-a-subscription-bourseview/), [Koyfin](https://toolradar.com/tools/koyfin/pricing)).
- Differentiators (global 2026, absent in Iran per sources): explainable NL screener with monthly quota and reason-per-filter ([FX News Group](https://fxnewsgroup.com/forex-news/platforms/tradingview-introduces-ai-screener/)); AI summaries of Codal filings with jump-to-source and event-typed corporate news feed with watchlist filters ([TradersPost](https://blog.traderspost.io/article/tradingview-ai-features-chart-copilot-documents-news)); cited conversational research + market summary + earnings hub ([Perplexity Finance guide](https://sidsaladi.substack.com/p/perplexity-finance-101-the-complete)); multi-chart grids and colour-linked panel groups with lockable workspaces ([Quantower](https://quantower.com/interface-features)); command-line/palette search ([Bloomberg help](https://datacenter.safe-frankfurt.de/documents/Bloomberg_help_page.pdf), [Capital.com](https://tradeinformer.com/broker-news/capital-com-launches-newly-designed-app-with-ai-features)); Lite/Pro toggle ([Devexperts](https://devexperts.com/blog/?p=8093)); visual fundamentals score à la Snowflake ([StockUnlock](https://stockunlock.com/simply-wall-st-review.html)); rule-based scan → alert → backtest on the same rules ([Gitnux/TrendSpider](https://gitnux.org/best/stock-analysis-software/)); colour-blind-safe redundant encoding ([DataCalculus](https://datacalculus.com/en/knowledge-hub/data-analytics/data-visualization/colorblind-friendly-visualization-design)).
- Mistakes to avoid: moving real-time data/alerts behind paywall after launch and opaque renewals ([Strike.money](https://www.strike.money/reviews/tradingview), [Trustpilot](https://www.trustpilot.com/review/tradingview.com?page=9)); slow alert registration ([Strike.money](https://www.strike.money/reviews/tradingview)); outages at open with no user messaging ([Tejarat News](https://tejaratnews.com/قطع-سامانه-معاملاتی-کارگزاری-آگاه)); confusing cash flows ([Cafe Bazaar](https://cafebazaar.ir/app/com.agah.asatrader)); dense jargon for retail, missing loading/stale/partial states, AI that adds noise, gamified over-trading, non-standard gain/loss colours ([Lollypop](https://lollypop.design/blog/2026/june/trading-app-design/)); treemaps where exact comparison is needed and dual axes without a clear reason ([Querio](https://querio.ai/blogs/data-visualisation-best-practice), [Datawrapper](https://www.datawrapper.de/blog/dual-axis-charts-guide)); launching big UI changes abruptly to professional users ([Bloomberg UX](https://www.bloomberg.com/ux/2017/11/10/relaunching-launchpad-disguising-ux-revolution-within-evolution/)); AI buy/sell "signals" (regulatory and trust risk — Sahmeto explicitly avoids them) ([Sahmeto](https://sahmeto.com/)).

### Inferences
- A best-in-class Iranian app in 2026 = TSETMC's data completeness + Mofid-grade market map/screener + TradingView-style multi-chart workspace + Perplexity/TradingView-style explainable, source-linked AI over Codal and ناظر messages, delivered with incident transparency and a stable, public Toman price list.

### Gaps
- Because Iranian Toman prices and app-store ratings for the main analysis tools were not indexable, the benchmark's pricing column for Iran is empty except for Farabi commissions and Sahmeto's "free during trial".
