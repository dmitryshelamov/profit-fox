using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.ValueObjects.Strategies;

public sealed record DurationValue(TimeSpan Value) : NodeValue
{
    public override PinType Type => PinType.Duration;
}