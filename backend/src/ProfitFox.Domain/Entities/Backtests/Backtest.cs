using ProfitFox.Domain.Entities.Base;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Domain.Entities.Backtests;

/// <summary>
///     A backtest of a saved version of a strategy (spec strategy-backtest; change add-strategy-backtest, design.md,
///     decision 2) as its row says: the strategy by id, name, version and kind, the expanded graph of the version, the
///     settings and the sets, where the backtest is in the queue and who holds it, and once it is done what it read and how
///     long it took. It keeps a copy of the graph, so a deleted or changed strategy does not change it. A backtest is born
///     and changes only through the statements of its repository: the time of the database decides when it was queued,
///     who takes it and when its lease ends.
/// </summary>
public sealed class Backtest : AggregateRoot
{
    /// <summary>
    ///     The most sets a backtest computes (design.md, decision 3).
    /// </summary>
    public const int MaxSets = 200;

    /// <summary>
    ///     The most pairs a backtest of a strategy for a bot takes (design.md, decision 4): its minutes are read for the whole
    ///     history.
    /// </summary>
    public const int MaxBotPairs = 20;

    /// <summary>
    ///     How many weeks before the end the history of a strategy for a bot starts when the request has no start.
    /// </summary>
    public const int BotWeeks = 26;

    /// <summary>
    ///     The most pairs of the control group of a week of a category of ideas (change fix-backtest-random-control,
    ///     design.md, decision 3): an order has at most this many random entries, one on each pair of the group.
    /// </summary>
    public const int Randoms = 20;

    /// <summary>
    ///     The backtest that loses its lease this many times ends as failed.
    /// </summary>
    public const int MaxLostLeases = 3;

    /// <summary>
    ///     The warning every result carries (spec strategy-backtest «Предупреждение о выживших парах»).
    /// </summary>
    public const string SurvivorWarning =
        "История есть только у пар, торгуемых сейчас: пары, снятые с торгов, в прогон не входят, и результат от этого выше настоящего.";

    public required string Id { get; init; }

    public required Guid StrategyId { get; init; }

    public required string StrategyName { get; init; }

    public required int StrategyVersion { get; init; }

    public required StrategyKind Kind { get; init; }

    /// <summary>
    ///     The graph of the version with each signal node expanded: what every set runs, with its values.
    /// </summary>
    public required GraphDefinition Graph { get; init; }

    public required BacktestSettings Settings { get; init; }

    /// <summary>
    ///     The sets in the order of the grid; their numbers once the backtest is done, and only then.
    /// </summary>
    public IReadOnlyList<BacktestSetOutcome> Sets { get; init; } = [];

    /// <summary>
    ///     The number of sets, known also when <see cref="Sets" /> is not read.
    /// </summary>
    public required int SetCount { get; init; }

    /// <summary>
    ///     The backtest and the set this one computes on the rest of history (spec strategy-backtest «Набор на остатке
    ///     истории»); null for a backtest asked by itself.
    /// </summary>
    public BacktestSource? Source { get; init; }

    /// <summary>
    ///     One of <see cref="BacktestState" />.
    /// </summary>
    public string Status { get; init; } = BacktestState.Queued;

    public DateTimeOffset QueuedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public string? LeaseOwner { get; init; }

    public DateTimeOffset? LeasedUntil { get; init; }

    public int LostLeases { get; init; }

    public bool CancelRequested { get; init; }

    public int PairsDone { get; init; }

    public int? PairsTotal { get; init; }

    public int? EngineVersion { get; init; }

    public string? DataHash { get; init; }

    public double? ReadSeconds { get; init; }

    public double? ComputeSeconds { get; init; }

    /// <summary>
    ///     Why a failed or cancelled backtest has no result.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    ///     The sum in USDT of the best set of a done backtest, when the list reads it.
    /// </summary>
    public double? BestAmount { get; init; }

    public bool InterruptedTooOften => LostLeases >= MaxLostLeases;

    public bool IsQueued => Status == BacktestState.Queued;

    public bool IsActive => Status is BacktestState.Queued or BacktestState.Running;
}