using MediatR;

namespace ProfitFox.Application.Backtests.Commands.RunNextBacktestWork;

/// <summary>Does the next work of the oldest backtest that has any: a step of loading its candles or a chunk; returns whether it computed a chunk.</summary>
public sealed record RunNextBacktestWorkCommand : IRequest<bool>;