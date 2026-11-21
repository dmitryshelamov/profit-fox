using System.Text.Json;

using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Presentation.Contracts.Strategies;

/// <summary>
///     What replaces a saved strategy: its description, graph and positions; the name stays.
/// </summary>
public sealed record ReplaceStrategyRequest(string? Description, JsonElement? Graph, IReadOnlyDictionary<string, NodePosition>? Layout);
