using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Application.Contracts.Strategies;

/// <summary>
///     A saved strategy as the API shows it: its name, description, the graph as written, the positions of its nodes by id,
///     the number of its latest version and what each of its signal nodes points at. <see cref="Raised" /> says that the
///     graph is the stored one lifted to the form of change add-position-node, not yet saved so.
/// </summary>
public sealed record StrategyInternal(
    string Name,
    string? Description,
    GraphDefinition Graph,
    IReadOnlyDictionary<string, NodePosition> Layout,
    int Version,
    IReadOnlyList<SignalUseInternal> Signals,
    bool Raised = false);
