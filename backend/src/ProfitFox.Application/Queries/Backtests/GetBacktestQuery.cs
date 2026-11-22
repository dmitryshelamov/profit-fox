using MediatR;

using ProfitFox.Application.Contracts.Backtests;

namespace ProfitFox.Application.Queries.Backtests;

/// <summary>
///     One backtest with its settings, state, progress and the numbers of its sets; null when there is none.
/// </summary>
public sealed record GetBacktestQuery(string Id) : IRequest<BacktestInternal?>;
