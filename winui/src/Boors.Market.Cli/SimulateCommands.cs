using Boors.Market.Simulation;

namespace Boors.Market.Cli;

/// <summary>Strategy simulations over the store: the Simulations page's engine, in the terminal.</summary>
public sealed class SimulateCommands(MarketSimulator simulator)
{
    public int Run(CliArgs cli)
    {
        var preset = cli.Get("preset") ?? SimulationPresets.Balanced;
        if (SimulationPresets.All.All(p => p.Key != preset))
            throw new ArgumentException($"Unknown preset '{preset}'. Presets: {string.Join(", ", SimulationPresets.All.Select(p => p.Key))}.");

        var settings = SimulationPresets.Get(preset);
        double? Fraction(string name) => double.TryParse(cli.Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : null;
        settings = settings with
        {
            Positions = cli.GetInt("positions", settings.Positions),
            RebalanceSessions = cli.GetInt("rebalance", settings.RebalanceSessions),
            MinHoldSessions = cli.GetInt("min-hold", settings.MinHoldSessions),
            HoldPercentile = Fraction("hold") ?? settings.HoldPercentile,
            StopLoss = Fraction("stop") ?? settings.StopLoss,
            TrailingStop = Fraction("trailing") ?? settings.TrailingStop,
            Adaptive = settings.Adaptive || cli.Has("adaptive"),
            RegimeFilter = settings.RegimeFilter && !cli.Has("no-regime"),
            Fill = cli.Get("fill") switch
            {
                null => settings.Fill,
                "immediate" => FillPrice.Immediate,
                "next-close" => FillPrice.NextClose,
                "next-open" => FillPrice.NextOpen,
                var other => throw new ArgumentException($"Unknown fill '{other}'. Fills: immediate, next-close, next-open."),
            },
            InstantSaleProceeds = settings.InstantSaleProceeds && !cli.Has("t2"),
        };
        var capital = decimal.TryParse(cli.Get("capital"), NumberStyles.Number, CultureInfo.InvariantCulture, out var c) ? c : 10_000_000_000m;
        var start = DateOnly.TryParseExact(cli.Get("start"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var s)
            ? s
            : DateOnly.FromDateTime(DateTime.Today).AddYears(-1);
        var spec = new SimulationSpec { Id = "cli", Capital = capital, Start = start, Revisions = [new SettingsRevision(start, settings)] };

        if (cli.At(1) == "tune")
            return Tune(cli, spec);

        var stopwatch = Stopwatch.StartNew();
        var result = simulator.Run(spec);
        var first = stopwatch.Elapsed.TotalSeconds;
        stopwatch.Restart();
        simulator.Run(spec, detail: false);
        var again = stopwatch.Elapsed.TotalSeconds;
        if (result.AsOf is null)
        {
            Console.WriteLine($"No session on or after {start:yyyy-MM-dd} in the store yet.");
            return 1;
        }

        var stats = result.Stats;
        Console.WriteLine($"{preset}: {result.Start:yyyy-MM-dd} → {result.AsOf:yyyy-MM-dd}, {stats.Sessions} sessions, {first:0.00} s (again {again:0.00} s)");
        Output.Table(["measure", "value"],
        [
            ["equity (B rial)", Math.Round(stats.Equity / 1_000_000_000m, 2)],
            ["return %", Pct(stats.TotalReturn)],
            ["per year %", Pct(stats.AnnualizedReturn)],
            ["TEDPIX %", Pct(stats.TedpixReturn)],
            ["average share %", Pct(stats.EqualWeightReturn)],
            ["in dollars %", Pct(stats.UsdReturn)],
            ["volatility %", Pct(stats.Volatility)],
            ["sharpe", Round(stats.Sharpe)],
            ["deepest fall %", Pct(stats.MaxDrawdown)],
            ["beta", Round(stats.Beta)],
            ["trades", stats.Trades],
            ["win rate %", Pct(stats.WinRate)],
            ["profit factor", Round(stats.ProfitFactor)],
            ["turnover / year", Round(stats.Turnover)],
            ["costs (B rial)", Math.Round(stats.CostsPaid / 1_000_000_000m, 2)],
            ["exposure %", Pct(stats.Exposure)],
            ["universe", result.Universe],
            ["risk on", result.RiskOn],
        ]);

        Console.WriteLine();
        Output.Table(["symbol", "shares", "value (B rial)", "weight %", "return %", "held", "rank"],
            result.Holdings.Select(h => (IReadOnlyList<object?>)[h.Symbol, Math.Round(h.Shares), Math.Round(h.Value / 1_000_000_000m, 2), Pct(h.Weight), Pct(h.UnrealizedReturn), h.SessionsHeld, h.Rank]));

        Console.WriteLine();
        Output.Table(["date", "side", "symbol", "shares", "price", "pnl %", "reason"],
            result.Trades.TakeLast(cli.GetInt("trades", 15)).Select(t => (IReadOnlyList<object?>)[t.Date, t.Side, t.Symbol, Math.Round(t.Shares), Math.Round(t.Price), Pct(t.RealizedReturn), t.Reason]));

        Console.WriteLine();
        Output.Table(["waiting", "symbol", "shares", "value (B rial)", "reason"],
            result.Planned.Select(p => (IReadOnlyList<object?>)[p.Side, p.Symbol, Math.Round(p.Shares), Math.Round(p.Value / 1_000_000_000m, 2), p.Reason]));

        Console.WriteLine();
        Output.Table(["factor", "chosen %", "used %", "mean IC", "months"],
            result.Factors.Select(f => (IReadOnlyList<object?>)[f.Factor, Pct(f.Chosen), Pct(f.Used), Round(f.MeanIc, 3), f.Months]));
        return 0;
    }

    private int Tune(CliArgs cli, SimulationSpec spec)
    {
        var objective = Enum.TryParse<TuningObjective>(cli.Get("objective"), ignoreCase: true, out var o) ? o : TuningObjective.Sharpe;
        var stopwatch = Stopwatch.StartNew();
        var tuning = simulator.Tune(spec, cli.GetInt("years", 3), objective);
        Console.WriteLine($"{tuning.From:yyyy-MM-dd} → {tuning.Split:yyyy-MM-dd} (in sample) → {tuning.To:yyyy-MM-dd} (out of sample), {tuning.Rows.Count} sets in {stopwatch.Elapsed.TotalSeconds:0.0} s; TEDPIX {Pct(tuning.TedpixIn)}% / {Pct(tuning.TedpixOut)}%");
        Output.Table(["positions", "every", "trailing %", "regime", "objective", "in %/yr", "in sharpe", "in fall %", "out %", "out sharpe", "out fall %", "trades"],
            tuning.Rows.Take(cli.GetInt("take", 15)).Select(r => (IReadOnlyList<object?>)[
                r.Settings.Positions, r.Settings.RebalanceSessions, Pct(r.Settings.TrailingStop), r.Settings.RegimeFilter, Round(r.Objective),
                Pct(r.InAnnualized), Round(r.InSharpe), Pct(r.InMaxDrawdown), Pct(r.OutReturn), Round(r.OutSharpe), Pct(r.OutMaxDrawdown), r.Trades]));
        return 0;
    }

    private static double? Pct(double? value) => value is { } v ? Math.Round(v * 100, 2) : null;

    private static double? Round(double? value, int digits = 2) => value is { } v ? Math.Round(v, digits) : null;
}
