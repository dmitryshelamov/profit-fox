namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>
///     A backtest (spec bench-run-queue «Страница прогона», «Результат прогона»): its strategy and settings, its state —
///     <c>queued</c>, <c>running</c>, <c>done</c>, <c>failed</c> or <c>cancelled</c> —, the Worker and the progress of a going
///     one, and once it ended the version of the engine, the hash of the history read, how long it read and computed, the
///     reason of a failed or cancelled one, the sets with their numbers, the sum of the best set and the warning about
///     survivors. A list gives no sets.
/// </summary>
public sealed record BacktestResponse(
    string Id,
    BacktestStrategyResponse Strategy,
    BacktestSettingsResponse Settings,
    string State,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Worker,
    int PairsDone,
    int? PairsTotal,
    int SetCount,
    IReadOnlyList<BacktestSetResponse> Sets,
    string Warning,
    int? EngineVersion = null,
    string? DataHash = null,
    double? ReadSeconds = null,
    double? ComputeSeconds = null,
    string? Reason = null,
    double? BestAmount = null,
    BacktestSourceResponse? Source = null);
