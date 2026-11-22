namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     What a done backtest writes with its end (spec bench-run-queue «Результат прогона»): the numbers of each set and of
///     its pairs with orders, the version of the engine, the hash of the history it read, and how long it read and
///     computed.
/// </summary>
public sealed record BacktestOutcome(
    IReadOnlyList<(int Set, SetSummary Numbers, IReadOnlyList<PairSummary> Pairs)> Sets,
    int EngineVersion,
    string DataHash,
    double ReadSeconds,
    double ComputeSeconds);
