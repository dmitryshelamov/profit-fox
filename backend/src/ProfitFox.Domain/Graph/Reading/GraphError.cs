using ProfitFox.Domain.Graph.Model;

namespace ProfitFox.Domain.Graph.Reading;

/// <summary>
///     An error found in a graph and what it is about: a field of the JSON, a parameter, a node, its pin or setting, or a
///     wire.
/// </summary>
public sealed record GraphError(GraphErrorCode Code, string Reason)
{
    public string? Field { get; init; }

    public string? Parameter { get; init; }

    public string? Node { get; init; }

    public string? Pin { get; init; }

    public string? Setting { get; init; }

    public WireDefinition? Wire { get; init; }

    /// <summary>
    ///     The nodes of a cycle, in the order of its wires.
    /// </summary>
    public IReadOnlyList<string> Nodes { get; init; } = [];

    public override string ToString()
    {
        string where = Wire is not null ? $"wire {Wire.From} -> {Wire.To}"
            : Node is not null && Pin is not null ? $"{Node}.{Pin}"
            : Node is not null && Setting is not null ? $"{Node} setting {Setting}"
            : Node ?? Parameter ?? Field ?? "graph";

        return $"{Code} at {where}: {Reason}";
    }
}