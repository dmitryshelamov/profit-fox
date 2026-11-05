using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Services.Strategies.Templates;

/// <summary>The reference strategies EMA Short, Ladder and DCA as templates: their fixtures in the tests are the same graphs.</summary>
internal static class ReferenceTemplates
{
    public static StrategyDefinition EmaShort()
    {
        return new StrategyDefinition(
            StrategyDirection.Short,
            [TemplateParts.Period("fast", 12m, 2m, 200m), TemplateParts.Period("slow", 26m, 2m, 400m), TemplateParts.Number("amount", 100m)],
            new GraphSketch()
                .Node("params", SimpleNodeKind.Parameters)
                .Node("candle", SimpleNodeKind.Candle)
                .Node("position", SimpleNodeKind.Position)
                .Composite("cross", BuiltInNode.EmaCross)
                .Node("flat", SimpleNodeKind.Not)
                .Node("entry", SimpleNodeKind.And)
                .Node("enter", SimpleNodeKind.MarketOrder, new NodeSettings(Side: OrderSide.Sell, SizeMode: OrderSizeMode.Amount))
                .Node("exit", SimpleNodeKind.ClosePosition)
                .Wire("candle.close", "cross.source")
                .Wire("params.fast", "cross.fast")
                .Wire("params.slow", "cross.slow")
                .Wire("position.hasPosition", "flat.value")
                .Wire("cross.down", "entry.a")
                .Wire("flat.result", "entry.b")
                .Wire("entry.result", "enter.when")
                .Wire("params.amount", "enter.size")
                .Wire("cross.up", "exit.when")
                .Build());
    }

    public static StrategyDefinition Ladder()
    {
        return new StrategyDefinition(StrategyDirection.Long, LadderParameters(), LadderSketch().Build());
    }

    /// <summary>The ladder that averages down from the average of the closes, not from its last entry.</summary>
    public static StrategyDefinition LadderSma()
    {
        return new StrategyDefinition(
            StrategyDirection.Long,
            [.. LadderParameters(), TemplateParts.Period("smaPeriod", 50m)],
            LadderSketch(buyBasis: "average.value")
                .Node("candle", SimpleNodeKind.Candle)
                .Node("average", SimpleNodeKind.Sma)
                .Wire("candle.close", "average.source")
                .Wire("params.smaPeriod", "average.period")
                .Build());
    }

    /// <summary>The ladder that stops out below its average price and starts no position once the closed ones made the profit target.</summary>
    public static StrategyDefinition LadderPlus()
    {
        return new StrategyDefinition(
            StrategyDirection.Long,
            [.. LadderParameters(), TemplateParts.Share("stopLoss", 0.2m), TemplateParts.Number("profitTarget", 1000m)],
            LadderSketch(entryGate: "canEnter.result")
                .Node("candle", SimpleNodeKind.Candle)
                .Node("belowTarget", SimpleNodeKind.Compare, TemplateParts.Compare(CompareOperation.Less))
                .Node("canEnter", SimpleNodeKind.And)
                .Node("stopFactor", SimpleNodeKind.Subtract, null, TemplateParts.Constant("a", 1m))
                .Node("stopPrice", SimpleNodeKind.Multiply)
                .Node("stopHit", SimpleNodeKind.Compare, TemplateParts.Compare(CompareOperation.LessOrEqual))
                .Node("exit", SimpleNodeKind.ClosePosition)
                .Wire("position.closedPnl", "belowTarget.a")
                .Wire("params.profitTarget", "belowTarget.b")
                .Wire("flat.result", "canEnter.a")
                .Wire("belowTarget.result", "canEnter.b")
                .Wire("params.stopLoss", "stopFactor.b")
                .Wire("position.averagePrice", "stopPrice.a")
                .Wire("stopFactor.result", "stopPrice.b")
                .Wire("candle.close", "stopHit.a")
                .Wire("stopPrice.result", "stopHit.b")
                .Wire("stopHit.result", "exit.when")
                .Build());
    }

    private static IReadOnlyList<StrategyParameter> LadderParameters()
    {
        return
        [
            TemplateParts.Number("limitPerOrder", 100m),
            TemplateParts.Number("limitDeposit", 300m),
            TemplateParts.Share("priceDropForBuy", 0.01m),
            TemplateParts.Number("priceRiseForSell", 0.01m)
        ];
    }

    /// <summary>The ladder graph; the variants change where the buy price starts from and what lets the first purchase out.</summary>
    private static GraphSketch LadderSketch(string buyBasis = "position.lastEntryPrice", string entryGate = "flat.result")
    {
        return new GraphSketch()
            .Node("params", SimpleNodeKind.Parameters)
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("riseFactor", SimpleNodeKind.Add, null, TemplateParts.Constant("a", 1m))
            .Node("sellPrice", SimpleNodeKind.Multiply)
            .Node("dropFactor", SimpleNodeKind.Subtract, null, TemplateParts.Constant("a", 1m))
            .Node("buyPrice", SimpleNodeKind.Multiply)
            .Node("nextInvested", SimpleNodeKind.Add)
            .Node("budget", SimpleNodeKind.Compare, TemplateParts.Compare(CompareOperation.LessOrEqual))
            .Node("canAverage", SimpleNodeKind.And)
            .Node("entry", SimpleNodeKind.MarketOrder, TemplateParts.Buy)
            .Node("sell", SimpleNodeKind.HoldLimit, new NodeSettings(Side: OrderSide.Sell, SizeMode: OrderSizeMode.Quantity))
            .Node("buy", SimpleNodeKind.HoldLimit, TemplateParts.Buy)
            .Wire("position.hasPosition", "flat.value")
            .Wire("params.priceRiseForSell", "riseFactor.b")
            .Wire("position.averagePrice", "sellPrice.a")
            .Wire("riseFactor.result", "sellPrice.b")
            .Wire("params.priceDropForBuy", "dropFactor.b")
            .Wire(buyBasis, "buyPrice.a")
            .Wire("dropFactor.result", "buyPrice.b")
            .Wire("position.invested", "nextInvested.a")
            .Wire("params.limitPerOrder", "nextInvested.b")
            .Wire("nextInvested.result", "budget.a")
            .Wire("params.limitDeposit", "budget.b")
            .Wire("position.hasPosition", "canAverage.a")
            .Wire("budget.result", "canAverage.b")
            .Wire(entryGate, "entry.when")
            .Wire("params.limitPerOrder", "entry.size")
            .Wire("position.hasPosition", "sell.when")
            .Wire("sellPrice.result", "sell.price")
            .Wire("position.quantity", "sell.size")
            .Wire("canAverage.result", "buy.when")
            .Wire("buyPrice.result", "buy.price")
            .Wire("params.limitPerOrder", "buy.size");
    }

    public static StrategyDefinition Dca()
    {
        return new StrategyDefinition(
            StrategyDirection.Long,
            [
                TemplateParts.Number("limitPerOrder", 100m),
                TemplateParts.Number("limitDeposit", 250m),
                new StrategyParameter("purchaseInterval", PinType.Duration, new DurationValue(TimeSpan.FromHours(1)))
            ],
            new GraphSketch()
                .Node("params", SimpleNodeKind.Parameters)
                .Node("position", SimpleNodeKind.Position)
                .Node("flat", SimpleNodeKind.Not)
                .Node("due", SimpleNodeKind.Compare, TemplateParts.Compare(CompareOperation.GreaterOrEqual, PinType.Duration))
                .Node("time", SimpleNodeKind.Or)
                .Node("nextInvested", SimpleNodeKind.Add)
                .Node("budget", SimpleNodeKind.Compare, TemplateParts.Compare(CompareOperation.LessOrEqual))
                .Node("go", SimpleNodeKind.And)
                .Node("buy", SimpleNodeKind.MarketOrder, TemplateParts.Buy)
                .Wire("position.hasPosition", "flat.value")
                .Wire("position.sinceLastEntry", "due.a")
                .Wire("params.purchaseInterval", "due.b")
                .Wire("flat.result", "time.a")
                .Wire("due.result", "time.b")
                .Wire("position.invested", "nextInvested.a")
                .Wire("params.limitPerOrder", "nextInvested.b")
                .Wire("nextInvested.result", "budget.a")
                .Wire("params.limitDeposit", "budget.b")
                .Wire("time.result", "go.a")
                .Wire("budget.result", "go.b")
                .Wire("go.result", "buy.when")
                .Wire("params.limitPerOrder", "buy.size")
                .Build());
    }
}