using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Application.Strategies;

/// <summary>The settings of every simple node: reading a definition and the node catalog both use this table.</summary>
public static class NodeSettingSpecs
{
    private static readonly NodeSettingSpec Operation = new NodeSettingSpec(
        "operation",
        [CompareOperation.Greater, CompareOperation.GreaterOrEqual, CompareOperation.Less, CompareOperation.LessOrEqual],
        CompareOperation.Greater,
        Required: true,
        [],
        (settings, value) => settings with { Operation = (CompareOperation)value });

    private static readonly NodeSettingSpec ComparedType = new NodeSettingSpec(
        "type",
        [PinType.Number, PinType.Duration],
        PinType.Number,
        Required: false,
        ["a", "b"],
        (settings, value) => settings with { ValueType = (PinType)value });

    private static readonly NodeSettingSpec Side = new NodeSettingSpec(
        "side",
        [OrderSide.Buy, OrderSide.Sell],
        OrderSide.Buy,
        Required: true,
        [],
        (settings, value) => settings with { Side = (OrderSide)value });

    private static readonly NodeSettingSpec Size = new NodeSettingSpec(
        "size",
        [OrderSizeMode.Quantity, OrderSizeMode.Amount],
        OrderSizeMode.Quantity,
        Required: true,
        [],
        (settings, value) => settings with { SizeMode = (OrderSizeMode)value });

    public static IReadOnlyList<NodeSettingSpec> Of(SimpleNodeKind kind)
    {
        return kind switch
        {
            SimpleNodeKind.Compare => [Operation, ComparedType],
            SimpleNodeKind.MarketOrder or SimpleNodeKind.HoldLimit => [Side, Size],
            _ => []
        };
    }
}