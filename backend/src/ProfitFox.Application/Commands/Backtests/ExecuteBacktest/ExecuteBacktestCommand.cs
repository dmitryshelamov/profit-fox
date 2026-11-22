using MediatR;

using ProfitFox.Application.Commands.Runs.ExecuteRun;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.ValueObjects.Runs;

namespace ProfitFox.Application.Commands.Backtests.ExecuteBacktest;

/// <summary>
///     Computes a backtest of the queue held by the Worker of <see cref="Assignment" /> (spec strategy-backtest; spec
///     bench-run-queue «Результат прогона»). <see cref="Progress" /> gets the pairs as they pass.
/// </summary>
public sealed record ExecuteBacktestCommand(Backtest Backtest, RunAssignment Assignment, RunProgress? Progress = null) : IRequest<ExecuteBacktestResult>;
