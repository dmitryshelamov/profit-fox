namespace ProfitFox.Cli.Contracts.Backtests;

/// <summary>
///     A backtest to put in the queue, as the bench API takes it.
/// </summary>
internal sealed record CreateBacktestDto(
    string Strategy,
    int? Version,
    IReadOnlyDictionary<string, string[]>? Values,
    string[]? Timeframes,
    string? Start,
    string? End,
    string[]? Pairs);
