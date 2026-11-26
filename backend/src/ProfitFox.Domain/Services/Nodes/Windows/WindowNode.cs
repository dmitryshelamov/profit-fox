using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Windows;

/// <summary>
///     <c>window</c>: the largest, the smallest, the sum, the average, the median or the earliest of <c>value</c> on the
///     last <c>length</c> closed candles of the pair, the current one counted (spec strategy-execution «Окно значений»;
///     change add-setup-strategies, design.md, decision 2). No result until the window is full, and while a value in it is
///     missing. The memory moves at the close of a candle only: a computation again after a fill gives what the last close
///     gave (spec strategy-execution «Узлы окна при пересчёте после исполнения»).
/// </summary>
internal sealed class WindowNode : Node
{
    public const string Operation = "operation";

    /// <summary>
    ///     The operations of the setting <see cref="Operation" />.
    /// </summary>
    public static readonly IReadOnlyList<string> Operations = ["max", "min", "sum", "average", "median", "first"];

    private readonly string _operation;
    private readonly WindowValues _values;
    private decimal? _output;

    public WindowNode(string operation, bool fixedLength)
    {
        _operation = operation;
        _values = new WindowValues(operation switch
        {
            "max" => WindowOrder.Largest,
            "min" => WindowOrder.Smallest,
            "sum" or "average" => WindowOrder.Sum,
            "median" => WindowOrder.Sorted,
            "first" => WindowOrder.None,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "The graph checks the operation of a window.")
        }, fixedLength);
    }

    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        if (context.Commit)
        {
            int? length = WindowLength.Of(inputs[1].Number);
            _values.Add(inputs[0].Number, length);
            _output = length is { } window && _values.Full(window) ? Result(window) : null;
        }

        outputs[0] = PinValue.FromNumber(_output);
    }

    private decimal? Result(int length)
    {
        switch (_operation)
        {
            case "max" or "min":
                return _values.Extreme;
            case "sum":
                return _values.Sum;
            case "average":
                return _values.Sum / length;
            case "median":
                IReadOnlyList<decimal> sorted = _values.Sorted;
                return length % 2 == 1 ? sorted[length / 2] : (sorted[(length / 2) - 1] + sorted[length / 2]) / 2;
            default:
                return _values.Back(length - 1);
        }
    }
}
