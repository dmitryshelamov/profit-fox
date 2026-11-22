namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     What a backtest is asked with, as the request gives it, before any check (spec strategy-backtest «Настройки
///     прогона»): the values of the parameters by name, the timeframes, the start and the end of history as dates UTC, the
///     pairs by symbol and the fees of a taker and of a maker. Anything may be missing.
/// </summary>
public sealed record BacktestRequest(
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Values = null,
    IReadOnlyList<string>? Timeframes = null,
    string? Start = null,
    string? End = null,
    IReadOnlyList<string>? Pairs = null,
    double? TakerFee = null,
    double? MakerFee = null);
