using MediatR;

using ProfitFox.Domain.Entities.Runs.Enums;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Commands.Backtests.CancelBacktest;

internal sealed class CancelBacktestCommandHandler(IBacktestRepository backtests) : IRequestHandler<CancelBacktestCommand, CancelOutcome>
{
    public Task<CancelOutcome> Handle(CancelBacktestCommand request, CancellationToken cancellationToken)
    {
        return backtests.CancelAsync(request.Id, cancellationToken);
    }
}
