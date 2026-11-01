using Boors.Market.Analytics;
using Boors.Market.Domain;
using Boors.Market.Filters;

namespace Boors.Market.Cli;

/// <summary>Read-side commands over the market store: what the Phase 2 pages will show, in the terminal.</summary>
public sealed class MarketCommands(MarketAnalytics analytics, MarketWatchService marketWatch)
{
    private const decimal Hemat = 10_000_000_000_000m; // همت = 1,000 billion toman = 10^13 rial

    public int Run(CliArgs cli)
    {
        DateOnly? session = DateOnly.TryParseExact(cli.Get("date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        var take = cli.GetInt("take", 15);

        switch (cli.At(1))
        {
            case "overview":
                return Overview(session);
            case "breadth":
                Output.Table(["date", "adv", "dec", "unch", "no trade", "halted", "locked up", "locked down", "value (hemat)"],
                    analytics.GetBreadth(cli.GetInt("days", 20)).Select(b => (IReadOnlyList<object?>)[b.Date, b.Advancers, b.Decliners, b.Unchanged, b.NotTraded, b.Halted, b.LockedUp, b.LockedDown, Math.Round(b.EquityValue / Hemat, 1)]));
                return 0;
            case "sectors":
                Output.Table(["sector", "name", "members", "traded", "value (hemat)", "weighted %", "median %", "indiv. net (hemat)", "adv", "dec"],
                    analytics.GetSectors(session).Take(take).Select(s => (IReadOnlyList<object?>)[s.SectorCode, Short(s.SectorName, 28), s.Instruments, s.Traded, Math.Round(s.Value / Hemat, 2),
                        Pct(s.WeightedReturn), Pct(s.MedianReturn), Math.Round(s.IndividualNetFlow / Hemat, 2), s.Advancers, s.Decliners]));
                return 0;
            case "movers":
                var kind = Enum.TryParse<MoverKind>(cli.At(2), ignoreCase: true, out var k) ? k : MoverKind.Value;
                Output.Table(["symbol", "status", "close", "change %", "value (B rial)", "indiv. net (B rial)"],
                    analytics.GetMovers(kind, take, session).Select(m => (IReadOnlyList<object?>)[m.Symbol, m.DayStatus, m.Close, Round(m.ChangePercent), Math.Round(m.Value / 1_000_000_000m, 1), m.IndividualNetFlow is { } n ? Math.Round(n / 1_000_000_000m, 1) : null]));
                return 0;
            case "screen":
                var screen = Enum.TryParse<ScreenKind>(cli.At(2), ignoreCase: true, out var s) ? s : ScreenKind.Momentum6To1;
                Output.Table(["rank", "symbol", "class", "sector", "score", "days"],
                    analytics.Screen(screen, take, session).Select((r, i) => (IReadOnlyList<object?>)[i + 1, r.Symbol, r.InstrumentClass, r.SectorCode, Math.Round(r.Score, 4), r.Observations]));
                return 0;
            case "metrics":
                return Metrics(cli.At(2) ?? throw new ArgumentException("Usage: boors market metrics <symbol>"));
            case "search":
                Output.Table(["ins_code", "symbol", "name", "class", "parent"], analytics.Search(cli.At(2) ?? string.Empty, take)
                    .Select(i => (IReadOnlyList<object?>)[i.InsCode, i.Symbol, Short(i.Name, 40), i.Class.ToRuleKey(), i.ParentInsCode]));
                return 0;
            case "watch":
                return Watch(cli, session, take);
            default:
                throw new ArgumentException("Usage: boors market overview|breadth|sectors|movers <gainers|losers|value|inflow|outflow>|screen <kind>|metrics <symbol>|search <text>|watch [--filter \"...\"] [--date yyyy-MM-dd] [--take N]");
        }
    }

    private int Overview(DateOnly? session)
    {
        var o = analytics.GetOverview(session);
        if (o is null)
        {
            Console.WriteLine("No sessions in the store yet. Run 'boors collect all'.");
            return 1;
        }

        Console.WriteLine($"Session {o.Session:yyyy-MM-dd}");
        foreach (var index in o.Indices)
            Console.WriteLine($"  {index.Name,-28} {index.Value,16:N0}  {Round(index.ChangePercent),7}%");
        if (o.TedpixUsdChangePercent is { } usdChange)
            Console.WriteLine($"  {"TEDPIX in free-market USD",-28} {"",16}  {Math.Round(usdChange, 2),7}%");
        Console.WriteLine();
        Console.WriteLine($"  Equity value   {o.EquityValue / Hemat,10:N2} hemat   trades {o.EquityTrades:N0}");
        Console.WriteLine($"  Fund value     {o.FundValue / Hemat,10:N2} hemat");
        Console.WriteLine($"  Breadth        +{o.Advancers} / -{o.Decliners} / ={o.Unchanged}   no trade {o.NotTraded}, halted {o.Halted}");
        Console.WriteLine($"  Buy queues     {o.BuyQueues,5}   {(o.BuyQueueValue is { } bq ? $"{bq / Hemat:N2} hemat" : "value n/a (no close snapshot)")}");
        Console.WriteLine($"  Sell queues    {o.SellQueues,5}   {(o.SellQueueValue is { } sq ? $"{sq / Hemat:N2} hemat" : "value n/a (no close snapshot)")}");
        Console.WriteLine($"  Individual net {o.IndividualNetFlow / Hemat,10:N2} hemat{(o.FlowValuesEstimated ? " (includes estimates)" : "")}   per-capita buy {o.IndividualPerCapitaBuy / 10_000_000m:N0} / sell {o.IndividualPerCapitaSell / 10_000_000m:N0} million toman");
        if (o.Usd is { } usd)
            Console.WriteLine($"  USD (free)     {usd.Value,14:N0} rial  {Round(usd.ChangePercent),6}%  ({usd.Date:yyyy-MM-dd})");
        if (o.Coin is { } coin)
            Console.WriteLine($"  Emami coin     {coin.Value,14:N0} rial  {Round(coin.ChangePercent),6}%  ({coin.Date:yyyy-MM-dd})");
        return 0;
    }

    private int Metrics(string symbol)
    {
        var instrument = analytics.Search(symbol, 1).FirstOrDefault();
        var m = instrument is null ? null : analytics.GetInstrumentMetrics(instrument.InsCode);
        if (m is null)
        {
            Console.Error.WriteLine($"Unknown symbol '{symbol}'.");
            return 1;
        }

        Console.WriteLine($"{m.Symbol} — {m.Name} ({m.InstrumentClass}, sector {m.SectorCode} {m.SectorName})");
        Console.WriteLine($"  As of {m.AsOf:yyyy-MM-dd} [{m.DayStatus}]  close {m.Close:N0}  {Round(m.ChangePercent)}%  market cap {m.MarketCap / Hemat:N1} hemat");
        Console.WriteLine($"  52w high {m.High52Week:N0}  low {m.Low52Week:N0}  from high {Round(m.FromHigh52WeekPercent)}%  (adjusted closes)");
        Console.WriteLine($"  Returns  {string.Join("  ", m.Returns.Select(r => $"{r.Key} {Pct(r.Value)}%"))}   1y in USD {Pct(m.UsdReturn1Year)}%");
        Console.WriteLine($"  Value 20d avg {m.AverageValue20 / 1_000_000_000m:N1} B rial   individual net 20d {m.IndividualNetFlow20 / 1_000_000_000m:N1} B rial");
        Console.WriteLine($"  Per-capita buy {m.PerCapitaBuy / 10_000_000m:N0} / sell {m.PerCapitaSell / 10_000_000m:N0} million toman   buyer power {m.BuyerPower:N2} (percentile {Pct(m.BuyerPowerPercentile)})");
        return 0;
    }

    private int Watch(CliArgs cli, DateOnly? session, int take)
    {
        MarketWatchResult result;
        try
        {
            result = marketWatch.Query(new MarketWatchRequest { Session = session, Filter = cli.Get("filter"), TradedOnly = cli.Has("traded") });
        }
        catch (FilterSyntaxException e)
        {
            Console.Error.WriteLine($"Filter error: {e.Message}");
            return 1;
        }

        Output.Table(["symbol", "class", "close", "change %", "value (B rial)", "queue", "P/E", "buyer power", "indiv. buy share"],
            result.Entries.Take(take).Select(e => (IReadOnlyList<object?>)[e.Symbol, e.InstrumentClass, e.Close, Round(e.ChangePercent), Math.Round(e.Value / 1_000_000_000m, 1),
                e.Queue, e.PriceEarnings, e.BuyerPower is { } p ? Math.Round(p, 2) : null, Pct(e.IndividualBuyShare)]));
        Console.WriteLine($"{result.Entries.Count} instrument(s), session {result.Session:yyyy-MM-dd}{(result.Filter is { } f ? $", filter evaluated on {f.Evaluated} rows" : "")}.");
        foreach (var error in result.Filter?.Errors ?? [])
            Console.WriteLine($"  filter error: {error}");
        return 0;
    }

    private static double? Round(double? value) => value is { } v ? Math.Round(v, 2) : null;

    private static double? Pct(double? fraction) => fraction is { } f ? Math.Round(f * 100, 2) : null;

    private static string? Short(string? value, int max) => value is null || value.Length <= max ? value : value[..max] + "…";
}
