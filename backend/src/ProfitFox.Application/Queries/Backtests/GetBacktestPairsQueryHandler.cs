using MediatR;

using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Backtests;

namespace ProfitFox.Application.Queries.Backtests;

internal sealed class GetBacktestPairsQueryHandler(IBacktestRepository backtests) : IRequestHandler<GetBacktestPairsQuery, IReadOnlyList<PairSummary>?>
{
    public Task<IReadOnlyList<PairSummary>?> Handle(GetBacktestPairsQuery request, CancellationToken cancellationToken)
    {
        return backtests.PairsAsync(request.Id, request.Set, cancellationToken);
    }
}
