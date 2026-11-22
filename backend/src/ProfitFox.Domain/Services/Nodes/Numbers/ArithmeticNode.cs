using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Numbers;

/// <summary>
///     <c>add</c>, <c>subtract</c> and <c>multiply</c>: an operation on <c>a</c> and <c>b</c> in decimals (spec
///     strategy-execution «Арифметические узлы»); missing when either is missing.
/// </summary>
internal sealed class ArithmeticNode(Func<decimal, decimal, decimal> operation) : Node
{
    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        decimal? a = inputs[0].Number;
        decimal? b = inputs[1].Number;
        outputs[0] = PinValue.FromNumber(a is { } left && b is { } right ? operation(left, right) : null);
    }
}
