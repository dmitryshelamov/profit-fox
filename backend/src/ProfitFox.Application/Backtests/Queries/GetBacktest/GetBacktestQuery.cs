using MediatR;

namespace ProfitFox.Application.Backtests.Queries.GetBacktest;

/// <summary>The backtest with this id and whether its bot changed since, or <c>null</c> when there is none.</summary>
public sealed record GetBacktestQuery(Guid Id) : IRequest<GetBacktestResult?>;