using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Services.Strategies.Templates;

/// <summary>What the graphs of the templates share: the parameters they declare and the settings of their nodes.</summary>
internal static class TemplateParts
{
    public static readonly NodeSettings Buy = new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Amount);

    /// <summary>A whole number of bars from <paramref name="min" />.</summary>
    public static StrategyParameter Period(string name, decimal value, decimal min = 1m, decimal? max = null)
    {
        return new StrategyParameter(name, PinType.Number, new NumberValue(value), min, max, Integer: true);
    }

    /// <summary>A share of a price from 0 to 1.</summary>
    public static StrategyParameter Share(string name, decimal value)
    {
        return new StrategyParameter(name, PinType.Number, new NumberValue(value), 0m, 1m);
    }

    /// <summary>A number from 0, or from <paramref name="min" />, with no upper limit.</summary>
    public static StrategyParameter Number(string name, decimal value, decimal min = 0m, decimal? max = null)
    {
        return new StrategyParameter(name, PinType.Number, new NumberValue(value), min, max);
    }

    public static NodeSettings Compare(CompareOperation operation, PinType? type = null)
    {
        return new NodeSettings(type, operation);
    }

    public static (string Pin, NodeValue Value) Constant(string pin, decimal value)
    {
        return (pin, new NumberValue(value));
    }
}