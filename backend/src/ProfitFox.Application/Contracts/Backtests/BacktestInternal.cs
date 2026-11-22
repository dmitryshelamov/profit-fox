using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Application.Contracts.Backtests;

/// <summary>
///     A backtest as the API shows it (spec bench-run-queue «Страница прогона»): the strategy with its version and kind,
///     the settings and the sets, where it is in the queue and who holds it, how far it came, and once it ended its version
///     of the engine, the hash of the history it read, how long it read and computed, why it has no result, the sum of its
///     best set and the warning about survivors.
/// </summary>
public sealed record BacktestInternal(
    string Id,
    Guid StrategyId,
    string StrategyName,
    int StrategyVersion,
    StrategyKind Kind,
    BacktestSettings Settings,
    IReadOnlyList<BacktestSetOutcome> Sets,
    int SetCount,
    BacktestSource? Source,
    string Status,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Worker,
    int PairsDone,
    int? PairsTotal,
    int? EngineVersion,
    string? DataHash,
    double? ReadSeconds,
    double? ComputeSeconds,
    string? Reason,
    double? BestAmount,
    string Warning);
