using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Services.Strategies.Execution;

/// <summary>Stateless node calculations; <c>null</c> is a missing value.</summary>
public static class NodeFunctions
{
    public static NodeValue? Arithmetic(SimpleNodeKind kind, NodeValue? a, NodeValue? b)
    {
        if (a is not NumberValue { Value: var left } || b is not NumberValue { Value: var right })
        {
            return null;
        }

        if (kind == SimpleNodeKind.Divide)
        {
            return right == 0m ? null : new NumberValue(left / right);
        }

        return new NumberValue(kind switch
        {
            SimpleNodeKind.Add => left + right,
            SimpleNodeKind.Subtract => left - right,
            _ => left * right
        });
    }

    /// <summary>A comparison with a missing value is false.</summary>
    public static bool Compare(CompareOperation operation, NodeValue? a, NodeValue? b)
    {
        int? order = (a, b) switch
        {
            (NumberValue left, NumberValue right) => left.Value.CompareTo(right.Value),
            (DurationValue left, DurationValue right) => left.Value.CompareTo(right.Value),
            _ => null
        };

        return order is { } sign && operation switch
        {
            CompareOperation.Greater => sign > 0,
            CompareOperation.GreaterOrEqual => sign >= 0,
            CompareOperation.Less => sign < 0,
            _ => sign <= 0
        };
    }
}