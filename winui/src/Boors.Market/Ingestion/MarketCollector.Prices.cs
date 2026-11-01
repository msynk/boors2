using Boors.Market.Analytics;
using Boors.Market.Domain;
using Boors.Market.Sources;
using Boors.Market.Sources.Tsetmc;
using Boors.Market.Storage;
using Boors.Market.Text;
using Microsoft.Extensions.Logging;

namespace Boors.Market.Ingestion;

// Prices: one-time history backfill, the nightly end-of-day append, missed-session repair, and halt resolution.
public sealed partial class MarketCollector
{
    /// <summary>
    /// Full history (bars, flows, published and implied adjustments, share changes) for instruments in
    /// <paramref name="scope"/> that have no official history yet (or all of them with <paramref name="force"/>).
    /// About four requests per instrument; resumable because responses are served from the raw cache on rerun.
    /// </summary>
    public Task<CollectionResult> BackfillHistoryAsync(int maxInstruments, bool force, bool withMessages, IReadOnlySet<InstrumentClass>? scope, CachePolicy policy, CancellationToken cancellationToken) =>
        RunAsync("backfill", async () =>
        {
            var calendar = await EnsureCalendarAsync(policy, cancellationToken);
            var withHistory = force ? new HashSet<long>() : store.GetInstrumentsWithHistory();
            var classes = scope ?? CollectionScope.Default;
            var candidates = store.GetInstruments()
                .Where(i => classes.Contains(i.Class) && i.ParentInsCode is null && !withHistory.Contains(i.InsCode))
                .OrderBy(i => i.Class)
                .ThenBy(i => i.Symbol, StringComparer.Ordinal)
                .Take(maxInstruments)
                .ToList();

            var symbols = withMessages ? SymbolMap() : null;
            var fetchPolicy = force ? policy : Prefer(policy);
            var done = 0;
            var failures = 0;
            foreach (var instrument in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // A rebuild only replays what the cache holds; instruments never fetched are simply not part of it.
                if (policy == CachePolicy.CacheOnly && await rawCache.GetLatestAsync(tsetmc.Endpoints.ClosingPriceDailyList(instrument.InsCode), cancellationToken) is null)
                    continue;
                failures += await TryAsync($"history {instrument.Symbol} ({instrument.InsCode})", async () =>
                {
                    var bars = await tsetmc.GetDailyHistoryAsync(instrument.InsCode, fetchPolicy, cancellationToken);
                    var flows = await tsetmc.GetClientTypeHistoryAsync(instrument.InsCode, fetchPolicy, cancellationToken);
                    var published = await tsetmc.GetPriceAdjustmentsAsync(instrument.InsCode, fetchPolicy, cancellationToken);
                    var shareChanges = await tsetmc.GetShareChangesAsync(instrument.InsCode, fetchPolicy, cancellationToken);
                    if (withMessages)
                        store.UpsertMessages(await tsetmc.GetMessagesAsync(instrument.InsCode, fetchPolicy, cancellationToken), symbols!);

                    var (classified, reasons) = ClassifyWithHalts(instrument, bars, calendar);
                    store.UpsertDailyBars(classified, "history", reasons);
                    store.UpsertClientTypes(flows);
                    store.UpsertPriceAdjustments(published, "published");
                    store.UpsertPriceAdjustments(PointInTime.ImpliedAdjustments(bars), "implied");
                    store.UpsertShareChanges(shareChanges);
                    done++;
                });

                if ((done + failures) % 50 == 0)
                    logger.LogInformation("Backfill: {Done}/{Total} instruments ({Failures} failures)", done, candidates.Count, failures);
            }

            return (done, failures, $"{done} of {candidates.Count} candidate instruments backfilled");
        });

    /// <summary>
    /// End-of-day append from three bulk calls (market watch, same-day flows, index snapshots) instead of thousands of
    /// per-instrument calls. Bars are tagged source=marketwatch; a later history backfill replaces them with official rows.
    /// </summary>
    public Task<CollectionResult> EndOfDayAsync(bool force, CachePolicy policy, CancellationToken cancellationToken) =>
        RunAsync("eod", async () =>
        {
            var now = IranTime.Now(timeProvider);
            var today = TodayInIran;
            var session = rules.GetSession(InstrumentClass.TseStock, today).Value;
            var isTradingWeekday = now.DayOfWeek is not (DayOfWeek.Thursday or DayOfWeek.Friday);
            if (!force && isTradingWeekday && TimeOnly.FromTimeSpan(now.TimeOfDay) < session.Close.AddMinutes(5))
                return (0, 0, $"skipped: the session closes at {session.Close:HH:mm} Iran time (use --force to snapshot mid-session)");

            var snapshot = await tsetmc.GetMarketWatchAsync(new MarketWatchQuery(), policy, cancellationToken);
            var sessionDate = await DetermineSessionDateAsync(snapshot, policy, cancellationToken);
            if (sessionDate is null)
                return (0, 0, "skipped: nothing traded in the snapshot (market closed)");

            UpsertFromMarketWatch(snapshot, today);
            var instruments = store.GetInstruments().ToDictionary(i => i.InsCode);
            var recent = store.GetRecentBars(10);
            var calendar = LoadCalendar();
            var failures = 0;

            // Bars, classified against the last stored days of each instrument.
            var newBars = new List<ClassifiedBar>();
            var corporateActions = new List<PriceAdjustment>();
            foreach (var row in snapshot.Rows)
            {
                var bar = new DailyBar
                {
                    InsCode = row.InsCode,
                    Date = sessionDate.Value,
                    Open = row.Open,
                    High = row.High,
                    Low = row.Low,
                    Close = row.Close,
                    Last = row.Last,
                    Yesterday = row.Yesterday,
                    TradeCount = row.TradeCount,
                    Volume = row.Volume,
                    Value = row.Value,
                    LastTradeTime = row.LastTradeTime,
                };

                var history = recent.GetValueOrDefault(row.InsCode)?.Where(b => b.Bar.Date < sessionDate).Select(b => b.Bar).ToList() ?? [];
                var instrumentClass = instruments.TryGetValue(row.InsCode, out var instrument) ? instrument.Class : row.Class;
                var (classified, corporateAction) = ClassifySessionBar(bar, history, instrumentClass, calendar);
                newBars.Add(classified);
                if (corporateAction is not null)
                    corporateActions.Add(corporateAction);
            }

            store.UpsertDailyBars(newBars, "marketwatch");
            store.UpdateSessionSnapshots(snapshot.Rows.Select(r => SessionSnapshot.From(r, sessionDate.Value)));
            store.UpsertPriceAdjustments(corporateActions, "implied");
            failures += await FetchCorporateActionsAsync(corporateActions, instruments, policy, cancellationToken);

            // Same-day flows: volumes and counts are official, rial values are estimated from the day's average price.
            failures += await TryAsync("client types", async () =>
            {
                var bars = newBars.ToDictionary(b => b.Bar.InsCode, b => b.Bar);
                var flows = (await tsetmc.GetClientTypeAllAsync(policy, cancellationToken))
                    .Where(f => bars.TryGetValue(f.InsCode, out var b) && b.Volume > 0)
                    .Select(f => f.ToEstimatedDay(sessionDate.Value, bars[f.InsCode].Value / bars[f.InsCode].Volume));
                store.UpsertClientTypes(flows);
            });

            failures += await TryAsync("instrument statistics", async () =>
                store.UpsertInstrumentStatistics(await tsetmc.GetInstrumentStatisticsAsync(policy, cancellationToken)));

            foreach (var flow in new[] { 1, 2 })
            {
                failures += await TryAsync($"index snapshots flow {flow}", async () =>
                {
                    var indices = await tsetmc.GetIndexSnapshotsAsync(flow, policy, cancellationToken);
                    store.UpsertIndexInfo(indices.Select(i => (i.InsCode, i.Name, flow)));
                    store.UpsertIndexBars(indices.Select(i => new IndexBar { InsCode = i.InsCode, Date = sessionDate.Value, Close = i.Value, Low = i.Low, High = i.High }), "snapshot");
                });
            }

            RefreshCalendar();
            var pendingBackfill = newBars.Count(b => instruments.TryGetValue(b.Bar.InsCode, out var i) && CollectionScope.Default.Contains(i.Class) && !recent.ContainsKey(b.Bar.InsCode));
            return (newBars.Count, failures,
                $"session {sessionDate:yyyy-MM-dd}: {newBars.Count(b => b.Bar.TradeCount > 0)} traded, {corporateActions.Count} corporate actions, {pendingBackfill} new instruments awaiting backfill");
        });

    /// <summary>
    /// Fills the trading days of the last <paramref name="lookbackDays"/> on which no instrument has a bar because the
    /// end-of-day job never ran (the desktop was closed, the node was down): one GetInstrmentsHistoryInDay call per day,
    /// oldest first, classified and checked for corporate actions like the end-of-day append. It stops at the first day
    /// it cannot fetch, so a later day is never compared with a hole before it; the next run continues from there.
    /// The latest stored session before today is rewritten from the same official list (one more call, cached after
    /// the first run): its bars may be the 13:35 snapshot, taken before the ETFs closed at 18:00, and the next
    /// end-of-day append compares against them. Same-day flows have no per-day bulk source, so flows stay missing on
    /// repaired days.
    /// </summary>
    public Task<CollectionResult> RepairMissedSessionsAsync(int lookbackDays, CachePolicy policy, CancellationToken cancellationToken) =>
        RunAsync("repair", async () =>
        {
            var instruments = store.GetInstruments().ToDictionary(i => i.InsCode);
            if (instruments.Count == 0)
                return (0, 0, "skipped: no instruments yet (end of day and backfill fill a new store)");

            // Today's session belongs to the end-of-day job.
            var today = TodayInIran;
            var from = today.AddDays(-lookbackDays);
            var to = today.AddDays(-1);
            var closures = store.GetClosureDays().ToHashSet();
            var withBars = store.GetBarDates(from, to);
            var missed = EachDay(from, to).Where(d => !withBars.Contains(d) && DataHealthService.IsTradingDay(d, closures)).ToList();
            DateOnly? latest = withBars.Count > 0 ? withBars.Max() : null;
            var days = missed.Concat(latest is { } l ? [l] : []).Order().ToList();
            if (days.Count == 0)
                return (0, 0, "no missed sessions");

            var calendar = LoadCalendar();
            var history = store.GetBarsBetween(days[0].AddDays(-30), today)
                .ToDictionary(kv => kv.Key, kv => kv.Value.Select(b => b.Bar).ToList());
            var corporateActions = new List<PriceAdjustment>();
            var filled = 0;
            var stored = 0;
            var failures = 0;
            var refreshed = false;
            foreach (var day in days)
            {
                IReadOnlyList<DailyBar>? traded = null;
                failures += await TryAsync($"session {day:yyyy-MM-dd}", async () =>
                    traded = await tsetmc.GetInstrumentsHistoryInDayAsync(day, Prefer(policy), cancellationToken));
                // A missed day that cannot be fetched stops the run; the stored session just keeps its bars.
                if (traded is null && day == latest)
                    continue;
                if (traded is null)
                    break;

                // Rows only for instruments that traded; an empty day was a closure the calendar does not know.
                var newBars = new List<ClassifiedBar>();
                foreach (var bar in traded)
                {
                    if (!instruments.TryGetValue(bar.InsCode, out var instrument))
                        continue;

                    if (!history.TryGetValue(bar.InsCode, out var bars))
                        history[bar.InsCode] = bars = [];
                    var (classified, corporateAction) = ClassifySessionBar(bar, [.. bars.Where(b => b.Date < day).OrderBy(b => b.Date)], instrument.Class, calendar);
                    newBars.Add(classified);
                    bars.Add(bar);

                    // The end-of-day append already checked the stored session for corporate actions; secondary
                    // listings (issuance/redemption units at price 1, blocks) roll their reference price every day.
                    if (corporateAction is not null && day != latest && instrument.ParentInsCode is null)
                        corporateActions.Add(corporateAction);
                }

                // Tagged like the end-of-day rows: a bulk end-of-day source that a per-instrument backfill replaces.
                store.UpsertDailyBars(newBars, "marketwatch");
                stored += newBars.Count;
                if (day == latest)
                {
                    refreshed = true;
                }
                else
                {
                    filled++;
                    logger.LogInformation("Repair: {Day:yyyy-MM-dd} filled with {Bars} bars ({Filled}/{Missed})", day, newBars.Count, filled, missed.Count);
                }
            }

            store.UpsertPriceAdjustments(corporateActions, "implied");
            failures += await FetchCorporateActionsAsync(corporateActions, instruments, policy, cancellationToken);
            var refresh = refreshed ? $"; {latest:yyyy-MM-dd} rewritten from the official list" : string.Empty;
            return (stored, failures, missed.Count == 0
                ? $"no missed sessions{refresh}"
                : $"{filled} of {missed.Count} missed sessions filled ({missed[0]:yyyy-MM-dd}..{missed[^1]:yyyy-MM-dd}), {stored} bars, {corporateActions.Count} corporate actions{refresh}");
        });

    /// <summary>
    /// Classifies a session's bar against the instrument's stored days before it, and spots a corporate action: the
    /// session's reference price differs from the last stored close.
    /// </summary>
    private (ClassifiedBar Bar, PriceAdjustment? CorporateAction) ClassifySessionBar(DailyBar bar, IReadOnlyList<DailyBar> history, InstrumentClass instrumentClass, MarketCalendar calendar)
    {
        var classifier = new DayStatusClassifier(calendar, d => rules.GetPriceLimit(instrumentClass, d).Value);
        var classified = classifier.Classify([.. history, bar])[^1];
        var corporateAction = history.Count > 0 && history[^1].Close > 0 && bar.TradeCount > 0 && bar.Yesterday > 0
            && Math.Abs(bar.Yesterday / history[^1].Close - 1) > 0.0005m
            ? new PriceAdjustment { InsCode = bar.InsCode, Date = bar.Date, AdjustedClose = bar.Yesterday, UnadjustedClose = history[^1].Close }
            : null;
        return (classified, corporateAction);
    }

    /// <summary>
    /// Published adjustments and share changes of the primary default-scope listings that had a corporate action
    /// (secondary listings roll their reference price daily, so they always look like one).
    /// </summary>
    private async Task<int> FetchCorporateActionsAsync(IReadOnlyList<PriceAdjustment> corporateActions, IReadOnlyDictionary<long, Instrument> instruments, CachePolicy policy, CancellationToken cancellationToken)
    {
        var failures = 0;
        foreach (var insCode in corporateActions.Select(a => a.InsCode).Distinct()
            .Where(c => instruments.TryGetValue(c, out var i) && i.ParentInsCode is null && CollectionScope.Default.Contains(i.Class)))
        {
            failures += await TryAsync($"corporate action {insCode}", async () =>
            {
                store.UpsertPriceAdjustments(await tsetmc.GetPriceAdjustmentsAsync(insCode, policy, cancellationToken), "published");
                store.UpsertShareChanges(await tsetmc.GetShareChangesAsync(insCode, policy, cancellationToken));
            });
        }

        return failures;
    }

    /// <summary>
    /// Resolves zero-trade runs: asks TSETMC for the instrument state on the first day of each unresolved run; a halted
    /// state ("IS") turns the run into SymbolHalted with a reason from the nearest supervisor message.
    /// </summary>
    public Task<CollectionResult> ResolveHaltsAsync(int maxQueries, IReadOnlySet<InstrumentClass>? scope, CachePolicy policy, CancellationToken cancellationToken) =>
        RunAsync("halts", async () =>
        {
            var calendar = LoadCalendar();
            var classes = scope ?? CollectionScope.Default;
            var queries = 0;
            var updated = 0;
            var failures = 0;
            foreach (var instrument in store.GetInstruments().Where(i => classes.Contains(i.Class) && i.ParentInsCode is null))
            {
                if (queries >= maxQueries)
                    break;

                var stored = store.GetDailyBars(instrument.InsCode);
                if (stored.Count == 0)
                    continue;

                var states = store.GetInstrumentStates(instrument.InsCode);
                var unresolved = NoTradeRuns(stored.Select(s => new ClassifiedBar(s.Bar, s.Status ?? DayStatus.Traded)).ToList())
                    .Where(run => !states.ContainsKey(run.First))
                    .ToList();
                if (unresolved.Count == 0)
                    continue;

                foreach (var run in unresolved)
                {
                    if (queries++ >= maxQueries)
                        break;
                    failures += await TryAsync($"state {instrument.Symbol} {run.First:yyyy-MM-dd}", async () =>
                        store.UpsertInstrumentStates([await tsetmc.GetInstrumentStateAsync(instrument.InsCode, run.First, Prefer(policy), cancellationToken)]));
                }

                var (classified, reasons) = ClassifyWithHalts(instrument, stored.Select(s => s.Bar).ToList(), calendar);
                var changes = classified
                    .Zip(stored, (now, before) => (now, before))
                    .Where(x => x.now.Status != x.before.Status || reasons.ContainsKey(x.now.Bar.Date))
                    .Select(x => (x.now.Bar.Date, x.now.Status, reasons.TryGetValue(x.now.Bar.Date, out var r) ? r : (HaltReason?)null))
                    .ToList();
                store.UpdateDayStatuses(instrument.InsCode, changes);
                updated += changes.Count;
            }

            return (updated, failures, $"{queries} state queries, {updated} day statuses updated");
        });

    /// <summary>Classifies bars, turning zero-trade runs that start on a known halted day into SymbolHalted.</summary>
    private (IReadOnlyList<ClassifiedBar> Bars, IReadOnlyDictionary<DateOnly, HaltReason> Reasons) ClassifyWithHalts(Instrument instrument, IReadOnlyList<DailyBar> bars, MarketCalendar calendar)
    {
        var classifier = new DayStatusClassifier(calendar, d => rules.GetPriceLimit(instrument.Class, d).Value);
        var firstPass = classifier.Classify(bars);
        var states = store.GetInstrumentStates(instrument.InsCode);
        if (states.Count == 0)
            return (firstPass, new Dictionary<DateOnly, HaltReason>());

        var messages = store.GetMessages(instrument.InsCode).Where(m => m.Kind == MessageKind.Halt).ToList();
        var halted = new HashSet<DateOnly>();
        var reasons = new Dictionary<DateOnly, HaltReason>();
        foreach (var run in NoTradeRuns(firstPass))
        {
            if (!states.TryGetValue(run.First, out var state) || state is null || state == "A")
                continue;

            var reason = messages
                .Where(m => m.Date <= run.First && m.Date >= run.First.AddDays(-5))
                .OrderByDescending(m => m.Date)
                .Select(m => m.Reason)
                .FirstOrDefault(HaltReason.Unknown);
            foreach (var day in run.Days)
            {
                halted.Add(day);
                reasons[day] = reason;
            }
        }

        return (halted.Count == 0 ? firstPass : classifier.Classify(bars, halted), reasons);
    }

    /// <summary>Consecutive NoTrade days; market-closed days inside a run neither break nor join it.</summary>
    private static IEnumerable<(DateOnly First, IReadOnlyList<DateOnly> Days)> NoTradeRuns(IReadOnlyList<ClassifiedBar> classified)
    {
        List<DateOnly>? current = null;
        foreach (var bar in classified.OrderBy(b => b.Bar.Date))
        {
            switch (bar.Status)
            {
                case DayStatus.NoTrade or DayStatus.SymbolHalted:
                    (current ??= []).Add(bar.Bar.Date);
                    break;
                case DayStatus.MarketClosed:
                    break;
                default:
                    if (current is { Count: > 0 })
                        yield return (current[0], current);
                    current = null;
                    break;
            }
        }

        if (current is { Count: > 0 })
            yield return (current[0], current);
    }

    /// <summary>The trading date of a market-watch snapshot: the latest last-trade date among the most-traded instruments.</summary>
    private async Task<DateOnly?> DetermineSessionDateAsync(MarketWatchSnapshot snapshot, CachePolicy policy, CancellationToken cancellationToken)
    {
        DateOnly? latest = null;
        foreach (var row in snapshot.Rows.Where(r => r.TradeCount > 0).OrderByDescending(r => r.Value).Take(3))
        {
            ClosingPriceSnapshot info;
            try
            {
                info = await tsetmc.GetClosingPriceInfoAsync(row.InsCode, policy, cancellationToken);
            }
            catch (Exception e) when (e is SourceRequestException or NotCachedException or HttpRequestException or Polly.ExecutionRejectedException)
            {
                logger.LogDebug("Session date: {InsCode} unavailable ({Message})", row.InsCode, e.Message);
                continue;
            }

            if (info.Date is { } date && (latest is null || date > latest))
                latest = date;
        }

        return latest;
    }

    private async Task<MarketCalendar> EnsureCalendarAsync(CachePolicy policy, CancellationToken cancellationToken)
    {
        if (store.GetIndexBars(TsetmcEndpoints.TedpixInsCode).Count == 0)
            store.UpsertIndexBars(await tsetmc.GetIndexHistoryAsync(TsetmcEndpoints.TedpixInsCode, Prefer(policy), cancellationToken), "history");

        return RefreshCalendar();
    }
}
