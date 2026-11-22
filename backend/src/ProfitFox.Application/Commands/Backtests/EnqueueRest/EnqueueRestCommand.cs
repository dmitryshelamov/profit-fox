using MediatR;

using ProfitFox.Application.Contracts.Backtests;

namespace ProfitFox.Application.Commands.Backtests.EnqueueRest;

/// <summary>
///     Puts the set <see cref="Set" /> of the done backtest <see cref="Backtest" /> in the queue on the rest of history
///     (spec strategy-backtest «Набор на остатке истории»).
/// </summary>
public sealed record EnqueueRestCommand(string Backtest, int Set) : IRequest<BacktestInternal>;
