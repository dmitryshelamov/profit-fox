using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Domain.ValueObjects.Graph;

/// <summary>
///     An <c>enter</c> node of a strategy: its id in the graph and its side.
/// </summary>
public sealed record StrategyEntry(string Node, EntrySide Side);
