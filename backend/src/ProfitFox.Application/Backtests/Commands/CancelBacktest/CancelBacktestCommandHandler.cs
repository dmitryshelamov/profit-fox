using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Backtests.Commands.CancelBacktest;

public sealed class CancelBacktestCommandHandler(IBacktestRepository backtests, TimeProvider time) : IRequestHandler<CancelBacktestCommand>
{
    /// <summary>
    ///     One statement cancels the backtest if it is unfinished, whatever its run does meanwhile. A pending or loading backtest ends at
    ///     once; a running one stops at its next save of results, which finds the status changed.
    /// </summary>
    /// <exception cref="NotFoundException">There is no such backtest.</exception>
    /// <exception cref="ConflictException">The backtest has finished.</exception>
    public async Task Handle(CancelBacktestCommand request, CancellationToken cancellationToken)
    {
        if (await backtests.UpdateStatusAsync(request.BacktestId, Backtest.Unfinished, BacktestStatus.Cancelled, time.GetUtcNow(), cancellationToken))
        {
            return;
        }

        Backtest backtest = await backtests.GetAsync(request.BacktestId, cancellationToken)
            ?? throw new NotFoundException($"There is no backtest {request.BacktestId}.");

        throw new ConflictException($"Only an unfinished backtest can be cancelled; this one is {backtest.Status}.");
    }
}