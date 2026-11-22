using MediatR;

using ProfitFox.Application.Contracts.Backtests;
using ProfitFox.Application.Contracts.Backtests.Converters;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Queries.Backtests;

internal sealed class GetBacktestQueryHandler(IBacktestRepository backtests) : IRequestHandler<GetBacktestQuery, BacktestInternal?>
{
    public async Task<BacktestInternal?> Handle(GetBacktestQuery request, CancellationToken cancellationToken)
    {
        return (await backtests.FindAsync(request.Id, cancellationToken))?.ToInternal();
    }
}
