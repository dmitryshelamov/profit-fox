using ProfitFox.Domain.ValueObjects.Backtests;

namespace ProfitFox.Application.Commands.Backtests.ExecuteBacktest;

/// <summary>
///     What a done backtest wrote with its end.
/// </summary>
public sealed record ExecuteBacktestResult(BacktestOutcome Outcome);
