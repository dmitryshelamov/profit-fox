using System.Text.Json;

namespace ProfitFox.Presentation.Hypotheses;

/// <summary>
///     The latest version of a hypothesis of the bench for the page: its signal graph and timeframe, or why its text cannot
///     be read, and whether it can be run on each period in each kind.
/// </summary>
public sealed record HypothesisResponse(string Id, int Version, string Name, string? Timeframe, JsonElement? Signal, string? Error, IReadOnlyList<RunAvailabilityResponse> Runs);
