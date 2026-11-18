namespace ProfitFox.Engine.Strategy.Graph;

/// <summary>
///     A signal: a composite node with exactly one logical output, «entry now».
/// </summary>
public sealed class Signal
{
    /// <summary>
    ///     The composite node of the signal.
    /// </summary>
    public CompositeNode Graph { get; }

    /// <summary>
    ///     The name of its only output.
    /// </summary>
    public string Output => Graph.Outputs[0].Name;

    internal Signal(CompositeNode graph)
    {
        Graph = graph;
    }
}