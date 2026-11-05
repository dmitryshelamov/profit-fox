using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Application.IdeaTrades;

/// <summary>
///     Codes of trades by ideas in the API and in stored rows: statuses (<c>awaiting_entry</c>, <c>open</c>, <c>closed_at_target</c>,
///     <c>closed_at_stop</c>, <c>closed_manually</c>, <c>cancelled</c>, <c>failed</c>), order roles, legs and times in force.
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