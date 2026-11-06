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
    public static IReadOnlyList<BuiltInComposite> All { get; } =
    [
        new BuiltInComposite(BuiltInNode.EmaCross, "Пересечение EMA", EmaCross()),
        new BuiltInComposite(BuiltInNode.Macd, "MACD", Macd()),
        new BuiltInComposite(BuiltInNode.Bollinger, "Полосы Боллинджера", Bollinger())
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

    /// <summary>The line (fast EMA less slow EMA of <c>source</c>), its signal (the EMA of the line) and the histogram (line less signal).</summary>
    private static CompositeDefinition Macd()
    {
        return new CompositeDefinition(
            [Number("source"), Number("fast"), Number("slow"), Number("signal")],
            [Number("macd"), Number("signal"), Number("histogram")],
            new GraphSketch()
                .Node("in", SimpleNodeKind.Inputs)
                .Node("fastEma", SimpleNodeKind.Ema)
                .Node("slowEma", SimpleNodeKind.Ema)
                .Node("line", SimpleNodeKind.Subtract)
                .Node("signalEma", SimpleNodeKind.Ema)
                .Node("histogram", SimpleNodeKind.Subtract)
                .Node("out", SimpleNodeKind.Outputs)
                .Wire("in.source", "fastEma.source")
                .Wire("in.fast", "fastEma.period")
                .Wire("in.source", "slowEma.source")
                .Wire("in.slow", "slowEma.period")
                .Wire("fastEma.value", "line.a")
                .Wire("slowEma.value", "line.b")
                .Wire("line.result", "signalEma.source")
                .Wire("in.signal", "signalEma.period")
                .Wire("line.result", "histogram.a")
                .Wire("signalEma.value", "histogram.b")
                .Wire("line.result", "out.macd")
                .Wire("signalEma.value", "out.signal")
                .Wire("histogram.result", "out.histogram")
                .Build());
    }

    /// <summary>The average of <c>source</c> over <c>period</c> bars and the bands <c>width</c> standard deviations above and below it.</summary>
    private static CompositeDefinition Bollinger()
    {
        return new CompositeDefinition(
            [Number("source"), Number("period"), Number("width")],
            [Number("upper"), Number("middle"), Number("lower")],
            new GraphSketch()
                .Node("in", SimpleNodeKind.Inputs)
                .Node("average", SimpleNodeKind.Sma)
                .Node("deviation", SimpleNodeKind.StdDev)
                .Node("offset", SimpleNodeKind.Multiply)
                .Node("upper", SimpleNodeKind.Add)
                .Node("lower", SimpleNodeKind.Subtract)
                .Node("out", SimpleNodeKind.Outputs)
                .Wire("in.source", "average.source")
                .Wire("in.period", "average.period")
                .Wire("in.source", "deviation.source")
                .Wire("in.period", "deviation.period")
                .Wire("deviation.value", "offset.a")
                .Wire("in.width", "offset.b")
                .Wire("average.value", "upper.a")
                .Wire("offset.result", "upper.b")
                .Wire("average.value", "lower.a")
                .Wire("offset.result", "lower.b")
                .Wire("upper.result", "out.upper")
                .Wire("average.value", "out.middle")
                .Wire("lower.result", "out.lower")
                .Build());
    }

    private static PinDeclaration Number(string name)
    {
        return new PinDeclaration(name, PinType.Number);
    }
}