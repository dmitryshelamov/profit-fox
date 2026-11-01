using MediatR;

namespace ProfitFox.Application.Backtests.Queries.GetBacktestResults;

/// <summary>
///     A page of results of the backtest; without a sort they go by combination, without an order ascending. Returns <c>null</c> when
///     there is no such backtest.
/// </summary>
public sealed record GetBacktestResultsQuery(Guid BacktestId, string? Sort, string? Order, int? Offset, int? Limit) : IRequest<GetBacktestResultsResult?>;