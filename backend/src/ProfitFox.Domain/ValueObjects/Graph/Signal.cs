namespace ProfitFox.Domain.ValueObjects.Graph;

/// <summary>
///     A checked signal: a graph that reads only the market and has one output, an event, «entry now».
/// </summary>
public sealed class Signal
{
    public GraphDefinition Graph { get; }

    /// <summary>
    ///     The name of its only output.
    /// </summary>
    public string Output => Graph.Outputs[0].Name;

    internal Signal(GraphDefinition graph)
    {
        Graph = graph;
    }
}