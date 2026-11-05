using Boors.Market.Services.Contracts;
using Boors.Market.Simulation;

namespace Boors.Market.Services.Local;

/// <summary>
/// Simulation DTOs ↔ Boors.Market.Simulation. Requests are checked here: an unknown mode, side, sizing, fill or factor
/// name is a 400, and every number is clamped to a range the engine can work with.
/// </summary>
public static class SimulationMapping
{
    public static SimulationSpec ToDomain(this SimulationSpecDto dto)
    {
        if (dto.Capital <= 0)
            throw MarketApiException.BadRequest("A simulation needs a positive starting capital.");
        if (dto.Revisions is not { Count: > 0 })
            throw MarketApiException.BadRequest("A simulation needs at least one set of parameters.");
        if (dto.Orders?.FirstOrDefault(o => o.Value is not null && o.Shares is null && Parse<OrderSide>(o.Side, "side") == OrderSide.Sell) is { } sellByValue)
            throw MarketApiException.BadRequest($"Sell order '{sellByValue.Id}' gives a value: a sell takes a number of shares, or neither for everything.");

        return new SimulationSpec
        {
            Id = string.IsNullOrWhiteSpace(dto.Id) ? "simulation" : dto.Id,
            Mode = Parse<SimulationMode>(dto.Mode, "mode"),
            Capital = dto.Capital,
            Start = dto.Start,
            End = dto.End,
            Revisions = [.. dto.Revisions.OrderBy(r => r.From).Select(r => new SettingsRevision(r.From, r.Settings.ToDomain()))],
            Orders = [.. (dto.Orders ?? []).Select(o => new ManualOrder
            {
                Id = o.Id,
                PlacedOn = o.PlacedOn,
                InsCode = o.InsCode,
                Side = Parse<OrderSide>(o.Side, "side"),
                Value = o.Value is > 0 ? o.Value : null,
                // Only a sell with no amount means "everything": zero shares stays a sell of nothing.
                Shares = o.Shares is > 0 || Parse<OrderSide>(o.Side, "side") == OrderSide.Sell ? o.Shares : null,
                LimitPrice = o.LimitPrice is > 0 ? o.LimitPrice : null,
                GoodForSessions = Math.Clamp(o.GoodForSessions, 1, 60),
                CancelledOn = o.CancelledOn,
            })],
        };
    }

    public static SimulationSettings ToDomain(this SimulationSettingsDto s) => new()
    {
        MinValue = Math.Max(0, s.MinValue),
        MinHistorySessions = Math.Clamp(s.MinHistorySessions, 0, 2400),
        Positions = Math.Clamp(s.Positions, 1, 50),
        MaxWeight = Math.Clamp(s.MaxWeight, 0.01, 1),
        PerSector = Math.Clamp(s.PerSector, 0, 50),
        Sizing = Parse<SizingMethod>(s.Sizing, "sizing"),
        CashReserve = Math.Clamp(s.CashReserve, 0, 0.9),
        RebalanceSessions = Math.Clamp(s.RebalanceSessions, 1, 240),
        HoldPercentile = Math.Clamp(s.HoldPercentile, 0, 1),
        MinHoldSessions = Math.Clamp(s.MinHoldSessions, 0, 240),
        RebalanceBand = Math.Clamp(s.RebalanceBand, 0, 1),
        // Keys are case-insensitive ("momentum" and "Momentum" are one factor; the larger weight counts).
        Weights = s.Weights.Where(w => w.Value > 0).GroupBy(w => Parse<FactorKind>(w.Key, "factor")).ToDictionary(g => g.Key, g => Math.Min(g.Max(w => w.Value), 10)),
        Adaptive = s.Adaptive,
        AdaptiveLookbackSessions = Math.Clamp(s.AdaptiveLookbackSessions, 120, 2400),
        AdaptiveShrink = Math.Clamp(s.AdaptiveShrink, 0, 1),
        StopLoss = Math.Clamp(s.StopLoss, 0, 0.9),
        TrailingStop = Math.Clamp(s.TrailingStop, 0, 0.9),
        TakeProfit = Math.Clamp(s.TakeProfit, 0, 10),
        MaxHoldSessions = Math.Clamp(s.MaxHoldSessions, 0, 2400),
        CooldownSessions = Math.Clamp(s.CooldownSessions, 0, 240),
        RegimeFilter = s.RegimeFilter,
        RegimeSessions = Math.Clamp(s.RegimeSessions, 2, 500),
        DefensiveExposure = Math.Clamp(s.DefensiveExposure, 0, 1),
        TargetVolatility = Math.Clamp(s.TargetVolatility, 0, 2),
        Fill = Parse<FillPrice>(s.Fill, "fill"),
        SlippageBps = Math.Clamp(s.SlippageBps, 0, 500),
        ParticipationCap = Math.Clamp(s.ParticipationCap, 0, 1),
        Brokerage = s.Brokerage is { } b ? Math.Clamp(b, 0, 0.01m) : null,
        InstantSaleProceeds = s.InstantSaleProceeds,
        CashYield = Math.Clamp(s.CashYield, -0.5, 3),
        Excluded = s.Excluded.ToHashSet(),
    };

    public static SimulationSettingsDto ToDto(this SimulationSettings s) => new()
    {
        MinValue = s.MinValue,
        MinHistorySessions = s.MinHistorySessions,
        Positions = s.Positions,
        MaxWeight = s.MaxWeight,
        PerSector = s.PerSector,
        Sizing = s.Sizing.ToString(),
        CashReserve = s.CashReserve,
        RebalanceSessions = s.RebalanceSessions,
        HoldPercentile = s.HoldPercentile,
        MinHoldSessions = s.MinHoldSessions,
        RebalanceBand = s.RebalanceBand,
        Weights = s.Weights.Where(w => w.Value > 0).OrderBy(w => w.Key).ToDictionary(w => w.Key.ToString(), w => w.Value),
        Adaptive = s.Adaptive,
        AdaptiveLookbackSessions = s.AdaptiveLookbackSessions,
        AdaptiveShrink = s.AdaptiveShrink,
        StopLoss = s.StopLoss,
        TrailingStop = s.TrailingStop,
        TakeProfit = s.TakeProfit,
        MaxHoldSessions = s.MaxHoldSessions,
        CooldownSessions = s.CooldownSessions,
        RegimeFilter = s.RegimeFilter,
        RegimeSessions = s.RegimeSessions,
        DefensiveExposure = s.DefensiveExposure,
        TargetVolatility = s.TargetVolatility,
        Fill = s.Fill.ToString(),
        SlippageBps = s.SlippageBps,
        ParticipationCap = s.ParticipationCap,
        Brokerage = s.Brokerage,
        InstantSaleProceeds = s.InstantSaleProceeds,
        CashYield = s.CashYield,
        Excluded = [.. s.Excluded.Order()],
    };

    public static SimulationResultDto ToDto(this SimulationResult r) => new(
        r.Id,
        r.Start,
        r.AsOf,
        r.Latest,
        r.Stats.ToDto(),
        [.. r.Equity.Select(p => new SimulationPointDto(p.Date, p.Equity, p.Cash, p.Invested, p.Tedpix, p.EqualWeight, p.Usd))],
        [.. r.Holdings.Select(h => new SimulationHoldingDto(
            h.InsCode, h.Symbol, h.Name, h.SectorName, h.Shares, h.AverageCost, h.LastPrice, h.Value, h.Weight, h.UnrealizedPnl, h.UnrealizedReturn,
            h.DayChange, h.OpenedOn, h.SessionsHeld, h.StopPrice, h.DayStatus, h.Score, h.Rank))],
        [.. r.Trades.Select(t => new SimulationTradeDto(
            t.Date, t.InsCode, t.Symbol, t.Side.ToString(), t.Shares, t.Price, t.Value, t.Costs, t.RealizedPnl, t.RealizedReturn, t.SessionsHeld, t.Reason, t.OrderId))],
        [.. r.Journal.Select(j => new SimulationEventDto(j.Date, j.Kind, j.InsCode, j.Symbol, j.Value, j.Value2))],
        [.. r.Planned.Select(ToDto)],
        [.. r.Suggestions.Select(ToDto)],
        [.. r.Orders.Select(o => new SimulationOrderStatusDto(o.OrderId, o.State, o.FilledShares, o.AveragePrice, o.LastFillOn, o.Reason))],
        [.. r.Factors.Select(f => new SimulationFactorDto(f.Factor.ToString(), FactorFamilies.Of(f.Factor), f.Chosen, f.Used, f.MeanIc, f.Months, f.Coverage))],
        r.RiskOn,
        r.TargetExposure,
        r.NextRebalanceIn,
        r.Universe,
        r.FundamentalsSince);

    public static SimulationStatsDto ToDto(this SimulationStats s) => new(
        s.Sessions, s.Equity, s.Cash, s.Invested, s.TotalReturn, s.AnnualizedReturn, s.DayReturn, s.WeekReturn, s.MonthReturn, s.Volatility, s.Sharpe,
        s.Sortino, s.MaxDrawdown, s.CurrentDrawdown, s.Calmar, s.Beta, s.Alpha, s.Correlation, s.TedpixReturn, s.ExcessReturn, s.EqualWeightReturn,
        s.UsdReturn, s.Trades, s.ClosedTrades, s.WinRate, s.ProfitFactor, s.AverageWin, s.AverageLoss, s.AverageHoldSessions, s.Turnover, s.CostsPaid,
        s.RealizedPnl, s.UnrealizedPnl, s.Exposure, s.BestDay, s.WorstDay);

    public static SimulationOrderPlanDto ToDto(this PlannedOrder p) =>
        new(p.InsCode, p.Symbol, p.Name, p.Side.ToString(), p.Shares, p.Value, p.Price, p.Reason, p.Score, p.Rank, p.OrderId);

    public static SimulationTuningDto ToDto(this SimulationTuning t) => new(
        t.From, t.Split, t.To, t.Objective.ToString(), t.TedpixIn, t.TedpixOut,
        [.. t.Rows.Select(r => new SimulationTuneRowDto(
            r.Settings.ToDto(), r.Objective, r.InReturn, r.InAnnualized, r.InSharpe, r.InMaxDrawdown, r.OutReturn, r.OutAnnualized, r.OutSharpe,
            r.OutMaxDrawdown, r.Trades, r.Turnover))]);

    private static T Parse<T>(string? value, string what) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw MarketApiException.BadRequest($"Unknown {what} '{value}' (expected {string.Join(", ", Enum.GetNames<T>())}).");
}
