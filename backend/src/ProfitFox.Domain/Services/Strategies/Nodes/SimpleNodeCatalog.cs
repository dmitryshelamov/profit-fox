using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Services.Strategies.Nodes;

/// <summary>Pins of every simple node kind.</summary>
public static class SimpleNodeCatalog
{
    private static readonly PinDeclaration[] None = [];

    public static NodePins Pins(SimpleNodeKind kind, NodeSettings settings, PinContext context)
    {
        return kind switch
        {
            SimpleNodeKind.Parameters => new NodePins(
                None,
                [.. context.Parameters.DistinctBy(parameter => parameter.Name).Select(parameter => new PinDeclaration(parameter.Name, parameter.Type))]),
            SimpleNodeKind.Inputs => new NodePins(None, context.CompositeInputs),
            SimpleNodeKind.Outputs => new NodePins(context.CompositeOutputs, None),
            SimpleNodeKind.Candle => new NodePins(None, Numbers("open", "high", "low", "close", "volume")),
            SimpleNodeKind.Position => new NodePins(
                None,
                [
                    new PinDeclaration("hasPosition", PinType.Boolean),
                    .. Numbers("quantity", "averagePrice", "invested", "lastEntryPrice"),
                    new PinDeclaration("sinceLastEntry", PinType.Duration),
                    new PinDeclaration("free", PinType.Number)
                ]),
            SimpleNodeKind.Ema => new NodePins(Numbers("source", "period"), Numbers("value")),
            SimpleNodeKind.Cross => new NodePins(Numbers("a", "b"), Booleans("up", "down")),
            SimpleNodeKind.Add or SimpleNodeKind.Subtract or SimpleNodeKind.Multiply or SimpleNodeKind.Divide =>
                new NodePins(Numbers("a", "b"), Numbers("result")),
            SimpleNodeKind.Compare => new NodePins(
                [new PinDeclaration("a", settings.ValueType ?? PinType.Number), new PinDeclaration("b", settings.ValueType ?? PinType.Number)],
                Booleans("result")),
            SimpleNodeKind.And or SimpleNodeKind.Or => new NodePins(Booleans("a", "b"), Booleans("result")),
            SimpleNodeKind.Not => new NodePins(Booleans("value"), Booleans("result")),
            SimpleNodeKind.MarketOrder => new NodePins([.. Booleans("when"), .. Numbers("size")], None),
            SimpleNodeKind.ClosePosition => new NodePins(Booleans("when"), None),
            SimpleNodeKind.HoldLimit => new NodePins([.. Booleans("when"), .. Numbers("price", "size")], None),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    private static PinDeclaration[] Numbers(params string[] names)
    {
        return [.. names.Select(name => new PinDeclaration(name, PinType.Number))];
    }

    private static PinDeclaration[] Booleans(params string[] names)
    {
        return [.. names.Select(name => new PinDeclaration(name, PinType.Boolean))];
    }
}