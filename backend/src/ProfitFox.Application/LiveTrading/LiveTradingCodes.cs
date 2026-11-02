using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Application.LiveTrading;

/// <summary>Codes of live trading in the API and in stored rows: <c>running</c>, <c>pending_submit</c>, <c>quote</c> and so on.</summary>
public static class LiveTradingCodes
{
    private static readonly Dictionary<string, LiveBotStatus> StatusesByCode = new Dictionary<string, LiveBotStatus>(StringComparer.Ordinal)
    {
        ["stopped"] = LiveBotStatus.Stopped,
        ["running"] = LiveBotStatus.Running,
        ["paused"] = LiveBotStatus.Paused,
        ["stopping"] = LiveBotStatus.Stopping,
        ["failed"] = LiveBotStatus.Failed
    };

    private static readonly Dictionary<string, LiveOrderStatus> OrderStatusesByCode = new Dictionary<string, LiveOrderStatus>(StringComparer.Ordinal)
    {
        ["pending_submit"] = LiveOrderStatus.PendingSubmit,
        ["active"] = LiveOrderStatus.Active,
        ["pending_cancel"] = LiveOrderStatus.PendingCancel,
        ["cancel_sent"] = LiveOrderStatus.CancelSent,
        ["filled"] = LiveOrderStatus.Filled,
        ["cancelled"] = LiveOrderStatus.Cancelled,
        ["rejected"] = LiveOrderStatus.Rejected
    };

    private static readonly Dictionary<string, FeeAsset> FeeAssetsByCode = new Dictionary<string, FeeAsset>(StringComparer.Ordinal)
    {
        ["quote"] = FeeAsset.Quote,
        ["base"] = FeeAsset.Base
    };

    public static bool TryParseStatus(string code, out LiveBotStatus status)
    {
        return StatusesByCode.TryGetValue(code, out status);
    }

    public static bool TryParseOrderStatus(string code, out LiveOrderStatus status)
    {
        return OrderStatusesByCode.TryGetValue(code, out status);
    }

    public static bool TryParseFeeAsset(string code, out FeeAsset asset)
    {
        return FeeAssetsByCode.TryGetValue(code, out asset);
    }

    public static string ToCode(this LiveBotStatus status)
    {
        return StatusesByCode.Single(pair => pair.Value == status).Key;
    }

    public static string ToCode(this LiveOrderStatus status)
    {
        return OrderStatusesByCode.Single(pair => pair.Value == status).Key;
    }

    public static string ToCode(this FeeAsset asset)
    {
        return FeeAssetsByCode.Single(pair => pair.Value == asset).Key;
    }
}