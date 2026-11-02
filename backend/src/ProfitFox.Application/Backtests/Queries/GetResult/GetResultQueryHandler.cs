using MediatR;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Backtests.Queries.GetResult;

public sealed class GetResultQueryHandler(IBacktestRepository backtests) : IRequestHandler<GetResultQuery, BacktestResult?>
{
    /// <remarks>A result row never exists without its backtest, so the row alone answers both "not found" cases.</remarks>
    public async Task<BacktestResult?> Handle(GetResultQuery request, CancellationToken cancellationToken)
    {
        return await backtests.GetResultAsync(request.BacktestId, request.Combination, cancellationToken);
    }
}