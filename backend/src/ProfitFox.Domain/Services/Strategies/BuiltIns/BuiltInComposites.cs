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
        new BuiltInComposite(BuiltInNode.EmaCross, "Пересечение EMA", EmaCross())
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

    private static PinDeclaration Number(string name)
    {
        return new PinDeclaration(name, PinType.Number);
    }
}