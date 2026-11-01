using MediatR;

namespace ProfitFox.Application.Backtests.Commands.CancelBacktest;

public sealed record CancelBacktestCommand(Guid BacktestId) : IRequest;