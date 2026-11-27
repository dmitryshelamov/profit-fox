namespace ProfitFox.Application.Contracts.Preview;

/// <summary>
///     A preview, or every problem that refused its query, in the order they were found: the timeframe, the graph, the
///     pair.
/// </summary>
public sealed record PreviewOutcomeInternal(SignalPreviewInternal? Preview, IReadOnlyList<PreviewErrorInternal> Errors);
