using MediatR;

using ProfitFox.Domain.Entities.Backtests;

namespace ProfitFox.Application.Backtests.Queries.GetBacktest;

/// <summary>The backtest with this id, or <c>null</c> when there is none.</summary>
public sealed record GetBacktestQuery(Guid Id) : IRequest<Backtest?>;