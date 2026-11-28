using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.Services.Trades;

/// <summary>
///     The words of the states, reasons, sources and kinds of a trade (change take-idea-trades), as the store keeps them and
///     the API sends them.
/// </summary>
public static class TradeNames
{
    public static string Of(TradeSide side)
    {
        return side == TradeSide.Long ? "long" : "short";
    }

    public static TradeSide Side(string name)
    {
        return name switch
        {
            "long" => TradeSide.Long,
            "short" => TradeSide.Short,
            _ => throw new InvalidOperationException($"{name} is not a side.")
        };
    }

    public static string Of(TradeState state)
    {
        return state switch
        {
            TradeState.Pending => "pending",
            TradeState.Open => "open",
            TradeState.Closing => "closing",
            TradeState.Closed => "closed",
            TradeState.Cancelled => "cancelled",
            _ => "error"
        };
    }

    public static TradeState State(string name)
    {
        foreach (TradeState state in Enum.GetValues<TradeState>())
        {
            if (Of(state) == name)
            {
                return state;
            }
        }

        throw new InvalidOperationException($"{name} is not a state of a trade.");
    }

    public static string Of(TradeCloseReason reason)
    {
        return reason switch
        {
            TradeCloseReason.Target => "target",
            TradeCloseReason.Stop => "stop",
            TradeCloseReason.Time => "time",
            TradeCloseReason.Manual => "manual",
            TradeCloseReason.Halt => "halt",
            TradeCloseReason.NoStop => "no-stop",
            TradeCloseReason.Liquidation => "liquidation",
            TradeCloseReason.Adl => "adl",
            _ => "exchange"
        };
    }

    public static TradeCloseReason? Reason(string? name)
    {
        if (name is null)
        {
            return null;
        }

        foreach (TradeCloseReason reason in Enum.GetValues<TradeCloseReason>())
        {
            if (Of(reason) == name)
            {
                return reason;
            }
        }

        throw new InvalidOperationException($"{name} is not a reason of a close.");
    }

    public static string Of(StopSource source)
    {
        return source == StopSource.Idea ? "idea" : "limit";
    }

    public static StopSource Source(string name)
    {
        return name == "idea" ? StopSource.Idea : StopSource.Limit;
    }

    public static string Of(TradeOrderPurpose purpose)
    {
        return purpose == TradeOrderPurpose.Entry ? "entry" : "close";
    }

    public static TradeOrderPurpose Purpose(string name)
    {
        return name == "entry" ? TradeOrderPurpose.Entry : TradeOrderPurpose.Close;
    }

    public static string Of(TradeOrderStatus status)
    {
        return status.ToString().ToLowerInvariant();
    }

    public static TradeOrderStatus Status(string name)
    {
        return Enum.Parse<TradeOrderStatus>(name, ignoreCase: true);
    }

    public static string Of(FillKind kind)
    {
        return kind.ToString().ToLowerInvariant();
    }

    public static FillKind Kind(string name)
    {
        return Enum.Parse<FillKind>(name, ignoreCase: true);
    }
}
