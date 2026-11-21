using System.Text.Json;

using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Presentation.Contracts.Strategies;

/// <summary>
///     A new strategy: its name, an optional description, the graph as written and the positions of its nodes.
/// </summary>
public sealed record CreateStrategyRequest(string? Name, string? Description, JsonElement? Graph, IReadOnlyDictionary<string, NodePosition>? Layout);
