using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Numbers;

/// <summary>
///     <c>add</c>, <c>subtract</c> and <c>multiply</c>: an operation on <c>a</c> and <c>b</c>.
/// </summary>
internal sealed class ArithmeticNode(Func<decimal, decimal, decimal> operation) : Node
{
    private readonly Func<decimal, decimal, decimal> _operation = operation;

    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        outputs[0] = PinValue.FromNumber(null);
    }
}
