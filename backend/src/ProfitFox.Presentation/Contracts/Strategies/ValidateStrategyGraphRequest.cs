using System.Text.Json;

namespace ProfitFox.Presentation.Contracts.Strategies;

/// <summary>
///     A graph to check as a strategy of a kind, <c>bot</c> or <c>idea</c>, without any history; without a kind the
///     rules of a strategy for a bot.
/// </summary>
public sealed record ValidateStrategyGraphRequest(JsonElement? Graph, string? Kind = null);