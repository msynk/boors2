# Iran-specific infrastructure, distribution and compliance environment for a hybrid .NET stock-market app (as of 17 Oct 2026 / 25 Mehr 1405)

Scope note: research done 17 Oct 2026. Persian dates are converted where sources gave them; "site date" banners (25 Mehr 1405) on Iranian news pages were ignored in favour of article timestamps. Everything that could not be sourced is listed under Gaps rather than guessed. Toman (T) = 10 rials.

---

## KQ1. Domestic cloud/VPS providers, managed services, registries/mirrors, .NET download blockages

### Takeaway
Iranian PaaS/IaaS is priced in Toman and is usable for a .NET + Postgres stack (Liara, Hamravesh/Darkube, ArvanCloud, ParsPack), with S3-compatible object storage widely offered; Hamravesh also sells a Germany region. Sanctions-driven breakage of Docker Hub/NuGet/npm is a recognised, recurring problem that ParsPack's free-to-use `mirror.abrha.net` (NuGet, Docker, npm, PyPI, Maven, Go, apt) now addresses; no 2026 source confirmed a hard Microsoft block on `dotnet` SDK downloads from Iranian IPs, so treat that as "intermittent/unverified" and keep a mirror or abroad-node fallback.

### Cited Findings

**Pricing (Toman, 2026)**
- Liara PaaS/DBaaS plans (same price table for apps and managed DBs): Earth 750,000 T/mo (0.5 GB RAM, 0.5 vCPU, 5 GB), Mars 1,300,000 T (1 GB/1 vCPU/10 GB), Jupiter 2,300,000 T (2 GB), Saturn 4,125,000 T (4 GB/2 vCPU), Uranus 7,250,000 T (8 GB/4 vCPU), Neptune 12,600,000 T (16 GB/8 vCPU), Pluto 22,000,000 T (32 GB/16 vCPU); PostgreSQL listed among engines; S3-compatible object storage 475,000 T/mo for 20 GB; "location of all plans is Iran, 10 Gbit shared bandwidth"; footer links a ".NET cloud hosting" page — [Liara pricing](https://liara.ir/pricing/)
- Hamravesh (Darkube PaaS) per-resource pricing: Iran DC vCore 595,000 T/mo (826 T/h), GB RAM 290,000 T/mo, GB SSD 16,500 T/mo; Germany DC vCore 990,000 T/mo, GB RAM 480,000 T/mo, GB SSD 32,000 T/mo; traffic internal download 3,200 T/GB, external 500 T/GB; dedicated external IP 4,800,000 T/yr, internal IP 2,880,000 T/yr; managed PostgreSQL, object storage, Kubernetes, container registry and GPU are listed but unpriced on the page — [Hamravesh pricing](https://hamravesh.com/pricing)
- ArvanCloud managed cloud database (DBaaS) has been available since early 1403 (spring 2024); Digiato (late 1403) claims DBaaS cuts cost ~40% vs self-managed — [Digiato](https://digiato.com/iran-technology-news/arvancloud-managed-cloud-database); ArvanCloud added geo-redundant cloud-server backups (Shahrivar 1404 / Aug–Sep 2025) — [Zoomit](https://www.zoomit.ir/tech-iran/447536-arvancloud-new-cloud-backup-feature-business-continuity/)
- Third-party aggregator (updated 2 Jul 2026) lists ArvanCloud Managed Kubernetes dev tier $0/mo and Managed Database (1 GB) from $8/mo — aggregator, not official — [comparetiers](https://comparetiers.com/compare/arvancloud-vs-upcloud)
- Zarinpal's pricing page carries a 1405 copyright, confirming Toman pricing pages are current for 1405 — [Zarinpal](https://www.zarinpal.com/pricing)

**Reliability during the 2026 blackouts**
- Digiato, 29 Dey 1404 (19 Jan 2026): per Cloudflare Radar, internet in most provinces still cut — [Digiato](https://digiato.com/iran-technology-news/internet-is-down-in-most-provinces)
- Digiato, 24 Farvardin 1405 (13 Apr 2026): ArvanCloud Radar showed connectivity to some foreign data centres restored, but users reported no noticeable change — [Digiato](https://digiato.com/iran-technology-news/connection-of-certain-foreign-data-centers)
- ArvanCloud CEO (Digiato, 9 Khordad 1405 / 30 May 2026): internet still not back to pre-Dey-1404 conditions — [Digiato](https://digiato.com/iran-technology-news/the-internet-is-not-normal-yet-2); he attributes the frequent severe disruptions since the start of the year to large-scale cyber-attacks the infrastructure cannot withstand (company claim, not independently verified) — [Zoomit](https://www.zoomit.ir/tech-iran/421063-internet-disruption-due-to-cyber-attacks)
- During the Jan 2026 whitelist phase the NIN (domestic network) itself was "largely inaccessible" on 8 Jan, and traffic ran at ~25% of pre-shutdown levels by late Jan (Kentik) — [Filterwatch](https://filter.watch/english/2026/01/28/network-monitoring-january-2026-from-regional-disuptions-to-total-blackout-and-whitelisted-access/)
- Wikipedia timeline: 8 Jan nationwide cut; 28 Jan relaxed to whitelist; 28 Feb strikes -> connectivity 4%; Mar–Apr ~1%; 26 May partial restoration, still slow/restricted; minister: cost $35.7M/day; 185 million drop in financial transactions in Jan 2026; TSE index lost 450,000 points over four days — [Wikipedia](https://en.wikipedia.org/wiki/2026_Internet_blackout_in_Iran)
- Precedent: in the 1398 (Nov 2019) shutdown ArvanCloud gave startups two weeks of free CDN — [Zoomit 2020](https://www.zoomit.ir/2020/2/18/345428/arvancloud-challenges-internet-shutdown/)

**Registries and mirrors**
- ParsPack launched developer mirrors (announced Digiato 7 Ordibehesht 1405 / 27 Apr 2026) for Docker, npm, PyPI, Composer, NuGet, Maven, Go, Ubuntu/Debian, explicitly "to neutralise IP-based sanctions" and keep CI/CD alive during full international outages; article does not state price — [Digiato](https://digiato.com/tech/parspack-developer-repositories-mirror)
- Exact ParsPack mirror endpoints (Zoomit guide, 6 Ordibehesht 1405 / 26 Apr 2026): NuGet `https://mirror.abrha.net/repository/nuget/index.json` (`dotnet nuget add source ... --name ParspackMirror`); npm `https://mirror.abrha.net/repository/npm/`; PyPI `https://mirror.abrha.net/repository/pypi/simple`; Go `https://mirror.abrha.net/repository/go/`; Maven `https://mirror.abrha.net/repository/maven/`; Composer `https://mirror.abrha.net/repository/composer/` — [Zoomit](https://www.zoomit.ir/computer-learning/459207-bypass-internet-outage-developer-mirrors/)
- ParsPack docs index lists separate pages for Docker Registry and Docker Engine mirrors, AlmaLinux/Ubuntu/Debian/CentOS/EPEL, NGINX, cPanel; states mirrors are served at "half-price traffic" (نیم‌بها), no other pricing — [ParsPack docs](https://docs.parspack.com/reference/mirror/)
- Iranian developer account: "officially blocked by Docker because of US laws"; workaround `registry-mirrors` in daemon config — [Substack](https://ahmadallish.substack.com/p/using-docker-registry-from-iran-65e1a4efffcc); an ArvanCloud Docker mirror exists but "mirrors are not complete replicas and may lag" — [LinkedIn post](https://ca.linkedin.com/in/devvindan)

**.NET SDK / Microsoft**
- Only a sanctions reseller's marketing text claims "Microsoft is not allowed to work with Iranian companies due to sanctions"; no technical/official source found for a 403 on .NET downloads from Iranian IPs — [search result summary](https://www.sqlservercentral.com/forums/user/microsoft1a) (low-quality; see Gaps)
- Filterwatch's Jan 2026 whitelist included GitHub (reachable), which is where `dotnet-install` scripts live — [Filterwatch](https://filter.watch/english/2026/01/28/network-monitoring-january-2026-from-regional-disuptions-to-total-blackout-and-whitelisted-access/)

### Inferences
- A production Iran node of roughly 4 GB RAM / 2 vCPU app + similar managed Postgres on Liara is ~8–9 million T/month list price; Hamravesh's per-resource model is cheaper for odd shapes and its Germany region lets one vendor cover both nodes (billing in Toman) — but a German region on an Iranian vendor is still an Iranian-sanctioned counterparty for upstream services.
- Nothing found says domestic data centres went dark; the evidence (NIN "largely inaccessible" on 8 Jan, traffic 1–4%, Arvan CEO statements) indicates the risk is international reachability and intermittent domestic degradation, not provider shutdown. Plan for the Iran node to run autonomously with no outbound dependency for days-to-weeks.
- For .NET builds inside Iran, pin NuGet to `mirror.abrha.net` plus nuget.org fallback and vendor the SDK installer (or build on the abroad node and ship images via a domestic registry mirror).

### Gaps
- No official ArvanCloud/ParsPack IaaS price table could be fetched (ArvanCloud pricing URL redirected to a placeholder); no Liara/Hamravesh uptime figures or blackout-period status pages found.
- No 2025–2026 primary report (GitHub issue, Microsoft statement) confirming whether `builds.dotnet.microsoft.com` / `dotnetcli.azureedge.net` return 403 to Iranian IPs; status of `registry.hamdocker.ir` and `docker.arvancloud.ir` in 2026 unverified.
- Whether the ParsPack mirrors are free for non-customers is not stated anywhere found.

---

## KQ2. Tiered/whitelisted internet, business access, outbound reach from domestic servers, and reverse reachability of Iranian data sources

### Takeaway
As of late Sept–mid Oct 2026, international connectivity is "up nationally" but heavily filtered, uneven (median 4.7 Mbps, 135 ms per Cloudflare Aug–Sep 2026) and politically fragile; Telegram and YouTube remain blocked, WhatsApp was re-restricted after its Dec 2024 unblock. Legal "whitelist" access exists only through the operator-run "Internet Pro / Pro SIM" and NASR dedicated-IP schemes for registered companies (mobile APN, ~40,000 T/GB international, 2–5 GB/day caps), with an official stance that "tiered internet" does not exist. No source confirms that Iranian data sites (tsetmc, codal, tgju, tse.ir) are reachable from Europe; the SEO head has acknowledged external access to TSETMC/Codal as a security-sensitive, unresolved issue.

### Cited Findings

**Status Sept–Oct 2026**
- 6 Mehr 1405 (28 Sep 2026): "international internet is up nationally"; a 3 Mehr partial disruption on Russia-transit routes resolved; a public dashboard showed 81% of 54 international routes healthy; Cloudflare 12 Aug–28 Sep 2026 median download 4.7 Mbps, median latency 135 ms; commentary warns latency/packet loss cause retries and errors for cloud services and external APIs even without outages — [Nabz-e Gheymat](https://nabzgheymat.ir/%D8%A2%D8%AE%D8%B1%DB%8C%D9%86-%D8%A7%D8%AE%D8%A8%D8%A7%D8%B1-%D8%A7%DB%8C%D9%86%D8%AA%D8%B1%D9%86%D8%AA-%D8%A7%DB%8C%D8%B1%D8%A7%D9%86-06-07-1405/)
- 25 Mehr 1405 (17 Oct 2026): SCC secretary Aghamiri declassified the 32-article resolution "Facilitating access to foreign platforms and improving lawful governance" (basis for Dey 1403 unblocking of Google Play and WhatsApp); WhatsApp access was later restricted again; Telegram and YouTube not unblocked — ICT ministry talks with both reached "promising stages" but a working group with veto blocked implementation; of 12 members of the body on restoring internet, only Aghamiri and IRIB head Jebeli reportedly opposed lifting filters — [Khabar Foori](https://www.khabarfoori.com/%D8%A8%D8%AE%D8%B4-%D8%A7%D9%82%D8%AA%D8%B5%D8%A7%D8%AF%DB%8C-145/3252936-%D9%85%D8%B5%D9%88%D8%A8%D9%87-%D9%85%D8%AD%D8%B1%D9%85%D8%A7%D9%86%D9%87-%D9%81%DB%8C%D9%84%D8%AA%D8%B1%DB%8C%D9%86%DA%AF-%D8%A7%D8%AF%D8%A7%D9%85%D9%87-%D8%AF%D8%A7%D8%B1-%D8%A7%D8%B3%D8%AA%D8%A7%D8%B1%D9%84%DB%8C%D9%86%DA%A9-%D9%85%D8%B1%D8%AF-%D9%BE%D8%B4%D8%AA-%D8%AF%D8%B1%D9%87%D8%A7%DB%8C-%D8%A8%D8%B3%D8%AA%D9%87-%D9%85%D8%AD%D8%AF%D9%88%D8%AF%DB%8C%D8%AA-%D8%A7%DB%8C%D9%86%D8%AA%D8%B1%D9%86%D8%AA-%DA%A9%DB%8C%D8%B3%D8%AA)
- Dey 1403 (Dec 2024) SCC decision unblocked WhatsApp and Google Play as "first step" of a 10-policy/30-step plan expected to take 14 months — [Zoomg](https://www.zoomg.ir/tech-news/402508-whatsapp-google-play-unbanned/); [Aftab News](https://aftabnews.ir/fa/news/958738/جزئیات-طرح-مرحله‌ای-رفع-فیلترینگ)
- By Aug 2026 Cloudflare Radar showed activity on major Iranian networks but "did not establish that access was fully open, stable or equal" — [abit.ee summary of NetBlocks/Cloudflare](https://abit.ee/en/wi-fi-routers-modems/iran-internet-shutdown-netblocks-internet-blackout-whitelist-censorship-iran-2026-en)
- NetBlocks: public cut off from global internet for ~70% of 2026 to date (May 2026); 88-day shutdown from 28 Feb — [The National](https://www.thenationalnews.com/future/technology/2026/05/08/iranians-reach-120-days-without-the-internet-as-blackout-continues/)

**Whitelist mechanics (Jan 2026 phase)**
- Default-block model; access varied by ISP. Whitelisted: Google (Search, Gmail, Meet, Maps), Bing, Outlook, Play Store, App Store, Apple/Samsung sites, ChatGPT, GitHub, PlayStation. Circumvention-only: Instagram, Telegram, YouTube, X. Pilot at Tehran Chamber of Commerce (15 Jan 2026): merchants registered device IPs and business cards; unfiltered access only in designated zones. Draft Cyberspace Regulatory System bill puts international gateway protocols under the SNSC — [Filterwatch](https://filter.watch/english/2026/01/28/network-monitoring-january-2026-from-regional-disuptions-to-total-blackout-and-whitelisted-access/)
- Internal planning name "Barracks Internet"; "strict security whitelist"; ~16,000 "white SIM" holders since 2013; Georgia Tech IIL measured ~3% connectivity persisting for officials/state services — [Rest of World, 23 Jan 2026](https://restofworld.org/2026/iran-blackout-tiered-internet/)
- NetBlocks: traffic patterns pointed to "whitelisting" of select platforms — [Iran International, 20 Jan 2026](https://www.iranintl.com/en/202601208428)
- Minister Hashemi: "Tiered internet or a 'whitelist' system has no validity"; adviser said Internet Pro "has now been misused" — [Wikipedia: Sattar Hashemi](https://en.wikipedia.org/wiki/Sattar_Hashemi); CNN, 10 May 2026, on two-tier anger (page returned HTTP 451 to this researcher) — [CNN](https://www.cnn.com/2026/05/10/middleeast/iran-internet-pro-blackout-access-vpn-intl)

**Internet Pro / Pro SIM / NASR business access**
- NASR (computer guild) accepting member-company requests for international access via dedicated IP or "Pro SIM" (article 16 Farvardin 1405 / 5 Apr 2026): email request + Excel form (company national ID, CEO details, each named user), identity/eligibility check, payment link, SMS activation; only named users for work needs; companies commit to use only through IPs under their official control; price not stated — [Zoomit](https://www.zoomit.ir/tech-iran/458457-nasr-internet-pro/)
- Internet Pro details (5 Ordibehesht 1405 / 25 Apr 2026, via Fararu): private APN on mobile networks; initial packages ~2,000,000 T for 40–50 GB; domestic ~8,000 T/GB; international up to ~40,000 T/GB; Heda ID-verification 19,500 T; eligible: chamber/guild members, registered companies, startups/knowledge-based firms, logistics firms, faculty/researchers, bar members; manual route needs letterhead letter, introduction letter, user list; review up to 10 working days; daily cap 2–5 GB (Rightel ~2 GB), then cut until next morning; Telegram/WhatsApp generally work, Instagram/YouTube/X inconsistent; "programming tools and specific servers may work" — explicitly "not a general solution for filtered sites"; no public sign-up — [Eghtesad Online](https://www.eghtesadonline.com/fa/news/2133662/%D8%B5%D9%81%D8%B1-%D8%AA%D8%A7-%D8%B5%D8%AF-%D8%B3%DB%8C%D9%85%E2%80%8C%DA%A9%D8%A7%D8%B1%D8%AA-%D9%BE%D8%B1%D9%88-%D8%A7%DB%8C%D9%86%D8%AA%D8%B1%D9%86%D8%AA-%D9%BE%D8%B1%D9%88-%D8%A8%D8%B1%D8%A7%DB%8C-%DA%86%D9%87-%DA%A9%D8%B3%D8%A7%D9%86%DB%8C-%D8%A7%D8%B3%D8%AA-%D9%82%DB%8C%D9%85%D8%AA-%D9%88-%D9%86%D8%AD%D9%88%D9%87-%D8%AB%D8%A8%D8%AA%E2%80%8C%D9%86%D8%A7%D9%85)
- MCI manual registration email reported as Internetpro@mci.ir; general public and unregistered freelancers excluded unless introduced by a registered business — [Hamyab24 guide (1405)](https://hamyab24.ir/blog/internet-peru/); [Way2Pay guide](https://way2pay.ir/%D8%B1%D8%A7%D9%87%D9%86%D9%85%D8%A7%DB%8C-%D8%AF%D8%B1%DB%8C%D8%A7%D9%81%D8%AA-%D8%A7%DB%8C%D9%86%D8%AA%D8%B1%D9%86%D8%AA-%D9%BE%D8%B1%D9%88-%D9%87%D9%85%D8%B1%D8%A7%D9%87-%D8%A7%D9%88/)
- Black-market resale of tiered access reported — [Fararu](https://fararu.com/fa/news/973195/%D8%A7%DB%8C%D9%86%D8%AA%D8%B1%D9%86%D8%AA-%D8%B7%D8%A8%D9%82%D8%A7%D8%AA%DB%8C-%DA%86%DA%AF%D9%88%D9%86%D9%87-%D8%A8%D9%87-%D8%A8%D8%A7%D8%B2%D8%A7%D8%B1-%D8%B3%DB%8C%D8%A7%D9%87-%D8%B1%D8%B3%DB%8C%D8%AF)
- Earlier (25 Tir 1404 / 16 Jul 2025): National Cyberspace Center deputy Amir Sayyah said the "Digital Economy Facilitation HQ" would open IPs of some businesses; 30,000+ signed a petition rejecting tiered internet — [Etemad Online](https://www.etemadonline.com/%D8%A8%D8%AE%D8%B4-%D8%B3%DB%8C%D8%A7%D8%B3%DB%8C-9/724399-%D8%A7%DB%8C%D9%86%D8%AA%D8%B1%D9%86%D8%AA-%D8%B7%D8%A8%D9%82%D8%A7%D8%AA%DB%8C-%D8%A7%D9%85%DB%8C%D8%B1-%D8%B3%DB%8C%D8%A7%D8%AD-%D9%81%D8%B6%D8%A7%DB%8C-%D9%85%D8%AC%D8%A7%D8%B2%DB%8C); SCC resolution of 26 Tir 1404 created the "Committee for Facilitating Digital Economy Businesses" (chaired by SCC secretary; includes MOIS, IRGC, police): businesses file complaints about restrictions, agency reply within 2–7 days, majority vote, can suspend measures; text has no IP-opening/whitelist process — [Tabnak](https://www.tabnak.ir/fa/news/1317744/%D8%AC%D8%B2%D8%A6%DB%8C%D8%A7%D8%AA-%D9%85%D8%B5%D9%88%D8%A8%D9%87-%D8%AC%D8%AF%DB%8C%D8%AF-%D8%B4%D9%88%D8%B1%D8%A7%DB%8C-%D8%B9%D8%A7%D9%84%DB%8C-%D9%81%D8%B6%D8%A7%DB%8C-%D9%85%D8%AC%D8%A7%D8%B2%DB%8C-%D8%AD%D9%85%D8%A7%DB%8C%D8%AA-%D8%A7%D8%B2-%D8%A7%D9%82%D8%AA%D8%B5%D8%A7%D8%AF-%D8%AF%DB%8C%D8%AC%DB%8C%D8%AA%D8%A7%D9%84-%DB%8C%D8%A7-%D8%A7%DB%8C%D9%86%D8%AA%D8%B1%D9%86%D8%AA-%D8%B7%D8%A8%D9%82%D8%A7%D8%AA%DB%8C); government spokesperson: tiered internet "has no legal basis and will never be on the agenda" — [Entekhab](https://www.entekhab.ir/fa/news/875453/)

**Domestic server -> foreign IPs (Telegram API, GitHub, abroad VPS)**
- Open-source "telegram-web-bridge" (archived 15 Sep 2023): apps hosted inside Iran "cannot send requests to the Telegram Bot API and cannot receive webhooks"; solution is a relay hosted abroad with `api.telegram.org` base URL swapped — [GitHub](https://github.com/AmirrezaNasiri/telegram-web-bridge)
- Lantern 2026 analyses: during the shutdown the censor blocked nearly all IPs with DPI as primary enforcement; a few Cloudflare IPs completed TCP handshakes but DPI killed sessions before data; SNI blocklists applied even without DNS records — [Lantern corpus](https://corpus.lantern.io/papers/2026-tuleo1-internet-analysis-shutdown/); [Lantern DPI](https://corpus.lantern.io/papers/2026-pooribitwise-iran-advanced-dpi/)

**Reverse direction: Iranian data sources from abroad**
- SEO chief Seyyedi, asked to open Codal and TSETMC to investors abroad, said the matter would be reviewed and, if tied to information security, an alternative route would be offered — implying external access is restricted (undated Nabz-e Bourse item) — [Nabz-e Bourse](https://nabzebourse.com/fa/news/83105/بورس-تعطیل-یا-محدود-می‌شود)
- Banks' "Iran Access" (blocking non-Iranian IPs) was described by IRNA as a temporary cyber-defence measure for a limited number of banks — [Zoomit](https://www.zoomit.ir/tech-iran/402852-iran-access-digital-economy)
- old.tsetmc.com remained available as an alternative front-end (1402 / 2023 item) — [Nabz-e Bourse](https://nabzebourse.com/fa/news/57325/سایت-قدیم-tsetmc-همچنان-در-دسترس-است)

### Inferences
- Assume a domestic server cannot reliably reach `api.telegram.org`, and that reaching a European VPS/GitHub is possible but subject to DPI/SNI filtering and route instability; design sync as outbound-from-Iran over plain TLS to your own non-blocked domain with queue/retry, and keep an inbound-from-abroad fallback. Business whitelisting is currently a mobile-APN product (Pro SIM) or NASR dedicated IP, not a data-centre-grade service — ask the hosting provider (Liara/Hamravesh/Arvan) in writing what "international" egress they guarantee.
- Reverse reachability must be tested empirically: the SEO's own statements plus bank "Iran Access" practice make it likely that tsetmc/codal apply Iran-only IP policies at least intermittently; the Iran node should be the sole collector and the abroad node a consumer of synced data.

### Gaps
- No measurement found for Oct 2026 of which specific foreign services/ports a domestic data-centre IP can reach; no provider publishes this.
- No 2026 source on whether tsetmc.com/codal.ir/tgju.org/tse.ir geo-block foreign IPs or sit behind Cloudflare/ArvanCloud CDN with country rules.
- Internet Pro availability for fixed-line/data-centre IPs is unconfirmed; NASR dedicated-IP pricing not found.
- CNN (10 May 2026) article content unavailable (HTTP 451).

---

## KQ3. Messaging/alert channels: Telegram, Bale, Eitaa, Rubika, SMS, push

### Takeaway
Bale's Bot API is a documented Telegram-Bot-API clone at `https://tapi.bale.ai/bot<token>/METHOD` (webhooks on 443/88, in-app IRR payments, business bulk API), so a .NET Telegram library can be re-pointed; Telegram itself remains blocked and domestic hosts historically cannot reach its API. Eitaa's official bot API surface is undocumented publicly (token via eitaayar.ir, send-only community libraries); Rubika has only unofficial libraries. SMS costs 11.6–34.6 T per 70-char part at operator level (1405), with Kavenegar OTP requiring a 600,000–1,200,000 T/yr plan; FCM token issuance has been reported broken in Iran since Aug 2022.

### Cited Findings

**Telegram**
- Blocked; ICT minister said unblocking Telegram is for the Committee for Determining Criminal Content; WhatsApp unblocked first (Dec 2024) then re-restricted; Telegram/YouTube talks blocked by a working group (Oct 2026) — [Eghtesad News](https://www.eghtesadnews.com/بخش-%D8%B3%D8%A7%DB%8C%D8%B1-%D8%B1%D8%B3%D8%A7%D9%86%D9%87-%D9%87%D8%A7-61/379671-); [Khabar Foori 17 Oct 2026](https://www.khabarfoori.com/%D8%A8%D8%AE%D8%B4-%D8%A7%D9%82%D8%AA%D8%B5%D8%A7%D8%AF%DB%8C-145/3252936-%D9%85%D8%B5%D9%88%D8%A8%D9%87-%D9%85%D8%AD%D8%B1%D9%85%D8%A7%D9%86%D9%87-%D9%81%DB%8C%D9%84%D8%AA%D8%B1%DB%8C%D9%86%DA%AF-%D8%A7%D8%AF%D8%A7%D9%85%D9%87-%D8%AF%D8%A7%D8%B1-%D8%A7%D8%B3%D8%AA%D8%A7%D8%B1%D9%84%DB%8C%D9%86%DA%A9-%D9%85%D8%B1%D8%AF-%D9%BE%D8%B4%D8%AA-%D8%AF%D8%B1%D9%87%D8%A7%DB%8C-%D8%A8%D8%B3%D8%AA%D9%87-%D9%85%D8%AD%D8%AF%D9%88%D8%AF%DB%8C%D8%AA-%D8%A7%DB%8C%D9%86%D8%AA%D8%B1%D9%86%D8%AA-%DA%A9%DB%8C%D8%B3%D8%AA)
- Voidly tracker: telegram.org blocked in Iran, last verified 9 May 2026 (low confidence 0.24) — [Voidly](https://voidly.ai/blocked/telegram.org/ir)
- Telegram's Feb 2026 update removed the Stories restriction for non-Premium users in Iran (feature, not access) — [Telegram bugs](https://bugs.telegram.org/c/44987/211)
- Iran-hosted apps cannot call Bot API or receive webhooks; relay abroad needed — [GitHub telegram-web-bridge](https://github.com/AmirrezaNasiri/telegram-web-bridge)

**Bale (بله) Bot API — official docs**
- Base URL `https://tapi.bale.ai/bot<token>/METHOD_NAME`; files `https://tapi.bale.ai/file/bot<token>/<file_path>`; "based on the Telegram Bot API with minor changes" so Telegram libraries work; GET/POST, JSON/form/multipart; method names case-insensitive — [docs.bale.ai](https://docs.bale.ai/)
- Updates: `message`, `edited_message`, `callback_query`, `pre_checkout_query`; `getUpdates` long-polling (limit 1–100) or `setWebhook`/`deleteWebhook`/`getWebhookInfo` on ports 443 and 88; server keeps last 2,000 updates for 24 h — [docs.bale.ai](https://docs.bale.ai/)
- Methods: getMe, sendMessage (1–4096 chars), forward/copyMessage, sendPhoto/Audio/Document/Video/Animation/Voice/MediaGroup/Location/Contact, sendChatAction, getFile, answerCallbackQuery, chat admin methods, edit/delete message, stickers, payments (`sendInvoice`, `createInvoiceLink`, `answerPreCheckoutQuery` within 10 s; `LabeledPrice` in IRR; test token `WALLET-TEST-1111111111111111`; wallet-only payout, card-to-card removed); Bale-only `askReview`, `inquireTransaction`; `copy_text` inline button — [docs.bale.ai](https://docs.bale.ai/)
- Rate limits: standard bots limited "interactively" based on user engagement in private chats (`retry_after` in ResponseParameters); Business API `https://tapi.bale.ai/business/bot<TOKEN>/METHOD` for bulk/re-engagement, prepaid via Bale business panel, per-message cost deducted, private chats only, file_id-only media; no numeric caps published — [docs.bale.ai](https://docs.bale.ai/)
- Limits: multipart upload 10 MB photo / 50 MB other; by-URL 5 MB / 20 MB; getFile 20 MB, links valid ~1 h; clients older than Khordad 1404 lack `answerCallbackQuery` support; bot creation via `ble.ir/botfather` — [docs.bale.ai](https://docs.bale.ai/)
- Community libs note: no `parse_mode` param (always Markdown) — [baleio on PyPI](https://pypi.org/project/baleio/)

**Eitaa (ایتا)**
- Official EitaaYar site describes an "API for interacting with web services" but publishes no base URL, methods, limits or pricing; support via ticket at my.eitaa.com; channel posting requires adding @sender — [eitaayar.ir](https://eitaayar.ir/)
- Community wrappers (pyeitaa, eitaapy, PHP usermp/eitaayar) expose `sendMessage`/`sendDocument`/`sendFile` with token from eitaayar.ir and a channel id; token format `bot<number>:<uuid>`; send-oriented (no documented update receipt) — [pyeitaa](https://pypi.org/project/pyeitaa/); [eitaapy](https://pypi.org/project/eitaapy/); [usermp/eitaayar](https://root.packagist.org/packages/usermp/eitaayar)

**Rubika**
- Only unofficial libraries found (rubpy, rubkey, rubika-bot-api); `rubika-bot` package describes BotFather-created bots receiving `receiveUpdate`/`receiveInlineMessage` callbacks to your endpoint; official docs not located — [rubpy](https://pypi.org/project/rubpy); [rubkey](https://pypi.org/project/rubkey/); [rubika_bot_api](https://github.com/rubika-bot-api/rubika_bot_api)

**SMS**
- Operator tariffs (1405): 11.6–34.6 T per part depending on Persian/English and SIM type; MCI postpaid Persian 11.6 T, English 28.9 T; prepaid Persian 13.9 T, English 34.6 T; one Persian part = 70 chars — [Melipayamak blog (Tir 1405)](https://www.melipayamak.com/blog/posts/sms-pricing/)
- Kavenegar plans: Standard free; Advanced 600,000 T/yr; Super Advanced 1,200,000 T/yr; VIP negotiable; OTP/Verify lookup only in Advanced+; 50,000 rial free test credit; dedicated line purchase e.g. 1000-prefix 14-digit ~650,000 rial, 10-digit ~6,500,000 rial; 2000-prefix renewals 70%/yr — [Kavenegar pricing](https://kavenegar.com/pricing.html)
- Zoomit PR: Kavenegar positions web-service SMS as best for verification — [Zoomit](https://www.zoomit.ir/pr/335816-kavenegar-why-web-service-sms-the-best-way-verify/)

**Push notifications**
- Digiato (9 Aug 2022): Iranian users could not obtain FCM tokens since 3 Aug 2022; experts attributed it to sanctions — [Digiato](https://digiato.com/article/2022/08/09/firebase-cloud-messaging-banned-in-iran-by-google); same in [Taadol](https://www.taadolnewspaper.ir/fa/news/191511/)
- WMTips 2026 Iran push-tech share: OneSignal 32.2%, FCM 30%, WebEngage 17.8% (usage, not reachability) — [WMTips](https://www.wmtips.com/technologies/push-notifications/country/ir)
- Jan 2026 whitelist included Google services, Play Store and App Store (so APNs/FCM endpoints plausibly reachable in that phase) — [Filterwatch](https://filter.watch/english/2026/01/28/network-monitoring-january-2026-from-regional-disuptions-to-total-blackout-and-whitelisted-access/)

### Inferences
- For a .NET bot: use the Telegram.Bot-style client with base URL overridden to `https://tapi.bale.ai` for Bale (watch for `parse_mode` and update-type differences), and run the Telegram variant from the abroad node only. Bale bulk alerts require funding the Business API; design alert fan-out to prefer user-initiated (interactive) chats to stay within the free tier.
- Eitaa is practical for one-way channel broadcasts via eitaayar (send-only); treat two-way Eitaa bots as unsupported until official docs are obtained.
- Web push in Iran is unreliable (FCM token issuance history); for PWA alerts prefer in-app polling/WebSocket from the Iran node plus Bale/SMS; budget SMS at roughly 12–35 T/part plus a Kavenegar Advanced plan for OTP.

### Gaps
- No official Eitaa or Rubika bot API documentation (endpoints, rate limits, pricing) was reachable; Bale Business API per-message price not published on docs page.
- No 2025–2026 confirmation whether FCM/APNs delivery works inside Iran today; no SMS.ir/Melipayamak per-message panel price (operator cost found, panel markup not).
- No .NET/C# SDK for Bale referenced in official docs.

---

## KQ4. LLM access from Iran: foreign APIs, Iranian gateways, Persian LLMs, local hardware

### Takeaway
OpenAI and Anthropic exclude Iran from supported countries (OpenAI began blocking unsupported-region API traffic July 2024; Anthropic in Sept 2025 also barred entities majority-owned from Iran), so direct, payable access from Iran is not legitimately available; the practical legal route from inside Iran is a Toman-billed OpenAI-compatible gateway such as AvalAI (`https://api.avalai.ir/v1`, lists OpenAI/Anthropic/Google/DeepSeek models in USD per Mtoken), while the abroad node can call providers directly. Persian open models (Part AI Dorna 3–13B, MCI Silk 1.3B/Ahooran 8B/Ava 13B) exist; local inference hardware is expensive (RTX 4090 ~285–500 million T, RTX 5090 >500 million T at 2025 prices).

### Cited Findings
- OpenAI supported-countries list omits Iran; blocking of API traffic from unsupported regions took effect July 2024 — [The Decoder](https://the-decoder.com/openai-to-restrict-api-access-for-unsupported-countries-in-july/); payments from unsupported regions can trigger account blocks — [heise](https://heise.de/-9778032); rgb.ir commentary: accounts opened around restrictions can be closed without refund — [rgb.ir](https://rgb.ir/en/ai/iran/)
- Anthropic: Iran on neither API nor claude.ai country list; Sept 2025 terms bar companies majority-controlled by entities from China, Russia, Iran, North Korea — [Malay Mail](https://www.malaymail.com/news/tech-gadgets/2025/09/05/amazon-backed-anthropic-says-no-to-china-russia-iran-north-korea-on-claude-chatbot/190109); [The Decoder](https://the-decoder.com/?p=26977)
- June 2026 analysis: ChatGPT and Claude unreachable in Iran because vendors decline the market under US sanctions/export controls — [Voidly](https://voidly.ai/atlas/findings/ai-blocked-two-ways-vendors-and-states-2026-06)
- Jan 2026 whitelist included ChatGPT (web) — [Filterwatch](https://filter.watch/english/2026/01/28/network-monitoring-january-2026-from-regional-disuptions-to-total-blackout-and-whitelisted-access/)
- AvalAI docs: base URL `https://api.avalai.ir/v1`, OpenAI-compatible single endpoint, works with OpenAI Python/JS SDKs and cURL; providers OpenAI, Anthropic, Google, DeepSeek, Qwen, Meta, Mistral, xAI, Cohere, Groq etc.; prices quoted in USD per million tokens (examples on page: "GPT-6.1 Sol" $2/$10, "Claude Opus 5.5" $4/$20 — model names as listed by AvalAI, not verified against vendors); credit packages, rate-limit docs linked but not detailed; no .NET sample — [docs.avalai.ir](https://docs.avalai.ir/)
- AvalAI described as one platform with hundreds of models (GPT, Claude, Gemini, DeepSeek, Grok), API services and credit packages, startup support programme — [Zoomit PR](https://www.zoomit.ir/pr/460866-aval-ai-services/); [Digiato promoted](https://digiato.com/promoted/avalai-platform-services)
- Part AI "Dorna" family 3–13B parameters (Esfand 1402 / Feb 2024); an 8B variant later open-sourced — [Digiato](https://digiato.com/artificial-intelligence/dorna-large-language-model-llm-part-ai); [Digiato open-source](https://digiato.com/iran-technology-news/dorna-llm-was-released-as-open-source); Part AI's 2026 "AI and Persian Sugar" event launched Open Persian LLM Leaderboard v2 — [Zoomit](https://www.zoomit.ir/tech-iran/440753-part-ai-event)
- MCI (Hamrah-e Aval) unveiled Persian LLMs at Elecomp 1403: Silk 1.3B (trained from scratch on Persian), Ahooran 8B, Ava 13B — [Zoomit](https://www.zoomit.ir/tech-iran/423194-persian-language-models-unveiled-by-mci-in-elecomp27-exhibition); [Digiato](https://digiato.com/iran-technology-news/mci-introduces-three-llm-models)
- GPU prices in Iran (2025 reports, not Oct 2026): RTX 5090 entered at >500 million T; an RTX 4090 ~384 million T in same report; another Zoomit piece 285 million T; ASUS ROG Strix 4090 ~500 million T — [Zoomit 5090](https://www.zoomit.ir/graphic/435678-rtx-5090-gpu-iran-market/); [Gadgetnews](https://gadgetnews.net/966727/graphics-card-500-million-iran-market/)

### Inferences
- Architecture: route all LLM calls from the abroad node (direct vendor APIs, paid with a non-Iranian entity/card) or via AvalAI from the Iran node for resilience during blackouts; keep prompts/outputs cached domestically. Do not attempt direct OpenAI/Anthropic use from Iranian IPs or Iranian-owned entities (terms violation, account loss).
- A single-GPU local fallback (8B Persian model) costs roughly the same as 1–2 years of mid-tier PaaS; only justified if offline operation during blackouts is a hard requirement.

### Gaps
- No pricing/markup, Toman conversion or payment-method details for AvalAI; nothing found at all for Gilas, Metis, TalkBot or Hamravesh AI (names may be stale or sites unindexed).
- No 2025–2026 announcement of a new Iranian LLM ("Hormoz"/Mann-E or others) was found.
- No Oct 2026 GPU price data.

---

## KQ5. Payments and distribution for a later public phase

### Takeaway
Zarinpal charges 0.5% (cap 16,000 T) + 500 T fixed, Zibal 1% (cap 20,000 T) on successful transactions; Enamad (e-trust seal) costs 600,000 T per issuance via Zarinpal and is issued by the E-Commerce Development Centre with FATA police vetting; Cafe Bazaar takes 15% on the first 1 billion T of annual sales then 30% (policy since 1399/1400); Apple has no Iranian App Store and has periodically removed Iran-serving apps, and Google Play has done likewise, so PWA + Bazaar/Myket is the realistic path. The SEO's 1 May 2025 letter ordered app stores and messengers to stop distributing and to remove unlicensed "signal" apps, with periodic removal reports to the SEO. A personal-data-protection law has still not been enacted (government bill passed cabinet 24 Tir 1403 / July 2024; parliament stage unresolved).

### Cited Findings

**Payment gateways**
- Zarinpal: 0.5% up to 16,000 T + 500 T fixed per transaction; first month free (only Shaparak fee); Enamad without star 600,000 T per issuance; >150 tx/day custom quote; page copyright 1405 — [Zarinpal pricing](https://www.zarinpal.com/pricing); rade.ir (updated 23 Tir 1405) lists 0.5% + 350 T, cap 12,000 T — conflicting with Zarinpal's own page — [Rade](https://www.rade.ir/online-payment-service/zarinpal)
- Zibal: 1% per transaction, cap 20,000 T, fees only on successful transactions; corporate banking 0.03%/transfer from 30,000,000 T; VAT added on fee; no subscription/direct-debit product described — [Zibal pricing](https://zibal.ir/pricing)
- Enamad requirements (Zarinpal blog, 1405): site must sell goods/services with full product info; applicant must own the domain and be 18+; FATA police vets applicant; different documents for natural vs legal persons — [Zarinpal blog](https://www.zarinpal.com/blog/%D8%A7%DB%8C%D9%86%D9%85%D8%A7%D8%AF-%DA%86%DB%8C%D8%B3%D8%AA/); home businesses licensed by Ministry of Labour get Enamad systemically without a virtual-business guild licence — [Taadol](https://www.taadolnewspaper.ir/fa/news/206416/); seven categories need a virtual-business licence for Enamad (smart transport, classifieds, network marketing, online auction, gold/jewellery...) — [Zoomit](https://www.zoomit.ir/tech-iran/405418-obtaining-permission-facilitating/)

**App stores**
- Cafe Bazaar cut its share on each developer's first 1 billion T (10 billion rial) of annual sales from 30% to 15% (announced Esfand 1399 / Mar 2021; reported again Aban 1400) — [Digiato](https://digiato.com/article/2021/03/18/%d8%a8%d8%b1%d8%a7%db%8c-%d8%ad%d9%85%d8%a7%db%8c%d8%aa-%d8%a7%d8%b2-%da%a9%d8%b3%d8%a8%e2%80%8c%d9%88%da%a9%d8%a7%d8%b1%d9%87%d8%a7%d8%8c-%d8%a8%d8%a7%d8%b2%d8%a7%d8%b1-%db%b1%db%b5%d9%aa); [Zoomit](https://www.zoomit.ir/tech-iran/369835-bazaar-decreasing-revenue-share-impact/); 14,000 apps use Bazaar IAP — [Zoomit](https://www.zoomit.ir/tech-iran/374706-annual-report-of-bazaar-cafe-99/)
- Myket also reduced revenue share (percentage not in accessible text) — [Zoomit](https://www.zoomit.ir/tech-iran/370016-myket-decreasing-revenue-share/); Myket paid 181 billion T to app developers in H1 1403 — [Digiato](https://digiato.com/iran-technology-news/income-report-myket-1403-half-period)
- SEO letter (11 Ordibehesht 1404 / 1 May 2025), signed by Mohammad Mahdi Montazeri (Head, Dept. for Prevention of Anomalies and Promotion of Capital Market Health), to CEOs of messengers and app stores: stop offering/selling unlicensed "stock signal"/advisory apps, continuously monitor and remove them, periodically report removals to SEO Legal & Disciplinary Affairs; legal basis Securities Market Act art. 49(1) (unlicensed activity is a crime), art. 52 (SEO as complainant), Penal Code art. 749 and item 6 of section (ه) of the judiciary's criminal-content list — [Taadol](https://www.taadolnewspaper.ir/fa/news/341582/هشدار-سازمان-بورس-به-اپليكيشن‌هاي-سيگنال-فروش)
- Apple removed Iranian apps including Digikala from the App Store (Bahman 1395 / Jan–Feb 2017), citing sanctions; Shaparak payment integration was identified as the trigger; Google removed Iranian apps two weeks later with the same justification — [Digikala Mag](https://www.digikala.com/mag/چرا-اپل-اپلیکیشن‌های-ایرانی-را-از-اپ‌/); [Donya-e Eqtesad](https://donya-e-eqtesad.com/fa/tiny/news-3891294); [Radio Farda](https://www.radiofarda.com/a/f3-appstore-iran-omitted/28269873.html)
- Google Play was formally unblocked by SCC in Dey 1403 (Dec 2024) — [Zoomg](https://www.zoomg.ir/tech-news/402508-whatsapp-google-play-unbanned/); Play Store and App Store were on the Jan 2026 whitelist — [Filterwatch](https://filter.watch/english/2026/01/28/network-monitoring-january-2026-from-regional-disuptions-to-total-blackout-and-whitelisted-access/)

**Data protection law**
- Government "Protection of Personal Data" bill passed the cabinet 24 Tir 1403 (14 Jul 2024) and was to be sent to parliament — [Shargh](https://www.sharghdaily.com/%D8%A8%D8%AE%D8%B4-%D9%81%D9%86%D8%A7%D9%88%D8%B1%DB%8C-298/936913-); [Aftana](https://www.aftana.ir/news/22310/); a separate MPs' bill registered Mehr 1403 with 41 signatures — [Digiato](https://digiato.com/tech/personal-data-protection-plan-in-parliment); Peivast: MPs' plan announced received while government bill still under review — [Peivast](https://peivast.com/p/212052); ICT minister said the bill went back to commission for finalisation (undated) — [Tabnak](https://www.tabnak.ir/005JNE)
- No source found reporting parliamentary passage or Guardian Council approval through Oct 2026.

### Inferences
- A subscription product on web/PWA can launch with Zarinpal/Zibal + Enamad without an app store; recurring billing would have to be implemented as repeated one-off payments or via a gateway's direct-debit product (none confirmed on the fetched pages).
- Listing a stock-signal/advisory app on Bazaar/Myket without an SEO licence is now an enforcement target (stores are obliged to remove and report); a pure data/analytics tool with explicit "not investment advice" positioning is lower risk but still at the stores' discretion.
- Apple/Google distribution is both sanctions-exposed (developer account, Shaparak IAP) and politically unstable; PWA is the only store-independent channel for iOS users in Iran.

### Gaps
- Could not fetch Cafe Bazaar's developer policy page (JS-only); no explicit Bazaar/Myket rule text on financial-app licensing found; Myket's current commission percentage not found.
- IDPay, Pay.ir, NextPay fee pages not retrieved; Zarinpal settlement timing and direct-debit terms not on pricing page.
- Samandehi (Ministry of Culture) registration requirements not researched due to budget.
- No post-2024 status of the data-protection bill; current Enamad fee for legal persons beyond the Zarinpal-quoted 600,000 T not confirmed.

---

## KQ6. Regulatory specifics for going public: SEO type-2 adviser, fintech sandbox, financial-information-processing licence, enforcement, TSETMC data display

### Takeaway
The type-2 investment adviser (مشاور سرمایه‌گذاری نوع دوم) directive (announced Mehr 1400 / Oct 2021) created a private joint-stock vehicle with only 50 million T minimum capital for three activities — buy/sell/hold recommendations, analysis, and education — specifically to legalise online analysts; the general investment-adviser and financial-information-processing licences require 80 billion and 60 billion rials paid-in capital respectively (SEO circular, 3 Azar 1400). The SEO's capital-market sandbox has an approved executive directive (applications via irantama.ir per one report). Enforcement is active: 97 court cases, 170 referrals to economic-security police, 26 websites pursued via the Prosecutor General (SEO legal deputy, date not stated) plus the 1 May 2025 letter to app stores. No rule text on third-party display of TSETMC data was found.

### Cited Findings
- Type-2 adviser directive (Eghtesad Online, 10 Mehr 1400): private joint-stock company registered at the companies registry; minimum capital 50 million T; activity scope = recommending buy/sell/hold, analysing price trends/supply-demand, capital-market education; designed to organise online investment-advice actors; forms on SEO website; shorter licensing, eased founder requirements, oversight shifts to post-launch monitoring — [Eghtesad Online](https://www.eghtesadonline.com/fa/news/567663/جزییات-دستورالعمل-تاسیس-و-فعالیت-مشاور-سرمایه-گذاری-نوع-دوم); directive and model articles issued 1400; type-2 is an instance of clause 16, art. 1 of the Securities Market Act — [Nabz-e Bourse](https://nabzebourse.com/fa/news/33635/فعالیت-تاسیس-مشاور-سرمایه‌گذاری-نوع-دوم)
- SEO minimum-capital circular (3 Azar 1400 / 24 Nov 2021): portfolio manager 100 billion rial; investment adviser 80 billion rial; financial-information processor 60 billion rial; capital-market services group 500 billion rial; add-on licences +100/+80/+60 billion rial; underwriting/listing adviser +20 billion rial each; existing firms to comply by end Khordad 1402 — [Eghtesad Online](https://www.eghtesadonline.com/fa/news/583819/حداقل-سرمایه-لازم-برای-نهادهای-مالی-ابلاغ-شد-جزییات); earlier 1398 figures were 4 / 3.5 / 2.5 billion T — [Eghtesad Online (1398)](https://www.eghtesadonline.com/fa/news/583819/)
- A Taadol item says the SEO at one point dropped the 8-billion-T minimum for advisers in favour of business-plan review (undated) — [Taadol](https://www.taadolnewspaper.ir/fa/news/196826/تاسیس-مشاوره-سرمایه‌گذاری-۸-میلیارد-تومان-سرمایه-نیاز-ندارد); "currently 30 investment advisory companies" (undated) — [Taadol](https://www.taadolnewspaper.ir/fa/news/342321/تحول-جديد-براي-بورس)
- Financial-information processors must document source-selection and verification procedures (Hamshahri, 1391) — [Hamshahri](https://www.hamshahrionline.ir/news/171108/)
- Sandbox: SEO obtained High Council of Securities approval, in-principle approval from the Ministry of Economy's integrated sandbox working group, and the board approved the executive directive for "capital market test environments"; text on the SEO regulations portal — [Eghtesad Online](https://www.eghtesadonline.com/fa/news/565467/دستورالعمل-اجرایی-محیط-های-آزمون-بازار-سرمایه-ابلاغ-شد); [Mizan](https://www.mizanonline.ir/fa/news/760717/); one-year performance report — [Taadol](https://www.taadolnewspaper.ir/fa/news/194720/); proposals submitted only via irantama.ir (undated) — [Eghtesad Online](https://www.eghtesadonline.com/fa/news/603981/)
- Enforcement stats (SEO legal deputy, undated): 97 cases filed, 170 referrals to economic-security police, 26 websites pursued via Prosecutor General, 76 under documentation — [Eghtesad Online](https://www.eghtesadonline.com/fa/news/480151/); unlicensed advisory/portfolio management is a crime under the Securities Market Act — [Nabz-e Bourse](https://nabzebourse.com/fa/news/15741/); High Council resolution 21 Aban 1399 on restorative-justice centre for online actors — [Eghtesad News](https://www.eghtesadnews.com/بخش-%D8%A7%D8%AE%D8%A8%D8%A7%D8%B1-%D8%A7%D9%82%D8%AA%D8%B5%D8%A7%D8%AF%DB%8C-67/414058-)
- 1 May 2025 letter to app stores/messengers (see KQ5) — [Taadol](https://www.taadolnewspaper.ir/fa/news/341582/هشدار-سازمان-بورس-به-اپليكيشن‌هاي-سيگنال-فروش)

### Inferences
- For a public phase that issues buy/sell signals, the type-2 adviser company (50 million T capital, private JSC) is the proportionate licence; a pure data/analytics product would more naturally fall under "financial information processing", whose 60-billion-rial capital bar is prohibitive for a small team, so framing as analysis/education under type-2 or entering the sandbox is the realistic route. Confirm current capital figures directly with the SEO because the 1400 numbers may have been revised in 1403–1405.

### Gaps
- No 1404/1405 amendments to the type-2 directive, current licence counts, fees or processing times were found; no SEO fintech-platform ("سکوهای فناوری مالی") licensing text for 1404 found.
- No rule or contract text on redistributing TSETMC/TSE data in third-party apps (TSETMC terms, data-vendor agreements) was located.
- irantama.ir sandbox intake status in 2026 unverified.
