using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.ValueObjects.Strategies;

public sealed record NumberValue(decimal Value) : NodeValue
{
    public override PinType Type => PinType.Number;
}