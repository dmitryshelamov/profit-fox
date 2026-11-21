namespace ProfitFox.Application.Preview;

/// <summary>
///     A preview, or every problem that refused its query, in the order they were found: the timeframe, the graph, the
///     pair.
/// </summary>
public sealed record PreviewOutcome(SignalPreviewResult? Preview, IReadOnlyList<PreviewError> Errors);
