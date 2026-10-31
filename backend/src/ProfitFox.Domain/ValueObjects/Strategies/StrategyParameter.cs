using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>A setting a bot or a backtest fills in; <paramref name="Min" />, <paramref name="Max" /> and <paramref name="Integer" /> limit numbers.</summary>
public sealed record StrategyParameter(string Name, PinType Type, NodeValue Default, decimal? Min = null, decimal? Max = null, bool Integer = false)
{
    /// <summary>Why <paramref name="value" /> cannot be this parameter's value, as the end of a sentence; <c>null</c> when it can.</summary>
    public string? Reject(NodeValue value)
    {
        if (value.Type != Type)
        {
            return $"must be of type {Type}.";
        }

        if (value is not NumberValue { Value: var number })
        {
            return null;
        }

        if (number < Min)
        {
            return $"must be at least {Min}.";
        }

        if (number > Max)
        {
            return $"must be at most {Max}.";
        }

        return Integer && number != decimal.Truncate(number) ? "must be a whole number." : null;
    }
}