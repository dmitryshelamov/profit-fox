namespace ProfitFox.Domain.ValueObjects.Lists;

/// <summary>
///     A row of the summary of the journal (spec strategy-ideas «Сводка журнала»): a strategy with its version and a
///     timeframe, its closed ideas, the share of those in the plus, the mean outcome as a fraction and the sum in USDT.
/// </summary>
public sealed record IdeaSummary(Guid StrategyId, string Strategy, int Version, string Timeframe, int Ideas, double Wins, double Mean, double Sum);
