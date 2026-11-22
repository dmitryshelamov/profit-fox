using System.Text.Json;

using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Presentation.Contracts.Strategies;

/// <summary>
///     A saved strategy: the graph of its latest version as written, with its signal nodes, the positions of its nodes apart
///     from it, and what each signal node points at. <see cref="Raised" /> is true when the store keeps the graph in the
///     form before the node <c>position</c> and this graph is that one lifted on reading: it differs from the stored one,
///     and saving it as it is makes the next version.
/// </summary>
public sealed record StrategyResponse(
    string Name,
    string? Description,
    int Version,
    JsonElement Graph,
    IReadOnlyDictionary<string, NodePosition> Layout,
    IReadOnlyList<SignalUseResponse> Signals,
    bool Raised = false);
