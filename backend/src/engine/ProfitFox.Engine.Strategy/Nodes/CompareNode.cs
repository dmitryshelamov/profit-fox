using ProfitFox.Engine.Strategy.Graph;

namespace ProfitFox.Engine.Strategy.Nodes;

/// <summary>
///     <c>compare</c>: <c>a ≥ b</c> for the operation <c>ge</c>, <c>a &lt; b</c> for <c>lt</c>; false when <c>a</c> or <c>b</c> is
///     absent.
/// </summary>
internal sealed class CompareNode : Node
{
    public const string GreaterOrEqual = "ge";
    public const string Less = "lt";

    private readonly bool _greaterOrEqual;

    public CompareNode(string operation)
    {
        _greaterOrEqual = operation switch
        {
            GreaterOrEqual => true,
            Less => false,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "The operation is ge or lt.")
        };
    }

    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        decimal? a = inputs[0].Number;
        decimal? b = inputs[1].Number;
        bool result = a is not null && b is not null && (_greaterOrEqual ? a.Value >= b.Value : a.Value < b.Value);
        outputs[0] = PinValue.FromBoolean(result);
    }
}