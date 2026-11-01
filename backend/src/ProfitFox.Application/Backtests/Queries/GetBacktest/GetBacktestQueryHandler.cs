using MediatR;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Backtests.Queries.GetBacktest;

public sealed class GetBacktestQueryHandler(IBacktestRepository backtests) : IRequestHandler<GetBacktestQuery, Backtest?>
{
    public Task<Backtest?> Handle(GetBacktestQuery request, CancellationToken cancellationToken)
    {
        return backtests.GetAsync(request.Id, cancellationToken);
    }
}