using Boors.Desktop.Infrastructure;
using Boors.Market.Services.Contracts;

namespace Boors.Desktop.ViewModels;

/// <summary>Words for the simulation engine's keys: modes, presets, factors, trade reasons, journal events and order states.</summary>
public static class SimulationText
{
    public const string Automatic = "Automatic";
    public const string Manual = "Manual";

    /// <summary>The factor keys in the order the editor shows them, with their family.</summary>
    public static IReadOnlyList<(string Key, string Family)> Factors { get; } =
    [
        ("Momentum", "technical"), ("Trend", "technical"), ("High52Week", "technical"), ("Breakout", "technical"),
        ("ShortReversal", "technical"), ("RsiPullback", "technical"), ("Macd", "technical"), ("LowVolatility", "technical"),
        ("EarningsYield", "fundamental"), ("SectorValue", "fundamental"), ("EpsRevision", "fundamental"),
        ("IndividualFlow", "flow"), ("BuyerPower", "flow"),
        ("Consistency", "quality"), ("RiskAdjusted", "quality"),
    ];

    public static IReadOnlyList<string> Presets { get; } = ["balanced", "adaptive", "trend", "value", "flow", "swing"];

    public static string Mode(string? mode) => mode == Manual ? Loc.T("Manual") : Loc.T("Automatic");

    public static string Preset(string? key) => key switch
    {
        "balanced" => Loc.T("Balanced multi-factor"),
        "adaptive" => Loc.T("Adaptive (learns what works)"),
        "trend" => Loc.T("Trend following"),
        "value" => Loc.T("Value (fundamental)"),
        "flow" => Loc.T("Smart-money flow"),
        "swing" => Loc.T("Swing (pullbacks)"),
        null or "" or "custom" => Loc.T("Custom"),
        _ => key,
    };

    public static string PresetDescription(string? key) => key switch
    {
        "balanced" => Loc.T("Momentum, trend, closeness to the 52-week high, return per risk, low volatility, individual inflow and earnings together; weekly ranking, stops at 12% and a 15% trailing stop."),
        "adaptive" => Loc.T("Every factor to begin with; at each ranking it moves weight towards the factors whose ranking actually predicted the next month's returns (only months already over count), keeping half of the starting weights."),
        "trend" => Loc.T("Strong 12-month trends near their highs and breaking out; cut losers early with a 10% stop and a 15% trailing stop."),
        "value" => Loc.T("Low P/E for the market and for the sector, rising EPS, calm and consistent shares; monthly ranking. Needs the EPS of end-of-day market-watch snapshots — until enough are collected it ranks on the other factors."),
        "flow" => Loc.T("Where individual money is coming in and buyers outweigh sellers, in an uptrend; weekly ranking, tight stops. Fast-moving: watch the cost of the turnover."),
        "swing" => Loc.T("Pullbacks inside uptrends (low RSI above the 200-session average) and last month's laggards; take profit at 10%, stop at 7%, sell after 30 sessions."),
        _ => Loc.T("Your own mix of factors and rules."),
    };

    public static string Factor(string factor) => factor switch
    {
        "Momentum" => Loc.T("Momentum (12 months, skipping the last)"),
        "Trend" => Loc.T("Trend (above the 200-session average)"),
        "High52Week" => Loc.T("Near the 52-week high"),
        "Breakout" => Loc.T("Breakout (55-session high)"),
        "ShortReversal" => Loc.T("Short-term reversal (last month's laggards)"),
        "RsiPullback" => Loc.T("Pullback in an uptrend (RSI)"),
        "Macd" => Loc.T("MACD turning up"),
        "LowVolatility" => Loc.T("Low volatility"),
        "EarningsYield" => Loc.T("Earnings yield (low P/E)"),
        "SectorValue" => Loc.T("Cheap for its sector"),
        "EpsRevision" => Loc.T("EPS revisions"),
        "IndividualFlow" => Loc.T("Individual money inflow"),
        "BuyerPower" => Loc.T("Buyer power"),
        "Consistency" => Loc.T("Consistency (up months)"),
        "RiskAdjusted" => Loc.T("Return per unit of risk"),
        _ => factor,
    };

    public static string Family(string family) => family switch
    {
        "technical" => Loc.T("Technical"),
        "fundamental" => Loc.T("Fundamental"),
        "flow" => Loc.T("Money flow"),
        "quality" => Loc.T("Quality"),
        _ => family,
    };

    public static string Side(string side) => side == "Sell" ? Loc.T("Sell") : Loc.T("Buy");

    public static Tone SideTone(string side) => side == "Sell" ? Tone.Down : Tone.Up;

    // For x:Bind in templates (one call, exact argument types).
    public static Microsoft.UI.Xaml.Media.Brush SideBrush(string side) => Palette.ToneBrush(SideTone(side));

    public static Microsoft.UI.Xaml.Media.Brush ToneBrush(Tone tone) => Palette.ToneBrush(tone);

    public static string Million(decimal rials) => Fmt.MillionToman(rials);

    public static string Day(DateOnly date) => Fmt.Date(date);

    public static string WeightText(double weight) => weight > 0 ? Fmt.Num(weight, "0.#") : Fmt.Dash;

    /// <summary>The instrument classes a simulation trades: primary listings of shares (the engine's universe).</summary>
    public static bool IsSimulated(string instrumentClass) => instrumentClass is "tse_stock" or "ifb_stock" or "ifb_base_yellow" or "ifb_base_orange" or "ifb_base_red";

    public static string Reason(string reason) => reason switch
    {
        "entry" => Loc.T("New position"),
        "resize" => Loc.T("Back to its target size"),
        "rank-exit" => Loc.T("Dropped in the ranking"),
        "stop-loss" => Loc.T("Stop loss"),
        "trailing-stop" => Loc.T("Trailing stop"),
        "take-profit" => Loc.T("Take profit"),
        "time-exit" => Loc.T("Held long enough"),
        "manual" => Loc.T("Your order"),
        _ => reason,
    };

    public static string OrderState(string state) => state switch
    {
        "pending" => Loc.T("Waiting to fill"),
        "filled" => Loc.T("Filled"),
        "partial" => Loc.T("Partly filled"),
        "expired" => Loc.T("Expired"),
        "cancelled" => Loc.T("Cancelled"),
        "rejected" => Loc.T("Rejected"),
        _ => state,
    };

    public static (string Text, Tone Tone) OrderStateTag(string state) => state switch
    {
        "filled" => ($"✓ {OrderState(state)}", Tone.Neutral),
        "pending" => ($"… {OrderState(state)}", Tone.Accent),
        "partial" => ($"◐ {OrderState(state)}", Tone.Accent),
        "rejected" => ($"✕ {OrderState(state)}", Tone.Neutral),
        _ => ($"○ {OrderState(state)}", Tone.Muted),
    };

    /// <summary>
    /// A journal line as a sentence (the values depend on its kind; see SimulationEvents in the engine). In English a
    /// left-to-right mark leads, so a line that starts with a Persian symbol still reads left to right.
    /// </summary>
    public static string Event(SimulationEventDto e) => Loc.IsPersian ? Sentence(e) : "\u200E" + Sentence(e);

    private static string Sentence(SimulationEventDto e)
    {
        var symbol = e.Symbol ?? string.Empty;
        return e.Kind switch
        {
            "start" => Loc.F("Started with {0} million toman", Fmt.MillionToman((decimal?)e.Value)),
            "rebalance" => Loc.F("Ranking: {0} positions, {1} of the portfolio to invest", Fmt.Number((int)(e.Value ?? 0)), Fmt.Percent(e.Value2, 0)),
            "risk-off" => Loc.F("TEDPIX fell below its average ({0}): investing less", Fmt.FractionChange(e.Value)),
            "risk-on" => Loc.F("TEDPIX is back above its average ({0}): fully invested", Fmt.FractionChange(e.Value)),
            "stop-loss" => Loc.F("{0}: stop loss ({1} from the entry)", symbol, Fmt.FractionChange(e.Value)),
            "trailing-stop" => Loc.F("{0}: trailing stop ({1} from the entry)", symbol, Fmt.FractionChange(e.Value)),
            "take-profit" => Loc.F("{0}: profit taken ({1} from the entry)", symbol, Fmt.FractionChange(e.Value)),
            "time-exit" => Loc.F("{0}: held for the longest allowed ({1} from the entry)", symbol, Fmt.FractionChange(e.Value)),
            "rank-exit" => e.Value is { } rank ? Loc.F("{0} dropped to rank {1}: sold", symbol, Fmt.Number((int)rank)) : Loc.F("{0} is no longer ranked: sold", symbol),
            "blocked-buy-queue" => Loc.F("{0}: the buy waits — locked in a buy queue", symbol),
            "blocked-sell-queue" => Loc.F("{0}: the sell waits — locked in a sell queue", symbol),
            "blocked-no-trade" => Loc.F("{0}: the order waits — halted or not traded", symbol),
            "blocked-limit" => Loc.F("{0}: the order waits — the price did not reach its limit", symbol),
            "expired" => Loc.F("{0}: the order expired", symbol),
            "no-cash" => Loc.F("{0}: the buy waits for cash (sale proceeds settle two sessions later)", symbol),
            "not-held" => Loc.F("{0}: nothing to sell", symbol),
            "not-tradable" => Loc.T("Only shares of TSE and IFB are simulated (not funds, bonds or rights)"),
            "too-small" => Loc.F("{0}: the amount is less than one share", symbol),
            "partial-fill" => Loc.F("{0}: partly filled — the rest waits for the next session", symbol),
            "corporate-action" => Loc.F("{0}: capital increase or dividend, shares adjusted (× {1})", symbol, Fmt.Num(1 / (e.Value ?? 1), "N3")),
            "settings-changed" => Loc.T("New parameters apply from this session"),
            "end" => Loc.T("Tracking stopped"),
            _ => e.Kind,
        };
    }

    public static Tone EventTone(string kind) => kind switch
    {
        "stop-loss" or "trailing-stop" or "risk-off" => Tone.Down,
        "take-profit" or "risk-on" => Tone.Up,
        "blocked-buy-queue" or "blocked-sell-queue" or "blocked-no-trade" or "blocked-limit" or "no-cash" or "partial-fill" or "expired" or "not-held" or "not-tradable" or "too-small" => Tone.Accent,
        _ => Tone.Neutral,
    };
}
