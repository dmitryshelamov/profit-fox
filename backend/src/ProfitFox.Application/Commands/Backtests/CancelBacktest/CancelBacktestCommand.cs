using MediatR;

using ProfitFox.Domain.Entities.Runs.Enums;

namespace ProfitFox.Application.Commands.Backtests.CancelBacktest;

/// <summary>
///     Takes a waiting backtest out of the queue, or asks the Worker of a going one to stop (spec bench-run-queue «Отмена
///     прогона»).
/// </summary>
public sealed record CancelBacktestCommand(string Id) : IRequest<CancelOutcome>;
