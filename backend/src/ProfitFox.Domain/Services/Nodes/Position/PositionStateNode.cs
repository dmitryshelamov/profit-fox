using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Position;

/// <summary>
///     <c>position</c>: the state of the position of the pair, a source with no input it reads (spec strategy-execution
///     «Узел позиции»). Its input <c>orders</c> only shows where the orders go; the run of the strategy reads them from
///     the nodes of the orders.
/// </summary>
internal sealed class PositionStateNode : Node
{
    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        outputs[0] = PinValue.Missing(PinType.Position);
        outputs[1] = PinValue.FromBoolean(false);
        outputs[2] = PinValue.FromNumber(0);
        outputs[3] = PinValue.FromNumber(null);
        outputs[4] = PinValue.FromNumber(null);
        outputs[5] = PinValue.FromNumber(0);
        outputs[6] = PinValue.FromDuration(null);
    }
}
