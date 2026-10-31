using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.ValueObjects.Strategies;

public sealed record BooleanValue(bool Value) : NodeValue
{
    public override PinType Type => PinType.Boolean;
}