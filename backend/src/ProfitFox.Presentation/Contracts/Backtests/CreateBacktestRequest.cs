namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>
///     A backtest to put in the queue (spec strategy-backtest «Настройки прогона»): the name of the strategy and, when
///     wanted, the number of its version (the latest without one), the values of its parameters by name as texts — several
///     to a parameter make sets —, the timeframes of <c>5m</c>, <c>15m</c>, <c>1h</c>, <c>4h</c> and <c>1d</c>, the start and the end of history
///     as dates UTC such as <c>2026-06-01</c>, the pairs by symbol (every listed pair without them) and the fees of a taker
///     and of a maker as fractions.
/// </summary>
public sealed record CreateBacktestRequest(
    string? Strategy,
    int? Version = null,
    IReadOnlyDictionary<string, string[]>? Values = null,
    string[]? Timeframes = null,
    string? Start = null,
    string? End = null,
    string[]? Pairs = null,
    double? TakerFee = null,
    double? MakerFee = null);
