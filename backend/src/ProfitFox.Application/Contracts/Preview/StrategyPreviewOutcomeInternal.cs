namespace ProfitFox.Application.Contracts.Preview;

/// <summary>
///     A preview of a strategy, or every problem that refused its query, in the order they were found: the timeframe, the
///     graph, the pair.
/// </summary>
public sealed record StrategyPreviewOutcomeInternal(StrategyPreviewInternal? Preview, IReadOnlyList<PreviewErrorInternal> Errors);
