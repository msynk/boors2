namespace Boors.Market.Simulation;

/// <summary>Performance statistics of a simulated equity curve, in the conventions of the reports (240 sessions a year).</summary>
internal static class SimulationStatistics
{
    private const double SessionsPerYear = SimulationPanel.Year;

    /// <summary>The headline figures of one stretch of an equity curve (the tuner's in-sample and out-of-sample parts).</summary>
    public readonly record struct SegmentStats(double Return, double? Annualized, double? Sharpe, double MaxDrawdown);

    public static SimulationStats Compute(IReadOnlyList<EquityPoint> curve, IReadOnlyList<SimulationTrade> trades, decimal capital, double cashYield)
    {
        if (curve.Count == 0 || capital <= 0)
            return SimulationStats.Empty;

        var last = curve[^1];
        var returns = DailyReturns(curve);
        var segment = Segment(curve, cashYield, capital);
        var sells = trades.Where(t => t.Side == OrderSide.Sell && t.RealizedPnl is not null).ToList();
        var wins = sells.Where(t => t.RealizedPnl > 0).ToList();
        var losses = sells.Where(t => t.RealizedPnl <= 0).ToList();
        var lossSum = -losses.Sum(t => t.RealizedPnl!.Value);
        var years = curve.Count / SessionsPerYear;
        var averageEquity = curve.Average(p => (double)p.Equity);

        // Beta to TEDPIX's daily changes, and alpha on returns over the cash hurdle (CAPM), where both are known.
        double? beta = null, alpha = null, correlation = null;
        var pairs = new List<(double Portfolio, double Market, double Hurdle)>();
        for (var i = 1; i < curve.Count; i++)
        {
            if (curve[i - 1].Equity > 0 && curve[i].Tedpix is { } m1 && curve[i - 1].Tedpix is { } m0 && m0 > 0)
                pairs.Add(((double)(curve[i].Equity / curve[i - 1].Equity) - 1, m1 / m0 - 1, Hurdle(curve[i - 1], curve[i], cashYield)));
        }

        if (pairs.Count >= 20)
        {
            var mp = pairs.Average(p => p.Portfolio);
            var mm = pairs.Average(p => p.Market);
            var covariance = pairs.Sum(p => (p.Portfolio - mp) * (p.Market - mm)) / (pairs.Count - 1);
            var variance = pairs.Sum(p => (p.Market - mm) * (p.Market - mm)) / (pairs.Count - 1);
            var portfolioVariance = pairs.Sum(p => (p.Portfolio - mp) * (p.Portfolio - mp)) / (pairs.Count - 1);
            if (variance > 0)
            {
                beta = covariance / variance;
                alpha = (pairs.Average(p => p.Portfolio - p.Hurdle) - beta.Value * pairs.Average(p => p.Market - p.Hurdle)) * SessionsPerYear;
                correlation = portfolioVariance > 0 ? covariance / Math.Sqrt(variance * portfolioVariance) : null;
            }
        }

        // Sortino: the mean return over the hurdle per unit of the shortfall below it.
        var excess = ExcessReturns(curve, cashYield);
        var shortfall = excess.Where(r => r < 0).ToList();
        double? sortino = shortfall.Count >= 5 && excess.Count >= 20
            ? excess.Average() / Math.Sqrt(shortfall.Sum(r => r * r) / excess.Count) * Math.Sqrt(SessionsPerYear)
            : null;

        var peak = curve.Max(p => p.Equity);
        var first = curve[0];
        double? Change(double? from, double? to) => from is > 0 && to is { } end ? end / from.Value - 1 : null;
        double? Back(int sessions) => curve.Count > sessions && curve[^(sessions + 1)].Equity > 0 ? (double)(last.Equity / curve[^(sessions + 1)].Equity) - 1 : null;
        var tedpix = Change(first.Tedpix, last.Tedpix);
        var usd = Change(first.Usd, last.Usd);

        return new SimulationStats
        {
            Sessions = curve.Count,
            Equity = last.Equity,
            Cash = last.Cash,
            Invested = last.Invested,
            TotalReturn = segment.Return,
            AnnualizedReturn = segment.Annualized,
            DayReturn = Back(1),
            WeekReturn = Back(5),
            MonthReturn = Back(20),
            Volatility = returns.Count >= 20 ? StandardDeviation(returns) * Math.Sqrt(SessionsPerYear) : null,
            Sharpe = segment.Sharpe,
            Sortino = sortino,
            MaxDrawdown = segment.MaxDrawdown,
            CurrentDrawdown = peak > 0 ? (double)(last.Equity / peak) - 1 : 0,
            Calmar = segment.Annualized is { } a && segment.MaxDrawdown < -0.001 ? a / -segment.MaxDrawdown : null,
            Beta = beta,
            Alpha = alpha,
            Correlation = correlation,
            TedpixReturn = tedpix,
            ExcessReturn = tedpix is { } t ? (1 + segment.Return) / (1 + t) - 1 : null,
            EqualWeightReturn = Change(first.EqualWeight, last.EqualWeight),
            UsdReturn = usd is { } u ? (1 + segment.Return) / (1 + u) - 1 : null,
            Trades = trades.Count,
            ClosedTrades = sells.Count,
            WinRate = sells.Count > 0 ? wins.Count / (double)sells.Count : null,
            ProfitFactor = lossSum > 0 ? (double)(wins.Sum(t => t.RealizedPnl!.Value) / lossSum) : null,
            AverageWin = wins.Count > 0 ? wins.Average(t => t.RealizedReturn ?? 0) : null,
            AverageLoss = losses.Count > 0 ? losses.Average(t => t.RealizedReturn ?? 0) : null,
            AverageHoldSessions = sells.Count > 0 ? sells.Average(t => t.SessionsHeld ?? 0) : null,
            Turnover = years > 0 && averageEquity > 0 ? (double)trades.Sum(t => t.Value) / averageEquity / Math.Max(years, 1.0 / 12) : null,
            CostsPaid = trades.Sum(t => t.Costs),
            RealizedPnl = sells.Sum(t => t.RealizedPnl!.Value),
            UnrealizedPnl = last.Equity - capital - sells.Sum(t => t.RealizedPnl!.Value),
            Exposure = curve.Average(p => p.Equity > 0 ? (double)(p.Invested / p.Equity) : 0),
            BestDay = returns.Count > 0 ? returns.Max() : null,
            WorstDay = returns.Count > 0 ? returns.Min() : null,
        };
    }

    /// <summary>Return, yearly return (from 60 sessions), Sharpe over the cash yield (from 20) and deepest fall of a stretch.</summary>
    public static SegmentStats Segment(IReadOnlyList<EquityPoint> curve, double cashYield, decimal? capital = null)
    {
        if (curve.Count == 0)
            return new SegmentStats(0, null, null, 0);
        var start = capital ?? curve[0].Equity;
        if (start <= 0)
            return new SegmentStats(0, null, null, 0);

        var total = (double)(curve[^1].Equity / start) - 1;
        var days = curve[^1].Date.DayNumber - curve[0].Date.DayNumber;
        double? annualized = curve.Count >= 60 && days > 0 && total > -1 ? Math.Pow(1 + total, 365.25 / days) - 1 : null;
        var excess = ExcessReturns(curve, cashYield);
        double? sharpe = null;
        if (excess.Count >= 20 && StandardDeviation(excess) is var deviation && deviation > 1e-12)
            sharpe = excess.Average() / deviation * Math.Sqrt(SessionsPerYear);

        decimal peak = start, deepest = 0;
        foreach (var point in curve)
        {
            peak = Math.Max(peak, point.Equity);
            if (peak > 0)
                deepest = Math.Min(deepest, point.Equity / peak - 1);
        }

        return new SegmentStats(total, annualized, sharpe, (double)deepest);
    }

    /// <summary>
    /// What idle cash earns between two sessions, by calendar days like the cash itself: an all-cash portfolio is then
    /// exactly at the hurdle (a Sharpe of zero), closures included.
    /// </summary>
    private static double Hurdle(EquityPoint from, EquityPoint to, double cashYield) =>
        Math.Pow(1 + cashYield, (to.Date.DayNumber - from.Date.DayNumber) / 365.0) - 1;

    /// <summary>Each session's return less the cash hurdle over the same days.</summary>
    private static List<double> ExcessReturns(IReadOnlyList<EquityPoint> curve, double cashYield)
    {
        var result = new List<double>(curve.Count);
        for (var i = 1; i < curve.Count; i++)
        {
            if (curve[i - 1].Equity > 0)
                result.Add((double)(curve[i].Equity / curve[i - 1].Equity) - 1 - Hurdle(curve[i - 1], curve[i], cashYield));
        }

        return result;
    }

    private static List<double> DailyReturns(IReadOnlyList<EquityPoint> curve)
    {
        var result = new List<double>(curve.Count);
        for (var i = 1; i < curve.Count; i++)
        {
            if (curve[i - 1].Equity > 0)
                result.Add((double)(curve[i].Equity / curve[i - 1].Equity) - 1);
        }

        return result;
    }

    private static double StandardDeviation(List<double> values)
    {
        if (values.Count < 2)
            return 0;
        var mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
    }
}
