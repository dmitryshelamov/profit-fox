using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Services.Strategies.Templates;

/// <summary>
///     One trade between three prices: a limit entry, a limit exit at the target while the close has not reached the stop, and a market
///     exit at the stop. The positions the bot closed before count, so one bot makes one trade.
/// </summary>
internal static class LevelTemplates
{
    public static StrategyDefinition LevelTrade()
    {
        return Trade(StrategyDirection.Long, 100m, 95m, 110m);
    }

    public static StrategyDefinition LevelTradeShort()
    {
        return Trade(StrategyDirection.Short, 100m, 105m, 90m);
    }

    private static StrategyDefinition Trade(StrategyDirection direction, decimal entryPrice, decimal stopPrice, decimal targetPrice)
    {
        bool isLong = direction == StrategyDirection.Long;
        OrderSide entrySide = isLong ? OrderSide.Buy : OrderSide.Sell;
        OrderSide exitSide = isLong ? OrderSide.Sell : OrderSide.Buy;

        return new StrategyDefinition(
            direction,
            [
                TemplateParts.Number("amount", 100m),
                TemplateParts.Number("entryPrice", entryPrice),
                TemplateParts.Number("stopPrice", stopPrice),
                TemplateParts.Number("targetPrice", targetPrice)
            ],
            new GraphSketch()
                .Node("params", SimpleNodeKind.Parameters)
                .Node("candle", SimpleNodeKind.Candle)
                .Node("position", SimpleNodeKind.Position)
                .Node("flat", SimpleNodeKind.Not)
                .Node("noTradeYet", SimpleNodeKind.Compare, TemplateParts.Compare(CompareOperation.Less), TemplateParts.Constant("b", 1m))
                .Node("canEnter", SimpleNodeKind.And)
                .Node("entry", SimpleNodeKind.HoldLimit, new NodeSettings(Side: entrySide, SizeMode: OrderSizeMode.Amount))
                .Node("beforeStop", SimpleNodeKind.Compare, TemplateParts.Compare(isLong ? CompareOperation.Greater : CompareOperation.Less))
                .Node("holdTarget", SimpleNodeKind.And)
                .Node("target", SimpleNodeKind.HoldLimit, new NodeSettings(Side: exitSide, SizeMode: OrderSizeMode.Quantity))
                .Node("stopHit", SimpleNodeKind.Compare, TemplateParts.Compare(isLong ? CompareOperation.LessOrEqual : CompareOperation.GreaterOrEqual))
                .Node("exit", SimpleNodeKind.ClosePosition)
                .Wire("position.hasPosition", "flat.value")
                .Wire("position.closedPositions", "noTradeYet.a")
                .Wire("flat.result", "canEnter.a")
                .Wire("noTradeYet.result", "canEnter.b")
                .Wire("canEnter.result", "entry.when")
                .Wire("params.entryPrice", "entry.price")
                .Wire("params.amount", "entry.size")
                .Wire("candle.close", "beforeStop.a")
                .Wire("params.stopPrice", "beforeStop.b")
                .Wire("position.hasPosition", "holdTarget.a")
                .Wire("beforeStop.result", "holdTarget.b")
                .Wire("holdTarget.result", "target.when")
                .Wire("params.targetPrice", "target.price")
                .Wire("position.quantity", "target.size")
                .Wire("candle.close", "stopHit.a")
                .Wire("params.stopPrice", "stopHit.b")
                .Wire("stopHit.result", "exit.when")
                .Build());
    }
}