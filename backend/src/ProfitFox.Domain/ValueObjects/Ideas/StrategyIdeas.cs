namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     Whether a strategy gives ideas and on which timeframes (change add-ideas-and-market, spec strategy-ideas «Включение
///     идей у стратегии»).
/// </summary>
public sealed record StrategyIdeas(Guid StrategyId, bool Enabled, IReadOnlyList<TimeSpan> Timeframes);
