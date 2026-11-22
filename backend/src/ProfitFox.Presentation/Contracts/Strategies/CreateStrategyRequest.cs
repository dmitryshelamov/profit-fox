using System.Text.Json;

using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Presentation.Contracts.Strategies;

/// <summary>
///     A new strategy: its name, an optional description, the graph as written, the positions of its nodes and its kind,
///     <c>bot</c> or <c>idea</c> (spec strategy-constructor «Вид при создании стратегии»). Without a kind the strategy
///     takes the kind of its graph.
/// </summary>
public sealed record CreateStrategyRequest(
    string? Name,
    string? Description,
    JsonElement? Graph,
    IReadOnlyDictionary<string, NodePosition>? Layout,
    string? Kind = null);
