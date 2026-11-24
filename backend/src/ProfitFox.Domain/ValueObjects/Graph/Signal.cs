using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Domain.ValueObjects.Graph;

/// <summary>
///     A checked signal: a graph that reads only the market and has one output — an event, «entry now», or a boolean, a
///     state that is true while its condition holds (change add-ideas-and-market, design.md, decision 7).
/// </summary>
public sealed class Signal
{
    public GraphDefinition Graph { get; }

    /// <summary>
    ///     The name of its only output.
    /// </summary>
    public string Output => Graph.Outputs[0].Name;

    /// <summary>
    ///     The type of its only output: <see cref="PinType.Event" /> or <see cref="PinType.Boolean" />.
    /// </summary>
    public PinType OutputType => Graph.Outputs[0].Type;

    /// <summary>
    ///     Whether the output is a boolean: the signal is a state, not an event.
    /// </summary>
    public bool IsState => OutputType == PinType.Boolean;

    internal Signal(GraphDefinition graph)
    {
        Graph = graph;
    }
}
