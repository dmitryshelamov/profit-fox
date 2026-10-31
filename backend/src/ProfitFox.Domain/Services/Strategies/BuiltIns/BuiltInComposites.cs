using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Services.Strategies.BuiltIns;

/// <summary>
///     Composite nodes shipped with the code. A compatible change reaches every usage; an incompatible one ships as a new
///     node, because it would break saved strategies.
/// </summary>
public static class BuiltInComposites
{
    private static readonly NumberValue One = new NumberValue(1m);

    public static IReadOnlyList<BuiltInComposite> All { get; } =
    [
        new BuiltInComposite(BuiltInNode.EmaCross, "Пересечение EMA", EmaCross()),
        new BuiltInComposite(BuiltInNode.Ladder, "Ladder", Ladder()),
        new BuiltInComposite(BuiltInNode.Dca, "DCA", Dca())
    ];

    public static CompositeCatalog Catalog { get; } = new CompositeCatalog(
        All.ToDictionary(composite => (CompositeNodeId)new BuiltInNodeId(composite.Node), composite => composite.Definition));

    /// <summary>Signals when the fast EMA of <c>source</c> crosses the slow one.</summary>
    private static CompositeDefinition EmaCross()
    {
        return new CompositeDefinition(
            [Number("source"), Number("fast"), Number("slow")],
            [new PinDeclaration("up", PinType.Boolean), new PinDeclaration("down", PinType.Boolean)],
            new GraphSketch()
                .Node("in", SimpleNodeKind.Inputs)
                .Node("fastEma", SimpleNodeKind.Ema)
                .Node("slowEma", SimpleNodeKind.Ema)
                .Node("cross", SimpleNodeKind.Cross)
                .Node("out", SimpleNodeKind.Outputs)
                .Wire("in.source", "fastEma.source")
                .Wire("in.fast", "fastEma.period")
                .Wire("in.source", "slowEma.source")
                .Wire("in.slow", "slowEma.period")
                .Wire("fastEma.value", "cross.a")
                .Wire("slowEma.value", "cross.b")
                .Wire("cross.up", "out.up")
                .Wire("cross.down", "out.down")
                .Build());
    }

    /// <summary>
    ///     Enters at market, then holds a sell of the whole position above the average price and an averaging buy below the
    ///     last entry while the deposit allows it; a closed position starts the next cycle.
    /// </summary>
    private static CompositeDefinition Ladder()
    {
        return new CompositeDefinition(
            [Number("limitPerOrder"), Number("limitDeposit"), Number("priceDropForBuy"), Number("priceRiseForSell")],
            [],
            new GraphSketch()
                .Node("in", SimpleNodeKind.Inputs)
                .Node("position", SimpleNodeKind.Position)
                .Node("flat", SimpleNodeKind.Not)
                .Node("riseFactor", SimpleNodeKind.Add, null, ("a", One))
                .Node("sellPrice", SimpleNodeKind.Multiply)
                .Node("dropFactor", SimpleNodeKind.Subtract, null, ("a", One))
                .Node("buyPrice", SimpleNodeKind.Multiply)
                .Node("nextInvested", SimpleNodeKind.Add)
                .Node("budget", SimpleNodeKind.Compare, new NodeSettings(Operation: CompareOperation.LessOrEqual))
                .Node("canAverage", SimpleNodeKind.And)
                .Node("entry", SimpleNodeKind.MarketOrder, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Amount))
                .Node("sell", SimpleNodeKind.HoldLimit, new NodeSettings(Side: OrderSide.Sell, SizeMode: OrderSizeMode.Quantity))
                .Node("buy", SimpleNodeKind.HoldLimit, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Amount))
                .Wire("position.hasPosition", "flat.value")
                .Wire("in.priceRiseForSell", "riseFactor.b")
                .Wire("position.averagePrice", "sellPrice.a")
                .Wire("riseFactor.result", "sellPrice.b")
                .Wire("in.priceDropForBuy", "dropFactor.b")
                .Wire("position.lastEntryPrice", "buyPrice.a")
                .Wire("dropFactor.result", "buyPrice.b")
                .Wire("position.invested", "nextInvested.a")
                .Wire("in.limitPerOrder", "nextInvested.b")
                .Wire("nextInvested.result", "budget.a")
                .Wire("in.limitDeposit", "budget.b")
                .Wire("position.hasPosition", "canAverage.a")
                .Wire("budget.result", "canAverage.b")
                .Wire("flat.result", "entry.when")
                .Wire("in.limitPerOrder", "entry.size")
                .Wire("position.hasPosition", "sell.when")
                .Wire("sellPrice.result", "sell.price")
                .Wire("position.quantity", "sell.size")
                .Wire("canAverage.result", "buy.when")
                .Wire("buyPrice.result", "buy.price")
                .Wire("in.limitPerOrder", "buy.size")
                .Build());
    }

    /// <summary>Buys at market on start and then every <c>purchaseInterval</c> after the last entry while the deposit allows it.</summary>
    private static CompositeDefinition Dca()
    {
        return new CompositeDefinition(
            [Number("limitPerOrder"), Number("limitDeposit"), new PinDeclaration("purchaseInterval", PinType.Duration)],
            [],
            new GraphSketch()
                .Node("in", SimpleNodeKind.Inputs)
                .Node("position", SimpleNodeKind.Position)
                .Node("flat", SimpleNodeKind.Not)
                .Node("due", SimpleNodeKind.Compare, new NodeSettings(PinType.Duration, CompareOperation.GreaterOrEqual))
                .Node("time", SimpleNodeKind.Or)
                .Node("nextInvested", SimpleNodeKind.Add)
                .Node("budget", SimpleNodeKind.Compare, new NodeSettings(Operation: CompareOperation.LessOrEqual))
                .Node("go", SimpleNodeKind.And)
                .Node("buy", SimpleNodeKind.MarketOrder, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Amount))
                .Wire("position.hasPosition", "flat.value")
                .Wire("position.sinceLastEntry", "due.a")
                .Wire("in.purchaseInterval", "due.b")
                .Wire("flat.result", "time.a")
                .Wire("due.result", "time.b")
                .Wire("position.invested", "nextInvested.a")
                .Wire("in.limitPerOrder", "nextInvested.b")
                .Wire("nextInvested.result", "budget.a")
                .Wire("in.limitDeposit", "budget.b")
                .Wire("time.result", "go.a")
                .Wire("budget.result", "go.b")
                .Wire("go.result", "buy.when")
                .Wire("in.limitPerOrder", "buy.size")
                .Build());
    }

    private static PinDeclaration Number(string name)
    {
        return new PinDeclaration(name, PinType.Number);
    }
}