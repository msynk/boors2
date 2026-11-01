# Iranian capital market (TSE / IFB): microstructure, trading rules, regulation and 2024-2026 events, as of 17 Oct 2026 (25 Mehr 1405)

Date conventions: Gregorian first, Jalali (SH) in brackets. Useful anchors: 1 Farvardin 1405 = 21 Mar 2026; 9 Esfand 1404 = 28 Feb 2026; 29 Ordibehesht 1405 = 19 May 2026; 16 Khordad 1405 = 6 Jun 2026; 1 Dey 1404 = 22 Dec 2025; 7 Tir 1404 = 28 Jun 2025; 21 Mehr 1405 = 13 Oct 2026.
Source note: research done 17 Oct 2026. Several Persian sources are broker education pages or news sites. Some facts come from search-engine summaries of pages I could not fully load; these are marked "(snippet)". Wikipedia articles about 2026 events are fast-moving and contain internal inconsistencies, so I cross-checked them where possible.

## Q1. Trading rules an analysis/backtest must model (price limits, base volume, settlement, hours, fees, halts, IPOs, queues, funds)

### Takeaway
As of Oct 2026, the daily price limit is ±3% for shares and rights on TSE and on the IFB main markets. The IFB base market uses ±3/±2/±1% by board color. These are crisis settings: the "normal" limit was ±5%. Base volume (حجم مبنا) was effectively abolished (set to 1 share) for admitted companies from 22 Dec 2025. Shares still settle T+2; a move to T+1 was only under consultation. Regular trading runs 09:00–12:30, plus a closing-price trading stage (TAL) from 12:45 to 13:00 on TSE main and secondary boards. Round-trip cost is about 1.25% (0.3712% to buy plus 0.88% to sell, including the 0.5% sell tax). A backtest must also model long multi-week market-wide closures, symbols that stay suspended for months, and reopenings without a price limit.

### Cited Findings
**Daily price limit (دامنه نوسان)**
- 1405 (2026): the limit for shares and rights on TSE and the IFB main markets (first, second and "Novafarin" boards) is ±3%. The IFB base market (بازار پایه) uses ±3% for the yellow board, ±2% for orange and ±1% for red. Some funds and futures have their own limits. Formula: max = base price × (1 + limit); min = base price × (1 − limit). The base price is normally the previous session's closing price. Limit changes are proposed by the exchanges and decided by the SEO board. — [Charisma Learning, updated 7 Shahrivar 1405](https://learning.charisma.ir/beginning-investing/%D8%AF%D8%A7%D9%85%D9%86%D9%87-%D9%86%D9%88%D8%B3%D8%A7%D9%86-%DA%86%DB%8C%D8%B3%D8%AA/)
- In Ordibehesht 1405 (May 2026), the SEO head confirmed the limit stayed at −3% to +3%. — [Charisma Learning](https://learning.charisma.ir/beginning-investing/%D8%AF%D8%A7%D9%85%D9%86%D9%87-%D9%86%D9%88%D8%B3%D8%A7%D9%86-%DA%86%DB%8C%D8%B3%D8%AA/)
- "Normally, Iran's daily price fluctuation limit is five percent." In earlier crises (Iran's missile strikes on Israel; the death of President Raisi in May 2024) the SEO cut daily limits to contain losses. — [Iran International, 28 Jun 2025](https://www.iranintl.com/en/202506283565)
- The ±3% regime was already in force before the June 2025 war. The SEO reopened after that war with ±3%, "the same as before the war". It rejected a ±1% option (seen as leading to an attritional decline) and an asymmetric-limit option, because asymmetric limits had been tried before and were judged unsuccessful. — (snippet) [Fararu "فرماندهی بورس پس از جنگ"](https://fararu.com/fa/news/885597/%D9%81%D8%B1%D9%85%D8%A7%D9%86%D8%AF%D9%87%DB%8C-%D8%A8%D9%88%D8%B1%D8%B3-%D9%BE%D8%B3-%D8%A7%D8%B2-%D8%AC%D9%86%DA%AF)
- May 2026: SEO head Hojatollah Seydi said the market "closed with a 3% limit and will reopen with 3%". He said the next planned step after the base-volume reform, before the war, had been to remove or widen the price limit, but that this was postponed because "now is not the time to change a regulation". — [Hamshahri Online](https://www.hamshahrionline.ir/news/1037271/); [Fararu](https://fararu.com/fa/news/972317/)
- About Sept 2026: the TSE CEO said the exchange had submitted "an idea on the price limit" to the SEO but gave no details. No change had been reported as of mid-Oct 2026. — [Sharq Daily](https://www.sharghdaily.com/%D8%A8%D8%AE%D8%B4-%D8%A7%D9%82%D8%AA%D8%B5%D8%A7%D8%AF%DB%8C-12/1124788-%D8%AF%D8%A7%D9%85%D9%86%D9%87-%D9%86%D9%88%D8%B3%D8%A7%D9%86-%D8%A8%D9%88%D8%B1%D8%B3-%D8%AA%D8%BA%DB%8C%DB%8C%D8%B1-%D9%85%DB%8C-%D9%83%D9%86%D8%AF-%D9%88%DB%8C%D8%AF%DB%8C%D9%88)
- Conflicting older broker pages still list ±5% (TSE) or ±7% (IFB). These are stale. — [Ebidar](https://ebidar.com/education/stock-fluctuation-range/); [SD Broker](https://www.sdbroker.ir/article/230/range-volatility-price-stocks)

**Base volume (حجم مبنا) and the "trading knot" (گره معاملاتی)**
- Historical rule: base volume = 0.0004 × shares outstanding, clamped by base value (TSE: 50–100 bn rials; other sources say up to 120 bn). It was recalculated weekly from Wednesday's close and applied from Saturday. If daily volume < base volume, close = prev_close + (VWAP − prev_close) × (volume / base_volume). Otherwise close = VWAP. IFB base market floors were 5/10/20 bn rials for red/orange/yellow boards. It was introduced about 22 years ago and extended to IFB in Esfand 1398. — [Khane Sarmaye](https://khanesarmaye.com/basis-volume/); [Parsian Bourse](https://parsianbourse.com/articles/base-volume/)
- **Current rule:** from 1 Dey 1404 (22 Dec 2025), base volume is set to **1 share** for all symbols of companies admitted (پذیرفته‌شده) on TSE and IFB. This is effectively abolition, decided by the TSE and IFB boards and approved by the SEO board. The "trading knot" rules were also discontinued from that date. As a result, the closing price ≈ the session's VWAP of trades. — [Nabz-e Bourse](https://nabzebourse.com/fa/news/120131/); [Digiato](https://digiato.com/business/base-volume-removed-major-market-change-two-decades)
- The IFB base market has used a "dynamic base volume" since Mehr 1404 (Sep–Oct 2025). IFB reported that the fill rate rose to 88% and that "lock-up" cases fell (6,309 under the fixed method). IFB then proposed adding a "queue coefficient" = weekly average daily volume ÷ weekly average daily queue volume, floored at 0.5 and capped at 1. Reports say it applies from Saturday 4 Mehr (26 Sep 2026). — [Vananews](https://vananews.com/fa/news/666470/); (snippet) [Petrochemiha](https://petrochemiha.ir/%D9%81%D8%B1%D9%85%D9%88%D9%84-%D8%AD%D8%AC%D9%85-%D9%85%D8%A8%D9%86%D8%A7-%D8%AF%D8%B1-%D8%A8%D8%A7%D8%B2%D8%A7%D8%B1-%D9%BE%D8%A7%DB%8C%D9%87-%D9%81%D8%B1%D8%A7%D8%A8%D9%88%D8%B1%D8%B3-%D8%AA%D8%BA/)
- Base volume of 1 has long applied to ETFs, on the first reopening day after a long halt, and to block trades. — [Khane Sarmaye](https://khanesarmaye.com/basis-volume/)

**Settlement cycle**
- Shares and rights still settle **T+2**. Some instruments (e.g., commodity deposit certificates) settle T+1. — [Pasargad academy](https://academy.ipasargad.ir/transaction-settlement/); [Emofid](https://learning.emofid.com/tasviyeh_moamelat/)
- Mehr 1404 (Oct 2025): the SEO asked market participants for views on cutting cash settlement of shares from T+2 to T+1. The SEO's head of exchange supervision said the letter "has no executive force" and was only for consultation. I found no report that T+1 was implemented by Oct 2026. — [Taadol newspaper](https://www.taadolnewspaper.ir/fa/news/362447/)

**Trading hours**
- From Saturday 16 Khordad 1405 (6 Jun 2026), all equity markets, equity funds and their derivatives end at **12:30**. Shares trade 09:00–12:30. The closing-price stage (TAL, معاملات پایانی) was reactivated for all TSE first- and second-board shares at **12:45–13:00**, and its trading symbol suffix changed from "نماد۴" to "نماد۳". The notice is TSE notice dated 13 Khordad 1405, referencing notice 181/1405024403 of 5 Khordad 1405. — [Nabz-e Bourse](https://nabzebourse.com/fa/news/130871/)
- On the 19–20 May 2026 reopening days, the trading window was extended by one hour. — [Al Jazeera, 20 May 2026](https://www.aljazeera.com/economy/2026/5/20/controlled-reopening-ends-irans-lengthy-stock-market-shutdown)
- The trading week is Saturday–Wednesday. Base-volume and other weekly calculations use Wednesday's close and apply from Saturday. — [Parsian Bourse](https://parsianbourse.com/articles/base-volume/)

**Fees and taxes**
- 1405 broker guide: total buy-side costs = 0.003712 (0.3712%) of trade value; total sell-side costs = 0.0088 (0.88%), which includes the 0.5% transfer (sell) tax. Round trip ≈ 1.25%. — [Rade Bourse, "کارمزد معاملات بورس در سال 1405"](https://www.rade.ir/16808-%DA%A9%D8%A7%D8%B1%D9%85%D8%B2%D8%AF-%D9%85%D8%B9%D8%A7%D9%85%D9%84%D8%A7%D8%AA-%D8%A8%D9%88%D8%B1%D8%B3/)
- The 0.5% tax is levied on the seller's gross value regardless of gain or loss. There is no separate capital gains tax for shareholders. Fees are made up of broker commission, SEO supervision fee, exchange, CSD (سمات) and technology-company fees, plus the tax. Fund-unit trades are reported as exempt from the transfer tax. — (snippet) [Tejarat News](https://tejaratnews.com/نرخ-مالیات-بر-نقل-و-انتقال-سهام); [Emofid trading fees](https://learning.emofid.com/trading-fees/)
- Market makers have a separate fee ceiling (SEO board, Aban 1402 / Nov 2023). — (snippet) [Nabz-e Bourse 66405](https://nabzebourse.com/fa/news/66405/)

**Halts, reopenings and corporate events**
- A symbol reopens **without a price limit** (and with base volume 1) on its first day after: (a) a material "type A" disclosure (افشای اطلاعات با اهمیت الف) halt, or (b) an AGM (مجمع). At the 19 May 2026 market reopening, 178 symbols reopened without a limit for material disclosures and 71 for AGMs, 249 in total. — [Jamaran](https://www.jamaran.news/%D8%A8%D8%AE%D8%B4-%D8%A7%D9%82%D8%AA%D8%B5%D8%A7%D8%AF-13/1709640)
- Market-wide closures: about 9 trading days in June 2025 and 80 days from 28 Feb to 19 May 2026. During the 2026 closure, fixed-income, gold, real-estate funds and deposit certificates kept trading. — [Iran International](https://www.iranintl.com/en/202506283565); [Hamshahri](https://www.hamshahrionline.ir/news/1037271/)
- 42 symbols (~36% of market cap) stayed suspended at the May 2026 reopening. They included Fajr and Mobin petrochemicals, Khuzestan and Mobarakeh steel, utilities and some holding companies. Equity funds with more than 35% of their portfolio in affected companies were also suspended "until further notice". — [Al Jazeera](https://www.aljazeera.com/economy/2026/5/20/controlled-reopening-ends-irans-lengthy-stock-market-shutdown)
- Administrative selling curbs at the reopening: reported limits on large-scale sales by institutions, caps on major shareholders in some symbols, and selling ceilings on some leveraged funds (e.g., 100,000 units). — [Iran International, 19 May 2026](https://www.iranintl.com/en/202605198001)
- Brokers were temporarily barred from making margin calls or force-selling leveraged (credit) accounts. Options that expired during the closure left holders "without clear recourse". — [Al Jazeera](https://www.aljazeera.com/economy/2026/5/20/controlled-reopening-ends-irans-lengthy-stock-market-shutdown)

**IPOs (عرضه اولیه)**
- Each IPO's terms are set in the market supervisor's message on TSETMC: order-entry window, per-code quota (سهمیه هر کد) and price band. Cash must be ≥ quota × upper band price, and orders can be edited during the window. Example: the 1403 "بپویا" IPO had a per-code quota of 500 shares. — (snippet) [Khane Sarmaye how-to-buy-ipo](https://khanesarmaye.com/how-to-buy-ipo); [Nabz-e Bourse 89335](https://nabzebourse.com/fa/news/89335/)
- IPOs continued in 1405: the "فکور" IPO was described as "the 25th new share" (of the year), and "سیمکات" was in roadshow. — [Boursepress sidebar headlines, Oct 2026](https://boursepress.ir/news/158294)

**Queues (صف خرید/فروش)**
- With ±3% limits, one-sided demand stacks up at the limit price as queues, and price adjustment to news is spread over several sessions. — [Charisma Learning](https://learning.charisma.ir/beginning-investing/%D8%AF%D8%A7%D9%85%D9%86%D9%87-%D9%86%D9%88%D8%B3%D8%A7%D9%86-%DA%86%DB%8C%D8%B3%D8%AA/)
- Scale examples: on 28 Jun 2025 the reopening sell queue reached 350 trillion rials. On 19 May 2026 (first minutes) the sell queue was 8,371 bn tomans vs a buy queue of 2,856 bn tomans; one hour in it was 6,477 vs 4,417. — [Iran International](https://www.iranintl.com/en/202506283565); [Jamaran](https://www.jamaran.news/%D8%A8%D8%AE%D8%B4-%D8%A7%D9%82%D8%AA%D8%B5%D8%A7%D8%AF-13/1709640)

**Market Stabilization Fund (صندوق تثبیت بازار سرمایه)**
- The fund was created in 1396 (2017) under Art. 28 of the Law on Removing Production Barriers. It is funded by SEO and National Development Fund transfers, with roughly 23 hemat (230 trillion rials) of cumulative resources per an official. It reported about 6.5 hemat of support in 1403 (2024–25). Another report cites 17.6 hemat of "support" and 6.4 hemat of net buying. An undated SEO decision put 500 bn tomans into the fund for stock purchases. — (snippet) [Taadol 313764](https://www.taadolnewspaper.ir/fa/news/313764/); [Taadol 313325](https://www.taadolnewspaper.ir/fa/news/313325/)
- For the June 2025 reopening, SEO managers had proposed "tens of thousands of billions of tomans" of support, but reportedly only part was executed. — (snippet) [Taadol 349811](https://www.taadolnewspaper.ir/fa/news/349811/)

**Algo/order-splitting restrictions relevant to execution modeling**
- Since an SEO notice of 1 Mehr 1399 (22 Sep 2020), splitting sell orders by any method is prohibited for online clients (individuals and legal entities) in all symbols. Broker OMS must block it. — [Boursepress](https://boursepress.ir/news/158294)

**Fund types in active use (2025–26)**
- Fixed-income funds (صندوق درآمد ثابت) act as the cash parking lot: on the 19 May 2026 reopening about 2.8 hemat left fixed-income funds and roughly 1.3 hemat went into stocks. Other types in active use: gold funds, real-estate funds, equity and mixed funds, and leveraged funds (اهرمی). Gold funds had about 8.3 thousand bn tomans of trading value in 1404, more than stocks and rights (~7.4), a sign that liquidity moved toward gold. — [Jamaran](https://www.jamaran.news/%D8%A8%D8%AE%D8%B4-%D8%A7%D9%82%D8%AA%D8%B5%D8%A7%D8%AF-13/1709640); (snippet) [Iranbroker](https://iranbroker.net/?p=298592)

### Inferences
- Backtest engine requirements:
  - Model the asymmetric history of limits: ±5% normal, ±3% from at least before June 2025 through Oct 2026, ±3/2/1% on the IFB base market, and no limit on reopening days.
  - Fills at limit prices are rationed by queue, so assume fill probability < 1 when the limit is hit. With ±3%, a stock can sit in a queue for many days.
  - The closing-price formula changed on 22 Dec 2025: before that date it was base-volume-weighted; after it, it equals VWAP. "Close" series from before and after that date are not strictly comparable. TSETMC's "final price" (قیمت پایانی) and "last price" (آخرین معامله) differ.
  - Use T+2 cash availability for rotation strategies, and ~1.25% round-trip cost.
  - Handle market-wide closures (Jun 2025: ~9 days; Feb–May 2026: 80 days) and long single-symbol suspensions (42 large caps in May 2026) as non-tradable periods, not as zero returns.
- Rule settings are a policy variable that changes in crises: limits narrow, sell-side curbs appear, margin calls are suspended, hours change. An app should load these rules from a dated configuration table rather than hard-coding them.

### Gaps
- I found no official SEO or TSE circular giving the exact date the limit went from ±5% to ±3%. It was in place before June 2025 and may date from the Oct 2024 or Raisi-era crisis cuts.
- I did not verify per-order min/max size rules (e.g., the maximum order volume per symbol, or the minimum order value of ~5m rials) for 1405.
- I did not verify the current fixed-income and leveraged fund settlement cycles, or the details of market-maker fund obligations.
- I found no data on Stabilization Fund buying in 1405 (2026).
- I found no 1405-specific official IPO rule changes (e.g., a book-building vs fixed-price ratio).
- The 1405 fee figures come from a single broker-education page, not the SEO fee resolution.

## Q2. Regulation: market-data redistribution, algorithmic/API trading, investment advice and signal provision (personal vs public app)

### Takeaway
Under the Securities Market Act (1 Azar 1384 / 22 Nov 2005), giving investment advice, signals or portfolio management to others without an SEO licence is a crime (Art. 49(1)). The SEO explicitly extended this to apps and online platforms in an April 2025 warning. A public app that recommends buys or sells therefore needs at least a "type-2 investment adviser" licence or a partnership with a licensed entity. A strictly personal tool does not.

Retail algorithmic trading is allowed only through brokers whose algo platforms have been approved by the SEO, TSE, IFB, TSETMC and the capital-market security centre. There is no direct exchange API for individuals, and sell-side order splitting by online clients is banned.

Data: TSETMC (operated by the SEO-supervised Tehran Securities Exchange Technology Management Co.) is free to view. Commercial data businesses fall under the "financial information processing company" licence. I found no published rule specifically governing redistribution of TSETMC data.

### Cited Findings
**Legal basis and enforcement against unlicensed advice/signals**
- SEO letter (reported 10 Ordibehesht 1404 / 30 Apr 2025) on online sales platforms and app stores. Under the Securities Market Act of 1 Azar 1384, providing financial services such as investment advice and portfolio management requires an SEO licence. Under **Art. 49(1)**, doing so without a licence is criminalised. Under **Art. 52**, the SEO acts as plaintiff. The letter also cited Art. 749 of the Islamic Penal Code (the computer-crimes/filtering provision). — [YJC](https://www.yjc.ir/fa/news/8944654/)
- The SEO has called signal-selling a crime. In earlier years it filed criminal complaints against 18 Telegram channels, some of which traded from investors' accounts using their usernames and passwords. It reported 97 judicial cases against online violators and 170 referrals to the economic security police, and worked with the cyber police (FATA) to identify channel operators' IPs. These figures are from older (c. 2017–2020) reporting. — (snippet) [Asriran](https://www.asriran.com/fa/news/752103/); [Tahlil Bazaar](https://www.tahlilbazaar.com/news/52789/); [Boursepress 121154](https://boursepress.ir/news/121154/)

**Licensed route for online/social-media advice: "type-2 investment adviser" (مشاور سرمایه‌گذاری نوع دوم)**
- In Mehr 1400 (Oct 2021) the SEO issued a guideline for capital-market activity in cyberspace through type-2 investment advisers. Terms: minimum capital of 50m tomans; a two-step licensing process (principle approval within about 2–3 months, then establishment, which also serves as the activity licence); no physical-office or staff-count requirements; a single qualified person can hold the licence, with experience substituting for professional certificates; the number of licences is unlimited. The SEO says it will monitor registered channels for "abnormal signalling". — [IRIB News, 10 Mehr 1400](https://www.irib-news.ir/fa/news/3236944/)
- In 1404 the SEO's financial-institutions supervision department said its plan was fewer ex-ante controls and more ex-post supervision. — (snippet) [Bourse24 315230](https://www.bourse24.ir/news/315230)

**Algorithmic trading**
- 1 Mehr 1399 (22 Sep 2020): the SEO (Mohsen Khodabakhsh, head of exchange supervision) disabled algorithmic trading for all clients in all TSE and IFB symbols, except licensed market makers using a designated algo ID. It also banned sell-side order splitting by online clients. — [Boursepress](https://boursepress.ir/news/158294)
- Later (c. 1399–1400; exact date not shown), algo services by licensed institutions were re-permitted subject to 8 conditions:
  1. comply with the algo-trading requirements annexed to SEO board minutes 619 (7 Bahman 1398);
  2. provide supervisory access to the system to the SEO, TSE and IFB;
  3. use no algorithms that violate market rules or the broker disciplinary code;
  4. obtain TSE approval of business/operational capabilities;
  5. obtain the same approval from IFB;
  6. obtain approval of technical requirements from Tehran Exchange Technology Management Co.;
  7. obtain approval of security requirements from the Capital Market Information Security Supervision Centre;
  8. submit logs and periodic reports to that centre.

  — [Mehr News 5192979](https://www.mehrnews.com/news/5192979/)
- SEO head Dehghan Dehnavi ordered that all investors, individual and institutional, be given access to algo trading. Brokers lacking the required supervisory tools were to stop offering algo trading. — [Mehr News 5320219](https://www.mehrnews.com/news/5320219/)
- Sena (via Tahlil Bazaar) reported that at least 4 companies held SEO licences to provide algo-trading services to financial institutions and brokers. It listed 19 brokers licensed to offer algo trading to their clients: Ayandeh Bank, Khebregan Sahm, Bimeh Iran, Amin Sahm, Atieh, Sahm Ashna, Shahr, Modabber Asia, Karafarin Bank, Andisheh va Binesh Pishro, Ansar, Statis Pouya, Ayandehnegar Kharazmi, Kian, Hafez, Behgozin, Sina and Armoon Bourse. Another 5 were under audit. Publication date not shown (c. 2021). — [Tahlil Bazaar 183412](https://www.tahlilbazaar.com/news/183412/)
- Example broker offering: Kian broker provides algo trading through the "Dahi" system by Mabna. — [Kian](https://kian.trade/services/algorithmic-trading)
- Legal hook: the online-trading executive guideline makes services like algo trading conditional on requirements notified by the SEO. — [Novinlaw text](https://novinlaw.ir/rules/legals/%D9%82%D9%88%D8%A7%D9%86%DB%8C%D9%86-%D9%88-%D9%85%D9%82%D8%B1%D8%B1%D8%A7%D8%AA/show/679/)

**Market data**
- TSETMC is run by Tehran Securities Exchange Technology Management Co. under direct SEO supervision. It is the public display of market data and is free, with no registration. — (snippet) [TSETMC about](https://cdn.tsetmc.com/Site.aspx?ParTree=1111); [Khane Sarmaye](https://khanesarmaye.com/tsetmc/)
- The "financial information processing company" (شرکت پردازش اطلاعات مالی) is a licensed financial institution under Art. 1(21) and Art. 7(6) of the Securities Market Act, with an SEO establishment guideline. Its minimum capital was reported as 2.5 bn tomans and was raised in 1398. — (snippet) [Sena](https://www.sena.ir/news/4140/); [Donya-e-Eqtesad](https://donya-e-eqtesad.com/%D8%A8%D8%AE%D8%B4-%D8%A8%D9%88%D8%B1%D8%B3-6/626734)

**Access from abroad**
- TSETMC has restricted foreign IPs at times. The TSETMC CEO admitted a 3-day restriction during a security upgrade. Market participants said foreign-IP restrictions on TSETMC and Codal continued after the June 2025 war, and SEO head Seydi promised to fix this. The technology company says foreigners should use tsetmc.com (tsetmc.ir is for domestic users) and that restrictions apply only "in special conditions" after trading hours. — (snippet) [Sena 74069](https://www.sena.ir/news/74069/); [Boursepress 225995](https://boursepress.ir/news/225995/); [Bourse24 238082](https://www.bourse24.ir/news/238082)
- My own reachability test (17 Oct 2026, 09:19 UTC, from this research machine; location not verified but presumably outside Iran): www.tsetmc.com returned HTTP 200; cdnapi.tsetmc.com (the JSON API used by most scrapers) failed to connect; codal.ir and seo.ir timed out. This is a single test, not a definitive finding.

### Inferences
- Personal-use app (the owner analyses data and trades manually): no SEO licence appears necessary. Scraping public TSETMC pages for personal use is the norm, though API endpoints may be geo-restricted from abroad.
- Public app that issues buy/sell signals or rankings: under Art. 49(1) and the April 2025 SEO letter, this likely requires a type-2 investment adviser licence, or operating under a licensed adviser or broker. App stores inside Iran (Cafe Bazaar, Myket) were explicitly targeted by the SEO letter.
- Commercial data redistribution would plausibly fall under the financial information processing licence. This is an inference: I found no explicit rule text on reselling TSETMC data.
- Automated order placement by a personal script through a broker's retail web OMS is not covered by any approval I found. It would likely breach broker terms and the algo-trading rules. The compliant path is a broker-provided, SEO-approved algo platform.

### Gaps
- I did not find the current (1404–1405) text of the algo-trading requirements, current broker lists, or any retail API product terms.
- I found no SEO rule specifically on "information vendor / data services licence" (مجوز ارائه خدمات داده) or TSETMC terms of use regarding redistribution.
- I found no 2025–2026 enforcement statistics on Telegram or Bale signal channels.
- I did not establish whether non-Iranian or diaspora residents can legally hold or operate a sejam/brokerage account from abroad under current rules.

## Q3. Recent events 2024–Oct 2026: wars, closures, sanctions, rial/inflation, internet, index level, policy

### Takeaway
2025–26 brought two wars. The June 2025 Israel–Iran "12-day war" closed the market for about 9 days. The US–Israel war that began 28 Feb 2026 ("40-day war", also called the "Ramadan war" in Iranian media) closed the market for a record 80 days, until 19 May 2026. Other shocks: the UN snapback (late Sept 2025), the late-Dec 2025 protests and January 2026 internet blackout, an 88-day wartime internet blackout (Feb–May 2026), a US naval blockade from July 2026, the rial falling to about 2.7m per USD (Oct 2026) and inflation of roughly 74–90%. Even so, TEDPIX roughly doubled in nominal terms in 2026, to a record ~8.5m on 13 Oct 2026. Measured in dollars, though, it is roughly flat.

### Cited Findings
**June 2025 (Khordad–Tir 1404) war**
- The market reopened Saturday 28 Jun 2025 after a 9-day closure. Over 99% of stocks fell, and sell orders reached 350 trillion rials (~$417m). TEDPIX fell 62,503 points (−2.1%) to 2,922,101, and the equal-weight index fell to 908,163. Individuals withdrew about 12 trillion rials in the first 90 minutes. — [Iran International, 28 Jun 2025](https://www.iranintl.com/en/202506283565)
- The index fell about 200,000 points after reopening, to about 2,788,000 a few sessions later. — (snippet) [IranWire](https://iranwire.com/en/economy/142839-irans-stock-market-continues-to-plunge-after-war/)
- A then-record single-day outflow of individual ("real") money occurred on 11 Tir 1404 (2 Jul 2025). — (snippet) [Iranbroker](https://iranbroker.net/?p=298592)
- The internet was cut during the June 2025 war. — [Wikipedia: 2026 Internet blackout in Iran](https://en.wikipedia.org/wiki/2026_Internet_blackout_in_Iran)

**Snapback (Sept 2025)**
- The rial was at about 930,000/USD in August 2025, 1,030,000 in late August and over 1,074,000 on 24 Sep. It reached about 1,180,000 a week after the UN reimposed sanctions. President Pezeshkian said they were "not the end of the world". — (snippet) [Iran International 202508286118](https://www.iranintl.com/en/202508286118); [Iran International 202510025475](https://www.iranintl.com/en/202510025475)
- I found no reporting on TEDPIX's specific reaction to the snapback (see Gaps).

**Dec 2025 – Feb 2026**
- Base volume was abolished from 22 Dec 2025 (see Q1).
- Nationwide protests began in late December 2025. TEDPIX hit an all-time high of "nearly 4,500,000" at the start of 2026, then declined. — [Al Jazeera](https://www.aljazeera.com/economy/2026/5/20/controlled-reopening-ends-irans-lengthy-stock-market-shutdown)
- An internet blackout began 8 Jan 2026 and was relaxed on 28 Jan, leaving a whitelist system in place. During the blackout TEDPIX lost 450,000 points over 4 days. The ICT minister put the cost at $35.7m per day. — [Wikipedia: 2026 Internet blackout](https://en.wikipedia.org/wiki/2026_Internet_blackout_in_Iran); [Filterwatch](https://filter.watch/english/2026/01/28/network-monitoring-january-2026-from-regional-disuptions-to-total-blackout-and-whitelisted-access/)
- The government was building a tiered "Barracks Internet": global access by security whitelist, "white SIM cards" for insiders and access for vetted groups. The government spokesperson said access "will never return to its previous form". — [Rest of World](https://restofworld.org/2026/iran-blackout-tiered-internet/)
- TEDPIX at end-2025 (Jalali-year or calendar baseline, per source): 4,070,065. — [GuruFocus](https://www.gurufocus.com/news/9103205/irans-tedpix-index-soars-908-amid-economic-turbulence)

**Feb–May 2026 war and the 80-day closure**
- US–Israeli strikes began 28 Feb 2026. Equity trading halted from 9 Esfand 1404 (28 Feb 2026), while fixed-income, gold, real-estate funds and deposit certificates kept trading. — [Hamshahri](https://www.hamshahrionline.ir/news/1037271/); [Fararu](https://fararu.com/fa/news/972317/)
- Internet connectivity fell to about 4% of normal on 28 Feb and about 1% through March. Access was largely restored after 88 days (~26 May 2026), reaching 86% of normal traffic per NetBlocks, but social media stayed blocked. A paid "Internet Pro" tier with government approval was introduced. — [Wikipedia](https://en.wikipedia.org/wiki/2026_Internet_blackout_in_Iran); (snippet) [Iran International 202605251140](https://www.iranintl.com/en/202605251140); [CNN](https://www.cnn.com/2026/05/10/middleeast/iran-internet-pro-blackout-access-vpn-intl)
- The market reopened Tuesday 19 May 2026 after 80 days, under a ±3% limit, with 42 symbols (~36% of market cap) suspended and administrative sell curbs (see Q1). TEDPIX opened about −0.32% at ~3,707,000. After two sessions it was above 3,758,000, buy queues outpaced sell queues, and the equal-weight index rose slightly. — [Jamaran](https://www.jamaran.news/%D8%A8%D8%AE%D8%B4-%D8%A7%D9%82%D8%AA%D8%B5%D8%A7%D8%AF-13/1709640); [Al Jazeera](https://www.aljazeera.com/economy/2026/5/20/controlled-reopening-ends-irans-lengthy-stock-market-shutdown); [Iran International](https://www.iranintl.com/en/202605198001)
- Companies were prevented, on security grounds, from fully disclosing war damage. No revised earnings guidance was published at the reopening. — [Al Jazeera](https://www.aljazeera.com/economy/2026/5/20/controlled-reopening-ends-irans-lengthy-stock-market-shutdown); [Iran International](https://www.iranintl.com/en/202605198001)

**Macro, Jun–Oct 2026**
- A US naval blockade of Iranian ports was reimposed on 14 Jul 2026. On 19 Aug the US president announced "economic warfare". — [Wikipedia: Iranian economic crisis](https://en.wikipedia.org/wiki/Iranian_economic_crisis). This is Wikipedia, so treat with care. JPost/Iran International separately confirm that oil exports "effectively stopped under the US blockade". — [JPost, 14 Oct 2026](https://www.jpost.com/middle-east/iran-news/article-911670)
- The rial was at about 2.5m/USD in late Sept 2026 and about 2.7m on 6 Oct 2026 (free market). The CBI offered up to $2bn in cash dollars below the free rate from 30 Sep, with a $10,000 cap per person. CBI governor: Abdolnaser Hemmati. — [Iran International, 7 Oct 2026](https://www.iranintl.com/en/202610076990)
- Wikipedia's figure of a "record low 2,020,000 on 24 Aug 2026" predates the October lows. Trading Economics' ~1.8m figure likely reflects the official or managed rate. — [Wikipedia](https://en.wikipedia.org/wiki/Iranian_economic_crisis); (snippet) [Trading Economics](https://tradingeconomics.com/iran/currency)
- Inflation estimates:
  - more than 70% in late April 2026 (official) — [Al Jazeera](https://www.aljazeera.com/economy/2026/5/20/controlled-reopening-ends-irans-lengthy-stock-market-shutdown)
  - 73.6% — (snippet) [Sunday Guardian](https://sundayguardianlive.com/world/us-israel-iran-war-latest-news-why-is-irans-stock-market-rising-despite-rial-collapse-us-oil-blockade-and-736-inflation-imf-predicts-76-economic-contraction-in-2026-check-details-305612/)
  - "year-on-year inflation approaching 90%" per official data in mid-Oct 2026 — [Iran International, 14 Oct 2026](https://www.iranintl.com/en/202610147703)

  The difference likely reflects annual-average vs point-to-point measures.
- The IMF forecasts Iran's GDP will shrink by 7.6% in 2026 (vs −5.4% projected in July). — [JPost](https://www.jpost.com/middle-east/iran-news/article-911670)
- Policy rate: about 23% per aggregators. The CBI does not use a single benchmark and sets deposit and lending profit rates instead. Wikipedia says the CBI "removed interest rate caps" later in 2026; unverified. — (snippet) [Trading Economics/Eulerpool](https://eulerpool.com/macro/iran/interest-rate); [Wikipedia](https://en.wikipedia.org/wiki/Iranian_economic_crisis)
- Bank Noor and Ayandeh Bank reportedly collapsed in August 2026 and were merged into Bank Melli. This is single-source (Wikipedia) and unverified. — [Wikipedia](https://en.wikipedia.org/wiki/Iranian_economic_crisis)

**Market level, Sept–Oct 2026**
- 30 Sep 2026 (8 Mehr 1405): TEDPIX +170,725 (+2.25%) to 7,765,682, a record, up about 90.8% vs end-2025. — [IMNA](https://www.imna.ir/news/1008517/); [GuruFocus](https://www.gurufocus.com/news/9103205/irans-tedpix-index-soars-908-amid-economic-turbulence)
- 10 Oct 2026 (18 Mehr): +134k points with about 2.2 hemat of net individual inflow. — [Vananews](https://vananews.com/fa/news/668066/)
- 11 Oct 2026 (19 Mehr): −22,597 (−0.27%) to about 8,242,000. — [Sharq](https://www.sharghdaily.com/%D8%A8%D8%AE%D8%B4-%D8%A7%D9%82%D8%AA%D8%B5%D8%A7%D8%AF%DB%8C-12/1134089)
- 13 Oct 2026 (21 Mehr): TEDPIX +171,564 to **8,505,588**; equal-weight index +41,402 to 2,222,860; IFB index +1,134 to 66,447. — [Asriran/Khabaronline](https://www.asriran.com/fa/news/1198533/)
- Dollar terms: between 8 Jul and 2 Sep 2026 the index rose 23.02% in rials but −0.43% in USD. — (snippet) [tehranindex.com](https://tehranindex.com/insights/iran-stock-returns-in-dollars). Year to September: about +91% in rials vs a ~46% fall in the rial, so about +1.2% in USD. — (snippet) [Vocal Media, low-quality source](https://vocal.media/trader/irans-currency-is-dying-its-stock-market-just-hit-a-record)
- Explanations for the rally: a hedge against inflation, since property and deposits are losing value, plus some hope of a US–Iran deal. Risk of a "third round of war". — [JPost citing Iran International](https://www.jpost.com/middle-east/iran-news/article-911670)
- Mid-Oct 2026 Persian market previews cite "new Trump threats" as a downside risk. — [Vananews, 25 Mehr 1405](https://vananews.com/fa/news/668487/)

### Inferences
- Any 2025–26 backtest window contains regime breaks. These include the closures, the switch from base-volume closing to VWAP closing, the ±3% limit era and selective suspensions of the largest caps. On reopening days, administrative curbs make prices non-market for a period.
- Nominal returns are dominated by currency debasement. An app should show USD-adjusted (free-market rate) and CPI-adjusted returns alongside rial returns.
- Data access risk is material. National internet shutdowns, whitelisting and foreign-IP blocks can cut an app's data feed or the user's broker access for weeks. Long blackouts happened in Jan 2026 and Feb–May 2026. The app needs offline tolerance and local caching, and in-country hosting may be needed to reach TSETMC and Codal APIs.

### Gaps
- I found no reliable source on TEDPIX's move specifically around the 28 Sep 2025 snapback.
- I found no exact end date or ceasefire date for the 2026 war in market sources. Hamshahri calls it the "40-day war", which suggests hostilities ended around early-to-mid April 2026 while the market stayed closed until 19 May.
- I could not confirm the internet-access status for Sept–Oct 2026; the latest found was partial restoration in late May 2026, with social media blocked.
- I did not verify official CBI policy-rate decisions in 2026 or the Statistical Centre's exact Mehr 1405 inflation print.
- Several figures (bank collapses, blockade dates) rest on Wikipedia only.

## Q4. Market characteristics relevant to analysis (herding/queues, real-vs-legal money flow, inflation/FX, sector concentration, state influence, dividend season, mandated prices)

### Takeaway
TEDPIX is dominated by commodity exporters (petrochemicals, base metals and steel, refineries), whose rial earnings scale with the free-market dollar. That is why the index tracks FX and why war damage to those firms moved the whole index. Daily market commentary centres on queues and on "real money" (individual) net inflows and outflows, including flows between fixed-income, gold and equity funds. Pricing policies (refinery feedstock formulas, car price lists) and administrative interventions directly change fundamentals and price discovery.

### Cited Findings
- Steel and petrochemical companies are "traditionally among the most influential drivers of the TEDPIX index". At the 2026 reopening, banks were capital-constrained and automakers were loss-making before the war. — [Iran International, 19 May 2026](https://www.iranintl.com/en/202605198001)
- Rial depreciation "made export-oriented companies appear more attractive as revenues often translate into higher domestic currency earnings". — [Al Jazeera](https://www.aljazeera.com/economy/2026/5/20/controlled-reopening-ends-irans-lengthy-stock-market-shutdown)
- Daily market reporting tracks:
  - individual (حقیقی) net money flow, e.g., "ورود ۲.۲ همت پول حقیقی" on 18 Mehr 1405 — [Vananews](https://vananews.com/fa/news/668066/)
  - buy and sell queue values, the top-5 order-book value and switching between fixed-income and equity funds — [Jamaran](https://www.jamaran.news/%D8%A8%D8%AE%D8%B4-%D8%A7%D9%82%D8%AA%D8%B5%D8%A7%D8%AF-13/1709640)
  - record single-day individual outflows (e.g., 9,404 bn tomans, the second-largest on record, with gold funds the largest outflow) — (snippet) [Iranbroker](https://iranbroker.net/?p=298592)
- Authorities explicitly manage the index as a political signal: "TEDPIX functions as a visible signal of economic stability". Reopening rules were designed "to manage the behavior of the index". — [Iran International](https://www.iranintl.com/en/202605198001)
- Persian commentary also describes "حمایت‌های دستوری" (ordered or administrative support) leading to loss of trust. — [Boursepress headline](https://boursepress.ir/news/158294)
- Mandated pricing and feedstock: a "new refinery feedstock formula was notified", with estimated effects on earnings. In Oct 2026, refinery feedstock rates were to be "calculated as before until the end of emergency conditions". Reopening six refineries was conditional on disclosure requirements. The Shetran refinery issued a clarification on the feedstock rate. These are headlines only. — [Boursepress sidebar](https://boursepress.ir/news/158294); [YJC sidebar, 25 Mehr 1405](https://www.yjc.ir/fa/news/8944654/); [Nabz-e Bourse sidebar](https://nabzebourse.com/fa/news/120131/)
- Car prices are set by administered lists, e.g., Iran Khodro's Mehr 1405 price list showing "sharp increases". This is a headline only. — [Digiato sidebar](https://digiato.com/business/base-volume-removed-major-market-change-two-decades)
- Dividend/AGM season: AGM approvals cluster in Khordad (May–June), e.g., the "مصوبات مجامع ۱۳ خرداد ۱۴۰۵" reports with capital increases and DPS. Symbols reopen without a price limit after an AGM; 71 such symbols were in the 19 May 2026 reopening. Large holdings have announced AGM and dividend dates, e.g., Shasta for FY1405. — [Nabz-e Bourse](https://nabzebourse.com/fa/news/130871/); [Jamaran](https://www.jamaran.news/%D8%A8%D8%AE%D8%B4-%D8%A7%D9%82%D8%AA%D8%B5%D8%A7%D8%AF-13/1709640); [Boursepress headline](https://boursepress.ir/news/158294)
- Capital increases from asset revaluation (e.g., 236%, 592% for "خبهمن") are frequent corporate actions in an inflationary setting. These are headlines only. — [Boursepress sidebar](https://boursepress.ir/news/158294)
- Large state-linked holdings, such as Shasta (the Social Security Investment Co.), are among the largest symbols; Shasta was in a sell queue at the May 2026 reopening. — [Jamaran](https://www.jamaran.news/%D8%A8%D8%AE%D8%B4-%D8%A7%D9%82%D8%AA%D8%B5%D8%A7%D8%AF-13/1709640)

### Inferences
- Useful features for a decision-support app:
  - USD/free-market rate and its change, as an exporter earnings driver
  - individual vs institutional net flow per symbol and sector (from TSETMC client-type data)
  - queue state and queue value relative to market cap
  - days in queue
  - a flag for symbols reopening after an AGM or disclosure (no limit)
  - capital-increase-adjusted prices (revaluation capital increases are frequent)
  - dividend-adjusted prices around Khordad–Tir AGMs
  - flags for policy changes to feedstock and administered prices
- Herding and limit-induced momentum are structural in a ±3% regime with queues. Signals based on queues and flows may look strong in-sample but are hard to execute, because fills at the limit are rationed.

### Gaps
- I did not retrieve academic papers on TSE herding or price-limit "magnet" effects.
- I found no current figures on sector weights in TEDPIX or on the state/semi-state ownership share (e.g., "shebh-e dolati" holdings).
- I found no recent data on market-maker fund activity, or on any 2025–26 changes to cement or car pricing regimes beyond headlines.
