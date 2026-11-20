using System.Text.Json;

namespace ProfitFox.Bench.Application.Hypotheses;

/// <summary>
///     The latest version of a hypothesis of the bench for the page: its signal graph as written in its text and its
///     timeframe, or why its text cannot be read.
/// </summary>
public sealed record HypothesisItem(string Id, int Version, string Name, TimeSpan? Timeframe, JsonElement? Signal, string? Error);
