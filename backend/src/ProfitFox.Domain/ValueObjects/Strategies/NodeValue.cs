using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>A value on a pin: a number, a boolean or a duration.</summary>
public abstract record NodeValue
{
    public abstract PinType Type { get; }
}