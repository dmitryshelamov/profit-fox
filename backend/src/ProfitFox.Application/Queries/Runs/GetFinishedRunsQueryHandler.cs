using MediatR;

using ProfitFox.Application.Contracts.Runs;
using ProfitFox.Application.Contracts.Runs.Converters;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Queries.Runs;

internal sealed class GetFinishedRunsQueryHandler(ILedgerRepository ledger) : IRequestHandler<GetFinishedRunsQuery, IReadOnlyList<FinishedRunInternal>>
{
    public async Task<IReadOnlyList<FinishedRunInternal>> Handle(GetFinishedRunsQuery request, CancellationToken cancellationToken)
    {
        return [.. (await ledger.RecentAsync(request.Limit, cancellationToken)).Select(finished => finished.ToInternal())];
    }
}
