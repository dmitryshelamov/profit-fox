using ProfitFox.Domain.Execution;

namespace ProfitFox.Domain.Nodes.Logic;

/// <summary>
///     <c>compare</c>: <c>a</c> against <c>b</c> by its operation; missing when either is missing.
/// </summary>
internal sealed class CompareNode : Node
{
    private readonly Func<decimal, decimal, bool> _compare;

    public CompareNode(string operation)
    {
        _compare = operation switch
        {
            "gt" => (a, b) => a > b,
            "ge" => (a, b) => a >= b,
            "lt" => (a, b) => a < b,
            "le" => (a, b) => a <= b,
            "eq" => (a, b) => a == b,
            "ne" => (a, b) => a != b,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation of compare.")
        };
    }

    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        decimal? a = inputs[0].Number;
        decimal? b = inputs[1].Number;
        outputs[0] = PinValue.FromBoolean(a is null || b is null ? null : _compare(a.Value, b.Value));
    }
}
