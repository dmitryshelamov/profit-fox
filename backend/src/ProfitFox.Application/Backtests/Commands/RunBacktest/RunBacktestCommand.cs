using MediatR;

namespace ProfitFox.Application.Backtests.Commands.RunBacktest;

public sealed record RunBacktestCommand(Guid BacktestId) : IRequest;