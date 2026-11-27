using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Numbers;

/// <summary>
///     <c>add</c>, <c>subtract</c>, <c>multiply</c>, <c>divide</c>, <c>min</c> and <c>max</c>: an operation on <c>a</c>
///     and <c>b</c> in decimals (spec strategy-execution «Арифметические узлы», «Деление», «Меньшее и большее»); missing
///     when either is missing, when <c>b</c> of a division is zero, and when the result does not fit a decimal: a graph may
///     multiply anything, and a number past <c>7.9e28</c> is no value rather than a failed run.
/// </summary>
internal sealed class ArithmeticNode(Func<decimal, decimal, decimal> operation) : Node
{
    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        decimal? a = inputs[0].Number;
        decimal? b = inputs[1].Number;
        outputs[0] = PinValue.FromNumber(a is { } left && b is { } right ? Result(left, right) : null);
    }

    private decimal? Result(decimal left, decimal right)
    {
        try
        {
            return operation(left, right);
        }
        catch (Exception exception) when (exception is OverflowException or DivideByZeroException)
        {
            return null;
        }
    }
}
