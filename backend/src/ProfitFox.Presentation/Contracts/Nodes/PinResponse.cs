namespace ProfitFox.Presentation.Contracts.Nodes;

/// <summary>
///     A pin of a node kind: its name and type (<c>number</c>, <c>boolean</c>, <c>duration</c>, <c>event</c>, <c>order</c> or
///     <c>position</c>), and whether the input takes several wires (spec strategy-graph «Вход с несколькими проводами»).
/// </summary>
public sealed record PinResponse(string Name, string Type)
{
    public bool Many { get; init; }
}
