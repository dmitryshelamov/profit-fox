using System.Text.Json;

namespace ProfitFox.Bench.Presentation.Hypotheses;

/// <summary>
///     A hypothesis of <c>bench/hypotheses</c> for the preview: its signal graph and timeframe, or why its file cannot be
///     read.
/// </summary>
public sealed record HypothesisResponse(string Id, string Name, string? Timeframe, JsonElement? Signal, string? Error);
