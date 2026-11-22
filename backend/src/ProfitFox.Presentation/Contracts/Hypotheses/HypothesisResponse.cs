using System.Text.Json;

using ProfitFox.Presentation.Contracts.Runs;

namespace ProfitFox.Presentation.Contracts.Hypotheses;

/// <summary>
///     The latest version of a hypothesis of the bench for the page: its signal graph and timeframe, or why its text cannot
///     be read, whether it can be run on each period in each kind, and the last run of this version (spec bench-run-queue
///     «Последний прогон гипотезы») without its report; null when the version has no runs.
/// </summary>
public sealed record HypothesisResponse(
    string Id,
    int Version,
    string Name,
    string? Timeframe,
    JsonElement? Signal,
    string? Error,
    IReadOnlyList<RunAvailabilityResponse> Runs,
    RunResponse? LastRun);
