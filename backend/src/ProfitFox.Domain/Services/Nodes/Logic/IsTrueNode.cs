using ProfitFox.Domain.Services.Nodes.Base;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.Services.Nodes.Logic;

/// <summary>
///     <c>is-true</c>: true when its input is true, false when it is false or missing; never missing itself (spec
///     strategy-execution «Известная истина»; change add-setup-strategies, design.md, decision 11). A comparison with the
///     edge of a zone that is not there becomes false, so <c>first-time</c> after such a candle sees a known false. The
///     node keeps nothing, so a computation again after a fill gives what the close gave.
/// </summary>
internal sealed class IsTrueNode : Node
{
    public override void Compute(in NodeContext context, ReadOnlySpan<PinValue> inputs, Span<PinValue> outputs)
    {
        outputs[0] = PinValue.FromBoolean(inputs[0].Boolean == true);
    }
}
