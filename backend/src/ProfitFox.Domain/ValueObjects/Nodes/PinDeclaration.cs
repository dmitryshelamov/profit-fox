using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Domain.ValueObjects.Nodes;

/// <summary>
///     An input or output a node kind declares. An input that is <see cref="Many" /> takes several wires, each of its type,
///     and no written value (spec strategy-graph «Вход с несколькими проводами»).
/// </summary>
public sealed record PinDeclaration(string Name, PinType Type)
{
    public bool Many { get; init; }

    /// <summary>
    ///     Whether the pin carries no value but shows a link: an event, an order or a position takes a wire and never a
    ///     written value.
    /// </summary>
    public bool WireOnly => Type is PinType.Event or PinType.Order or PinType.Position;
}