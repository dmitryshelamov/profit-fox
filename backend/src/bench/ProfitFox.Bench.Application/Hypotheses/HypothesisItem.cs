using System.Text.Json;

namespace ProfitFox.Bench.Application.Hypotheses;

/// <summary>
///     A hypothesis of the bench for the page: its signal graph as written in its file and its timeframe, or why its file
///     cannot be read.
/// </summary>
public sealed record HypothesisItem(string Id, string Name, TimeSpan? Timeframe, JsonElement? Signal, string? Error);
