namespace ProfitFox.Engine.Strategy.Graph;

/// <summary>
///     An error found in a definition: why, and where — the node, its pin or setting, or the field of the JSON, when there is one.
/// </summary>
public sealed record GraphError(GraphErrorCode Code, string Reason)
{
    /// <summary>
    ///     The id of the node the error is in.
    /// </summary>
    public string? Node { get; init; }

    /// <summary>
    ///     The pin of the node, or the declared output, the error is about.
    /// </summary>
    public string? Pin { get; init; }

    /// <summary>
    ///     The setting of the node the error is about.
    /// </summary>
    public string? Setting { get; init; }

    /// <summary>
    ///     The path of the JSON field the error is about, such as <c>nodes[2].layout</c>.
    /// </summary>
    public string? Field { get; init; }
}