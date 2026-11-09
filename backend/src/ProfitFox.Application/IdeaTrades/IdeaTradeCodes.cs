using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Application.IdeaTrades;

/// <summary>
///     Codes of trades by ideas in the API and in stored rows: statuses (<c>awaiting_entry</c>, <c>open</c>, <c>closed_at_target</c>,
///     <c>closed_at_stop</c>, <c>closed_manually</c>, <c>closed_at_expiry</c>, <c>liquidated</c>, <c>closed_by_adl</c>, <c>cancelled</c>,
///     <c>failed</c>), order roles, legs and times in force.
/// </summary>
public static class IdeaTradeCodes
{
    private static readonly Dictionary<string, IdeaTradeStatus> StatusesByCode = new Dictionary<string, IdeaTradeStatus>(StringComparer.Ordinal)
    {
        ["awaiting_entry"] = IdeaTradeStatus.AwaitingEntry,
        ["open"] = IdeaTradeStatus.Open,
        ["closed_at_target"] = IdeaTradeStatus.ClosedAtTarget,
        ["closed_at_stop"] = IdeaTradeStatus.ClosedAtStop,
        ["closed_manually"] = IdeaTradeStatus.ClosedManually,
        ["closed_at_expiry"] = IdeaTradeStatus.ClosedAtExpiry,
        ["liquidated"] = IdeaTradeStatus.Liquidated,
        ["closed_by_adl"] = IdeaTradeStatus.ClosedByAdl,
        ["cancelled"] = IdeaTradeStatus.Cancelled,
        ["failed"] = IdeaTradeStatus.Failed
    };

    private static readonly Dictionary<string, IdeaTradeOrderRole> RolesByCode = new Dictionary<string, IdeaTradeOrderRole>(StringComparer.Ordinal)
    {
        ["entry"] = IdeaTradeOrderRole.Entry,
        ["close"] = IdeaTradeOrderRole.Close
    };

    private static readonly Dictionary<string, IdeaTradeLeg> LegsByCode = new Dictionary<string, IdeaTradeLeg>(StringComparer.Ordinal)
    {
        ["take_profit"] = IdeaTradeLeg.TakeProfit,
        ["stop_loss"] = IdeaTradeLeg.StopLoss,
        ["manual"] = IdeaTradeLeg.Manual
    };

    private static readonly Dictionary<string, TimeInForce> TimesInForceByCode = new Dictionary<string, TimeInForce>(StringComparer.Ordinal)
    {
        ["gtc"] = TimeInForce.GoodTilCancelled,
        ["ioc"] = TimeInForce.ImmediateOrCancel
    };

    private static readonly Dictionary<IdeaTradeRefusal, string> RefusalsByCode = new Dictionary<IdeaTradeRefusal, string>
    {
        [IdeaTradeRefusal.IdeaNotOpen] = "idea_not_open",
        [IdeaTradeRefusal.PriceBeyondStop] = "price_beyond_stop",
        [IdeaTradeRefusal.PriceBeyondTarget] = "price_beyond_target",
        [IdeaTradeRefusal.RiskRewardTooLow] = "risk_reward_too_low",
        [IdeaTradeRefusal.AmountAboveMax] = "amount_above_max",
        [IdeaTradeRefusal.TooManyOpenTrades] = "too_many_open_trades",
        [IdeaTradeRefusal.TotalRiskExceeded] = "total_risk_exceeded",
        [IdeaTradeRefusal.OppositeSideOpen] = "opposite_side_open",
        [IdeaTradeRefusal.NotEnoughMoney] = "not_enough_money",
        [IdeaTradeRefusal.QuantityBelowMinimum] = "quantity_below_minimum",
        [IdeaTradeRefusal.AmountBelowMinimum] = "amount_below_minimum",
        [IdeaTradeRefusal.NoKey] = "no_key",
        [IdeaTradeRefusal.CategoryNotTraded] = "category_not_traded",
        [IdeaTradeRefusal.HedgeMode] = "hedge_mode",
        [IdeaTradeRefusal.StopEntryNotSupported] = "stop_entry_not_supported",
        [IdeaTradeRefusal.StopTooFarForLeverage] = "stop_too_far_for_leverage",
        [IdeaTradeRefusal.LeverageDiffers] = "leverage_differs",
        [IdeaTradeRefusal.LeverageNotSet] = "leverage_not_set",
        [IdeaTradeRefusal.DailyStop] = "daily_stop",
        [IdeaTradeRefusal.TooManySameDirection] = "too_many_same_direction",
        [IdeaTradeRefusal.PairTaken] = "pair_taken",
        [IdeaTradeRefusal.Late] = "late",
        [IdeaTradeRefusal.AutoOff] = "auto_off",
        [IdeaTradeRefusal.RulesUnknown] = "rules_unknown",
        [IdeaTradeRefusal.EntryError] = "entry_error",
        [IdeaTradeRefusal.BudgetTaken] = "budget_taken",
        [IdeaTradeRefusal.AutosBudgetTaken] = "autos_budget_taken",
        [IdeaTradeRefusal.MinuteLimit] = "minute_limit",
        [IdeaTradeRefusal.SideLimit] = "side_limit"
    };

    public static bool TryParseStatus(string code, out IdeaTradeStatus status)
    {
        return StatusesByCode.TryGetValue(code, out status);
    }

    public static bool TryParseRole(string code, out IdeaTradeOrderRole role)
    {
        return RolesByCode.TryGetValue(code, out role);
    }

    public static bool TryParseLeg(string code, out IdeaTradeLeg leg)
    {
        return LegsByCode.TryGetValue(code, out leg);
    }

    public static bool TryParseTimeInForce(string code, out TimeInForce timeInForce)
    {
        return TimesInForceByCode.TryGetValue(code, out timeInForce);
    }

    public static bool TryParseRefusal(string code, out IdeaTradeRefusal refusal)
    {
        foreach ((IdeaTradeRefusal key, string value) in RefusalsByCode)
        {
            if (value == code)
            {
                refusal = key;

                return true;
            }
        }

        refusal = default;

        return false;
    }

    public static string ToCode(this IdeaTradeRefusal refusal)
    {
        return RefusalsByCode[refusal];
    }

    public static string ToCode(this IdeaTradeStatus status)
    {
        return StatusesByCode.Single(pair => pair.Value == status).Key;
    }

    public static string ToCode(this IdeaTradeOrderRole role)
    {
        return RolesByCode.Single(pair => pair.Value == role).Key;
    }

    public static string ToCode(this IdeaTradeLeg leg)
    {
        return LegsByCode.Single(pair => pair.Value == leg).Key;
    }

    public static string ToCode(this TimeInForce timeInForce)
    {
        return TimesInForceByCode.Single(pair => pair.Value == timeInForce).Key;
    }
}