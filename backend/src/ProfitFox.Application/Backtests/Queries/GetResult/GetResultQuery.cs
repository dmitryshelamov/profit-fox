using MediatR;

using ProfitFox.Domain.Entities.Backtests;

namespace ProfitFox.Application.Backtests.Queries.GetResult;

/// <summary>The result of one combination of the backtest; <c>null</c> when there is no such backtest or the combination has no result.</summary>
public sealed record GetResultQuery(Guid BacktestId, long Combination) : IRequest<BacktestResult?>;