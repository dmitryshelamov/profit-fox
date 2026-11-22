using MediatR;

using ProfitFox.Application.Contracts.Backtests;
using ProfitFox.Application.Contracts.Backtests.Converters;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Queries.Backtests;

internal sealed class GetBacktestsQueryHandler(IBacktestRepository backtests) : IRequestHandler<GetBacktestsQuery, IReadOnlyList<BacktestInternal>>
{
    public async Task<IReadOnlyList<BacktestInternal>> Handle(GetBacktestsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<Backtest> found = request.Active
            ? await backtests.ActiveAsync(cancellationToken)
            : await backtests.FinishedAsync(request.Limit, cancellationToken);
        return [.. found.Select(backtest => backtest.ToInternal())];
    }
}
