using MediatR;

using ProfitFox.Domain.ValueObjects.Backtests;

namespace ProfitFox.Application.Backtests.Queries.GetResultDetails;

/// <summary>The details of a result, computed again; <c>null</c> when there is no such backtest or result.</summary>
public sealed record GetResultDetailsQuery(Guid BacktestId, long Combination) : IRequest<BacktestDetails?>;