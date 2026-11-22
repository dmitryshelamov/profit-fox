using MediatR;

using ProfitFox.Application.Contracts.Backtests;
using ProfitFox.Domain.ValueObjects.Backtests;

namespace ProfitFox.Application.Commands.Backtests.EnqueueBacktest;

/// <summary>
///     Puts a backtest of the version <see cref="Version" /> of the strategy <see cref="Strategy" /> — its latest when null —
///     in the queue with the settings of <see cref="Request" /> (spec strategy-backtest «Прогон версии стратегии»; spec
///     bench-run-queue «Постановка прогона стратегии»).
/// </summary>
public sealed record EnqueueBacktestCommand(string Strategy, int? Version, BacktestRequest Request) : IRequest<BacktestInternal>;
